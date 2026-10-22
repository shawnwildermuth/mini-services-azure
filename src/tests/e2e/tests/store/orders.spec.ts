import { expect, test, type Page } from '@playwright/test'
import { addAddress, addToCart, createProduct, customerWithOrder, newCustomer, signedInAsCustomer } from '../../support/api'
import { urls } from '../../support/urls'

/** Fills the checkout address form (shown when the customer has no saved address). */
async function fillAddress(page: Page, country = 'US') {
  await page.getByLabel('Full name').fill('Ada Lovelace')
  await page.getByLabel('Address', { exact: true }).fill('1 Main St')
  await page.getByLabel('City').fill('Atlanta')
  await page.getByLabel('Postal code').fill('30301')
  await page.getByLabel('Country').selectOption(country)
  await page.getByRole('button', { name: 'Save and use this address' }).click()
}

test.describe('checkout, payment and shipping', () => {
  test('a customer checks out, and the order is paid and shipped by UPS without any further action', async ({ page, request }) => {
    const product = await createProduct(request, { price: 100, stock: 5 })
    const customer = await newCustomer(request)
    await addToCart(request, customer, product.sku, 2)
    await signedInAsCustomer(page, customer)

    await page.goto('/checkout')
    await fillAddress(page, 'US')
    await expect(page.getByText('UPS', { exact: true })).toBeVisible()
    await expect(page.getByText('$200.00').first()).toBeVisible() // subtotal
    await expect(page.getByText('$212.00')).toBeVisible() // + $12 UPS shipping

    await page.getByRole('button', { name: 'Place order' }).click()

    await expect(page).toHaveURL(/\/account\/orders\/[0-9a-f-]{36}$/)
    // The page refreshes itself as billing and fulfillment complete in the background.
    await expect(page.getByText('Shipped', { exact: true }).first()).toBeVisible({ timeout: 45_000 })
    await expect(page.getByRole('heading', { name: 'Shipment' })).toBeVisible()
    await expect(page.locator('[data-test=tracking]')).toHaveText(/^1Z[0-9A-Z]{16}$/)
    await expect(page.getByText('UPS Ground')).toBeVisible()
    await expect(page.getByText('$212.00')).toBeVisible()

    // the cart was emptied by checkout
    await page.goto('/cart')
    await expect(page.getByText('Your cart is empty.')).toBeVisible()
  })

  test('an international address is quoted and shipped with DHL', async ({ page, request }) => {
    const product = await createProduct(request, { price: 50, stock: 5 })
    const customer = await newCustomer(request)
    await addToCart(request, customer, product.sku, 1)
    await signedInAsCustomer(page, customer)

    await page.goto('/checkout')
    await fillAddress(page, 'DE')
    await expect(page.getByText('DHL', { exact: true })).toBeVisible()
    await expect(page.getByText('$79.00')).toBeVisible() // 50 + 29 DHL

    await page.getByRole('button', { name: 'Place order' }).click()

    await expect(page.getByText('Shipped', { exact: true }).first()).toBeVisible({ timeout: 45_000 })
    await expect(page.locator('[data-test=tracking]')).toHaveText(/^\d{10}$/)
    await expect(page.getByText('DHL Express Worldwide')).toBeVisible()
  })

  test('the shipping quote follows the chosen address', async ({ page, request }) => {
    const product = await createProduct(request, { price: 10, stock: 5 })
    const customer = await newCustomer(request)
    await addAddress(request, customer, 'US', 'Home Address')
    await addAddress(request, customer, 'GB', 'Work Address')
    await addToCart(request, customer, product.sku, 1)
    await signedInAsCustomer(page, customer)

    await page.goto('/checkout')
    await page.getByRole('radio', { name: /Home Address/ }).check()
    await expect(page.getByText('$22.00')).toBeVisible() // 10 + 12 UPS
    await page.getByRole('radio', { name: /Work Address/ }).check()
    await expect(page.getByText('$39.00')).toBeVisible() // 10 + 29 DHL
  })

  test('a declined card ends as "Payment failed" and puts the stock back', async ({ page, request }) => {
    const product = await createProduct(request, { stock: 4 })
    const { customer, order } = await customerWithOrder(request, { tag: '+decline', sku: product.sku, quantity: 3 })

    await signedInAsCustomer(page, customer)
    await page.goto(`/account/orders/${order.id}`)

    await expect(page.getByText('Payment failed').first()).toBeVisible({ timeout: 45_000 })
    await expect(page.getByText('We could not take payment for this order')).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Shipment' })).toHaveCount(0)
    const availability = await (await request.get(`${urls.api}/api/products/${product.sku}/availability`)).json()
    expect(availability.available).toBe(4) // the 3 reserved units were released
  })

  test('a payment-provider outage leaves the order awaiting payment instead of failing it', async ({ page, request }) => {
    const { customer, order } = await customerWithOrder(request, { tag: '+error' })

    await signedInAsCustomer(page, customer)
    await page.goto(`/account/orders/${order.id}`)

    await expect(page.getByText('Awaiting payment').first()).toBeVisible()
    await page.waitForTimeout(6000) // long enough for several delivery attempts
    await expect(page.getByText('Awaiting payment').first()).toBeVisible()
    await expect(page.getByText('Payment failed')).toHaveCount(0)
  })

  test('if the last unit is bought while the customer is on the checkout page, placing the order says so and keeps the cart', async ({ page, request }) => {
    const product = await createProduct(request, { stock: 1 })
    const slow = await newCustomer(request)
    const quick = await newCustomer(request)
    await addToCart(request, slow, product.sku, 1)
    await addToCart(request, quick, product.sku, 1) // both can hold it in their cart...
    await addAddress(request, slow)
    const quickAddress = await addAddress(request, quick)

    await signedInAsCustomer(page, slow)
    await page.goto('/checkout')
    await expect(page.getByRole('button', { name: 'Place order' })).toBeEnabled()

    // ...and the other customer checks out while this one is still looking at the page
    await request.post(`${urls.api}/api/orders`, { headers: { Authorization: `Bearer ${quick.accessToken}` }, data: { addressId: quickAddress.id } })
    await page.getByRole('button', { name: 'Place order' }).click()

    await expect(page.getByRole('alert').filter({ hasText: 'out of stock' })).toBeVisible()
    await expect(page).toHaveURL(/\/checkout$/)
    await page.goto('/cart')
    await expect(page.getByRole('link', { name: product.name })).toBeVisible()
  })

  test('if the stock is already gone when checkout opens, it warns up front and does not allow ordering', async ({ page, request }) => {
    const product = await createProduct(request, { stock: 1 })
    const late = await newCustomer(request)
    const first = await newCustomer(request)
    await addToCart(request, late, product.sku, 1)
    await addToCart(request, first, product.sku, 1)
    await addAddress(request, late)
    const firstAddress = await addAddress(request, first)
    await request.post(`${urls.api}/api/orders`, { headers: { Authorization: `Bearer ${first.accessToken}` }, data: { addressId: firstAddress.id } })

    await signedInAsCustomer(page, late)
    await page.goto('/checkout')

    await expect(page.getByText('Some items are no longer available in the quantity you chose.')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Place order' })).toBeDisabled()
  })
  test('double-clicking "Place order" creates exactly one order', async ({ page, request }) => {
    const product = await createProduct(request, { stock: 5 })
    const customer = await newCustomer(request)
    await addAddress(request, customer)
    await addToCart(request, customer, product.sku, 1)
    await signedInAsCustomer(page, customer)

    await page.goto('/checkout')
    await page.getByRole('button', { name: 'Place order' }).dblclick()
    await expect(page).toHaveURL(/\/account\/orders\/[0-9a-f-]{36}$/)

    const orders = await (await request.get(`${urls.api}/api/orders`, { headers: { Authorization: `Bearer ${customer.accessToken}` } })).json()
    expect(orders.totalCount).toBe(1)
  })

  test('order history lists past orders and links to each one', async ({ page, request }) => {
    const { customer, order } = await customerWithOrder(request)
    await signedInAsCustomer(page, customer)

    await page.goto('/account/orders')
    await page.getByRole('link', { name: new RegExp(order.orderNumber) }).click()

    await expect(page).toHaveURL(new RegExp(`/account/orders/${order.id}$`))
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(order.orderNumber)
  })

  test("a customer cannot open someone else's order", async ({ page, request }) => {
    const { order } = await customerWithOrder(request)
    const stranger = await newCustomer(request)
    await signedInAsCustomer(page, stranger)

    await page.goto(`/account/orders/${order.id}`)

    await expect(page.getByRole('heading', { name: 'Order not found' })).toBeVisible()
  })
})
