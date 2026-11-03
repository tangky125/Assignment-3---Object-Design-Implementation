<script setup lang="ts">
// Asks "are you sure?" inside the page before anything is saved, so the user can change their mind.
defineProps<{ question: string; confirmLabel?: string; danger?: boolean; busy?: boolean }>()
const emit = defineEmits<{ confirm: []; cancel: [] }>()
</script>

<template>
  <div class="confirm" role="group" aria-label="Confirm">
    <p>
      <strong>{{ question }}</strong>
    </p>
    <slot />
    <div class="row">
      <button
        type="button"
        :class="['btn', danger ? 'btn-danger' : '']"
        :disabled="busy"
        @click="emit('confirm')"
      >
        {{ confirmLabel ?? 'Yes, go ahead' }}
      </button>
      <button type="button" class="btn btn-secondary" :disabled="busy" @click="emit('cancel')">
        No, go back
      </button>
    </div>
  </div>
</template>
