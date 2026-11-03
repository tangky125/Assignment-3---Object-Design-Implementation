<script setup lang="ts">
// Business area 2 (office): requests waiting for a quote, oldest first (A1 Task 2 subtask 1).
import { onMounted, ref } from 'vue'
import { quoteApi } from '@/api/dms'
import { takeFlash, type Message } from '@/session/flash'
import { useAsync } from '@/utils/useAsync'
import MessageBanner from '@/components/MessageBanner.vue'
import PageHeader from '@/components/PageHeader.vue'
import RequestTable from '@/components/RequestTable.vue'

const queue = useAsync(() => quoteApi.queue())
const message = ref<Message | null>(null)
onMounted(() => {
  message.value = takeFlash()
})
</script>

<template>
  <PageHeader
    title="Prepare quotes"
    intro="Requests waiting for a quote, oldest first, so no request is left sitting in the queue."
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
    <p v-if="queue.loading.value" class="muted">Loading...</p>
    <RequestTable
      v-else-if="queue.data.value"
      :rows="queue.data.value"
      :link-to="(r) => `/quotes/${r.reference}`"
      action-label="Prepare quote"
      caption="Requests waiting for a quote"
      empty-text="No requests are waiting for a quote."
    />
  </section>
</template>
