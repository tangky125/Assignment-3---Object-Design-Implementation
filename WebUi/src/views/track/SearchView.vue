<script setup lang="ts">
// Staff tracking (A1 Task 6): find requests by reference, customer phone, name or date logged
// (subtask 1), and review every open delivery with how long it has been open (variant 3a).
import { reactive, ref } from 'vue'
import { trackingApi } from '@/api/dms'
import type { RequestSummary } from '@/api/types'
import * as rules from '@/validation/rules'
import { errorText } from '@/utils/useAsync'
import FormField from '@/components/FormField.vue'
import MessageBanner from '@/components/MessageBanner.vue'
import PageHeader from '@/components/PageHeader.vue'
import RequestTable from '@/components/RequestTable.vue'

const criteria = reactive({ reference: '', phone: '', name: '', date: '' })
const errors = ref<Partial<Record<'reference' | 'phone' | 'name', string>>>({})
const problem = ref<string | null>(null)
const results = ref<RequestSummary[] | null>(null)
const title = ref('')
const busy = ref(false)

async function search(): Promise<void> {
  problem.value = null
  const found: Partial<Record<'reference' | 'phone' | 'name', string>> = {}
  if (criteria.reference.trim() !== '') {
    const e = rules.reference(criteria.reference)
    if (e) found.reference = e
  }
  if (criteria.phone.trim() !== '') {
    const e = rules.phone(criteria.phone)
    if (e) found.phone = e
  }
  if (criteria.name.trim() !== '' && criteria.name.trim().length < 2)
    found.name = 'Type at least 2 characters of the name.'
  errors.value = found
  if (Object.keys(found).length > 0) return
  if (Object.values(criteria).every((v) => v.trim() === '')) {
    problem.value = 'Fill in at least one box to search, or list all open deliveries.'
    return
  }
  await load('Search results', () => trackingApi.search({ ...criteria }))
}

async function openDeliveries(): Promise<void> {
  await load('All open deliveries (oldest first)', () => trackingApi.open())
}

async function load(heading: string, call: () => Promise<RequestSummary[]>): Promise<void> {
  busy.value = true
  try {
    results.value = await call()
    title.value = heading
  } catch (e) {
    problem.value = errorText(e)
  } finally {
    busy.value = false
  }
}

function clear(): void {
  Object.assign(criteria, { reference: '', phone: '', name: '', date: '' })
  errors.value = {}
  results.value = null
  problem.value = null
}
</script>

<template>
  <PageHeader
    title="Track or search deliveries"
    intro="Fill in any of the boxes. A customer who has lost their reference can still be found by phone, name or date."
    back-to="/dashboard"
    back-label="Main menu"
  />
  <MessageBanner v-if="problem" kind="error" :text="problem" />
  <form class="card" novalidate @submit.prevent="search">
    <div class="grid-2">
      <FormField id="s-reference" label="Reference" hint="e.g. DMS-2026-0001" :error="errors.reference">
        <template #default="f">
          <input
            :id="f.id"
            v-model="criteria.reference"
            type="text"
            :aria-describedby="f.describedBy"
            :aria-invalid="f.invalid"
          />
        </template>
      </FormField>
      <FormField
        id="s-phone"
        label="Customer or recipient phone"
        hint="e.g. 0412 345 678"
        :error="errors.phone"
      >
        <template #default="f">
          <input
            :id="f.id"
            v-model="criteria.phone"
            type="tel"
            :aria-describedby="f.describedBy"
            :aria-invalid="f.invalid"
          />
        </template>
      </FormField>
      <FormField id="s-name" label="Customer name" hint="Part of the name is enough" :error="errors.name">
        <template #default="f">
          <input
            :id="f.id"
            v-model="criteria.name"
            type="text"
            :aria-describedby="f.describedBy"
            :aria-invalid="f.invalid"
          />
        </template>
      </FormField>
      <FormField id="s-date" label="Date logged" hint="Choose a date">
        <template #default="f">
          <input :id="f.id" v-model="criteria.date" type="date" :aria-describedby="f.describedBy" />
        </template>
      </FormField>
    </div>
    <div class="row">
      <button type="submit" class="btn" :disabled="busy">Search</button>
      <button type="button" class="btn btn-secondary" :disabled="busy" @click="openDeliveries">
        List all open deliveries
      </button>
      <button type="button" class="btn-link" @click="clear">Clear</button>
    </div>
  </form>

  <section v-if="results" class="card" aria-live="polite">
    <h2>{{ title }} ({{ results.length }})</h2>
    <RequestTable
      :rows="results"
      :link-to="(r) => `/requests/${r.reference}`"
      action-label="View"
      :caption="title"
      empty-text="Nothing matches. Check the details and try again."
    />
  </section>
</template>
