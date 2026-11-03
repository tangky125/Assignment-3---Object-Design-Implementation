<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink, RouterView, useRouter } from 'vue-router'
import { hasRole, isOffice, isStaff, roleText, session, signOut } from '@/session/session'

const router = useRouter()

const roles = computed(() => (session.user ? session.user.roles.map(roleText).join(', ') : ''))

async function onSignOut(): Promise<void> {
  const name = session.user?.displayName ?? ''
  await signOut()
  await router.push({ name: 'signed-out', query: { name } })
}
</script>

<template>
  <a class="skip-link" href="#main">Skip to main content</a>
  <header class="site-header">
    <div class="container header-inner">
      <RouterLink
        :to="session.user ? '/dashboard' : '/'"
        class="brand"
        aria-label="Your Order We Deliver home"
      >
        <img src="/favicon.svg" alt="" width="34" height="34" />
        <span>
          <strong>Your Order We Deliver</strong>
          <small>Delivery Management System</small>
        </span>
      </RouterLink>
      <div v-if="session.user" class="who">
        <span>
          <strong>{{ session.user.displayName }}</strong>
          <small>{{ roles }}</small>
        </span>
        <button type="button" class="btn btn-secondary btn-small" @click="onSignOut">Sign out</button>
      </div>
      <RouterLink v-else to="/sign-in" class="btn btn-secondary btn-small">Sign in</RouterLink>
    </div>
    <nav v-if="session.user" class="site-nav" aria-label="Main">
      <div class="container nav-inner">
        <RouterLink to="/dashboard">Main menu</RouterLink>
        <RouterLink v-if="hasRole('Customer') || isOffice" to="/requests/new">Log request</RouterLink>
        <RouterLink v-if="hasRole('Customer')" to="/my/requests">My requests</RouterLink>
        <RouterLink v-if="isOffice" to="/quotes">Quotes</RouterLink>
        <RouterLink v-if="isOffice" to="/decisions">Customer decisions</RouterLink>
        <RouterLink v-if="isOffice" to="/allocation">Allocation</RouterLink>
        <RouterLink v-if="hasRole('Owner')" to="/owner/approvals">Owner approvals</RouterLink>
        <RouterLink v-if="hasRole('DeliveryStaff')" to="/jobs">My jobs</RouterLink>
        <RouterLink v-if="isStaff" to="/search">Track / search</RouterLink>
        <RouterLink v-if="!isStaff" to="/track">Track</RouterLink>
        <RouterLink to="/help">Help</RouterLink>
      </div>
    </nav>
  </header>

  <main id="main" class="container" tabindex="-1">
    <p v-if="session.expired" class="message message-notice" role="status">
      <span class="label">Signed out:</span> your session ended. Please sign in again.
    </p>
    <RouterView />
  </main>

  <footer class="site-footer">
    <div class="container spread">
      <span>&copy; Your Order We Deliver, Greater Melbourne</span>
      <span class="row">
        <RouterLink to="/help#privacy">Privacy policy</RouterLink>
        <RouterLink to="/help#terms">Terms of service</RouterLink>
        <RouterLink to="/help">Help</RouterLink>
      </span>
    </div>
  </footer>
</template>

<style scoped>
.site-header {
  background: var(--brand);
  color: #fff;
}

.header-inner {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 1rem;
  padding-top: 0.75rem;
  padding-bottom: 0.75rem;
}

.brand {
  display: flex;
  align-items: center;
  gap: 0.6rem;
  color: #fff;
  text-decoration: none;
}

.brand span,
.who span {
  display: flex;
  flex-direction: column;
  line-height: 1.2;
}

.brand strong {
  font-size: 1.15rem;
  letter-spacing: 0.01em;
}

.brand small,
.who small {
  color: #cfe3e7;
  font-size: 0.8rem;
}

.who {
  display: flex;
  align-items: center;
  gap: 0.8rem;
  text-align: right;
}

.btn-small {
  min-height: 2.2rem;
  padding: 0.3rem 0.9rem;
}

.site-header .btn-secondary {
  border-color: #fff;
}

.site-nav {
  background: var(--brand-dark);
}

.nav-inner {
  display: flex;
  flex-wrap: wrap;
  gap: 0.25rem 1.25rem;
  padding-top: 0.45rem;
  padding-bottom: 0.45rem;
}

.site-nav a {
  color: #e8f2f4;
  text-decoration: none;
  font-weight: 600;
  font-size: 0.95rem;
  padding: 0.2rem 0;
  border-bottom: 3px solid transparent;
}

.site-nav a:hover,
.site-nav a.router-link-active {
  color: #fff;
  border-bottom-color: var(--accent);
}

.site-footer {
  border-top: 1px solid var(--border);
  padding: 1rem 0;
  color: var(--muted);
  font-size: 0.875rem;
}

main:focus {
  outline: none;
}

@media (max-width: 600px) {
  .who small,
  .brand small {
    display: none;
  }

  .brand strong {
    font-size: 1rem;
  }

  .who {
    gap: 0.5rem;
  }

  .who strong {
    font-size: 0.85rem;
  }

  .btn-small {
    white-space: nowrap;
  }
}
</style>
