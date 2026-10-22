<script setup lang="ts">
import { ref } from 'vue'
import ErrorAlert from '@/components/ErrorAlert.vue'
import { usePageTitle } from '@/composables/usePageTitle'
import { useAuthStore } from '@/stores/auth'

usePageTitle('Forgot password')

const auth = useAuthStore()
const email = ref('')
const busy = ref(false)
const error = ref('')
const sent = ref(false)

async function submit() {
  busy.value = true
  error.value = ''
  const result = await auth.forgotPassword(email.value.trim())
  busy.value = false
  if (result.ok) sent.value = true
  else error.value = result.message
}
</script>

<template>
  <div class="mx-auto max-w-md">
    <div class="card card-border bg-base-100">
      <div v-if="sent" class="card-body gap-3">
        <h1 class="card-title text-2xl">Check your email</h1>
        <p>If an account exists for <strong>{{ email }}</strong>, we have sent a code to reset your password.</p>
        <RouterLink :to="{ path: '/reset-password', query: { email } }" class="btn btn-primary">I have a code</RouterLink>
      </div>

      <form v-else class="card-body gap-3" @submit.prevent="submit">
        <h1 class="card-title text-2xl">Reset your password</h1>
        <p class="text-sm opacity-70">Enter your email and we will send you a reset code.</p>
        <ErrorAlert v-if="error" :message="error" />
        <label class="form-control">
          <span class="label-text mb-1">Email</span>
          <input v-model="email" type="email" class="input w-full" required autocomplete="username" />
        </label>
        <button class="btn btn-primary" type="submit" :disabled="busy">
          <span v-if="busy" class="loading loading-spinner loading-sm" />
          Send reset code
        </button>
        <RouterLink to="/login" class="link text-sm">Back to sign in</RouterLink>
      </form>
    </div>
  </div>
</template>
