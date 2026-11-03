<script setup lang="ts">
// Shows where the user is in a multi-step task, e.g. "Step 2 of 6: Pickup".
defineProps<{ steps: string[]; current: number }>()
</script>

<template>
  <ol class="steps" aria-label="Progress">
    <li
      v-for="(step, i) in steps"
      :key="step"
      :class="{ current: i === current, done: i < current }"
      :aria-current="i === current ? 'step' : undefined"
    >
      {{ i + 1 }}. {{ step }}
    </li>
  </ol>
</template>
