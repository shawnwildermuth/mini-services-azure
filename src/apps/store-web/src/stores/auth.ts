import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { api } from '@/api/client'
import { fail, ok, problemMessage, type Result } from '@/api/errors'
import { clearTokens, hasRefreshToken, onSessionEnded, refreshSession, setTokens } from '@/api/session'

export const useAuthStore = defineStore('auth', () => {
  const email = ref<string | null>(null)
  const isEmailConfirmed = ref(false)
  const isSignedIn = computed(() => email.value !== null)

  onSessionEnded(() => {
    email.value = null
  })

  let initPromise: Promise<void> | null = null

  /** Restores a previous session, if any. Safe to call repeatedly; route guards await it. */
  function init(): Promise<void> {
    initPromise ??= (async () => {
      if (hasRefreshToken() && (await refreshSession())) await loadProfile()
    })()
    return initPromise
  }

  async function loadProfile() {
    const { data } = await api.GET('/api/auth/manage/info')
    email.value = data?.email ?? null
    isEmailConfirmed.value = data?.isEmailConfirmed ?? false
  }

  async function login(emailAddress: string, password: string): Promise<Result> {
    const { data, error, response } = await api.POST('/api/auth/login', {
      body: { email: emailAddress, password },
    })

    if (!data) {
      if (response.status === 429) return fail('Too many attempts. Please wait a minute and try again.')
      if ((error as { detail?: string } | undefined)?.detail === 'LockedOut')
        return fail('This account is temporarily locked after too many failed attempts. Try again later.')
      return fail('Invalid email or password.')
    }

    setTokens(data.accessToken, data.refreshToken)
    await loadProfile()
    return ok()
  }

  async function register(emailAddress: string, password: string): Promise<Result> {
    const { error, response } = await api.POST('/api/auth/register', {
      body: { email: emailAddress, password },
    })
    if (!response.ok) {
      if (response.status === 429) return fail('Too many attempts. Please wait a minute and try again.')
      return fail(problemMessage(error, 'We could not create your account.'))
    }
    return login(emailAddress, password)
  }

  async function logout() {
    // Best effort: the server revokes the refresh token; locally we always forget the session.
    if (isSignedIn.value) await api.POST('/api/auth/logout').catch(() => undefined)
    clearTokens()
    email.value = null
    isEmailConfirmed.value = false
  }

  async function forgotPassword(emailAddress: string): Promise<Result> {
    const { response } = await api.POST('/api/auth/forgotPassword', { body: { email: emailAddress } })
    return response.ok ? ok() : fail('We could not process that request. Please try again.')
  }

  async function resetPassword(emailAddress: string, resetCode: string, newPassword: string): Promise<Result> {
    const { error, response } = await api.POST('/api/auth/resetPassword', {
      body: { email: emailAddress, resetCode, newPassword },
    })
    return response.ok ? ok() : fail(problemMessage(error, 'The reset code is invalid or has expired.'))
  }

  async function changePassword(oldPassword: string, newPassword: string): Promise<Result> {
    const { error, response } = await api.POST('/api/auth/manage/info', { body: { oldPassword, newPassword } })
    return response.ok ? ok() : fail(problemMessage(error, 'We could not change your password.'))
  }

  async function resendConfirmation(): Promise<Result> {
    if (!email.value) return fail('You are not signed in.')
    const { response } = await api.POST('/api/auth/resendConfirmationEmail', { body: { email: email.value } })
    return response.ok ? ok() : fail('We could not send the confirmation email.')
  }

  return {
    email,
    isEmailConfirmed,
    isSignedIn,
    init,
    login,
    register,
    logout,
    forgotPassword,
    resetPassword,
    changePassword,
    resendConfirmation,
  }
})
