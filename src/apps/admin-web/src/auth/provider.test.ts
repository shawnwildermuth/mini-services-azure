import { describe, expect, it } from 'vitest'
import { decodeJwtClaims, isExpired, rolesFromToken } from './provider'

/** Builds an unsigned JWT-shaped string; the admin site only ever reads the claims. */
function token(claims: Record<string, unknown>) {
  const b64 = (value: object) => btoa(JSON.stringify(value)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
  return `${b64({ alg: 'none' })}.${b64(claims)}.signature`
}

describe('token helpers', () => {
  it('reads claims, including non-ASCII names', () => {
    expect(decodeJwtClaims(token({ name: 'Zoë Müller', sub: 'abc' }))).toMatchObject({ sub: 'abc' })
  })

  it('returns no claims for garbage instead of throwing', () => {
    expect(decodeJwtClaims('not-a-jwt')).toEqual({})
    expect(decodeJwtClaims('')).toEqual({})
  })

  it('extracts app roles', () => {
    expect(rolesFromToken(token({ roles: ['Admin', 'Reader'] }))).toEqual(['Admin', 'Reader'])
    expect(rolesFromToken(token({}))).toEqual([])
    expect(rolesFromToken(token({ roles: 'Admin' }))).toEqual(['Admin'])
    expect(rolesFromToken(token({ roles: [1, null, 'Reader'] }))).toEqual(['Reader'])
    expect(rolesFromToken(token({ roles: { Admin: true } }))).toEqual([])
  })

  it('detects expiry, with a safety margin', () => {
    const now = Math.floor(Date.now() / 1000)
    expect(isExpired(token({ exp: now + 3600 }))).toBe(false)
    expect(isExpired(token({ exp: now - 10 }))).toBe(true)
    expect(isExpired(token({ exp: now + 10 }))).toBe(true) // inside the 30s margin
    expect(isExpired(token({}))).toBe(true) // no exp: don't trust it
  })
})
