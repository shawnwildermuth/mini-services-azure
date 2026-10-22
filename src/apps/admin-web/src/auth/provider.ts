export interface AdminUser {
  name: string
  /** App roles from the access token; "Admin" is required by the API. */
  roles: string[]
}

/** How the admin site proves who the signed-in administrator is, and obtains tokens for the API. */
export interface AuthProvider {
  /** Completes a sign-in redirect if we are returning from one, then restores any existing session. */
  init(): Promise<AdminUser | null>
  /** Starts sign-in. May navigate away (Microsoft), in which case it never resolves with a user. */
  signIn(): Promise<AdminUser | null>
  signOut(): Promise<void>
  /** Drops the local session without contacting the identity provider (used when the API rejects our token). */
  forget(): Promise<void>
  /** A valid access token for the API, or null when the user must sign in again. */
  getToken(): Promise<string | null>
}

/** Reads the claims of a JWT without verifying it (the API does the verifying; this is only for display and routing). */
export function decodeJwtClaims(token: string): Record<string, unknown> {
  try {
    const payload = token.split('.')[1] ?? ''
    const json = atob(payload.replace(/-/g, '+').replace(/_/g, '/').padEnd(Math.ceil(payload.length / 4) * 4, '='))
    return JSON.parse(new TextDecoder().decode(Uint8Array.from(json, (c) => c.charCodeAt(0)))) as Record<string, unknown>
  } catch {
    return {}
  }
}

export function rolesFromToken(token: string): string[] {
  const roles = decodeJwtClaims(token).roles
  // Entra sends an array; a lone string is tolerated. Either way the API makes the real decision.
  const list = Array.isArray(roles) ? roles : typeof roles === 'string' ? [roles] : []
  return list.filter((r): r is string => typeof r === 'string')
}

export function isExpired(token: string, skewSeconds = 30): boolean {
  const exp = decodeJwtClaims(token).exp
  return typeof exp !== 'number' || exp * 1000 < Date.now() + skewSeconds * 1000
}
