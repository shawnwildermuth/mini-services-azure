import { apiBaseUrl } from '@/config'

// The access token lives only in memory; the refresh token is kept in localStorage so a reload
// can restore the session. Both are issued by the API's Identity endpoints.
const REFRESH_KEY = 'store.refreshToken'

let accessToken: string | null = null
let refreshInFlight: Promise<boolean> | null = null
const endedListeners = new Set<() => void>()

function readRefreshToken(): string | null {
  try {
    return localStorage.getItem(REFRESH_KEY)
  } catch {
    return null
  }
}

function writeRefreshToken(token: string | null) {
  try {
    if (token) localStorage.setItem(REFRESH_KEY, token)
    else localStorage.removeItem(REFRESH_KEY)
  } catch {
    // storage unavailable (private mode); the session just won't survive a reload
  }
}

export const getAccessToken = () => accessToken
export const hasRefreshToken = () => readRefreshToken() !== null

export function setTokens(access: string, refresh: string) {
  accessToken = access
  writeRefreshToken(refresh)
}

export function clearTokens() {
  accessToken = null
  writeRefreshToken(null)
}

/** Registers a callback for when the session is lost (refresh token rejected or expired). */
export function onSessionEnded(listener: () => void) {
  endedListeners.add(listener)
  return () => endedListeners.delete(listener)
}

/** Exchanges the refresh token for new tokens. Concurrent callers share one request. */
export function refreshSession(): Promise<boolean> {
  refreshInFlight ??= doRefresh().finally(() => {
    refreshInFlight = null
  })
  return refreshInFlight
}

async function doRefresh(): Promise<boolean> {
  const refreshToken = readRefreshToken()
  if (!refreshToken) return false

  try {
    const response = await fetch(`${apiBaseUrl}/api/auth/refresh`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ refreshToken }),
    })

    if (response.ok) {
      const tokens = (await response.json()) as { accessToken: string; refreshToken: string }
      setTokens(tokens.accessToken, tokens.refreshToken)
      return true
    }

    // The server rejected the token: the session is over (logged out elsewhere, expired, locked out).
    clearTokens()
    endedListeners.forEach((listener) => listener())
    return false
  } catch {
    // Network failure: keep the refresh token so a later attempt can succeed.
    return false
  }
}
