<script setup lang="ts">
// Office staff find the customer who phoned or emailed, then record their decision on a quote
// (A1 Task 3 variant 2c).
import { useRouter } from 'vue-router'
import type { CustomerSummary } from '@/api/types'
import CustomerSearch from '@/components/CustomerSearch.vue'
import PageHeader from '@/components/PageHeader.vue'

const router = useRouter()

async function open(customer: CustomerSummary): Promise<void> {
  await router.push({ name: 'customer-requests', params: { customerId: customer.customerId } })
}
</script>

<template>
  <PageHeader
    title="Record a customer's quote decision"
    intro="Find the customer who called or emailed, then open the request they are deciding on."
    back-to="/dashboard"
    back-label="Main menu"
  />
  <section class="card">
    <CustomerSearch :allow-register="false" @chosen="open" />
  </section>
</template>
