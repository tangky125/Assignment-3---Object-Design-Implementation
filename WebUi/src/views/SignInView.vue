<script setup lang="ts">
import { ref } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import { signIn } from '@/session/session'
import { errorText } from '@/utils/useAsync'
import FormField from '@/components/FormField.vue'
import MessageBanner from '@/components/MessageBanner.vue'
import PageHeader from '@/components/PageHeader.vue'

const route = useRoute()
const router = useRouter()

const username = ref('')
const password = ref('')
const errors = ref<{ username?: string; password?: string }>({})
const failure = ref<string | null>(null)
const attempts = ref(0)
const busy = ref(false)

async function submit(): Promise<void> {
  errors.value = {}
  failure.value = null
  if (username.value.trim() === '') errors.value.username = 'Username cannot be blank.'
  if (password.value === '') errors.value.password = 'Password cannot be blank.'
  if (Object.keys(errors.value).length > 0) return

  busy.value = true
  try {
    await signIn(username.value.trim(), password.value)
    const redirect = typeof route.query.redirect === 'string' ? route.query.redirect : '/dashboard'
    await router.push(redirect)
  } catch (e) {
    attempts.value++
    password.value = ''
    failure.value = errorText(e)
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="narrow">
    <PageHeader
      title="Sign in"
      intro="Customers and staff sign in with the username and password they were given."
    />
    <MessageBanner v-if="failure" kind="error" :text="`${failure} (attempt ${attempts})`" />
    <form class="card" novalidate @submit.prevent="submit">
      <FormField id="username" label="Username" :error="errors.username" required>
        <template #default="f">
          <input
            :id="f.id"
            v-model="username"
            type="text"
            autocomplete="username"
            autocapitalize="none"
            :aria-describedby="f.describedBy"
            :aria-invalid="f.invalid"
          />
        </template>
      </FormField>
      <FormField id="password" label="Password" :error="errors.password" required>
        <template #default="f">
          <input
            :id="f.id"
            v-model="password"
            type="password"
            autocomplete="current-password"
            :aria-describedby="f.describedBy"
            :aria-invalid="f.invalid"
          />
        </template>
      </FormField>
      <button type="submit" class="btn" :disabled="busy">Sign in</button>
    </form>
    <p class="muted small">
      Only want to follow a parcel? <RouterLink to="/track">Track with a reference</RouterLink> instead.
      Demonstration accounts are listed on the <RouterLink to="/help">Help</RouterLink> page.
    </p>
  </div>
</template>

<style scoped>
.narrow {
  max-width: 480px;
  margin: 0 auto;
}
</style>
