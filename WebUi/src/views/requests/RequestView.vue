<script setup lang="ts">
// One request in full, with the actions the viewer may take. Area 2 (customer side): accept,
// reject or ask for a revised quote (A1 Task 3); cancel; quote versions; payment receipt.
// Only actions the server's StatusLifecycle allows are offered.
import { computed, ref } from 'vue'
import { useRoute } from 'vue-router'
import { quoteApi, requestApi, trackingApi } from '@/api/dms'
import type { OperationResult, QuoteView } from '@/api/types'
import { canTransition, hasRole, isOffice, session } from '@/session/session'
import type { Message } from '@/session/flash'
import * as rules from '@/validation/rules'
import { money, when } from '@/utils/format'
import { errorText, useAsync } from '@/utils/useAsync'
import ConfirmBox from '@/components/ConfirmBox.vue'
import FormField from '@/components/FormField.vue'
import MessageBanner from '@/components/MessageBanner.vue'
import PageHeader from '@/components/PageHeader.vue'
import RequestDetailPanel from '@/components/RequestDetailPanel.vue'

type Action = 'accept' | 'reject' | 'revise' | 'cancel'

const route = useRoute()
const reference = computed(() => String(route.params.reference).toUpperCase())
const tracking = useAsync(() => trackingApi.find(reference.value))
const detail = computed(() => tracking.data.value?.detail ?? null)
const level = computed(() => tracking.data.value?.level ?? 'None')

const message = ref<Message | null>(null)
const action = ref<Action | null>(null)
const reason = ref('')
const reasonError = ref<string | null>(null)
const busy = ref(false)
const quoteVersions = ref<QuoteView[] | null>(null)
const showReceipt = ref(false)

const canDecide = computed(() => level.value === 'Customer' || isOffice.value)
const quote = computed(() => detail.value?.currentQuote ?? null)
const decisionOpen = computed(
  () =>
    canDecide.value &&
    detail.value?.status === 'Quoted' &&
    quote.value !== null &&
    !quote.value.awaitingOwnerApproval,
)
const canCancel = computed(
  () => canDecide.value && detail.value !== null && canTransition(detail.value.status, 'Cancelled'),
)
const paid = computed(() => detail.value?.payments.find((p) => p.successful) ?? null)
const backTo = computed(() => {
  if (hasRole('Customer')) return '/my/requests'
  if (isOffice.value && detail.value) return `/customers/${detail.value.customerId}/requests`
  return session.user ? '/search' : '/track'
})

const questions: Record<Action, string> = {
  accept: 'Accept this quote? Payment is taken when the parcels are collected.',
  reject: 'Reject this quote? The request will be closed.',
  revise: 'Ask office staff for a revised quote?',
  cancel: 'Cancel this request? This cannot be undone.',
}

function start(a: Action): void {
  action.value = a
  reason.value = ''
  reasonError.value = null
  message.value = null
}

function stop(): void {
  action.value = null
  message.value = { kind: 'notice', text: 'Nothing was changed.' }
}

async function confirm(): Promise<void> {
  if (action.value === null) return
  if (action.value !== 'accept') {
    reasonError.value = rules.reason(reason.value)
    if (reasonError.value) return
  }
  busy.value = true
  try {
    const r = reason.value.trim()
    const calls: Record<Action, () => Promise<OperationResult>> = {
      accept: () => quoteApi.accept(reference.value),
      reject: () => quoteApi.reject(reference.value, r),
      revise: () => quoteApi.revise(reference.value, r),
      cancel: () => requestApi.cancel(reference.value, r),
    }
    const result = await calls[action.value]()
    message.value = { kind: result.succeeded ? 'success' : 'error', text: result.message }
    action.value = null
    quoteVersions.value = null
    await tracking.reload()
  } catch (e) {
    message.value = { kind: 'error', text: errorText(e) }
  } finally {
    busy.value = false
  }
}

function printReceipt(): void {
  window.print()
}

async function toggleVersions(): Promise<void> {
  if (quoteVersions.value !== null) {
    quoteVersions.value = null
    return
  }
  try {
    quoteVersions.value = await quoteApi.history(reference.value)
  } catch (e) {
    message.value = { kind: 'error', text: errorText(e) }
  }
}
</script>

