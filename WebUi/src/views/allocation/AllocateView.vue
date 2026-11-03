<script setup lang="ts">
// Business area 3: choose a delivery staff member. Everyone is listed with whether they are free
// for both windows and, if not, why. The server checks again at the moment of saving.
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { allocationApi, trackingApi } from '@/api/dms'
import type { StaffOption } from '@/api/types'
import { flash } from '@/session/flash'
import * as rules from '@/validation/rules'
import { handlingText, windowText } from '@/utils/format'
import { errorText, useAsync } from '@/utils/useAsync'
import ConfirmBox from '@/components/ConfirmBox.vue'
import FormField from '@/components/FormField.vue'
import MessageBanner from '@/components/MessageBanner.vue'
import PageHeader from '@/components/PageHeader.vue'
import StatusBadge from '@/components/StatusBadge.vue'

const route = useRoute()
const router = useRouter()
const reference = String(route.params.reference).toUpperCase()
const replacing = computed(() => route.query.mode === 'replace')

const tracking = useAsync(() => trackingApi.find(reference))
const options = useAsync(() => allocationApi.staffOptions(reference))
const detail = computed(() => tracking.data.value?.detail ?? null)
const anyFree = computed(() => (options.data.value ?? []).some((o) => o.eligible))

const chosenId = ref<string>('')
const choiceError = ref<string | null>(null)
const reason = ref('')
const reasonError = ref<string | null>(null)
const confirming = ref(false)
const busy = ref(false)
const problem = ref<string | null>(null)
const chosen = computed<StaffOption | null>(
  () => options.data.value?.find((o) => o.staffId === chosenId.value) ?? null,
)

function review(): void {
  problem.value = null
  choiceError.value = null
  reasonError.value = null
  if (!chosen.value) {
    choiceError.value = 'Choose a staff member from the list.'
  } else if (!chosen.value.eligible) {
    choiceError.value = `${chosen.value.name} cannot take this delivery: ${chosen.value.reason}. Choose someone marked "Free".`
  }
  if (replacing.value) reasonError.value = rules.reason(reason.value)
  confirming.value = choiceError.value === null && reasonError.value === null
}

async function save(): Promise<void> {
  if (!chosen.value) return
  busy.value = true
  try {
    const result = replacing.value
      ? await allocationApi.replace(reference, chosen.value.staffId, reason.value.trim())
      : await allocationApi.allocate(reference, chosen.value.staffId)
    confirming.value = false
    if (result.succeeded) {
      flash('success', result.message)
      await router.push('/allocation')
    } else {
      problem.value = result.message
      await options.reload()
    }
  } catch (e) {
    problem.value = errorText(e)
  } finally {
    busy.value = false
  }
}

async function leave(): Promise<void> {
  flash(
    'notice',
    replacing.value ? `${reference} keeps its current staff member.` : `${reference} was not allocated.`,
  )
  await router.push('/allocation')
}
</script>

<template>
  <PageHeader
    :title="replacing ? `Replace the staff member on ${reference}` : `Allocate ${reference}`"
    back-to="/allocation"
    back-label="Back to allocation"
  />
  <MessageBanner v-if="tracking.error.value" kind="error" :text="tracking.error.value" />
  <MessageBanner v-if="problem" kind="error" :text="problem" />

  <div v-if="detail" class="grid-2 layout">
    <section class="card" aria-labelledby="delivery-title">
      <div class="spread">
        <h2 id="delivery-title">Delivery</h2>
        <StatusBadge :status="detail.status" />
      </div>
      <p v-if="detail.status === 'DeliveryFailed'" class="message message-notice">
        <span class="label">Note:</span> the last attempt failed. This delivery needs a staff member again.
      </p>
      <dl class="facts">
        <dt>Customer</dt>
        <dd>{{ detail.customerName }}</dd>
        <dt>Route</dt>
        <dd>
          {{ detail.pickup.suburb }} {{ detail.pickup.postcode }} &rarr; {{ detail.destination.suburb }}
          {{ detail.destination.postcode }}
        </dd>
        <dt>Pickup window</dt>
        <dd>{{ windowText(detail.currentQuote?.pickupWindow) }}</dd>
        <dt>Delivery window</dt>
        <dd>{{ windowText(detail.currentQuote?.deliveryWindow) }}</dd>
        <dt>Parcels</dt>
        <dd>
          <span v-for="p in detail.parcels" :key="p.parcelId" class="block"
            >{{ p.size }} ({{ handlingText(p.flags) }})</span
          >
        </dd>
        <template v-if="replacing">
          <dt>Currently allocated</dt>
          <dd>
            <strong>{{ detail.allocatedStaffName }}</strong>
          </dd>
        </template>
      </dl>
    </section>

    <form class="card" novalidate aria-labelledby="staff-title" @submit.prevent="review">
      <h2 id="staff-title">Delivery staff</h2>
      <p v-if="options.data.value && !anyFree" class="message message-notice">
        <span class="label">Note:</span> no delivery staff member is free for these windows, so the delivery
        stays unallocated. Agree a later window with the customer, or try again when availability changes.
      </p>
      <fieldset :aria-invalid="choiceError ? 'true' : undefined" aria-describedby="staff-error">
        <legend class="visually-hidden">Choose a staff member</legend>
        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th scope="col">Choose</th>
                <th scope="col">Name</th>
                <th scope="col">Free?</th>
                <th scope="col">Details</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="o in options.data.value ?? []"
                :key="o.staffId"
                :class="{ busy: !o.eligible }"
                data-testid="staff-row"
              >
                <td>
                  <input
                    :id="`staff-${o.staffId}`"
                    v-model="chosenId"
                    type="radio"
                    name="staff"
                    :value="o.staffId"
                  />
                </td>
                <td>
                  <label :for="`staff-${o.staffId}`">{{ o.name }}</label>
                </td>
                <td>
                  <span :class="['badge', o.eligible ? 'badge-done' : 'badge-problem']">{{
                    o.eligible ? 'Free' : 'Not free'
                  }}</span>
                </td>
                <td>{{ o.reason }}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </fieldset>
      <p v-if="choiceError" id="staff-error" class="error-text" role="alert">{{ choiceError }}</p>
      <FormField
        v-if="replacing"
        id="replace-reason"
        label="Reason for the change"
        hint="e.g. Ben is unwell today"
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
      <div class="row actions">
        <button type="submit" class="btn">{{ replacing ? 'Replace staff member' : 'Allocate' }}</button>
        <button type="button" class="btn-link" @click="leave">Back without changing anything</button>
      </div>
      <ConfirmBox
        v-if="confirming && chosen"
        :question="
          replacing
            ? `Replace ${detail.allocatedStaffName} with ${chosen.name} on ${reference}?`
            : `Allocate ${reference} to ${chosen.name}?`
        "
        :busy="busy"
        @confirm="save"
        @cancel="confirming = false"
      />
    </form>
  </div>
</template>

<style scoped>
.layout {
  align-items: start;
}

.block {
  display: block;
}

tr.busy td {
  color: var(--muted);
}

label {
  white-space: nowrap;
}

.error-text {
  color: var(--error);
  font-weight: 600;
}

.actions {
  margin-top: 1rem;
}
</style>
