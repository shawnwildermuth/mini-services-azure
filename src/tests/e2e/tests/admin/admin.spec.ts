import { expect, test } from '@playwright/test'
import { expectAccessible } from '../../support/a11y'
import {
  adminDeadLetters, adminDiscardDeadLetter, adminGetOrder, createProduct, customerWithOrder, signedInAsAdmin, unique,
} from '../../support/api'
import { urls } from '../../support/urls'

// The admin site is a different origin, so every URL here is absolute.
const admin = (path = '') => `${urls.admin}${path}`

test.describe('signing in to the administration site', () => {
  test('anonymous visitors are sent to sign in and land where they were headed; sign-out locks the door again', async ({ page }) => {
    await page.goto(admin('/orders'))
    await expect(page).toHaveURL(/\/login\?redirect=(%2F|\/)orders/)
    await expect(page.getByRole('heading', { name: 'Parts Store Admin' })).toBeVisible()

    await page.getByRole('button', { name: 'Sign in (development)' }).click()
    await expect(page).toHaveURL(/\/orders$/)
    await expect(page.getByRole('heading', { name: 'Orders', exact: true })).toBeVisible()

    await page.getByRole('button', { name: 'Sign out' }).click()
    await expect(page).toHaveURL(/\/login/)
    await page.goto(admin('/products'))
    await expect(page).toHaveURL(/\/login/)
  })

  test('an expired or rejected session returns to sign-in with an explanation', async ({ page }) => {
    // a syntactically fine token that the API does not accept
    await page.addInitScript(() => {
      const claims = btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + 3600, roles: ['Admin'] }))
      sessionStorage.setItem('admin.devSession', JSON.stringify({ token: `e30.${claims}.bad`, name: 'Stale Admin' }))
    })

    await page.goto(admin('/orders'))

    await expect(page).toHaveURL(/\/login.*expired=1/)
    await expect(page.getByText('Your session ended')).toBeVisible()
  })
})