<template>
  <PageHeader :title="`Request ${reference}`" :back-to="backTo" back-label="Back to the list" />
  <MessageBanner v-if="tracking.error.value" kind="error" :text="tracking.error.value" />
  <MessageBanner
    v-if="message"
    :kind="message.kind"
    :text="message.text"
    dismissible
    @dismiss="message = null"
  />

  <template v-if="detail">
    <!-- Actions -->
    <section v-if="canDecide" class="card actions-card" aria-labelledby="actions-title">
      <h2 id="actions-title">What would you like to do?</h2>
      <p v-if="detail.status === 'Requested' && !quote?.awaitingOwnerApproval" class="muted">
        This request is waiting for office staff to prepare a quote.
      </p>
      <p v-if="quote?.awaitingOwnerApproval" class="muted">
        The quote is waiting for an owner to set the price.
      </p>
      <p v-if="decisionOpen && quote?.isExpired" class="message message-notice">
        <span class="label">Note:</span> this quote expired on {{ when(quote.validUntil) }}.
      </p>
      <div class="row">
        <template v-if="decisionOpen && quote">
          <button type="button" class="btn" @click="start('accept')">
            Accept quote ({{ money(quote.price) }})
          </button>
          <button type="button" class="btn btn-secondary" @click="start('revise')">
            Ask for a revised quote
          </button>
          <button type="button" class="btn btn-secondary" @click="start('reject')">Reject the quote</button>
        </template>
        <button v-if="canCancel" type="button" class="btn btn-secondary" @click="start('cancel')">
          Cancel this request
        </button>
        <button v-if="quote" type="button" class="btn btn-secondary" @click="toggleVersions">
          {{ quoteVersions ? 'Hide quote versions' : 'See every quote version' }}
        </button>
        <button v-if="paid" type="button" class="btn btn-secondary" @click="showReceipt = !showReceipt">
          {{ showReceipt ? 'Hide receipt' : 'View the payment receipt' }}
        </button>
      </div>

      <ConfirmBox
        v-if="action"
        :question="questions[action]"
        :confirm-label="action === 'accept' ? 'Yes, accept' : 'Yes, go ahead'"
        :danger="action === 'cancel' || action === 'reject'"
        :busy="busy"
        @confirm="confirm"
        @cancel="stop"
      >
        <FormField
          v-if="action !== 'accept'"
          id="reason"
          :label="action === 'revise' ? 'What should change?' : 'Reason'"
          :hint="
            action === 'revise'
              ? 'e.g. price too high, or an earlier pickup is needed'
              : '3 to 200 characters'
          "
          :error="reasonError"
          required
        >
          <template #default="f">
            <input
              :id="f.id"
              v-model="reason"
              type="text"
              :aria-describedby="f.describedBy"
              :aria-invalid="f.invalid"
            />
          </template>
        </FormField>
      </ConfirmBox>
    </section>

    <section v-if="quoteVersions" class="card" aria-labelledby="versions-title">
      <h2 id="versions-title">Quote versions</h2>
      <div class="table-wrap">
        <table>
          <thead>
            <tr>
              <th scope="col">Version</th>
              <th scope="col">Price</th>
              <th scope="col">Issued</th>
              <th scope="col">Valid until</th>
              <th scope="col">Outcome</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="q in quoteVersions" :key="q.version">
              <td>v{{ q.version }}</td>
              <td>{{ q.price === null ? 'Owner to price' : money(q.price) }}</td>
              <td>{{ when(q.issuedAt) }}</td>
              <td>{{ when(q.validUntil) }}</td>
              <td>{{ q.outcome }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>

    <section v-if="showReceipt && paid" class="card receipt" aria-labelledby="receipt-title">
      <h2 id="receipt-title">Payment receipt</h2>
      <p class="muted">Your Order We Deliver</p>
      <dl class="facts">
        <dt>Reference</dt>
        <dd>{{ detail.reference }}</dd>
        <dt>Customer</dt>
        <dd>{{ detail.customerName }}</dd>
        <dt>Delivery</dt>
        <dd>
          {{ detail.pickup.suburb }} to {{ detail.destination.suburb }}, {{ detail.parcels.length }} parcel(s)
        </dd>
        <dt>Paid</dt>
        <dd>{{ when(paid.at) }} by {{ paid.method }}</dd>
        <dt>Amount (incl. GST)</dt>
        <dd>
          <strong>{{ money(paid.amount) }}</strong>
        </dd>
        <dt>GST included</dt>
        <dd>{{ money(Math.round((paid.amount / 11) * 100) / 100) }}</dd>
      </dl>
      <button type="button" class="btn btn-secondary print" @click="printReceipt">Print receipt</button>
    </section>

    <RequestDetailPanel :detail="detail" :level="level" />
  </template>
</template>

<style scoped>
.actions-card {
  border-left: 5px solid var(--brand);
}

.receipt {
  max-width: 560px;
  border-top: 6px solid var(--success);
}

.print {
  margin-top: 1rem;
}
</style>
