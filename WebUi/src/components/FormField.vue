<script setup lang="ts">
// A labelled form field: label, what the field needs (hint), and the error when the value is wrong.
// The slot receives the ids to put on the input so screen readers read the hint and the error.
import { computed } from 'vue'

const props = defineProps<{
  id: string
  label: string
  hint?: string
  error?: string | null
  required?: boolean
}>()

const describedBy = computed(() =>
  [props.hint ? `${props.id}-hint` : '', props.error ? `${props.id}-error` : '']
    .filter((s) => s !== '')
    .join(' '),
)
</script>

<template>
  <div class="field">
    <label :for="id">
      {{ label }}<span v-if="required" class="required" aria-hidden="true"> *</span>
      <span v-if="!required" class="muted small"> (optional)</span>
    </label>
    <span v-if="hint" :id="`${id}-hint`" class="hint">{{ hint }}</span>
    <slot :id="id" :described-by="describedBy || undefined" :invalid="error ? true : undefined" />
    <span v-if="error" :id="`${id}-error`" class="error-text" data-testid="field-error">{{ error }}</span>
  </div>
</template>
