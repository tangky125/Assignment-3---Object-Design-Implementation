<script setup lang="ts">
// The main menu after sign-in. Its tiles are built from the roles the user holds, so a person
// with several roles (e.g. owner and office staff) sees all of their work in one place.
import { computed, onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { hasRole, isOffice, isStaff, session } from '@/session/session'
import { takeFlash, type Message } from '@/session/flash'
import { decisionSummary, quoteQueueSummary, refreshWorkCounts, workCounts } from '@/session/workCounts'
import MessageBanner from '@/components/MessageBanner.vue'

interface Tile {
  to: string
  title: string
  text: string
  /** Work waiting behind this tile, e.g. "3 requests waiting for a quote: 2 new, 1 revision asked." */
  waiting?: string | null
}

const message = ref<Message | null>(null)
onMounted(() => {
  message.value = takeFlash()
  void refreshWorkCounts()
})

const ownerWaiting = computed(() => {
  const n = workCounts.ownerApprovals
  if (n === null) return null
  return n === 0 ? 'Nothing waiting for an owner.' : `${n} quote${n === 1 ? '' : 's'} waiting for your price.`
})

const tiles = computed<Tile[]>(() => {
  const list: Tile[] = []
  if (hasRole('Customer')) {
    list.push(
      {
        to: '/requests/new',
        title: 'Log a new delivery request',
        text: 'Tell us what to collect and where it is going.',
      },
      {
        to: '/my/requests',
        title: 'My requests and quotes',
        text: 'Accept, reject or revise a quote, cancel, or see a receipt.',
        waiting: decisionSummary(),
      },
      { to: '/track', title: 'Track a delivery', text: 'See where a delivery is with its reference.' },
    )
  }
  if (isOffice.value) {
    list.push(
      {
        to: '/requests/new',
        title: 'Log a request for a phone customer',
        text: 'Find or register the customer, then enter the request.',
      },
      {
        to: '/quotes',
        title: 'Prepare quotes',
        text: 'Requests waiting for a quote, oldest first.',
        waiting: quoteQueueSummary(),
      },
      {
        to: '/decisions',
        title: "Record a customer's quote decision",
        text: 'For decisions given by phone or email.',
      },
      {
        to: '/allocation',
        title: 'Allocate delivery staff',
        text: 'Give accepted deliveries to a free driver, or replace one.',
      },
    )
  }
  if (hasRole('Owner')) {
    list.push({
      to: '/owner/approvals',
      title: 'Price out-of-area quotes',
      text: 'Quotes held until an owner sets the price.',
      waiting: ownerWaiting.value,
    })
  }
  if (hasRole('DeliveryStaff')) {
    list.push({
      to: '/jobs',
      title: 'My jobs',
      text: 'Pick up, take payment and deliver your allocated jobs.',
    })
  }
  if (isStaff.value) {
    list.push({
      to: '/search',
      title: 'Track or search deliveries',
      text: 'Find by reference, phone, name or date; review open jobs.',
    })
  }
  return list
})
</script>

<template>
  <h1>Welcome, {{ session.user?.displayName }}</h1>
  <p class="muted">What would you like to do?</p>
  <MessageBanner
    v-if="message"
    :kind="message.kind"
    :text="message.text"
    dismissible
    @dismiss="message = null"
  />
  <div class="grid-3">
    <RouterLink
      v-for="tile in tiles"
      :key="tile.title"
      :to="tile.to"
      :class="['tile', 'card', { 'tile-busy': tile.waiting && !tile.waiting.startsWith('Nothing') }]"
    >
      <h2>{{ tile.title }}</h2>
      <p class="muted">{{ tile.text }}</p>
      <p v-if="tile.waiting" class="waiting" data-testid="tile-waiting">{{ tile.waiting }}</p>
    </RouterLink>
  </div>
</template>

<style scoped>
.tile {
  display: block;
  text-decoration: none;
  color: inherit;
  border-top: 4px solid var(--brand);
  transition: transform 0.1s ease;
}

.tile:hover {
  transform: translateY(-2px);
  border-top-color: var(--accent);
}

.tile h2 {
  color: var(--brand);
}

.waiting {
  margin: 0.6rem 0 0;
  font-weight: 600;
  color: var(--muted);
}

.tile-busy {
  border-top-color: var(--accent);
}

.tile-busy .waiting {
  color: var(--notice);
  background: var(--notice-bg);
  border-radius: 6px;
  padding: 0.35rem 0.6rem;
}
</style>