test.describe('managing the catalog', () => {
  test('a new product appears in the storefront, and price edits show up immediately', async ({ page, request }) => {
    await signedInAsAdmin(page, request)
    const category = await createProduct(request, { stock: 1 }) // just to get a category to put the product in
    const sku = unique('NEW-').toUpperCase()

    await page.goto(admin('/products/new'))
    await page.getByLabel('SKU').fill(sku)
    await page.getByLabel('Category').selectOption({ label: category.categoryName })
    await page.getByLabel('Name', { exact: true }).fill('Brand New Widget')
    await page.getByLabel('Brand').fill('Acme')
    await page.getByLabel('Price (USD)').fill('199.99')
    await page.getByLabel('Initial stock').fill('7')
    await page.getByRole('button', { name: '+ Add specification' }).click()
    await page.getByLabel('Specification 1 name').fill('socket')
    await page.getByLabel('Specification 1 value').fill('AM5')
    await page.getByRole('button', { name: 'Create product' }).click()
    await expect(page).toHaveURL(/\/products\/\d+$/)
    await expect(page.getByText('7 on hand')).toBeVisible()

    // the customer-facing site shows it (and warms its cache)...
    const storefront = await page.context().newPage()
    await storefront.goto(`${urls.store}/products/${sku}`)
    await expect(storefront.getByText('$199.99').first()).toBeVisible()

    // ...and a price change in the admin site is visible there on the very next load
    await page.getByLabel('Price (USD)').fill('321')
    await page.getByRole('button', { name: 'Save changes' }).click()
    await expect(page.getByRole('status').filter({ hasText: 'Product saved' })).toBeVisible()
    await storefront.reload()
    await expect(storefront.getByText('$321.00').first()).toBeVisible()
    await expect(storefront.getByText('$199.99')).toHaveCount(0)
  })

  test('deactivating a product hides it from customers but not from staff', async ({ page, request }) => {
    await signedInAsAdmin(page, request)
    const product = await createProduct(request)

    await page.goto(admin(`/products/${product.id}`))
    await page.getByRole('checkbox', { name: /Active|Inactive/ }).uncheck()
    await page.getByRole('button', { name: 'Save changes' }).click()
    await expect(page.getByRole('status').filter({ hasText: 'Product saved' })).toBeVisible()

    const storefront = await page.context().newPage()
    await storefront.goto(`${urls.store}/products/${product.sku}`)
    await expect(storefront.getByRole('heading', { name: 'Product not found' })).toBeVisible()

    await page.goto(admin(`/products?q=${product.sku}`))
    await page.getByLabel('Status').selectOption('inactive')
    await expect(page.getByRole('row', { name: new RegExp(product.sku) })).toBeVisible()
  })

  test('categories: a slug is suggested, duplicates are refused, and a category in use cannot be deleted', async ({ page, request }) => {
    await signedInAsAdmin(page, request)
    const used = await createProduct(request)
    const name = `E2E Gadgets ${unique()}`

    await page.goto(admin('/categories'))
    await page.getByLabel('Name', { exact: true }).fill(name)
    await expect(page.getByLabel('Slug (used in URLs)')).toHaveValue(/^e2e-gadgets-/)
    await page.getByRole('button', { name: 'Add', exact: true }).click()
    await expect(page.getByRole('row', { name: new RegExp(name) })).toBeVisible()

    // same slug again
    const slug = await page.getByRole('row', { name: new RegExp(name) }).locator('td').nth(1).innerText()
    await page.getByLabel('Name', { exact: true }).fill('Another name')
    await page.getByLabel('Slug (used in URLs)').fill(slug)
    await page.getByRole('button', { name: 'Add', exact: true }).click()
    await expect(page.getByRole('alert')).toContainText(/already in use/i)

    // a category with products can't be deleted
    await page.goto(admin('/categories'))
    await page.getByRole('row', { name: new RegExp(used.categoryName) }).getByRole('link', { name: 'Edit' }).click()
    await expect(page.getByRole('button', { name: 'Delete' })).toBeDisabled()

    // an empty one can
    await page.goto(admin('/categories'))
    await page.getByRole('row', { name: new RegExp(name) }).getByRole('link', { name: 'Edit' }).click()
    await page.getByRole('button', { name: 'Delete' }).click()
    await page.locator('dialog[open]').getByRole('button', { name: 'Delete' }).click()
    await expect(page).toHaveURL(/\/categories$/)
    await expect(page.getByRole('row', { name: new RegExp(name) })).toHaveCount(0)
  })

  test('stock can be corrected, but never below what open orders have reserved', async ({ page, request }) => {
    await signedInAsAdmin(page, request)
    const product = await createProduct(request, { stock: 10 })
    await customerWithOrder(request, { tag: '+error', sku: product.sku, quantity: 4 }) // 4 units stay reserved (payment never completes)

    await page.goto(admin(`/inventory?q=${product.sku}`))
    const stockInput = page.getByLabel(`On hand for ${product.name}`)
    await expect(stockInput).toHaveValue('10')

    await stockInput.fill('2')
    await page.getByRole('button', { name: 'Save' }).click()
    await expect(page.getByRole('status').filter({ hasText: /Cannot go below the 4 units reserved/ })).toBeVisible()

    await stockInput.fill('25')
    await page.getByRole('button', { name: 'Save' }).click()
    await expect(page.getByRole('status').filter({ hasText: '25 on hand' })).toBeVisible()
    const availability = await (await request.get(`${urls.api}/api/products/${product.sku}/availability`)).json()
    expect(availability.available).toBe(21) // 25 on hand - 4 reserved
  })
})

test.describe('handling orders', () => {
  test('a stuck order can be cancelled, which returns its stock', async ({ page, request }) => {
    await signedInAsAdmin(page, request)
    const product = await createProduct(request, { stock: 6 })
    const { order } = await customerWithOrder(request, { tag: '+error', sku: product.sku, quantity: 2 })

    await page.goto(admin(`/orders/${order.id}`))
    await expect(page.getByText('Awaiting payment').first()).toBeVisible()
    await page.getByRole('button', { name: 'Cancel order' }).click()
    await page.locator('dialog[open]').getByRole('button', { name: 'Cancel order' }).click()

    await expect(page.getByRole('status').filter({ hasText: 'Order cancelled' })).toBeVisible()
    await expect(page.getByText('Cancelled', { exact: true }).first()).toBeVisible()
    await expect(page.getByRole('button', { name: 'Cancel order' })).toHaveCount(0)
    const availability = await (await request.get(`${urls.api}/api/products/${product.sku}/availability`)).json()
    expect(availability.available).toBe(6)
  })

  test('a shipped order can be refunded', async ({ page, request }) => {
    await signedInAsAdmin(page, request)
    const { order } = await customerWithOrder(request)
    await expect.poll(async () => (await adminGetOrder(request, order.id)).order.status, { timeout: 45_000 }).toBe('Shipped')

    await page.goto(admin(`/orders/${order.id}`))
    await expect(page.getByText('Captured')).toBeVisible()
    await page.getByRole('button', { name: 'Refund order' }).click()
    await page.locator('dialog[open]').getByRole('button', { name: 'Refund order' }).click()

    await expect(page.getByRole('status').filter({ hasText: 'Order refunded' })).toBeVisible()
    await expect(page.getByText('Refunded').first()).toBeVisible()
    expect((await adminGetOrder(request, order.id)).payment?.status).toBe('Refunded')
  })

  test('orders can be found by number and filtered by status', async ({ page, request }) => {
    await signedInAsAdmin(page, request)
    const { order } = await customerWithOrder(request, { tag: '+error' })

    await page.goto(admin('/orders'))
    await page.getByLabel('Search orders').fill(order.orderNumber)
    await page.getByRole('button', { name: 'Search' }).click()
    await expect(page.getByRole('row', { name: new RegExp(order.orderNumber) })).toBeVisible()

    await page.getByLabel('Status').selectOption('Shipped')
    await expect(page.getByText('No orders match.')).toBeVisible()
  })
})

