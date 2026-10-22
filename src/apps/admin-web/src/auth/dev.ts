import { apiBaseUrl } from '@/config'
import { isExpired, rolesFromToken, type AdminUser, type AuthProvider } from './provider'

const KEY = 'admin.devSession'

interface Session {
  token: string
  name: string
}

function read(): Session | null {
  try {
    const session = JSON.parse(sessionStorage.getItem(KEY) ?? 'null') as Session | null
    return session && !isExpired(session.token) ? session : null
  } catch {
    return null
  }
}

const toUser = (session: Session): AdminUser => ({ name: session.name, roles: rolesFromToken(session.token) })

/**
 * LOCAL DEVELOPMENT ONLY. Gets a token from the API's /api/dev/admin-token endpoint, which the API only maps when
 * it runs in the Development environment with Admin:DevTokens:Enabled. Against any other API it simply fails.
 */
export class DevAuthProvider implements AuthProvider {
  async init() {
    const session = read()
    return session ? toUser(session) : null
  }

  async signIn() {
    const response = await fetch(`${apiBaseUrl}/api/dev/admin-token`, { method: 'POST' })
    if (!response.ok) throw new Error('The API did not issue a development token. Is it running in the Development environment?')

    const body = (await response.json()) as { accessToken: string; name: string }
    const session = { token: body.accessToken, name: body.name }
    sessionStorage.setItem(KEY, JSON.stringify(session))
    return toUser(session)
  }

  async signOut() {
    sessionStorage.removeItem(KEY)
  }

  forget() {
    return this.signOut()
  }

  async getToken() {
    return read()?.token ?? null
  }
}
