<script setup lang="ts">
// Business area 4: carry out one job (A1 Task 5): tick off each parcel, take payment at pickup,
// confirm pickup, then record the delivery or a failure. No card numbers are ever typed in:
// staff record only what the payment terminal showed (A1 assumption A6).
import { computed, ref } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { jobApi } from '@/api/dms'
import { Handling, type OperationResult } from '@/api/types'
import type { Message } from '@/session/flash'
import * as rules from '@/validation/rules'
import { handlingText, isLate, money, shortWhen, statusText, windowText } from '@/utils/format'
import { errorText, useAsync } from '@/utils/useAsync'
import ConfirmBox from '@/components/ConfirmBox.vue'
import FormField from '@/components/FormField.vue'
import MessageBanner from '@/components/MessageBanner.vue'
import PageHeader from '@/components/PageHeader.vue'
import RequestDetailPanel from '@/components/RequestDetailPanel.vue'
import StatusBadge from '@/components/StatusBadge.vue'

type Panel = 'cash' | 'card' | 'confirm' | 'delivered' | 'failure' | null

const pickupReasons = [
  'Parcels were not ready for pickup',
  'Customer was not available at the pickup address',
  'Pickup address is incorrect',
]
const deliveryReasons = ['Recipient was not available', 'Delivery address is incorrect', 'Parcel was damaged']

const route = useRoute()
const reference = String(route.params.reference).toUpperCase()
const job = useAsync(() => jobApi.get(reference))
const detail = computed(() => job.data.value?.detail ?? null)
const due = computed(() => job.data.value?.amountDue ?? 0)
const paid = computed(() => detail.value?.payments.find((p) => p.successful) ?? null)
// A5: money changes hands at pickup. Until a payment succeeds (a declined card does not count)
// the parcels are not ticked and the delivery is not handed over. A zero-amount job is never blocked.
const paymentRequired = computed(() => due.value > 0 && paid.value === null)
const canTickParcels = computed(() => detail.value?.status === 'Scheduled' && !paymentRequired.value)
const failedAttempts = computed(() => detail.value?.payments.filter((p) => !p.successful) ?? [])
const ticked = computed(() => detail.value?.parcels.filter((p) => p.collected).length ?? 0)
const needsSignature = computed(
  () => detail.value?.parcels.some((p) => (p.flags & Handling.SignatureRequired) !== 0) ?? false,
)
const closed = computed(
  () => detail.value !== null && !['Scheduled', 'Collected'].includes(detail.value.status),
)
const reasons = computed(() => (detail.value?.status === 'Collected' ? deliveryReasons : pickupReasons))

const message = ref<Message | null>(null)
const panel = ref<Panel>(null)
const busy = ref(false)
const cash = ref('')
const cashError = ref<string | null>(null)
const receivedBy = ref('')
const receivedByError = ref<string | null>(null)
const signed = ref(false)
const signedError = ref<string | null>(null)
const failureChoice = ref('')
const failureOther = ref('')
const failureError = ref<string | null>(null)

function open(p: Panel): void {
  panel.value = p
  message.value = null
  cashError.value = null
  receivedByError.value = null
  signedError.value = null
  failureError.value = null
  if (p === 'delivered') {
    receivedBy.value = detail.value?.recipient.name ?? ''
    signed.value = false
  }
  if (p === 'failure') {
    failureChoice.value = ''
    failureOther.value = ''
  }
  if (p === 'cash') cash.value = ''
}

function closePanel(note: string): void {
  panel.value = null
  message.value = { kind: 'notice', text: note }
}

async function run(call: () => Promise<OperationResult>): Promise<void> {
  busy.value = true
  try {
    const result = await call()
    message.value = { kind: result.succeeded ? 'success' : 'error', text: result.message }
    if (result.succeeded) panel.value = null
    await job.reload()
  } catch (e) {
    message.value = { kind: 'error', text: errorText(e) }
  } finally {
    busy.value = false
  }
}

async function toggleParcel(parcelId: string, box: HTMLInputElement): Promise<void> {
  await run(() => jobApi.setCollected(reference, parcelId, box.checked))
  // The server is the authority: put the box back if it refused (e.g. unpaid, or already collected).
  box.checked = detail.value?.parcels.find((p) => p.parcelId === parcelId)?.collected ?? false
}

