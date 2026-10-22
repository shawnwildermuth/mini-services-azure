<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import ErrorAlert from '@/components/ErrorAlert.vue'
import { usePageTitle } from '@/composables/usePageTitle'
import { safeRedirect } from '@/router'
import { useAuthStore } from '@/stores/auth'
import { useToastStore } from '@/stores/toast'

usePageTitle('Create account')

const auth = useAuthStore()
const toast = useToastStore()
const route = useRoute()
const router = useRouter()

const email = ref('')
const password = ref('')
const confirm = ref('')
const busy = ref(false)
const error = ref('')

const mismatch = computed(() => confirm.value !== '' && confirm.value !== password.value)

async function submit() {
  if (mismatch.value) return
  busy.value = true
  error.value = ''
  const result = await auth.register(email.value.trim(), password.value)
  busy.value = false

  if (!result.ok) {
    error.value = result.message
    return
  }
  toast.success('Your account is ready.')
  router.push(safeRedirect(route.query.redirect))
}
</script>

<template>
  <div class="mx-auto max-w-md">
    <form class="card card-border bg-base-100" @submit.prevent="submit">
      <div class="card-body gap-3">
        <h1 class="card-title text-2xl">Create your account</h1>
        <ErrorAlert v-if="error" :message="error" />

        <label class="form-control">
          <span class="label-text mb-1">Email</span>
          <input v-model="email" type="email" class="input w-full" required autocomplete="username" />
        </label>
        <div class="form-control">
          <label class="form-control">
            <span class="label-text mb-1">Password</span>
            <input
              v-model="password"
              type="password"
              class="input w-full"
              required
              minlength="8"
              autocomplete="new-password"
              aria-describedby="password-hint"
            />
          </label>
          <span id="password-hint" class="label-text-alt mt-1 opacity-70">
            At least 8 characters with upper and lower case letters, a number and a symbol.
          </span>
        </div>
        <div class="form-control">
          <label class="form-control">
            <span class="label-text mb-1">Confirm password</span>
            <input
              v-model="confirm"
              type="password"
              class="input w-full"
              :class="{ 'input-error': mismatch }"
              required
              autocomplete="new-password"
              :aria-invalid="mismatch"
              aria-describedby="confirm-error"
            />
          </label>
          <span v-if="mismatch" id="confirm-error" role="alert" class="label-text-alt mt-1 text-error">Passwords do not match.</span>
        </div>

        <button class="btn btn-primary mt-2" type="submit" :disabled="busy || mismatch">
          <span v-if="busy" class="loading loading-spinner loading-sm" />
          Create account
        </button>

        <p class="text-sm">
          Already have an account?
          <RouterLink :to="{ path: '/login', query: route.query }" class="link">Sign in</RouterLink>
        </p>
      </div>
    </form>
  </div>
</template>
