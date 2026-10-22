import { expect, test } from '@playwright/test'
import { PASSWORD, addToCart, createProduct, newCustomer, signedInAsCustomer } from '../../support/api'
import { urls } from '../../support/urls'

test.describe('the shopping cart', () => {
  test('a guest can add, change and remove items, and the cart survives a reload', async ({ page, request }) => {
    const product = await createProduct(request, { price: 40, stock: 10 })

    await page.goto(`/products/${product.sku}`)
    await page.getByRole('button', { name: 'Add to cart' }).click()
    await expect(page.getByRole('status').filter({ hasText: 'added to your cart' })).toBeVisible()
    await expect(page.getByRole('link', { name: 'Shopping cart' }).locator('.indicator-item')).toHaveText('1')

    await page.getByRole('link', { name: 'Shopping cart' }).click()
    const row = page.getByRole('row', { name: new RegExp(product.name) })
    await expect(row).toBeVisible()

    await page.getByLabel(`Quantity for ${product.name}`).selectOption('3')
    await expect(page.getByText('Subtotal (3 items)')).toBeVisible()
    await expect(page.getByText('$120.00').first()).toBeVisible()

    await page.reload()
    await expect(page.getByLabel(`Quantity for ${product.name}`)).toHaveValue('3')

    await page.getByRole('button', { name: `Remove ${product.name}` }).click()
    await expect(page.getByText('Your cart is empty.')).toBeVisible()
  })

  test('the quantity picker never offers more than is in stock', async ({ page, request }) => {
    const product = await createProduct(request, { stock: 2 })

    await page.goto(`/products/${product.sku}`)

    await expect(page.getByLabel('Qty').locator('option')).toHaveCount(2)
  })

  test('adding the same product again increases its quantity', async ({ page, request }) => {
    const product = await createProduct(request, { stock: 10 })
    await page.goto(`/products/${product.sku}`)

    await page.getByRole('button', { name: 'Add to cart' }).click()
    await expect(page.getByRole('link', { name: 'Shopping cart' }).locator('.indicator-item')).toHaveText('1')
    await page.getByRole('button', { name: 'Add to cart' }).click()

    await expect(page.getByRole('link', { name: 'Shopping cart' }).locator('.indicator-item')).toHaveText('2')
  })

  test('a signed-in customer finds their cart on any device', async ({ page, request }) => {
    const product = await createProduct(request, { stock: 10 })
    const customer = await newCustomer(request)
    await addToCart(request, customer, product.sku, 2)

    await signedInAsCustomer(page, customer)
    await page.goto('/cart')

    await expect(page.getByLabel(`Quantity for ${product.name}`)).toHaveValue('2')
    await expect(page.getByRole('button', { name: 'Proceed to checkout' })).toBeVisible()
  })

  test('a guest cart is merged into the account on sign-in, then checkout continues', async ({ page, request }) => {
    const product = await createProduct(request, { stock: 10 })
    const customer = await newCustomer(request)

    await page.goto(`/products/${product.sku}`)
    await page.getByRole('button', { name: 'Add to cart' }).click()
    await page.getByRole('link', { name: 'Shopping cart' }).click()

    // a guest is asked to sign in to check out, and brought straight back to checkout afterwards
    await page.getByRole('button', { name: 'Sign in to check out' }).click()
    await expect(page).toHaveURL(/\/login\?redirect=(%2F|\/)checkout/)
    await page.getByLabel('Email').fill(customer.email)
    await page.getByLabel('Password').fill(PASSWORD)
    await page.getByRole('button', { name: 'Sign in' }).click()
    await expect(page).toHaveURL(/\/checkout$/)
    await expect(page.getByText(`1 × ${product.name}`)).toBeVisible()

    // ...and the item is now in the customer's server-side cart
    const cart = await (await request.get(`${urls.api}/api/cart`, { headers: { Authorization: `Bearer ${customer.accessToken}` } })).json()
    expect(cart.items.map((i: { sku: string }) => i.sku)).toContain(product.sku)
  })

  test('@mobile the cart is usable on a phone', async ({ page, request }) => {
    const product = await createProduct(request, { price: 25, stock: 10 })
    await page.goto(`/products/${product.sku}`)
    await page.getByRole('button', { name: 'Add to cart' }).click()
    await page.getByRole('link', { name: 'Shopping cart' }).click()

    await expect(page.getByText('$25.00').first()).toBeVisible()
    expect(await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth)).toBe(false)
  })
})
