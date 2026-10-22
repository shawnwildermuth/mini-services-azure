import { expect, test } from '@playwright/test'
import { expectAccessible } from '../../support/a11y'
import { addAddress, addToCart, createProduct, customerWithOrder, newCustomer, signedInAsCustomer } from '../../support/api'

// Each page is scanned once its content has rendered. Run in light theme (the default); dark is covered in the theme test below.
test.describe('storefront accessibility (WCAG 2.1 A/AA)', () => {
  test('home', async ({ page }) => {
    await page.goto('/')
    await expect(page.getByRole('link', { name: /Processors/ }).first()).toBeVisible()
    await expectAccessible(page, 'the home page')
  })

  test('product list with filters', async ({ page }) => {
    await page.goto('/categories/motherboards')
    await expect(page.getByRole('article')).toHaveCount(6)
    await expectAccessible(page, 'the category page')
  })

  test('product detail', async ({ page, request }) => {
    const product = await createProduct(request, { stock: 5 })
    await page.goto(`/products/${product.sku}`)
    await expect(page.getByRole('heading', { level: 1, name: product.name })).toBeVisible()
    await expectAccessible(page, 'the product page')
  })

  test('cart with items', async ({ page, request }) => {
    const product = await createProduct(request, { stock: 5 })
    await page.goto(`/products/${product.sku}`)
    await page.getByRole('button', { name: 'Add to cart' }).click()
    await page.getByRole('link', { name: 'Shopping cart' }).click()
    await expect(page.getByLabel(`Quantity for ${product.name}`)).toBeVisible()
    await expectAccessible(page, 'the cart')
  })

  for (const [name, path, heading] of [
    ['sign in', '/login', 'Sign in'],
    ['register', '/register', 'Create your account'],
    ['forgot password', '/forgot-password', 'Reset your password'],
  ] as const) {
    test(name, async ({ page }) => {
      await page.goto(path)
      await expect(page.getByRole('heading', { name: heading })).toBeVisible()
      await expectAccessible(page, `the ${name} page`)
    })
  }

  test('checkout, account and orders (signed in)', async ({ page, request }) => {
    const product = await createProduct(request, { stock: 5 })
    const customer = await newCustomer(request)
    await addAddress(request, customer)
    await addToCart(request, customer, product.sku, 1)
    await signedInAsCustomer(page, customer)

    await page.goto('/checkout')
    await expect(page.getByRole('button', { name: 'Place order' })).toBeEnabled()
    await expectAccessible(page, 'checkout')

    await page.goto('/account')
    await expect(page.getByRole('heading', { name: 'Addresses' })).toBeVisible()
    await expectAccessible(page, 'the account page')

    await page.goto('/account/orders')
    await expect(page.getByRole('heading', { name: 'My orders' })).toBeVisible()
    await expectAccessible(page, 'order history')
  })

  test('order pages in each state (in progress, shipped, payment failed)', async ({ page, request }) => {
    const inProgress = await customerWithOrder(request, { tag: '+error' })
    const shipped = await customerWithOrder(request)
    const declined = await customerWithOrder(request, { tag: '+decline' })

    await signedInAsCustomer(page, inProgress.customer)
    await page.goto(`/account/orders/${inProgress.order.id}`)
    await expect(page.getByText('Awaiting payment').first()).toBeVisible()
    await expectAccessible(page, 'an order awaiting payment')

    await signedInAsCustomer(page, shipped.customer)
    await page.goto(`/account/orders/${shipped.order.id}`)
    await expect(page.locator('[data-test=tracking]')).toBeVisible({ timeout: 45_000 })
    await expectAccessible(page, 'a shipped order')

    await signedInAsCustomer(page, declined.customer)
    await page.goto(`/account/orders/${declined.order.id}`)
    await expect(page.getByText('We could not take payment for this order')).toBeVisible({ timeout: 45_000 })
    await expectAccessible(page, 'an order whose payment failed')
  })

  test('the dark theme is also accessible', async ({ page }) => {
    await page.emulateMedia({ colorScheme: 'dark' })
    await page.goto('/categories/cpus')
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark')
    await expect(page.getByRole('article').first()).toBeVisible()
    await expectAccessible(page, 'the category page in the dark theme')
  })
})