async function takeCash(): Promise<void> {
  cashError.value = rules.money(0.05, 10_000)(cash.value)
  if (cashError.value) return
  await run(async () => {
    const outcome = await jobApi.cash(reference, rules.parseMoney(cash.value) ?? 0)
    const change =
      outcome.changeDue && outcome.changeDue > 0 ? ` Give ${money(outcome.changeDue)} change.` : ''
    return { succeeded: outcome.successful, message: outcome.message + change }
  })
}

async function takeCard(approved: boolean): Promise<void> {
  await run(async () => {
    const outcome = await jobApi.card(reference, approved)
    return { succeeded: outcome.successful, message: outcome.message }
  })
}

async function delivered(): Promise<void> {
  receivedByError.value = rules.name(receivedBy.value)
  signedError.value =
    needsSignature.value && !signed.value
      ? 'A signature is required for this delivery. Get a signature, or record a failed delivery instead.'
      : null
  if (receivedByError.value || signedError.value) return
  await run(() => jobApi.delivered(reference, receivedBy.value.trim()))
}

async function failure(): Promise<void> {
  const reason = failureChoice.value === 'other' ? failureOther.value : failureChoice.value
  failureError.value = failureChoice.value === '' ? 'Choose what went wrong.' : rules.reason(reason)
  if (failureError.value) return
  await run(() => jobApi.failure(reference, reason.trim()))
}
</script>

