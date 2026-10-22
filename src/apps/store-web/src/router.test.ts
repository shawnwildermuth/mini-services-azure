import { beforeEach, describe, expect, it } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { router, safeRedirect } from './router'

window.scrollTo = () => {} // not implemented by jsdom; the router calls it on navigation

beforeEach(() => {
  localStorage.clear()
  setActivePinia(createPinia())
})

describe('file-based routes', () => {
  it('maps pages to routes', () => {
    expect(router.resolve('/').name).toBe('/')
    expect(router.resolve('/products').name).toBe('/products/')
    expect(router.resolve('/cart').name).toBe('/cart')
    expect(router.resolve('/login').name).toBe('/login')
  })

  it('extracts params from [bracket] segments', () => {
    const product = router.resolve('/products/CPU-AMD-9950X')
    expect(product.name).toBe('/products/[sku]')
    expect(product.params).toEqual({ sku: 'CPU-AMD-9950X' })

    expect(router.resolve('/categories/gpus').params).toEqual({ slug: 'gpus' })
  })

  it('sends unknown URLs to the catch-all page', () => {
    expect(router.resolve('/no/such/page').name).toBe('/[...path]')
  })

  it('marks the (protected) group as requiring auth without changing its URLs', () => {
    expect(router.resolve('/checkout').meta.requiresAuth).toBe(true)
    expect(router.resolve('/account').meta.requiresAuth).toBe(true)
    expect(router.resolve('/cart').meta.requiresAuth).toBeUndefined()
    expect(router.resolve('/products').meta.requiresAuth).toBeUndefined()
  })
})

describe('guards', () => {
  it('redirects anonymous visitors from protected pages to login, remembering the target', async () => {
    await router.push('/checkout')

    expect(router.currentRoute.value.path).toBe('/login')
    expect(router.currentRoute.value.query.redirect).toBe('/checkout')
  })

  it('lets anonymous visitors browse public pages', async () => {
    await router.push('/products/CPU-AMD-9950X')

    expect(router.currentRoute.value.name).toBe('/products/[sku]')
  })
})

describe('safeRedirect', () => {
  it('allows same-site paths only', () => {
    expect(safeRedirect('/checkout')).toBe('/checkout')
    expect(safeRedirect('/products?q=ram')).toBe('/products?q=ram')
    expect(safeRedirect('//evil.example')).toBe('/')
    expect(safeRedirect('https://evil.example')).toBe('/')
    expect(safeRedirect(undefined)).toBe('/')
    expect(safeRedirect(['/a'])).toBe('/')
  })
})
