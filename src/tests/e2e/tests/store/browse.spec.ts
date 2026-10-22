import { expect, test } from '@playwright/test'
import { createProduct } from '../../support/api'

const price = (text: string) => Number(text.replace(/[^0-9.]/g, ''))

test.describe('browsing the catalog', () => {
  test('the home page leads into a category', async ({ page }) => {
    await page.goto('/')
    await expect(page.getByRole('heading', { name: 'Build your next machine' })).toBeVisible()

    await page.getByRole('link', { name: /Processors/ }).first().click()

    await expect(page).toHaveURL(/\/categories\/cpus$/)
    await expect(page.getByRole('heading', { level: 1, name: 'Processors' })).toBeVisible()
    await expect(page.getByRole('article')).toHaveCount(7)
  })

  test('spec filters narrow the results, live in the URL, and can be cleared', async ({ page }) => {
    await page.goto('/categories/motherboards')
    await expect(page.getByRole('article')).toHaveCount(6)

    await page.getByLabel('AM5').check()
    await expect(page.getByRole('article')).toHaveCount(3)
    await expect(page).toHaveURL(/spec=socket(%3A|:)AM5/)

    // different spec names are ANDed
    await page.getByLabel('ATX', { exact: true }).check()
    await expect(page.getByRole('article')).toHaveCount(2)

    // the filtered view survives a reload (it lives in the URL)
    await page.reload()
    await expect(page.getByRole('article')).toHaveCount(2)
    await expect(page.getByLabel('AM5')).toBeChecked()

    await page.getByRole('button', { name: 'Clear filters' }).click()
    await expect(page.getByRole('article')).toHaveCount(6)
  })

  test('sorting by price puts the products in price order', async ({ page }) => {
    await page.goto('/products?category=gpus&sort=price')
    await expect(page.getByRole('article').first()).toBeVisible() // wait for results before reading them
    const prices = (await page.locator('article p.font-bold').allTextContents()).map(price)
    expect(prices.length).toBeGreaterThan(3)
    expect(prices).toEqual([...prices].sort((a, b) => a - b))

    await page.getByLabel('Sort by').selectOption('-price')
    await expect(page).toHaveURL(/sort=-price/)
    // wait for the re-sorted results to arrive (the URL changes first)
    await expect
      .poll(async () => {
        const shown = (await page.locator('article p.font-bold').allTextContents()).map(price)
        return shown.length > 3 && shown.every((p, i) => i === 0 || shown[i - 1]! >= p)
      })
      .toBe(true)
  })

  test('the price range filter applies', async ({ page }) => {
    await page.goto('/products?category=memory')
    await page.getByLabel('Minimum price').fill('100')
    await page.getByLabel('Maximum price').fill('150')
    await page.getByRole('button', { name: 'Set' }).click()

    await expect(page).toHaveURL(/minPrice=100/)
    await expect
      .poll(async () => {
        const shown = (await page.locator('article p.font-bold').allTextContents()).map(price)
        return shown.length > 0 && shown.every((p) => p >= 100 && p <= 150)
      })
      .toBe(true)
  })

  test('searching from the navbar finds products by brand', async ({ page }) => {
    await page.goto('/')
    await page.getByRole('searchbox', { name: 'Search parts' }).fill('Corsair')
    await page.getByRole('searchbox', { name: 'Search parts' }).press('Enter')

    await expect(page).toHaveURL(/\/products\?q=Corsair/)
    await expect(page.getByRole('heading', { level: 1 })).toContainText('Corsair')
    await expect(page.getByRole('article')).toHaveCount(6)
  })

  test('a search with no matches says so and offers a way out', async ({ page }) => {
    await page.goto('/products?q=zzzznothingmatches')

    await expect(page.getByText('No products match your filters.')).toBeVisible()
    await page.getByRole('button', { name: 'Clear filters' }).first().click()
    await expect(page.getByRole('article').first()).toBeVisible()
  })

  test('a category with more than one page of products is paged', async ({ page, request }) => {
    // its own category, so products other tests create at the same time can't shift the pages
    const first = await createProduct(request, { stock: 1 })
    for (let i = 0; i < 13; i++) await createProduct(request, { stock: 1, sameCategoryAs: first })

    await page.goto(`/categories/${first.categorySlug}`)
    await expect(page.getByRole('article')).toHaveCount(12)
    const firstPage = await page.locator('article h2').allTextContents()

    await page.getByRole('navigation', { name: 'Pagination' }).getByRole('button', { name: '2', exact: true }).click()

    await expect(page).toHaveURL(/page=2/)
    await expect(page.getByRole('article')).toHaveCount(2)
    const secondPage = await page.locator('article h2').allTextContents()
    expect(secondPage.filter((name) => firstPage.includes(name))).toEqual([])
    expect(firstPage.length + secondPage.length).toBe(14)
  })
  test('a product page shows its specifications and stock', async ({ page, request }) => {
    const product = await createProduct(request, { price: 249.5, stock: 3, specs: [{ name: 'formFactor', value: 'ATX' }, { name: 'wattage', value: '750W' }] })

    await page.goto(`/products/${product.sku}`)

    await expect(page.getByRole('heading', { level: 1, name: product.name })).toBeVisible()
    await expect(page.getByText('$249.50').first()).toBeVisible()
    await expect(page.getByText('In stock — only 3 left')).toBeVisible()
    await expect(page.getByRole('row', { name: /Form factor\s+ATX/ })).toBeVisible()
    await expect(page.getByRole('row', { name: /Wattage\s+750W/ })).toBeVisible()
    await expect(page.getByRole('navigation', { name: 'Breadcrumb' })).toContainText(product.categoryName)
  })

  test('an out-of-stock product cannot be added to the cart', async ({ page, request }) => {
    const product = await createProduct(request, { stock: 0 })

    await page.goto(`/products/${product.sku}`)

    await expect(page.getByText('Out of stock').first()).toBeVisible()
    await expect(page.getByRole('button', { name: 'Out of stock' })).toBeDisabled()
  })

  test('an unknown product and an unknown page show friendly not-found screens', async ({ page }) => {
    await page.goto('/products/DOES-NOT-EXIST')
    await expect(page.getByRole('heading', { name: 'Product not found' })).toBeVisible()

    await page.goto('/no/such/page')
    await expect(page.getByRole('heading', { name: 'Page not found' })).toBeVisible()
    await page.getByRole('link', { name: 'Back to the store' }).click()
    await expect(page).toHaveURL(/\/$/)
  })

  test('the theme choice sticks across reloads', async ({ page }) => {
    await page.goto('/')
    const html = page.locator('html')
    const initial = await html.getAttribute('data-theme')
    const target = initial === 'dark' ? 'light' : 'dark'

    await page.getByRole('button', { name: `Switch to ${target} theme` }).click()
    await expect(html).toHaveAttribute('data-theme', target)

    await page.reload()
    await expect(html).toHaveAttribute('data-theme', target)
  })

  test('@mobile the catalog works on a phone: filters collapse, nothing overflows', async ({ page }) => {
    await page.goto('/categories/motherboards')

    await expect(page.getByRole('article').first()).toBeVisible()
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth)
    expect(overflow, 'the page scrolls sideways on a phone').toBe(false)

    // filters are collapsed behind a toggle, and open when asked
    const filterToggle = page.getByRole('checkbox', { name: 'Toggle filters' })
    await expect(page.getByLabel('AM5')).toBeHidden()
    await filterToggle.check()
    await expect(page.getByLabel('AM5')).toBeVisible()
    await page.getByLabel('AM5').check()
    await expect(page.getByRole('article')).toHaveCount(3)

    // the navigation drawer opens and works
    await page.getByRole('button', { name: 'Open menu' }).click()
    await page.getByRole('link', { name: 'All products' }).first().click()
    await expect(page).toHaveURL(/\/products$/)
  })
})
