<script setup lang="ts">
import { computed } from 'vue'
import type { Status } from '@/api/types'
import { statusText } from '@/utils/format'

const props = defineProps<{ status: Status; late?: boolean }>()

// Blue waits on the office, orange waits on the customer, green is agreed, teal is in progress,
// solid green with a tick is finished, red is a problem and grey is closed. The text always
// names the status, so colour is never the only signal.
const style = computed(() => {
  switch (props.status) {
    case 'Requested':
      return 'badge-open'
    case 'Quoted':
      return 'badge-customer'
    case 'Accepted':
      return 'badge-agreed'
    case 'Scheduled':
    case 'Collected':
      return 'badge-active'
    case 'Delivered':
      return 'badge-finished'
    case 'DeliveryFailed':
      return 'badge-problem'
    default:
      return 'badge-closed'
  }
})
</script>

<template>
  <span class="row" style="gap: 0.3rem; display: inline-flex">
    <span :class="['badge', style]"
      ><span v-if="status === 'Delivered'" aria-hidden="true">&#10003; </span>{{ statusText(status) }}</span
    >
    <span v-if="late" class="badge badge-late">Running late</span>
  </span>
</template>
