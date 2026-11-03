<script setup lang="ts">
// Business area 2 (owner): out-of-area quotes are held until an owner sets the price and releases
// it to the customer (A1 Task 2 variant 2a).
import { ref } from 'vue'
import { quoteApi, trackingApi } from '@/api/dms'
import type { RequestDetail } from '@/api/types'
import type { Message } from '@/session/flash'
import * as rules from '@/validation/rules'
import { money, when, windowText } from '@/utils/format'
import { errorText, useAsync } from '@/utils/useAsync'
import ConfirmBox from '@/components/ConfirmBox.vue'
import FormField from '@/components/FormField.vue'
import MessageBanner from '@/components/MessageBanner.vue'
import PageHeader from '@/components/PageHeader.vue'

const queue = useAsync(() => quoteApi.ownerQueue())
const selected = ref<RequestDetail | null>(null)
const price = ref('')
const priceError = ref<string | null>(null)
const confirming = ref(false)
const busy = ref(false)
const message = ref<Message | null>(null)

async function open(reference: string): Promise<void> {
  message.value = null
  price.value = ''
  priceError.value = null
  confirming.value = false
  try {
    selected.value = (await trackingApi.find(reference)).detail
  } catch (e) {
    message.value = { kind: 'error', text: errorText(e) }
  }
}

function review(): void {
  priceError.value = rules.money(1, 10_000)(price.value)
  confirming.value = priceError.value === null
}

function cancel(): void {
  message.value = { kind: 'notice', text: `The quote for ${selected.value?.reference} was not released.` }
  confirming.value = false
  selected.value = null
}

async function release(): Promise<void> {
  if (!selected.value) return
  busy.value = true
  try {
    const result = await quoteApi.release(selected.value.reference, rules.parseMoney(price.value) ?? 0)
    message.value = { kind: result.succeeded ? 'success' : 'error', text: result.message }
    if (result.succeeded) selected.value = null
    confirming.value = false
    await queue.reload()
  } catch (e) {
    message.value = { kind: 'error', text: errorText(e) }
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <PageHeader
    title="Price out-of-area quotes"
    intro="These quotes are held until an owner sets the price. Releasing one sends it to the customer."
    back-to="/dashboard"
    back-label="Main menu"
  />
  <MessageBanner
    v-if="message"
    :kind="message.kind"
    :text="message.text"
    dismissible
    @dismiss="message = null"
  />
  <MessageBanner v-if="queue.error.value" kind="error" :text="queue.error.value" />
  <section class="card">
    <div class="table-wrap">
      <table>
        <thead>
          <tr>
            <th scope="col">Reference</th>
            <th scope="col">Customer</th>
            <th scope="col">Route</th>
            <th scope="col">Note</th>
            <th scope="col"><span class="visually-hidden">Action</span></th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="r in queue.data.value ?? []" :key="r.reference">
            <td>
              <strong>{{ r.reference }}</strong>
            </td>
            <td>{{ r.customerName }}</td>
            <td>{{ r.pickupSuburb }} &rarr; {{ r.destinationSuburb }}</td>
            <td class="muted">{{ r.note }}</td>
            <td>
              <button type="button" class="btn btn-secondary" @click="open(r.reference)">Set price</button>
            </td>
          </tr>
          <tr v-if="queue.data.value && queue.data.value.length === 0">
            <td colspan="5" class="muted">No quotes are waiting for owner approval.</td>
          </tr>
        </tbody>
      </table>
    </div>
  </section>

  <form v-if="selected" class="card" novalidate @submit.prevent="review">
    <h2>Price {{ selected.reference }}</h2>
    <dl class="facts">
      <dt>Customer</dt>
      <dd>{{ selected.customerName }}</dd>
      <dt>From</dt>
      <dd>{{ selected.pickup.street }}, {{ selected.pickup.suburb }} {{ selected.pickup.postcode }}</dd>
      <dt>To</dt>
      <dd>
        {{ selected.destination.street }}, {{ selected.destination.suburb }}
        {{ selected.destination.postcode }}
      </dd>
      <dt>Parcels</dt>
      <dd>{{ selected.parcels.map((p) => p.size).join(', ') }}</dd>
      <dt>Ready</dt>
      <dd>{{ when(selected.readyTime) }}</dd>
      <template v-if="selected.currentQuote">
        <dt>Pickup window</dt>
        <dd>{{ windowText(selected.currentQuote.pickupWindow) }}</dd>
        <dt>Delivery window</dt>
        <dd>{{ windowText(selected.currentQuote.deliveryWindow) }}</dd>
      </template>
    </dl>
    <FormField
      id="owner-price"
      label="Price for this out-of-area delivery"
      hint="Dollars, $1.00 to $10,000.00"
      :error="priceError"
      required
    >
      <template #default="f">
        <input
          :id="f.id"
          v-model="price"
          type="text"
          inputmode="decimal"
          :aria-describedby="f.describedBy"
          :aria-invalid="f.invalid"
        />
      </template>
    </FormField>
    <div class="row">
      <button type="submit" class="btn">Release quote</button>
      <button type="button" class="btn btn-secondary" @click="cancel">Not now</button>
    </div>
    <ConfirmBox
      v-if="confirming"
      :question="`Release the quote to ${selected.customerName} at ${money(rules.parseMoney(price))}?`"
      :busy="busy"
      @confirm="release"
      @cancel="confirming = false"
    />
  </form>
</template>