<template>
  <PageHeader :title="`Job ${reference}`" back-to="/jobs" back-label="Back to my jobs" />
  <MessageBanner v-if="job.error.value" kind="error" :text="job.error.value" />
  <MessageBanner
    v-if="message"
    :kind="message.kind"
    :text="message.text"
    dismissible
    @dismiss="message = null"
  />

  <template v-if="detail && closed">
    <section class="card finished">
      <h2>This job is {{ statusText(detail.status).toLowerCase() }}</h2>
      <p v-if="detail.status === 'DeliveryFailed'">The delivery is back with the office to be rescheduled.</p>
      <p v-else>Nothing more needs to be recorded. The customer has been notified.</p>
      <RouterLink to="/jobs" class="btn">Back to my jobs</RouterLink>
    </section>
    <RequestDetailPanel :detail="detail" level="Staff" />
  </template>

  <template v-else-if="detail">
    <div class="spread stage">
      <p class="muted">
        {{
          detail.status === 'Scheduled'
            ? 'Stage 1 of 2: pick up the parcels and take payment'
            : 'Stage 2 of 2: deliver to the recipient'
        }}
      </p>
      <StatusBadge :status="detail.status" :late="isLate(detail)" />
    </div>

    <div class="grid-2 layout">
      <section class="card" aria-labelledby="pickup-title">
        <h2 id="pickup-title">Pick up</h2>
        <dl class="facts">
          <dt>Address</dt>
          <dd>{{ detail.pickup.street }}, {{ detail.pickup.suburb }} VIC {{ detail.pickup.postcode }}</dd>
          <dt>Contact</dt>
          <dd>
            {{ detail.pickupContact.name }},
            <a :href="`tel:${detail.pickupContact.phone.replace(/ /g, '')}`">{{
              detail.pickupContact.phone
            }}</a>
          </dd>
          <dt>Window</dt>
          <dd>{{ windowText(detail.currentQuote?.pickupWindow) }}</dd>
          <dt>Office notes</dt>
          <dd>{{ detail.notes ?? '-' }}</dd>
        </dl>
        <h2 class="gap">Deliver to</h2>
        <dl class="facts">
          <dt>Address</dt>
          <dd>
            {{ detail.destination.street }}, {{ detail.destination.suburb }} VIC
            {{ detail.destination.postcode }}
          </dd>
          <dt>Recipient</dt>
          <dd>
            {{ detail.recipient.name }},
            <a :href="`tel:${detail.recipient.phone.replace(/ /g, '')}`">{{ detail.recipient.phone }}</a>
          </dd>
          <dt>Window</dt>
          <dd>{{ windowText(detail.currentQuote?.deliveryWindow) }}</dd>
        </dl>
      </section>

      <section class="card" aria-labelledby="parcels-title">
        <h2 id="parcels-title">Parcels ({{ ticked }} of {{ detail.parcels.length }} collected)</h2>
        <p
          v-if="paymentRequired && detail.status === 'Scheduled'"
          class="muted small hint"
          data-testid="payment-first-hint"
        >
          Take payment first: the customer pays at pickup, before any parcel is ticked.
        </p>
        <ul class="checklist">
          <li v-for="(p, i) in detail.parcels" :key="p.parcelId" data-testid="job-parcel">
            <label class="choice">
              <input
                type="checkbox"
                :checked="p.collected"
                :disabled="!canTickParcels || busy"
                @change="toggleParcel(p.parcelId, $event.target as HTMLInputElement)"
              />
              <span>
                <strong>Parcel {{ i + 1 }}: {{ p.size }}</strong>
                <span class="muted"> &middot; {{ handlingText(p.flags) }}</span>
                <span v-if="p.description"> &middot; {{ p.description }}</span>
              </span>
            </label>
          </li>
        </ul>

        <h2 class="gap">Payment</h2>
        <dl class="facts">
          <dt>Amount due</dt>
          <dd>
            <strong>{{ money(due) }}</strong>
          </dd>
          <dt>Payment</dt>
          <dd>
            <span v-if="paid" class="badge badge-done"
              >Paid by {{ paid.method }} at {{ shortWhen(paid.at) }}</span
            >
            <span v-else class="badge badge-problem">Not yet paid</span>
          </dd>
        </dl>
        <p v-for="(f, i) in failedAttempts" :key="i" class="muted small">
          Earlier attempt: {{ f.method }} {{ f.outcome.toLowerCase() }} at {{ shortWhen(f.at) }}
        </p>
        <!-- A5: payment is offered in both stages, so a job that is already Collected without
             a payment can still be paid for (legacy or API-created state) instead of being stranded. -->
        <div v-if="paymentRequired" class="row gap">
          <button type="button" class="btn btn-secondary" data-testid="take-cash" @click="open('cash')">
            Take cash
          </button>
          <button type="button" class="btn btn-secondary" data-testid="take-card" @click="open('card')">
            Take card
          </button>
        </div>
      </section>
    </div>

    <section class="card" aria-labelledby="do-title">
      <h2 id="do-title">Record what happened</h2>
      <div class="row">
        <template v-if="detail.status === 'Scheduled'">
          <button type="button" class="btn" @click="open('confirm')">Confirm pickup is complete</button>
          <button type="button" class="btn btn-secondary" @click="open('failure')">
            Record a failed pickup
          </button>
        </template>
        <template v-else>
          <button
            type="button"
            class="btn"
            :disabled="busy || paymentRequired"
            data-testid="record-delivered"
            @click="open('delivered')"
          >
            Record the delivery as completed
          </button>
          <button type="button" class="btn btn-secondary" @click="open('failure')">
            Record a failed delivery attempt
          </button>
        </template>
      </div>
      <p v-if="detail.status !== 'Scheduled' && paymentRequired" class="muted small" data-testid="pay-before-handover">
        Payment must be collected before the parcels are handed over.
      </p>

      <!-- Cash -->
      <form v-if="panel === 'cash'" class="panel" novalidate @submit.prevent="takeCash">
        <h3>Cash payment</h3>
        <FormField
          id="cash"
          label="Cash received from the customer"
          :hint="`Amount due is ${money(due)}`"
          :error="cashError"
          required
        >
          <template #default="f">
            <input
              :id="f.id"
              v-model="cash"
              type="text"
              inputmode="decimal"
              :aria-describedby="f.describedBy"
              :aria-invalid="f.invalid"
            />
          </template>
        </FormField>
        <div class="row">
          <button type="submit" class="btn" :disabled="busy">Record cash payment</button>
          <button type="button" class="btn btn-secondary" @click="closePanel('No payment taken yet.')">
            Back
          </button>
        </div>
      </form>

      <!-- Card -->
      <div v-if="panel === 'card'" class="panel">
        <h3>Card payment</h3>
        <p class="message message-notice">
          <span class="label">Note:</span> do not type card numbers here. Take the card on the payment
          terminal for {{ money(due) }}, then record what the terminal showed.
        </p>
        <div class="row">
          <button type="button" class="btn" :disabled="busy" @click="takeCard(true)">
            Terminal showed APPROVED
          </button>
          <button type="button" class="btn btn-danger" :disabled="busy" @click="takeCard(false)">
            Terminal showed DECLINED
          </button>
          <button type="button" class="btn btn-secondary" @click="closePanel('No payment recorded.')">
            Back
          </button>
        </div>
      </div>

      <!-- Confirm pickup -->
      <ConfirmBox
        v-if="panel === 'confirm'"
        question="Confirm that every parcel is collected and payment has been taken?"
        :busy="busy"
        @confirm="run(() => jobApi.confirmCollection(reference))"
        @cancel="closePanel('Pickup not confirmed yet.')"
      />

      <!-- Delivered -->
      <form v-if="panel === 'delivered'" class="panel" novalidate @submit.prevent="delivered">
        <h3>Delivery completed</h3>
        <FormField
          id="received-by"
          label="Name of the person who received the parcels"
          :error="receivedByError"
          required
        >
          <template #default="f">
            <input
              :id="f.id"
              v-model="receivedBy"
              type="text"
              :aria-describedby="f.describedBy"
              :aria-invalid="f.invalid"
            />
          </template>
        </FormField>
        <div v-if="needsSignature" class="field">
          <label class="choice">
            <input v-model="signed" type="checkbox" :aria-invalid="signedError ? 'true' : undefined" />
            The receiver has signed for the parcels (signature required)
          </label>
          <span v-if="signedError" class="error-text" data-testid="field-error">{{ signedError }}</span>
        </div>
        <div class="row">
          <button type="submit" class="btn" :disabled="busy">Record delivery</button>
          <button type="button" class="btn btn-secondary" @click="closePanel('Delivery not recorded yet.')">
            Back
          </button>
        </div>
      </form>

      <!-- Failure -->
      <form v-if="panel === 'failure'" class="panel" novalidate @submit.prevent="failure">
        <h3>{{ detail.status === 'Scheduled' ? 'Failed pickup' : 'Failed delivery attempt' }}</h3>
        <fieldset class="field">
          <legend>What went wrong? <span class="required" aria-hidden="true">*</span></legend>
          <label v-for="r in reasons" :key="r" class="choice"
            ><input v-model="failureChoice" type="radio" name="reason" :value="r" /> {{ r }}</label
          >
          <label class="choice"
            ><input v-model="failureChoice" type="radio" name="reason" value="other" /> Other reason</label
          >
        </fieldset>
        <FormField
          v-if="failureChoice === 'other'"
          id="failure-other"
          label="Describe what went wrong"
          hint="3 to 200 characters"
          required
        >
          <template #default="f">
            <input :id="f.id" v-model="failureOther" type="text" :aria-describedby="f.describedBy" />
          </template>
        </FormField>
        <p v-if="failureError" class="error-text" role="alert">{{ failureError }}</p>
        <div class="row">
          <button type="submit" class="btn btn-danger" :disabled="busy">Record the failure</button>
          <button type="button" class="btn btn-secondary" @click="closePanel('No failure recorded.')">
            Back
          </button>
        </div>
      </form>
    </section>
  </template>
</template>

<style scoped>
.layout {
  align-items: start;
}

.stage {
  margin-bottom: 0.5rem;
}

.gap {
  margin-top: 1.25rem;
}

.hint {
  margin: 0 0 0.5rem;
}

.checklist {
  list-style: none;
  padding: 0;
  margin: 0;
}

.checklist li {
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 0.6rem 0.8rem;
  margin-bottom: 0.5rem;
}

.checklist input {
  width: 1.4rem;
  height: 1.4rem;
}

.panel {
  border-top: 1px solid var(--border);
  margin-top: 1rem;
  padding-top: 1rem;
}

.finished {
  border-top: 6px solid var(--success);
}

.error-text {
  color: var(--error);
  font-weight: 600;
}

.required {
  color: var(--error);
}
</style>
