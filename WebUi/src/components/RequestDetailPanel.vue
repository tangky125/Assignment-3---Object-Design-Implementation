<script setup lang="ts">
// Everything about one request, at the level of detail the viewer is allowed
// (customers do not see internal notes or which staff member did what).
import { computed } from 'vue'
import type { RequestDetail, ViewerLevel } from '@/api/types'
import StatusBadge from './StatusBadge.vue'
import { handlingText, isLate, money, statusText, when, windowText } from '@/utils/format'

const props = defineProps<{ detail: RequestDetail; level: ViewerLevel }>()
const staff = computed(() => props.level === 'Staff')
const quote = computed(() => props.detail.currentQuote)
</script>

<template>
  <div>
    <section class="card" aria-labelledby="summary-title">
      <div class="spread">
        <h2 id="summary-title">Request {{ detail.reference }}</h2>
        <StatusBadge :status="detail.status" :late="isLate(detail)" />
      </div>
      <dl class="facts">
        <dt>Customer</dt>
        <dd>
          {{ detail.customerName }}<template v-if="staff"> ({{ detail.customerPhone }})</template>
        </dd>
        <dt>Logged</dt>
        <dd>
          {{ when(detail.loggedAt) }}<template v-if="staff"> by {{ detail.loggedBy }}</template>
        </dd>
        <dt>Pickup</dt>
        <dd>{{ detail.pickup.street }}, {{ detail.pickup.suburb }} VIC {{ detail.pickup.postcode }}</dd>
        <dt>Pickup contact</dt>
        <dd>{{ detail.pickupContact.name }} ({{ detail.pickupContact.phone }})</dd>
        <dt>Parcels ready</dt>
        <dd>{{ when(detail.readyTime) }}</dd>
        <dt>Destination</dt>
        <dd>
          {{ detail.destination.street }}, {{ detail.destination.suburb }} VIC
          {{ detail.destination.postcode }}
        </dd>
        <dt>Recipient</dt>
        <dd>{{ detail.recipient.name }} ({{ detail.recipient.phone }})</dd>
        <template v-if="staff">
          <dt>Allocated to</dt>
          <dd>{{ detail.allocatedStaffName ?? 'Not allocated' }}</dd>
          <dt>Internal notes</dt>
          <dd>{{ detail.notes ?? '-' }}</dd>
        </template>
      </dl>
    </section>

    <section v-if="quote" class="card" aria-labelledby="quote-title">
      <h2 id="quote-title">Quote (version {{ quote.version }})</h2>
      <dl class="facts">
        <dt>Price</dt>
        <dd>
          <strong>{{
            quote.price === null ? 'Waiting for an owner to set the price' : money(quote.price)
          }}</strong>
          <span v-if="quote.price !== null" class="muted"> including GST, paid at pickup</span>
        </dd>
        <dt>Pickup window</dt>
        <dd>{{ windowText(quote.pickupWindow) }}</dd>
        <dt>Delivery window</dt>
        <dd>{{ windowText(quote.deliveryWindow) }}</dd>
        <template v-if="detail.status === 'Quoted'">
          <dt>Valid until</dt>
          <dd>
            {{ when(quote.validUntil) }}
            <span v-if="quote.isExpired" class="badge badge-problem">Expired</span>
          </dd>
        </template>
        <dt>Outcome</dt>
        <dd>{{ quote.outcome }}</dd>
      </dl>
    </section>

    <section class="card" aria-labelledby="parcels-title">
      <h2 id="parcels-title">Parcels ({{ detail.parcels.length }})</h2>
      <div class="table-wrap">
        <table>
          <thead>
            <tr>
              <th scope="col">#</th>
              <th scope="col">Size</th>
              <th scope="col">Handling</th>
              <th scope="col">Description</th>
              <th scope="col">Collected</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="(p, i) in detail.parcels" :key="p.parcelId">
              <td>{{ i + 1 }}</td>
              <td>{{ p.size }}</td>
              <td>{{ handlingText(p.flags) }}</td>
              <td>{{ p.description ?? '-' }}</td>
              <td>{{ p.collected ? 'Yes' : 'No' }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>

    <section v-if="detail.payments.length > 0" class="card" aria-labelledby="payments-title">
      <h2 id="payments-title">Payments</h2>
      <div class="table-wrap">
        <table>
          <thead>
            <tr>
              <th scope="col">Time</th>
              <th scope="col">Method</th>
              <th scope="col">Amount</th>
              <th scope="col">Outcome</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="(p, i) in detail.payments" :key="i">
              <td>{{ when(p.at) }}</td>
              <td>{{ p.method }}</td>
              <td>{{ money(p.amount) }}</td>
              <td>
                <span :class="['badge', p.successful ? 'badge-done' : 'badge-problem']">{{ p.outcome }}</span>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>

    <section class="card" aria-labelledby="history-title">
      <h2 id="history-title">History</h2>
      <div class="table-wrap">
        <table>
          <thead>
            <tr>
              <th scope="col">When</th>
              <th scope="col">Status</th>
              <th v-if="staff" scope="col">By</th>
              <th scope="col">What happened</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="(e, i) in detail.history" :key="i" data-testid="history-row">
              <td>{{ when(e.at) }}</td>
              <td>{{ statusText(e.to) }}</td>
              <td v-if="staff">{{ e.actor }}</td>
              <td>{{ e.note }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>
  </div>
</template>
