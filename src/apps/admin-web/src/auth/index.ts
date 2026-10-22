import { authConfig } from '@/config'
import { DevAuthProvider } from './dev'
import { MsalAuthProvider } from './msal'
import type { AuthProvider } from './provider'

/** What is wrong with the auth settings, or undefined when they are usable. */
export function authConfigProblem(): string | undefined {
  if (authConfig.mode === 'dev') return undefined
  const missing = (['tenantId', 'clientId', 'apiScope'] as const).filter((key) => !authConfig[key])
  return missing.length ? `Microsoft sign-in is not configured (missing ${missing.join(', ')} in env.js).` : undefined
}

function createProvider(): AuthProvider {
  if (authConfig.mode === 'dev') return new DevAuthProvider()
  return new MsalAuthProvider({
    tenantId: authConfig.tenantId,
    clientId: authConfig.clientId,
    apiScope: authConfig.apiScope,
  })
}

export const authProvider: AuthProvider = createProvider()

// Lets the API client report "the API rejected our token" without depending on the store or the router.
type Listener = () => void
const unauthorizedListeners = new Set<Listener>()
export const onUnauthorized = (listener: Listener) => unauthorizedListeners.add(listener)
export const reportUnauthorized = () => unauthorizedListeners.forEach((listener) => listener())
