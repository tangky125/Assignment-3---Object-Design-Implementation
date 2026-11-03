<script setup lang="ts">
// The exit screen: shown after signing out.
import { computed } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { when } from '@/utils/format'

const route = useRoute()
const name = computed(() =>
  typeof route.query.name === 'string' && route.query.name !== '' ? route.query.name : null,
)
const at = when(new Date())
</script>

<template>
  <div class="card done">
    <h1>You have signed out</h1>
    <p>
      <span v-if="name">Goodbye, {{ name }}. </span>Your session ended at {{ at }}. Nobody can use this
      browser tab to act on your account until you sign in again.
    </p>
    <p class="muted">Thank you for using Your Order We Deliver. You can close this tab now.</p>
    <div class="row">
      <RouterLink to="/sign-in" class="btn">Sign in again</RouterLink>
      <RouterLink to="/" class="btn btn-secondary">Back to the home page</RouterLink>
    </div>
  </div>
</template>

<style scoped>
.done {
  max-width: 640px;
  margin: 2rem auto;
  border-top: 6px solid var(--success);
}
</style>
