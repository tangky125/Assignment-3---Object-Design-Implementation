<script setup lang="ts">
// Business area 4: deliveries allocated to the signed-in delivery staff member.
import { onMounted, ref } from 'vue'
import { jobApi } from '@/api/dms'
import { takeFlash, type Message } from '@/session/flash'
import { useAsync } from '@/utils/useAsync'
import MessageBanner from '@/components/MessageBanner.vue'
import PageHeader from '@/components/PageHeader.vue'
import RequestTable from '@/components/RequestTable.vue'

const jobs = useAsync(() => jobApi.list())
const message = ref<Message | null>(null)
onMounted(() => {
  message.value = takeFlash()
})
</script>

<template>
  <PageHeader
    title="My jobs"
    intro="Deliveries allocated to you that are not finished, earliest pickup first."
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
  <MessageBanner v-if="jobs.error.value" kind="error" :text="jobs.error.value" />
  <section class="card">
    <RequestTable
      v-if="jobs.data.value"
      :rows="jobs.data.value"
      :link-to="(r) => `/jobs/${r.reference}`"
      action-label="Open job"
      time-header="Pickup from"
      caption="My jobs"
      empty-text="You have no open jobs."
    />
  </section>
</template>
