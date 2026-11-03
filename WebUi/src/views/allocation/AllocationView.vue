<script setup lang="ts">
// Business area 3: deliveries waiting for a staff member, and scheduled deliveries whose staff
// member can be replaced (A1 Task 4, variant 4a).
import { onMounted, ref } from 'vue'
import { allocationApi } from '@/api/dms'
import { takeFlash, type Message } from '@/session/flash'
import { useAsync } from '@/utils/useAsync'
import MessageBanner from '@/components/MessageBanner.vue'
import PageHeader from '@/components/PageHeader.vue'
import RequestTable from '@/components/RequestTable.vue'

const waiting = useAsync(() => allocationApi.waiting())
const scheduled = useAsync(() => allocationApi.scheduled())
const message = ref<Message | null>(null)
onMounted(() => {
  message.value = takeFlash()
})
</script>

<template>
  <PageHeader
    title="Allocate delivery staff"
    intro="Give each accepted delivery to a delivery staff member who is free for its windows."
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
  <MessageBanner v-if="waiting.error.value" kind="error" :text="waiting.error.value" />

  <section class="card" aria-labelledby="waiting-title">
    <h2 id="waiting-title">Waiting for a staff member ({{ waiting.data.value?.length ?? 0 }})</h2>
    <RequestTable
      v-if="waiting.data.value"
      :rows="waiting.data.value"
      :link-to="(r) => `/allocation/${r.reference}`"
      action-label="Allocate"
      time-header="Pickup from"
      caption="Deliveries waiting for a staff member"
      empty-text="No deliveries are waiting for allocation."
    />
  </section>

  <section class="card" aria-labelledby="scheduled-title">
    <h2 id="scheduled-title">Scheduled ({{ scheduled.data.value?.length ?? 0 }})</h2>
    <p class="muted">If the allocated person becomes unavailable, replace them with someone who is free.</p>
    <RequestTable
      v-if="scheduled.data.value"
      :rows="scheduled.data.value"
      :link-to="(r) => ({ path: `/allocation/${r.reference}`, query: { mode: 'replace' } })"
      action-label="Replace staff"
      time-header="Pickup from"
      caption="Scheduled deliveries"
      empty-text="No deliveries are scheduled."
    />
  </section>
</template>
