import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { authProvider } from '@/auth'
import type { AdminUser } from '@/auth/provider'
import { ADMIN_ROLE } from '@/config'

export const useAuthStore = defineStore('auth', () => {
  const user = ref<AdminUser | null>(null)
  const isSignedIn = computed(() => user.value !== null)
  /** Signed in with Microsoft but not assigned the Admin app role: the API would refuse every call. */
  const isAdmin = computed(() => user.value?.roles.includes(ADMIN_ROLE) ?? false)

  let initPromise: Promise<void> | null = null

  /** Restores a session / finishes a sign-in redirect. Safe to call repeatedly; route guards await it. */
  function init(): Promise<void> {
    initPromise ??= authProvider.init().then(
      (restored) => {
        user.value = restored
      },
      (error) => {
        console.error('Sign-in initialisation failed', error)
        user.value = null
      },
    )
    return initPromise
  }

  async function signIn() {
    user.value = await authProvider.signIn()
  }

  async function signOut() {
    user.value = null
    await authProvider.signOut()
  }

  /** The API no longer accepts our token: forget the session locally without a round trip to Microsoft. */
  function expire() {
    user.value = null
    void authProvider.forget().catch(() => undefined)
  }

  return { user, isSignedIn, isAdmin, init, signIn, signOut, expire }
})
