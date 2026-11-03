<script setup lang="ts">
// Shows a success, error or notice message. Each kind has a text label as well as a colour,
// and errors are announced to screen readers immediately (role="alert").
import { computed } from 'vue'
import type { MessageKind } from '@/session/flash'

const props = defineProps<{ kind: MessageKind; text: string; dismissible?: boolean }>()
const emit = defineEmits<{ dismiss: [] }>()

const label = computed(() => ({ success: 'Done:', error: 'Error:', notice: 'Note:' })[props.kind])
</script>

<template>
  <div
    :class="['message', `message-${kind}`]"
    :role="kind === 'error' ? 'alert' : 'status'"
    data-testid="message"
  >
    <span class="label">{{ label }}</span>
    <span>{{ text }}</span>
    <button
      v-if="dismissible"
      type="button"
      class="close"
      aria-label="Dismiss message"
      @click="emit('dismiss')"
    >
      &times;
    </button>
  </div>
</template>
