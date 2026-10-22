import { computed, ref, watch } from 'vue'
import { defineStore } from 'pinia'
import { api, type Schemas } from '@/api/client'
import { fail, ok, problemMessage, type Result } from '@/api/errors'
import { MAX_CART_QUANTITY } from '@/config'
import { useAuthStore } from './auth'

export interface CartLine {
  sku: string
  name: string
  brand: string
  unitPrice: number
  quantity: number
  lineTotal: number
  /** Units in stock; unknown (null) for guest carts until the customer signs in. */
  available: number | null
}

/** The product fields a cart line needs; guest lines are stored locally with a price snapshot. */
export interface ProductRef {
  sku: string
  name: string
  brand: string
  price: number
}

type GuestLine = Pick<CartLine, 'sku' | 'name' | 'brand' | 'unitPrice' | 'quantity'>

const GUEST_KEY = 'store.guestCart'

function loadGuest(): GuestLine[] {
  try {
    const parsed: unknown = JSON.parse(localStorage.getItem(GUEST_KEY) ?? '[]')
    return Array.isArray(parsed) ? (parsed as GuestLine[]) : []
  } catch {
    return []
  }
}

function saveGuest(lines: GuestLine[]) {
  try {
    if (lines.length) localStorage.setItem(GUEST_KEY, JSON.stringify(lines))
    else localStorage.removeItem(GUEST_KEY)
  } catch {
    // storage unavailable; the guest cart just won't survive a reload
  }
}

const fromGuest = (g: GuestLine): CartLine => ({
  ...g,
  lineTotal: g.unitPrice * g.quantity,
  available: null,
})

const fromServer = (item: Schemas['CartItemDto']): CartLine => ({ ...item })

/**
 * The cart. Signed-in customers use the server cart; guests keep a local one, which is merged
 * into the server cart when they sign in.
 */
export const useCartStore = defineStore('cart', () => {
  const auth = useAuthStore()
  const lines = ref<CartLine[]>(loadGuest().map(fromGuest))
  const loading = ref(false)

  const itemCount = computed(() => lines.value.reduce((sum, l) => sum + l.quantity, 0))
  const subtotal = computed(() => lines.value.reduce((sum, l) => sum + l.lineTotal, 0))

  async function load() {
    if (!auth.isSignedIn) {
      lines.value = loadGuest().map(fromGuest)
      return
    }
    loading.value = true
    try {
      const { data } = await api.GET('/api/cart')
      if (data) lines.value = data.items.map(fromServer)
    } finally {
      loading.value = false
    }
  }

  async function mergeGuestCart() {
    const guest = loadGuest()
    await load()
    if (!guest.length) return

    for (const g of guest) {
      const existing = lines.value.find((l) => l.sku === g.sku)?.quantity ?? 0
      // Lines that no longer fit (out of stock, removed from the catalog) are dropped silently.
      await api.PUT('/api/cart/items/{sku}', {
        params: { path: { sku: g.sku } },
        body: { quantity: Math.min(existing + g.quantity, MAX_CART_QUANTITY) },
      })
    }
    saveGuest([])
    await load()
  }

  // Runs immediately (restores the right cart on load) and again on every sign-in / sign-out.
  watch(
    () => auth.isSignedIn,
    (signedIn) => (signedIn ? mergeGuestCart() : load()),
    { immediate: true },
  )

  async function setQuantity(product: ProductRef, quantity: number): Promise<Result> {
    if (quantity < 1) return remove(product.sku)
    if (quantity > MAX_CART_QUANTITY) return fail(`You can order at most ${MAX_CART_QUANTITY} of an item.`)

    if (!auth.isSignedIn) {
      const guest = loadGuest().filter((g) => g.sku !== product.sku)
      guest.push({ sku: product.sku, name: product.name, brand: product.brand, unitPrice: product.price, quantity })
      guest.sort((a, b) => a.name.localeCompare(b.name))
      saveGuest(guest)
      lines.value = guest.map(fromGuest)
      return ok()
    }

    const { data, error, response } = await api.PUT('/api/cart/items/{sku}', {
      params: { path: { sku: product.sku } },
      body: { quantity },
    })
    if (data) {
      lines.value = data.items.map(fromServer)
      return ok()
    }
    if (response.status === 404) return fail('This product is no longer available.')
    return fail(problemMessage(error, 'We could not update your cart.'))
  }

  /** Adds units to the line for this product. */
  function add(product: ProductRef, quantity = 1): Promise<Result> {
    const current = lines.value.find((l) => l.sku === product.sku)?.quantity ?? 0
    if (current + quantity > MAX_CART_QUANTITY)
      return Promise.resolve(fail(`You can order at most ${MAX_CART_QUANTITY} of an item.`))
    return setQuantity(product, current + quantity)
  }

  async function remove(sku: string): Promise<Result> {
    if (!auth.isSignedIn) {
      const guest = loadGuest().filter((g) => g.sku !== sku)
      saveGuest(guest)
      lines.value = guest.map(fromGuest)
      return ok()
    }

    const { data } = await api.DELETE('/api/cart/items/{sku}', { params: { path: { sku } } })
    if (!data) return fail('We could not update your cart.')
    lines.value = data.items.map(fromServer)
    return ok()
  }

  async function clear() {
    if (auth.isSignedIn) await api.DELETE('/api/cart')
    saveGuest([])
    lines.value = []
  }

  return { lines, loading, itemCount, subtotal, load, setQuantity, add, remove, clear }
})
