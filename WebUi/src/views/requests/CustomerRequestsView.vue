<script setup lang="ts">
// A customer's requests, newest first. Customers see their own; office staff see the customer
// they searched for.
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { quoteApi, requestApi } from '@/api/dms'
import { session } from '@/session/session'
import { useAsync } from '@/utils/useAsync'
import MessageBanner from '@/components/MessageBanner.vue'
import PageHeader from '@/components/PageHeader.vue'
import RequestTable from '@/components/RequestTable.vue'

const route = useRoute()
const customerId = computed(() =>
  typeof route.params.customerId === 'string' ? route.params.customerId : (session.user?.customerId ?? ''),
)
const own = computed(() => route.params.customerId === undefined)

const customer = useAsync(() => requestApi.getCustomer(customerId.value))
const requests = useAsync(() => quoteApi.requestsFor(customerId.value))
</script>

<template>
  <PageHeader
    :title="own ? 'My requests and quotes' : `Requests for ${customer.data.value?.name ?? 'customer'}`"
    :intro="
      own
        ? 'Open a request to accept, reject or revise its quote, cancel it, or see the receipt.'
        : 'Open the request the customer is deciding on.'
    "
    :back-to="own ? '/dashboard' : '/decisions'"
    :back-label="own ? 'Main menu' : 'Find another customer'"
  />
  <MessageBanner v-if="requests.error.value" kind="error" :text="requests.error.value" />
  <section class="card">
    <p v-if="requests.loading.value" class="muted">Loading...</p>
    <RequestTable
      v-else-if="requests.data.value"
      :rows="requests.data.value"
      :link-to="(r) => `/requests/${r.reference}`"
      caption="Requests, newest first"
      empty-text="There are no requests yet."
    />
  </section>
</template>
