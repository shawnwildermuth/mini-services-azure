<script setup lang="ts">
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { authConfigProblem } from '@/auth'
import ErrorAlert from '@/components/ErrorAlert.vue'
import { usePageTitle } from '@/composables/usePageTitle'
import { authConfig } from '@/config'
import { safeRedirect } from '@/router'
import { useAuthStore } from '@/stores/auth'

usePageTitle('Sign in')

const auth = useAuthStore()
const route = useRoute()
const router = useRouter()

const busy = ref(false)
const error = ref('')
const configProblem = authConfigProblem()
const isDev = authConfig.mode === 'dev'

async function signIn() {
  busy.value = true
  error.value = ''
  try {
    await auth.signIn() // Microsoft: navigates away; dev: resolves with the user
    if (auth.isSignedIn) router.push(safeRedirect(route.query.redirect))
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Sign-in failed.'
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <main class="hero min-h-screen bg-base-200">
    <div class="card w-full max-w-sm bg-base-100 shadow-xl">
      <div class="card-body gap-4">
        <div class="text-center">
          <p class="text-4xl" aria-hidden="true">🖥️</p>
          <h1 class="mt-2 text-2xl font-bold">Parts Store Admin</h1>
          <p class="text-sm opacity-70">Staff only</p>
        </div>

        <div v-if="route.query.expired" role="alert" class="alert alert-warning alert-soft text-sm">
          Your session ended. Please sign in again.
        </div>
        <ErrorAlert v-if="configProblem" :message="configProblem" />
        <ErrorAlert v-if="error" :message="error" />

        <button class="btn btn-primary" :disabled="busy || !!configProblem" @click="signIn">
          <span v-if="busy" class="loading loading-spinner loading-sm" />
          {{ isDev ? 'Sign in (development)' : 'Sign in with Microsoft' }}
        </button>

        <p v-if="isDev" class="text-center text-xs opacity-75">
          Development sign-in is on. Real deployments use Microsoft Entra ID.
        </p>
      </div>
    </div>
  </main>
</template>