test.describe('operations', () => {
  test('a message that exhausted its retries shows up, links to its order, and replaying it is harmless once the order is cancelled', async ({ page, request }) => {
    test.setTimeout(150_000)
    await signedInAsAdmin(page, request)
    const { order } = await customerWithOrder(request, { tag: '+error' })

    // five failed deliveries, then the queue dead-letters it
    await expect.poll(async () => (await adminDeadLetters(request)).some((m) => m.orderId === order.id), { timeout: 90_000, intervals: [2000] }).toBe(true)

    await page.goto(admin('/operations/failed-messages'))
    const entry = page.locator('li', { has: page.locator(`a[href="/orders/${order.id}"]`) })
    await expect(entry).toContainText('MaxDeliveryCountExceeded')
    await expect(entry).toContainText('5 deliveries')

    await entry.getByRole('link', { name: 'View order' }).click()
    await page.getByRole('button', { name: 'Cancel order' }).click()
    await page.locator('dialog[open]').getByRole('button', { name: 'Cancel order' }).click()
    await expect(page.getByText('Cancelled', { exact: true }).first()).toBeVisible()

    await page.goto(admin('/operations/failed-messages'))
    await entry.getByRole('button', { name: 'Replay' }).click()
    await page.locator('dialog[open]').getByRole('button', { name: 'Replay' }).click()
    await expect(page.getByRole('status').filter({ hasText: 'Message sent back for processing' })).toBeVisible()
    await expect(entry).toHaveCount(0)

    // processing it again must not charge or ship the cancelled order
    await page.waitForTimeout(4000)
    const final = await adminGetOrder(request, order.id)
    expect(final.order.status).toBe('Cancelled')
    expect(final.payment ?? null).toBeNull()
  })

  test('a failed message can be discarded', async ({ page, request }) => {
    test.setTimeout(150_000)
    await signedInAsAdmin(page, request)
    const { order } = await customerWithOrder(request, { tag: '+error' })
    await expect.poll(async () => (await adminDeadLetters(request)).some((m) => m.orderId === order.id), { timeout: 90_000, intervals: [2000] }).toBe(true)

    await page.goto(admin('/operations/failed-messages'))
    const entry = page.locator('li', { has: page.locator(`a[href="/orders/${order.id}"]`) })
    await entry.getByRole('button', { name: 'Discard' }).click()
    await page.locator('dialog[open]').getByRole('button', { name: 'Discard' }).click()

    await expect(page.getByRole('status').filter({ hasText: 'Message discarded' })).toBeVisible()
    await expect(entry).toHaveCount(0)
  })
})

test.describe('administration accessibility and phones', () => {
  test('main pages meet WCAG 2.1 A/AA', async ({ page, request }) => {
    await signedInAsAdmin(page, request)
    const product = await createProduct(request)

    for (const [path, ready] of [
      ['/', 'Orders by status'],
      ['/products', 'Products'],
      [`/products/${product.id}`, product.name],
      ['/categories', 'Add a category'],
      ['/inventory', 'Inventory'],
      ['/orders', 'Orders'],
      ['/operations/failed-messages', 'Failed messages'],
    ] as const) {
      await page.goto(admin(path))
      await expect(page.getByText(ready, { exact: false }).first()).toBeVisible()
      await expect(page.locator('.skeleton')).toHaveCount(0)
      await expectAccessible(page, `admin ${path}`)
    }
  })

  test('the sign-in page meets WCAG 2.1 A/AA', async ({ page }) => {
    await page.goto(admin('/login'))
    await expect(page.getByRole('button', { name: 'Sign in (development)' })).toBeVisible()
    await expectAccessible(page, 'the admin sign-in page')
  })

  test('@mobile the navigation drawer works on a phone', async ({ page, request }) => {
    await signedInAsAdmin(page, request)
    await page.goto(admin('/'))
    await expect(page.getByText('Orders by status')).toBeVisible()
    expect(await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth)).toBe(false)

    await page.getByLabel('Open navigation').click()
    await page.getByRole('link', { name: 'Orders' }).click()

    await expect(page).toHaveURL(/\/orders$/)
    await expect(page.getByRole('heading', { name: 'Orders', exact: true })).toBeVisible()
  })
})
