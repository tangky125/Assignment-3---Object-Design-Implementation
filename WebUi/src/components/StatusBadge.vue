<script setup lang="ts">
import { computed } from 'vue'
import type { Status } from '@/api/types'
import { statusText } from '@/utils/format'

const props = defineProps<{ status: Status; late?: boolean }>()

const style = computed(() => {
  switch (props.status) {
    case 'Requested':
    case 'Quoted':
      return 'badge-open'
    case 'Accepted':
    case 'Scheduled':
    case 'Collected':
      return 'badge-active'
    case 'Delivered':
      return 'badge-done'
    case 'DeliveryFailed':
      return 'badge-problem'
    default:
      return 'badge-closed'
  }
})
</script>

<template>
  <span class="row" style="gap: 0.3rem; display: inline-flex">
    <span :class="['badge', style]">{{ statusText(status) }}</span>
    <span v-if="late" class="badge badge-late">Running late</span>
  </span>
</template>
