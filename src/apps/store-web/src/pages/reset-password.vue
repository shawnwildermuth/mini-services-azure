<script setup lang="ts">
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import ErrorAlert from '@/components/ErrorAlert.vue'
import { usePageTitle } from '@/composables/usePageTitle'
import { useAuthStore } from '@/stores/auth'
import { useToastStore } from '@/stores/toast'

usePageTitle('Choose a new password')

const auth = useAuthStore()
const toast = useToastStore()
const route = useRoute()
const router = useRouter()

const email = ref(typeof route.query.email === 'string' ? route.query.email : '')
const code = ref('')
const password = ref('')
const busy = ref(false)
const error = ref('')

async function submit() {
  busy.value = true
  error.value = ''
  const result = await auth.resetPassword(email.value.trim(), code.value.trim(), password.value)
  busy.value = false

  if (!result.ok) {
    error.value = result.message
    return
  }
  toast.success('Your password has been changed. Please sign in.')
  router.push('/login')
}
</script>

<template>
  <div class="mx-auto max-w-md">
    <form class="card card-border bg-base-100" @submit.prevent="submit">
      <div class="card-body gap-3">
        <h1 class="card-title text-2xl">Choose a new password</h1>
        <ErrorAlert v-if="error" :message="error" />

        <label class="form-control">
          <span class="label-text mb-1">Email</span>
          <input v-model="email" type="email" class="input w-full" required autocomplete="username" />
        </label>
        <label class="form-control">
          <span class="label-text mb-1">Reset code</span>
          <input v-model="code" class="input w-full font-mono" required autocomplete="one-time-code" />
        </label>
        <label class="form-control">
          <span class="label-text mb-1">New password</span>
          <input v-model="password" type="password" class="input w-full" required minlength="8" autocomplete="new-password" />
        </label>

        <button class="btn btn-primary" type="submit" :disabled="busy">
          <span v-if="busy" class="loading loading-spinner loading-sm" />
          Change password
        </button>
      </div>
    </form>
  </div>
</template>
