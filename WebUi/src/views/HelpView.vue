<script setup lang="ts">
import { authApi } from '@/api/dms'
import { useAsync } from '@/utils/useAsync'
import PageHeader from '@/components/PageHeader.vue'

const accounts = useAsync(() => authApi.demoAccounts())
</script>

<template>
  <PageHeader title="Help" intro="How to use the Delivery Management System." />
  <div class="grid-2">
    <section class="card">
      <h2>Getting around</h2>
      <ul>
        <li>Sign in, then choose a task from the main menu or the bar at the top.</li>
        <li>
          Fields marked <span class="required">*</span> must be filled in. The text under each label says what
          it needs.
        </li>
        <li>
          If something you typed is not accepted, a red message under the field says why. Correct it and try
          again.
        </li>
        <li>
          Nothing is saved until you press the final button (for example <em>Submit request</em>), and you are
          asked to confirm important actions, so you can change your mind.
        </li>
        <li>A request can be tracked by anyone with its reference, e.g. DMS-2026-0001.</li>
      </ul>
    </section>
    <section class="card">
      <h2>Formats</h2>
      <ul>
        <li>Phone numbers: Australian, 10 digits, e.g. 0412 345 678 or 03 9123 4567.</li>
        <li>Postcodes: 4 digits, e.g. 3000. Pickups must be inside Greater Melbourne (3000 to 3207).</li>
        <li>Dates and times: choose them with the date picker (shown as dd/mm/yyyy).</li>
        <li>Money: dollars and cents, e.g. 27.50. Prices include GST.</li>
      </ul>
    </section>
    <section id="privacy" class="card">
      <h2>Privacy policy</h2>
      <p>
        Names, phone numbers and addresses are used only to quote, collect and deliver your parcels and to
        tell you how they are going. Card numbers are never typed into or stored by this system: card payments
        are taken on the driver's payment terminal and only the result is recorded.
      </p>
      <p>
        People with a reference number see only the status and expected delivery time, never prices or
        customer details.
      </p>
    </section>
    <section id="terms" class="card">
      <h2>Terms of service</h2>
      <p>
        A quote is valid for 24 hours. Payment is taken when the parcels are collected. A paid delivery shows
        a receipt, including GST, under <em>My requests</em>.
      </p>
    </section>
    <section v-if="accounts.data.value && accounts.data.value.length > 0" class="card">
      <h2>Accounts for this demonstration</h2>
      <ul class="accounts">
        <li v-for="line in accounts.data.value" :key="line">
          <code>{{ line }}</code>
        </li>
      </ul>
    </section>
  </div>
</template>

<style scoped>
ul {
  padding-left: 1.2rem;
  margin: 0;
}

li {
  margin-bottom: 0.4rem;
}

.required {
  color: var(--error);
  font-weight: 700;
}

.accounts {
  list-style: none;
  padding: 0;
}
</style>
