<script setup lang="ts">
// Business area 2 (office): review the proposed price and windows, adjust them if needed (a
// reason is required for a price change), then issue the quote (A1 Task 2). Out-of-area quotes
// are sent to an owner for pricing instead (variant 2a).
import { computed, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { quoteApi, trackingApi } from '@/api/dms'
import type { QuoteProposal, QuoteView, RequestDetail } from '@/api/types'
import { flash } from '@/session/flash'
import * as rules from '@/validation/rules'
import { handlingText, isoToLocalInput, localInputToServer, money, when } from '@/utils/format'
import { errorText } from '@/utils/useAsync'
import ConfirmBox from '@/components/ConfirmBox.vue'
import FormField from '@/components/FormField.vue'
import MessageBanner from '@/components/MessageBanner.vue'
import PageHeader from '@/components/PageHeader.vue'

const route = useRoute()
const router = useRouter()
const reference = String(route.params.reference).toUpperCase()

const proposal = ref<QuoteProposal | null>(null)
const detail = ref<RequestDetail | null>(null)
const versions = ref<QuoteView[]>([])

// The version this quote replaces, when the customer asked for a revision or it expired, so the
// reason is in front of office staff before they set the new price (A1 Task 2 variant 3a).
const previous = computed(() => {
  const last = versions.value[versions.value.length - 1]
  return last && (last.decision === 'RevisionRequested' || last.decision === 'Expired') ? last : null
})
const loadError = ref<string | null>(null)
const problem = ref<string | null>(null)
const notice = ref<string | null>(null)
const confirming = ref(false)
const busy = ref(false)

const form = reactive({
  price: '',
  reason: '',
  pickupStart: '',
  pickupEnd: '',
  deliveryStart: '',
  deliveryEnd: '',
})
const errors = ref<Partial<Record<keyof typeof form, string>>>({})

const priceChanged = computed(() => {
  const p = proposal.value
  if (!p || p.proposedPrice === null) return false
  const entered = rules.parseMoney(form.price)
  return entered === null || entered !== p.proposedPrice
})

function reset(): void {
  const p = proposal.value
  if (!p) return
  form.price = p.proposedPrice === null ? '' : p.proposedPrice.toFixed(2)
  form.reason = ''
  form.pickupStart = isoToLocalInput(p.pickupWindow.start)
  form.pickupEnd = isoToLocalInput(p.pickupWindow.end)
  form.deliveryStart = isoToLocalInput(p.deliveryWindow.start)
  form.deliveryEnd = isoToLocalInput(p.deliveryWindow.end)
  errors.value = {}
}

async function load(): Promise<void> {
  try {
    const [result, tracking, history] = await Promise.all([
      quoteApi.propose(reference),
      trackingApi.find(reference),
      quoteApi.history(reference).catch(() => [] as QuoteView[]),
    ])
    detail.value = tracking.detail
    versions.value = history
    if (!result.succeeded || !result.value) {
      loadError.value = result.message
      return
    }
    proposal.value = result.value
    reset()
  } catch (e) {
    loadError.value = errorText(e)
  }
}
void load()

function undo(): void {
  reset()
  problem.value = null
  notice.value = 'Changes undone. The proposed price and windows are back.'
}

function windowRules(): Partial<Record<keyof typeof form, string>> {
  const ready = detail.value ? new Date(detail.value.readyTime) : new Date()
  const pickupStart = rules.parseLocalDateTime(form.pickupStart)
  const deliveryStart = rules.parseLocalDateTime(form.deliveryStart)
  const endAfter = (start: Date | null): rules.Rule =>
    rules.dateTime((d) => {
      if (start === null) return null
      if (d <= start) return `The end must be after the start (${when(start)}).`
      return d.getTime() - start.getTime() > 12 * 3_600_000 ? 'A window can be at most 12 hours long.' : null
    })
  return rules.check({
    pickupStart: [
      form.pickupStart,
      rules.dateTime((d) =>
        d < ready ? `Pickup cannot start before the parcels are ready (${when(ready)}).` : null,
      ),
    ],
    pickupEnd: [form.pickupEnd, endAfter(pickupStart)],
    deliveryStart: [
      form.deliveryStart,
      rules.dateTime((d) =>
        pickupStart !== null && d < pickupStart
          ? 'Delivery cannot start before the pickup window starts.'
          : null,
      ),
    ],
    deliveryEnd: [form.deliveryEnd, endAfter(deliveryStart)],
  })
}

function review(): void {
  problem.value = null
  notice.value = null
  const p = proposal.value
  if (!p) return
  const found = windowRules()
  if (!p.needsOwnerApproval) {
    const priceError = rules.money(1, 10_000)(form.price)
    if (priceError) found.price = priceError
    else if (priceChanged.value) {
      const reasonError = rules.reason(form.reason)
      if (reasonError) found.reason = reasonError
    }
  }
  errors.value = found
  if (Object.keys(found).length > 0) {
    problem.value = `Please correct the ${Object.keys(found).length === 1 ? 'field' : `${Object.keys(found).length} fields`} marked in red below.`
    return
  }
  confirming.value = true
}

async function issue(): Promise<void> {
  const p = proposal.value
  if (!p) return
  busy.value = true
  try {
    const result = await quoteApi.issue(reference, {
      adjustedPrice: !p.needsOwnerApproval && priceChanged.value ? rules.parseMoney(form.price) : null,
      adjustReason: !p.needsOwnerApproval && priceChanged.value ? form.reason.trim() : null,
      pickupWindow: { start: localInputToServer(form.pickupStart), end: localInputToServer(form.pickupEnd) },
      deliveryWindow: {
        start: localInputToServer(form.deliveryStart),
        end: localInputToServer(form.deliveryEnd),
      },
    })
    confirming.value = false
    if (result.succeeded) {
      flash('success', result.message)
      await router.push('/quotes')
    } else {
      problem.value = result.message
    }
  } catch (e) {
    problem.value = errorText(e)
  } finally {
    busy.value = false
  }
}

async function leave(): Promise<void> {
  flash('notice', `The quote for ${reference} was not issued. The request stays in the queue.`)
  await router.push('/quotes')
}
</script>

<template>
  <PageHeader :title="`Prepare a quote for ${reference}`" back-to="/quotes" back-label="Back to the queue" />
  <MessageBanner v-if="loadError" kind="error" :text="loadError" />
  <MessageBanner v-if="problem" kind="error" :text="problem" />
  <MessageBanner v-if="notice" kind="notice" :text="notice" dismissible @dismiss="notice = null" />

  <div v-if="proposal && detail" class="grid-2 layout">
    <section class="card" aria-labelledby="req-title">
      <h2 id="req-title">Request</h2>
      <dl class="facts">
        <dt>Customer</dt>
        <dd>{{ detail.customerName }} ({{ detail.customerPhone }})</dd>
        <dt>Pickup</dt>
        <dd>{{ detail.pickup.street }}, {{ detail.pickup.suburb }} {{ detail.pickup.postcode }}</dd>
        <dt>Destination</dt>
        <dd>
          {{ detail.destination.street }}, {{ detail.destination.suburb }} {{ detail.destination.postcode }}
        </dd>
        <dt>Parcels ready</dt>
        <dd>{{ when(detail.readyTime) }}</dd>
        <dt>Parcels</dt>
        <dd>
          <span v-for="(p, i) in detail.parcels" :key="p.parcelId" class="parcel-line">
            {{ i + 1 }}. {{ p.size }} &middot; {{ handlingText(p.flags)
            }}<template v-if="p.description"> &middot; {{ p.description }}</template>
          </span>
        </dd>
        <dt>Notes</dt>
        <dd>{{ detail.notes ?? '-' }}</dd>
      </dl>
      <h3 class="breakdown-title">How the price was worked out</h3>
      <ul class="breakdown">
        <li v-for="line in proposal.priceBreakdown" :key="line">{{ line }}</li>
      </ul>
    </section>

    <form class="card" novalidate aria-labelledby="quote-title" @submit.prevent="review">
      <h2 id="quote-title">Quote</h2>
      <p v-for="w in proposal.warnings" :key="w" class="message message-notice">
        <span class="label">Note:</span> {{ w }}
      </p>
      <p v-if="proposal.needsOwnerApproval" class="message message-notice">
        <span class="label">Note:</span> the destination is outside the service area, so an owner must set the
        price.
      </p>
      <section v-if="previous" class="previous" aria-labelledby="previous-title" data-testid="previous-quote">
        <h3 id="previous-title">
          {{
            previous.decision === 'RevisionRequested'
              ? 'The customer asked for a revision'
              : 'The last quote expired'
          }}
        </h3>
        <p>
          Previous quote v{{ previous.version }}:
          <strong>{{ previous.price === null ? 'no price' : money(previous.price) }}</strong>
          <template v-if="previous.adjustReason"> (adjusted: {{ previous.adjustReason }})</template>.
        </p>
        <p v-if="previous.decision === 'RevisionRequested'">
          Customer asked: <strong class="asked">&ldquo;{{ previous.decisionReason }}&rdquo;</strong>
          <span v-if="previous.confirmedVia" class="muted"> (by {{ previous.confirmedVia }})</span>
        </p>
        <p v-else>It expired on {{ when(previous.validUntil) }} without a decision from the customer.</p>
      </section>
      <template v-if="!proposal.needsOwnerApproval">
        <p>
          Proposed price: <strong>{{ money(proposal.proposedPrice) }}</strong>
        </p>
        <FormField
          id="price"
          label="Price to offer"
          hint="Dollars, $1.00 to $10,000.00. Change it only if needed."
          :error="errors.price"
          required
        >
          <template #default="f">
            <input
              :id="f.id"
              v-model="form.price"
              type="text"
              inputmode="decimal"
              :aria-describedby="f.describedBy"
              :aria-invalid="f.invalid"
            />
          </template>
        </FormField>
        <FormField
          v-if="priceChanged"
          id="reason"
          label="Reason for the price change"
          hint="Required when the price differs from the proposal, e.g. regular customer discount"
          :error="errors.reason"
          required
        >
          <template #default="f">
            <input
              :id="f.id"
              v-model="form.reason"
              type="text"
              :aria-describedby="f.describedBy"
              :aria-invalid="f.invalid"
            />
          </template>
        </FormField>
      </template>
      <fieldset>
        <legend class="legend">Pickup window</legend>
        <div class="grid-2">
          <FormField id="pickup-start" label="Starts" :error="errors.pickupStart" required>
            <template #default="f">
              <input
                :id="f.id"
                v-model="form.pickupStart"
                type="datetime-local"
                :aria-describedby="f.describedBy"
                :aria-invalid="f.invalid"
              />
            </template>
          </FormField>
          <FormField id="pickup-end" label="Ends" :error="errors.pickupEnd" required>
            <template #default="f">
              <input
                :id="f.id"
                v-model="form.pickupEnd"
                type="datetime-local"
                :aria-describedby="f.describedBy"
                :aria-invalid="f.invalid"
              />
            </template>
          </FormField>
        </div>
      </fieldset>
      <fieldset>
        <legend class="legend">Delivery window</legend>
        <div class="grid-2">
          <FormField id="delivery-start" label="Starts" :error="errors.deliveryStart" required>
            <template #default="f">
              <input
                :id="f.id"
                v-model="form.deliveryStart"
                type="datetime-local"
                :aria-describedby="f.describedBy"
                :aria-invalid="f.invalid"
              />
            </template>
          </FormField>
          <FormField id="delivery-end" label="Ends" :error="errors.deliveryEnd" required>
            <template #default="f">
              <input
                :id="f.id"
                v-model="form.deliveryEnd"
                type="datetime-local"
                :aria-describedby="f.describedBy"
                :aria-invalid="f.invalid"
              />
            </template>
          </FormField>
        </div>
      </fieldset>
      <div class="row">
        <button type="submit" class="btn">
          {{ proposal.needsOwnerApproval ? 'Send to an owner for pricing' : 'Issue quote' }}
        </button>
        <button type="button" class="btn btn-secondary" @click="undo">Undo my changes</button>
        <button type="button" class="btn-link" @click="leave">Back to the queue without issuing</button>
      </div>
      <ConfirmBox
        v-if="confirming"
        :question="
          proposal.needsOwnerApproval
            ? 'Send this quote to an owner for pricing now?'
            : `Issue a quote of ${money(rules.parseMoney(form.price))} to ${detail.customerName} now?`
        "
        :busy="busy"
        @confirm="issue"
        @cancel="confirming = false"
      />
    </form>
  </div>
</template>

<style scoped>
.layout {
  align-items: start;
}

.parcel-line {
  display: block;
}

.breakdown-title {
  margin-top: 1rem;
}

.breakdown {
  margin: 0;
  padding-left: 1.2rem;
  color: var(--muted);
}

.previous {
  border-left: 5px solid var(--accent);
  background: #fffaf0;
  border-radius: 6px;
  padding: 0.75rem 1rem;
  margin-bottom: 1rem;
}

.previous h3 {
  margin: 0 0 0.35rem;
  font-size: 1rem;
}

.previous p {
  margin: 0.2rem 0;
}

.asked {
  color: var(--brand-dark);
}

.legend {
  font-weight: 700;
  margin-bottom: 0.4rem;
}
</style>
