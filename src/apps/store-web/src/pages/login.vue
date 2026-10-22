<script setup lang="ts">
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import ErrorAlert from '@/components/ErrorAlert.vue'
import { usePageTitle } from '@/composables/usePageTitle'
import { safeRedirect } from '@/router'
import { useAuthStore } from '@/stores/auth'
import { useToastStore } from '@/stores/toast'

usePageTitle('Sign in')

const auth = useAuthStore()
const toast = useToastStore()
const route = useRoute()
const router = useRouter()

const email = ref('')
const password = ref('')
const busy = ref(false)
const error = ref('')

async function submit() {
  busy.value = true
  error.value = ''
  const result = await auth.login(email.value.trim(), password.value)
  busy.value = false

  if (!result.ok) {
    error.value = result.message
    return
  }
  toast.success('Welcome back!')
  router.push(safeRedirect(route.query.redirect))
}
</script>

<template>
  <div class="mx-auto max-w-md">
    <form class="card card-border bg-base-100" @submit.prevent="submit">
      <div class="card-body gap-3">
        <h1 class="card-title text-2xl">Sign in</h1>
        <ErrorAlert v-if="error" :message="error" />

        <label class="form-control">
          <span class="label-text mb-1">Email</span>
          <input v-model="email" type="email" class="input w-full" required autocomplete="username" />
        </label>
        <label class="form-control">
          <span class="label-text mb-1">Password</span>
          <input v-model="password" type="password" class="input w-full" required autocomplete="current-password" />
        </label>

        <button class="btn btn-primary mt-2" type="submit" :disabled="busy">
          <span v-if="busy" class="loading loading-spinner loading-sm" />
          Sign in
        </button>

        <div class="flex justify-between text-sm">
          <RouterLink to="/forgot-password" class="link">Forgot your password?</RouterLink>
          <RouterLink :to="{ path: '/register', query: route.query }" class="link">Create an account</RouterLink>
        </div>
      </div>
    </form>
  </div>
</template>
