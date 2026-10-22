import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import type { AdminUser } from '@/auth/provider'

// The router and stores talk to the auth provider through '@/auth'; replace it with a controllable fake.
const fake = vi.hoisted(() => ({
  user: null as AdminUser | null,
  unauthorized: [] as Array<() => void>,
}))
vi.mock('@/auth', () => ({
  authProvider: {
    init: vi.fn(async () => fake.user),
    signIn: vi.fn(),
    signOut: vi.fn(async () => undefined),
    forget: vi.fn(async () => undefined),
    getToken: vi.fn(async () => null),
  },
  onUnauthorized: (listener: () => void) => fake.unauthorized.push(listener),
  reportUnauthorized: () => fake.unauthorized.forEach((l) => l()),
  authConfigProblem: () => undefined,
}))

window.scrollTo = () => {} // not implemented by jsdom; the router calls it on navigation

import { router, safeRedirect } from './router'
import { useAuthStore } from './stores/auth'

beforeEach(() => {
  // A fresh store per test; it asks the fake provider who is signed in on the first navigation.
  fake.user = null
  setActivePinia(createPinia())
})

describe('file-based routes', () => {
  it('maps pages to routes, with the (protected) group invisible in URLs', () => {
    expect(router.resolve('/').name).toBe('/(protected)/')
    expect(router.resolve('/orders').name).toBe('/(protected)/orders/')
    expect(router.resolve('/login').name).toBe('/login')
    expect(router.resolve('/operations/failed-messages').name).toBe('/(protected)/operations/failed-messages')
  })

  it('extracts params from [bracket] segments', () => {
    expect(router.resolve('/products/42').params).toEqual({ id: '42' })
    expect(router.resolve('/orders/3f2a').params).toEqual({ id: '3f2a' })
    expect(router.resolve('/no/such/page').name).toBe('/[...path]')
  })

  it('marks every page except login as requiring auth', () => {
    for (const path of ['/', '/products', '/products/new', '/categories', '/inventory', '/orders', '/operations/failed-messages'])
      expect(router.resolve(path).meta.requiresAuth, path).toBe(true)
    expect(router.resolve('/login').meta.requiresAuth).toBeUndefined()
  })
})

describe('guards', () => {
  it('sends anonymous visitors to login, remembering where they were going', async () => {
    await router.push('/orders')

    expect(router.currentRoute.value.path).toBe('/login')
    expect(router.currentRoute.value.query.redirect).toBe('/orders')
  })

  it('lets a signed-in administrator in', async () => {
    fake.user = { name: 'Ada', roles: ['Admin'] }

    await router.push('/inventory')

    expect(router.currentRoute.value.path).toBe('/inventory')
  })

  it('bounces a signed-in administrator away from the login page, back to where they were headed', async () => {
    fake.user = { name: 'Ada', roles: ['Admin'] }

    await router.push('/login?redirect=/orders')

    expect(router.currentRoute.value.path).toBe('/orders')
  })

  it('signs the user out locally and returns to login when the API rejects the token', async () => {
    fake.user = { name: 'Ada', roles: ['Admin'] }
    await router.push('/categories') // a page other than the previous test's, so the guard really runs
    expect(useAuthStore().isSignedIn).toBe(true)

    fake.unauthorized.forEach((l) => l())
    await new Promise((r) => setTimeout(r, 0))

    expect(useAuthStore().isSignedIn).toBe(false)
    expect(router.currentRoute.value.path).toBe('/login')
    expect(router.currentRoute.value.query.expired).toBe('1')
  })
})

describe('roles', () => {
  it('only treats users with the Admin role as administrators', async () => {
    const auth = useAuthStore()

    fake.user = { name: 'Reader', roles: ['Reader'] }
    await auth.init()

    expect(auth.isSignedIn).toBe(true)
    expect(auth.isAdmin).toBe(false)
  })
})

describe('safeRedirect', () => {
  it('allows same-site paths only', () => {
    expect(safeRedirect('/orders?status=Paid')).toBe('/orders?status=Paid')
    expect(safeRedirect('//evil.example')).toBe('/')
    expect(safeRedirect('https://evil.example')).toBe('/')
    expect(safeRedirect(undefined)).toBe('/')
  })
})
