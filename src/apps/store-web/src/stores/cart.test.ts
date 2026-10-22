import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

const api = vi.hoisted(() => ({ GET: vi.fn(), PUT: vi.fn(), DELETE: vi.fn(), POST: vi.fn() }))
vi.mock('@/api/client', () => ({ api }))

import { useAuthStore } from './auth'
import { useCartStore } from './cart'

const cpu = { sku: 'CPU-1', name: 'Ryzen', brand: 'AMD', price: 300 }
const ram = { sku: 'RAM-1', name: 'DDR5', brand: 'Corsair', price: 100 }

beforeEach(() => {
  localStorage.clear()
  vi.clearAllMocks()
  setActivePinia(createPinia())
})

describe('guest cart', () => {
  it('adds, totals and persists lines locally', async () => {
    const cart = useCartStore()

    await cart.add(cpu)
    await cart.add(cpu, 2)
    await cart.add(ram)

    expect(cart.itemCount).toBe(4)
    expect(cart.subtotal).toBe(300 * 3 + 100)
    expect(JSON.parse(localStorage.getItem('store.guestCart')!)).toHaveLength(2)
    expect(api.PUT).not.toHaveBeenCalled()
  })

  it('restores lines from storage on startup', () => {
    localStorage.setItem(
      'store.guestCart',
      JSON.stringify([{ sku: 'CPU-1', name: 'Ryzen', brand: 'AMD', unitPrice: 300, quantity: 2 }]),
    )

    expect(useCartStore().itemCount).toBe(2)
  })

  it('enforces the per-item limit', async () => {
    const cart = useCartStore()
    await cart.setQuantity(cpu, 20)

    const result = await cart.add(cpu)

    expect(result.ok).toBe(false)
    expect(cart.itemCount).toBe(20)
  })

  it('removes lines, and treats quantity 0 as removal', async () => {
    const cart = useCartStore()
    await cart.add(cpu)
    await cart.add(ram)

    await cart.remove('CPU-1')
    await cart.setQuantity(ram, 0)

    expect(cart.lines).toEqual([])
    expect(localStorage.getItem('store.guestCart')).toBeNull()
  })
})

describe('signing in', () => {
  it('merges the guest cart into the server cart, then clears local storage', async () => {
    localStorage.setItem(
      'store.guestCart',
      JSON.stringify([
        { sku: 'CPU-1', name: 'Ryzen', brand: 'AMD', unitPrice: 300, quantity: 2 },
        { sku: 'RAM-1', name: 'DDR5', brand: 'Corsair', unitPrice: 100, quantity: 1 },
      ]),
    )
    const serverItem = { sku: 'CPU-1', name: 'Ryzen', brand: 'AMD', unitPrice: 300, quantity: 1, lineTotal: 300, available: 9 }
    api.GET.mockResolvedValue({ data: { items: [serverItem], itemCount: 1, subtotal: 300 } })
    api.PUT.mockResolvedValue({ data: { items: [] } })

    const cart = useCartStore()
    useAuthStore().email = 'shopper@example.com'
    await flushPromises()

    // 1 already on the server + 2 from the guest cart; the new line is added as-is.
    expect(api.PUT).toHaveBeenCalledWith('/api/cart/items/{sku}', {
      params: { path: { sku: 'CPU-1' } },
      body: { quantity: 3 },
    })
    expect(api.PUT).toHaveBeenCalledWith('/api/cart/items/{sku}', {
      params: { path: { sku: 'RAM-1' } },
      body: { quantity: 1 },
    })
    expect(localStorage.getItem('store.guestCart')).toBeNull()
    expect(cart.lines.map((l) => l.sku)).toEqual(['CPU-1']) // reloaded from the server
  })

  it('reports server-side stock rejections', async () => {
    api.GET.mockResolvedValue({ data: { items: [] } })
    const cart = useCartStore()
    useAuthStore().email = 'shopper@example.com'
    await flushPromises()

    api.PUT.mockResolvedValue({
      data: undefined,
      error: { errors: { Quantity: ['Only 2 available.'] } },
      response: { status: 400 },
    })
    const result = await cart.setQuantity(cpu, 5)

    expect(result).toEqual({ ok: false, message: 'Only 2 available.' })
  })
})
