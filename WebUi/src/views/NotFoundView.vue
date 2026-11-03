<script setup lang="ts">
import { RouterLink } from 'vue-router'
</script>

<template>
  <div class="card">
    <h1>Page not found</h1>
    <p>That address does not match any screen. Check the link or go back to the start.</p>
    <RouterLink to="/" class="btn">Home page</RouterLink>
  </div>
</template>
