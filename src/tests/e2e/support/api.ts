import { expect, type APIRequestContext, type Page } from '@playwright/test'
import { urls } from './urls'

/**
 * Test data is created through the API (fast and deterministic), then the browser is used for what is being tested.
 * Every helper makes brand-new, uniquely named data, so tests never depend on each other or on the order they run in.
 */

let counter = 0
export const unique = (prefix = '') => `${prefix}${Date.now().toString(36)}${(counter++).toString(36)}${Math.floor(Math.random() * 1e4).toString(36)}`

export const PASSWORD = 'Passw0rd!'

export interface Customer {
  email: string
  password: string
  accessToken: string
  refreshToken: string
}

async function json<T>(response: Awaited<ReturnType<APIRequestContext['get']>>): Promise<T> {
  expect(response.ok(), `${response.url()} -> ${response.status()} ${await response.text()}`).toBeTruthy()
  // some endpoints (register, delete) answer with no body
  const text = await response.text()
  return (text ? JSON.parse(text) : undefined) as T
}

/** Registers and signs in a customer. `tag` (e.g. "+decline") selects a simulated payment/carrier behavior. */
export async function newCustomer(request: APIRequestContext, tag = ''): Promise<Customer> {
  const email = `${unique('e2e-')}${tag}@example.com`
  await json(await request.post(`${urls.api}/api/auth/register`, { data: { email, password: PASSWORD } }))
  const tokens = await json<{ accessToken: string; refreshToken: string }>(
    await request.post(`${urls.api}/api/auth/login`, { data: { email, password: PASSWORD } }),
  )
  return { email, password: PASSWORD, ...tokens }
}

const bearer = (c: Customer) => ({ Authorization: `Bearer ${c.accessToken}` })

export async function addAddress(request: APIRequestContext, c: Customer, countryCode = 'US', fullName = 'Ada Lovelace') {
  return json<{ id: number }>(
    await request.post(`${urls.api}/api/addresses`, {
      headers: bearer(c),
      data: { fullName, line1: '1 Main St', city: 'Atlanta', region: 'GA', postalCode: '30301', countryCode },
    }),
  )
}

export async function addToCart(request: APIRequestContext, c: Customer, sku: string, quantity = 1) {
  await json(await request.put(`${urls.api}/api/cart/items/${sku}`, { headers: bearer(c), data: { quantity } }))
}

export async function placeOrder(request: APIRequestContext, c: Customer, addressId: number) {
  return json<{ id: string; orderNumber: string; status: string; total: number }>(
    await request.post(`${urls.api}/api/orders`, { headers: bearer(c), data: { addressId } }),
  )
}

export async function orderStatus(request: APIRequestContext, c: Customer, id: string) {
  return (await json<{ status: string }>(await request.get(`${urls.api}/api/orders/${id}`, { headers: bearer(c) }))).status
}

/**
 * A customer with an order already placed (and the cart emptied by checkout). Uses its own freshly created product unless
 * a SKU is given, so it never competes with other tests for stock.
 */
export async function customerWithOrder(request: APIRequestContext, opts: { tag?: string; country?: string; sku?: string; quantity?: number } = {}) {
  const product = opts.sku ? undefined : await createProduct(request, { stock: 20 })
  const customer = await newCustomer(request, opts.tag)
  const address = await addAddress(request, customer, opts.country ?? 'US')
  await addToCart(request, customer, opts.sku ?? product!.sku, opts.quantity ?? 1)
  const order = await placeOrder(request, customer, address.id)
  return { customer, order, product }
}

// ----- admin -----

export async function adminToken(request: APIRequestContext): Promise<string> {
  return (await json<{ accessToken: string }>(await request.post(`${urls.api}/api/dev/admin-token`))).accessToken
}

async function admin<T>(request: APIRequestContext, method: 'get' | 'post' | 'put' | 'delete', path: string, data?: unknown) {
  const headers = { Authorization: `Bearer ${await adminToken(request)}` }
  const response = await request[method](`${urls.api}/api/admin${path}`, { headers, data })
  return json<T>(response)
}

/** A new product in a brand-new category with the given stock; returns what tests need to find it. */
export async function createProduct(
  request: APIRequestContext,
  opts: { name?: string; price?: number; stock?: number; specs?: { name: string; value: string }[]; sameCategoryAs?: { categoryId: number; categoryName: string; categorySlug: string } } = {},
) {
  const slug = opts.sameCategoryAs?.categorySlug ?? unique('cat-')
  const category = opts.sameCategoryAs
    ? { id: opts.sameCategoryAs.categoryId, name: opts.sameCategoryAs.categoryName }
    : await admin<{ id: number; name: string }>(request, 'post', '/categories', { name: `Category ${slug}`, slug })
  const sku = unique('E2E-').toUpperCase()
  const product = await admin<{ id: number }>(request, 'post', '/products', {
    sku,
    name: opts.name ?? `Widget ${sku}`,
    brand: 'Acme',
    description: 'End-to-end test product',
    price: opts.price ?? 100,
    categoryId: category.id,
    isActive: true,
    initialStock: opts.stock ?? 10,
    specs: opts.specs ?? [{ name: 'socket', value: 'AM5' }],
  })
  return { id: product.id, sku, name: opts.name ?? `Widget ${sku}`, categoryId: category.id, categoryName: category.name, categorySlug: slug }
}

export const adminGetOrder = (request: APIRequestContext, id: string) =>
  admin<{ order: { status: string; shipment?: { trackingNumber: string } | null }; payment?: { status: string } | null }>(request, 'get', `/orders/${id}`)

export const adminDeadLetters = (request: APIRequestContext) =>
  admin<{ queue: string; sequenceNumber: number; orderId?: string }[]>(request, 'get', '/operations/dead-letters')

export const adminDiscardDeadLetter = (request: APIRequestContext, queue: string, sequenceNumber: number) =>
  admin<unknown>(request, 'delete', `/operations/dead-letters/${queue}/${sequenceNumber}`).catch(() => undefined)

// ----- signing in the browser without clicking through the form -----

/** Starts the page already signed in as `c` (the storefront restores a session from its stored refresh token). */
export async function signedInAsCustomer(page: Page, c: Customer) {
  await page.addInitScript((token) => localStorage.setItem('store.refreshToken', token), c.refreshToken)
}

/** Starts the admin site already signed in (development sign-in). */
export async function signedInAsAdmin(page: Page, request: APIRequestContext) {
  const token = await adminToken(request)
  await page.addInitScript((session) => sessionStorage.setItem('admin.devSession', session), JSON.stringify({ token, name: 'Dev Admin' }))
}