<script setup lang="ts">
import { RouterLink } from 'vue-router'
import { session } from '@/session/session'
</script>

<template>
  <section class="hero card">
    <h1>Parcels collected and delivered across Greater Melbourne</h1>
    <p>
      Customers and office staff log delivery requests here. Office staff send quotes and allocate each
      delivery to a driver, and delivery staff record pickup, payment and delivery as they go.
    </p>
    <div class="row">
      <RouterLink v-if="session.user" to="/dashboard" class="btn">Go to the main menu</RouterLink>
      <RouterLink v-else to="/sign-in" class="btn">Sign in</RouterLink>
      <RouterLink to="/track" class="btn btn-secondary">Track a delivery with a reference</RouterLink>
    </div>
  </section>

  <div class="grid-3">
    <section class="card">
      <h2>Customers</h2>
      <p>Log a request, see your quote, accept it, and follow your delivery.</p>
    </section>
    <section class="card">
      <h2>Recipients</h2>
      <p>
        Use the reference the sender gave you to see the delivery status and expected time. No sign-in needed.
      </p>
    </section>
    <section class="card">
      <h2>Staff</h2>
      <p>Quote, allocate and complete deliveries. Sign in with your staff account.</p>
    </section>
  </div>
  <p class="muted small">
    New here? Read <RouterLink to="/help">how to use this system</RouterLink>. Your details are used only to
    arrange your deliveries (<RouterLink to="/help#privacy">privacy policy</RouterLink>).
  </p>
</template>

<style scoped>
.hero {
  background: linear-gradient(135deg, var(--brand) 0%, #16697a 100%);
  color: #fff;
  border: none;
  padding: 2rem 1.5rem;
}

.hero h1 {
  font-size: 1.9rem;
  max-width: 34ch;
}

.hero p {
  max-width: 62ch;
  color: #e3f0f2;
}

.hero .btn {
  background: var(--accent);
  border-color: var(--accent);
  color: #1b2528;
}

.hero .btn-secondary {
  background: transparent;
  border-color: #fff;
  color: #fff;
}
</style>
