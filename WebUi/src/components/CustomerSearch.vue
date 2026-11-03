<script setup lang="ts">
// Finds a customer by phone and/or name, offering close matches before a new record is created,
// so returning customers are not entered twice (A1 Task 1 subtask 1).
import { reactive, ref } from 'vue'
import { requestApi } from '@/api/dms'
import type { CustomerSummary } from '@/api/types'
import * as rules from '@/validation/rules'
import { errorText } from '@/utils/useAsync'
import FormField from './FormField.vue'
import MessageBanner from './MessageBanner.vue'

const props = defineProps<{ allowRegister: boolean }>()
const emit = defineEmits<{ chosen: [customer: CustomerSummary] }>()

const search = reactive({ phone: '', name: '' })
const searchErrors = ref<{ phone?: string; form?: string }>({})
const results = ref<CustomerSummary[] | null>(null)
const busy = ref(false)

const registering = ref(false)
const newCustomer = reactive({ name: '', phone: '', email: '' })
const newErrors = ref<Partial<Record<'name' | 'phone' | 'email', string>>>({})
const serverError = ref<string | null>(null)

async function runSearch(): Promise<void> {
  searchErrors.value = {}
  serverError.value = null
  if (search.phone.trim() === '' && search.name.trim() === '') {
    searchErrors.value = { form: 'Enter a phone number or a name to search.' }
    return
  }
  if (search.phone.trim() !== '' && rules.phone(search.phone) !== null) {
    searchErrors.value = { phone: rules.phone(search.phone)! }
    return
  }
  busy.value = true
  try {
    results.value = await requestApi.findCustomers(search.phone, search.name)
  } catch (e) {
    serverError.value = errorText(e)
  } finally {
    busy.value = false
  }
}

function startRegister(): void {
  registering.value = true
  newCustomer.phone = search.phone
  newCustomer.name = ''
  newCustomer.email = ''
  newErrors.value = {}
}

async function register(): Promise<void> {
  newErrors.value = rules.check({
    name: [newCustomer.name, rules.name],
    phone: [newCustomer.phone, rules.phone],
    email: [newCustomer.email, rules.optionalEmail],
  })
  if (Object.keys(newErrors.value).length > 0) return
  busy.value = true
  serverError.value = null
  try {
    const result = await requestApi.registerCustomer(
      newCustomer.name.trim(),
      newCustomer.phone,
      newCustomer.email.trim(),
    )
    if (result.succeeded && result.value) {
      emit('chosen', result.value)
    } else {
      serverError.value = result.message
    }
  } catch (e) {
    serverError.value = errorText(e)
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div>
    <MessageBanner v-if="serverError" kind="error" :text="serverError" />
    <form v-if="!registering" novalidate @submit.prevent="runSearch">
      <p class="muted">Search by the customer's phone number, name, or both. Close spellings are matched.</p>
      <MessageBanner v-if="searchErrors.form" kind="error" :text="searchErrors.form" />
      <div class="grid-2">
        <FormField
          id="search-phone"
          label="Customer phone"
          hint="e.g. 0412 345 678"
          :error="searchErrors.phone"
        >
          <template #default="f">
            <input
              :id="f.id"
              v-model="search.phone"
              type="tel"
              :aria-describedby="f.describedBy"
              :aria-invalid="f.invalid"
            />
          </template>
        </FormField>
        <FormField id="search-name" label="Customer name or part of it" hint="e.g. Citizen">
          <template #default="f">
            <input :id="f.id" v-model="search.name" type="text" :aria-describedby="f.describedBy" />
          </template>
        </FormField>
      </div>
      <button type="submit" class="btn" :disabled="busy">Search customers</button>
    </form>

    <div v-if="results !== null && !registering" class="results">
      <h3>{{ results.length === 0 ? 'No held customer matches that search.' : 'Matching customers' }}</h3>
      <ul class="customer-list">
        <li v-for="c in results" :key="c.customerId" class="spread">
          <span>
            <strong>{{ c.name }}</strong> &middot; {{ c.phone }}
            <span v-if="c.email" class="muted"> &middot; {{ c.email }}</span>
          </span>
          <button type="button" class="btn btn-secondary" @click="emit('chosen', c)">Use {{ c.name }}</button>
        </li>
      </ul>
      <button v-if="props.allowRegister" type="button" class="btn btn-secondary" @click="startRegister">
        None of these: register a new customer
      </button>
    </div>

    <form v-if="registering" novalidate @submit.prevent="register">
      <h3>New customer</h3>
      <FormField
        id="new-name"
        label="Customer name"
        hint="Person or business, 2 to 60 characters"
        :error="newErrors.name"
        required
      >
        <template #default="f">
          <input
            :id="f.id"
            v-model="newCustomer.name"
            type="text"
            :aria-describedby="f.describedBy"
            :aria-invalid="f.invalid"
          />
        </template>
      </FormField>
      <div class="grid-2">
        <FormField
          id="new-phone"
          label="Customer phone"
          hint="e.g. 0412 345 678"
          :error="newErrors.phone"
          required
        >
          <template #default="f">
            <input
              :id="f.id"
              v-model="newCustomer.phone"
              type="tel"
              :aria-describedby="f.describedBy"
              :aria-invalid="f.invalid"
            />
          </template>
        </FormField>
        <FormField
          id="new-email"
          label="Customer email"
          hint="e.g. jane@example.com"
          :error="newErrors.email"
        >
          <template #default="f">
            <input
              :id="f.id"
              v-model="newCustomer.email"
              type="email"
              :aria-describedby="f.describedBy"
              :aria-invalid="f.invalid"
            />
          </template>
        </FormField>
      </div>
      <div class="row">
        <button type="submit" class="btn" :disabled="busy">Register customer</button>
        <button type="button" class="btn btn-secondary" @click="registering = false">Back to search</button>
      </div>
    </form>
  </div>
</template>

<style scoped>
.results {
  margin-top: 1.25rem;
}

.customer-list {
  list-style: none;
  padding: 0;
  margin: 0 0 1rem;
}

.customer-list li {
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 0.6rem 0.8rem;
  margin-bottom: 0.5rem;
}
</style>
