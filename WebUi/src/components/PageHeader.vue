<script setup lang="ts">
import { RouterLink, type RouteLocationRaw } from 'vue-router'

defineProps<{
  title: string
  intro?: string
  backTo?: RouteLocationRaw
  backLabel?: string
}>()
</script>

<template>
  <div class="page-header">
    <RouterLink v-if="backTo" :to="backTo" class="back">&larr; {{ backLabel ?? 'Back' }}</RouterLink>
    <h1>{{ title }}</h1>
    <p v-if="intro" class="muted">{{ intro }}</p>
    <slot />
  </div>
</template>

<style scoped>
.page-header {
  margin-bottom: 1.25rem;
}

.back {
  display: inline-block;
  margin-bottom: 0.4rem;
  font-weight: 600;
  text-decoration: none;
}
</style>
