<script setup lang="ts">
// Track a delivery with its reference (A1 Task 6). Anyone holding a reference sees the status and
// expected delivery window only; signed-in customers and staff see what they are allowed to see.
import { ref, watch } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import { trackingApi } from '@/api/dms'
import type { TrackingResponse } from '@/api/types'
import { hasRole } from '@/session/session'
import * as rules from '@/validation/rules'
import { statusText, windowText } from '@/utils/format'
import { errorText } from '@/utils/useAsync'
import FormField from '@/components/FormField.vue'
import MessageBanner from '@/components/MessageBanner.vue'
import PageHeader from '@/components/PageHeader.vue'
import RequestDetailPanel from '@/components/RequestDetailPanel.vue'
import StatusBadge from '@/components/StatusBadge.vue'

const route = useRoute()
const router = useRouter()
const reference = ref(typeof route.params.reference === 'string' ? route.params.reference : '')
const referenceError = ref<string | null>(null)
const notFound = ref<string | null>(null)
const result = ref<TrackingResponse | null>(null)
const busy = ref(false)

async function look(): Promise<void> {
  notFound.value = null
  result.value = null
  referenceError.value = rules.reference(reference.value)
  if (referenceError.value) return
  const id = reference.value.trim().toUpperCase()
  busy.value = true
  try {
    result.value = await trackingApi.find(id)
    if (route.params.reference !== id) await router.replace({ name: 'track', params: { reference: id } })
  } catch (e) {
    notFound.value = errorText(e)
  } finally {
    busy.value = false
  }
}

watch(
  () => route.params.reference,
  (value) => {
    if (
      typeof value === 'string' &&
      value !== '' &&
      result.value?.detail?.reference !== value &&
      result.value?.recipient?.reference !== value
    ) {
      reference.value = value
      void look()
    }
  },
  { immediate: true },
)
</script>

<template>
  <PageHeader
    title="Track a delivery"
    intro="Enter the reference number you were given to see where the delivery is."
  />
  <form class="card track-form" novalidate @submit.prevent="look">
    <FormField
      id="reference"
      label="Reference number"
      hint="Looks like DMS-2026-0001"
      :error="referenceError"
      required
    >
      <template #default="f">
        <input
          :id="f.id"
          v-model="reference"
          type="text"
          autocapitalize="characters"
          :aria-describedby="f.describedBy"
          :aria-invalid="f.invalid"
        />
      </template>
    </FormField>
    <button type="submit" class="btn" :disabled="busy">Track</button>
    <p v-if="hasRole('Customer')" class="muted small gap">
      Or see all of <RouterLink to="/my/requests">your requests</RouterLink>.
    </p>
  </form>

  <MessageBanner v-if="notFound" kind="error" :text="notFound" />

  <section v-if="result?.recipient" class="card recipient" aria-live="polite">
    <div class="spread">
      <h2>Delivery {{ result.recipient.reference }}</h2>
      <StatusBadge :status="result.recipient.status" :late="result.recipient.late" />
    </div>
    <dl class="facts">
      <dt>Status</dt>
      <dd>{{ statusText(result.recipient.status) }}</dd>
      <dt>Expected delivery</dt>
      <dd>{{ windowText(result.recipient.expectedDelivery) }}</dd>
      <dt>Destination suburb</dt>
      <dd>{{ result.recipient.destinationSuburb }}</dd>
    </dl>
    <p class="muted small gap">Prices and customer details are not shown to reference holders.</p>
  </section>

  <RequestDetailPanel v-if="result?.detail" :detail="result.detail" :level="result.level" />
</template>

<style scoped>
.track-form {
  max-width: 520px;
}

.recipient {
  max-width: 620px;
  border-top: 6px solid var(--brand);
}

.gap {
  margin-top: 0.75rem;
  margin-bottom: 0;
}
</style>
