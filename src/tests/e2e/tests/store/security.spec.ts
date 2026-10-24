import { expect, test, type Page } from '@playwright/test'
import { PASSWORD, createProduct, newCustomer } from '../../support/api'
import { urls } from '../../support/urls'

/** Fails the test if the page opens an alert/confirm/prompt: the signature of injected script actually running. */
function failOnDialogs(page: Page) {
  page.on('dialog', async (dialog) => {
    await dialog.dismiss()
    throw new Error(`a script ran and opened a dialog: ${dialog.message()}`)
  })
}

const b64url = (value: object) => Buffer.from(JSON.stringify(value)).toString('base64url')

test.describe('security: what the browser is told', () => {
  // These check the nginx images' headers and production bundles. The Aspire stack serves the Vite dev servers, which
  // have neither; set E2E_IMAGES=1 when the sites under test are the built images (CI, `docker compose`-style runs).
  test.skip(process.env.E2E_IMAGES !== '1', 'applies to the built images only (set E2E_IMAGES=1)')

  for (const [name, url] of [['storefront', urls.store], ['admin site', urls.admin]] as const) {
    test(`the ${name} is served with a strict content security policy and hardening headers`, async ({ request }) => {
      const headers = (await request.get(`${url}/`)).headers()

      const csp = headers['content-security-policy']!
      expect(csp).toContain("default-src 'self'")
      expect(csp).toContain("script-src 'self'") // no inline scripts, no eval, no third-party scripts
      expect(csp).not.toMatch(/unsafe-eval|unsafe-inline/)
      expect(csp).toContain("frame-ancestors 'none'") // cannot be framed (clickjacking)
      expect(csp).toContain("object-src 'none'")
      expect(headers['x-content-type-options']).toBe('nosniff')
      expect(headers['x-frame-options']).toBe('DENY')
      expect(headers['referrer-policy']).toBe('strict-origin-when-cross-origin')
      expect(headers['strict-transport-security']).toContain('max-age=31536000')
      expect(headers['permissions-policy']).toContain('camera=()')
      expect(headers['server'] ?? '').not.toMatch(/\d/) // no version number advertised
    })

    test(`the ${name} does not ship source maps or leave the app shell cacheable`, async ({ request }) => {
      const html = await (await request.get(`${url}/`)).text()
      const script = html.match(/\/assets\/[^"]+\.js/)![0]
      expect((await request.get(`${url}${script}.map`)).status()).toBe(404)
      expect((await request.get(`${url}/`)).headers()['cache-control']).toContain('no-cache')
    })
  }

  test('the API sends defensive headers and never lets private data be cached', async ({ request }) => {
    const catalog = (await request.get(`${urls.api}/api/categories`)).headers()
    expect(catalog['x-content-type-options']).toBe('nosniff')
    expect(catalog['content-security-policy']).toContain("default-src 'none'")

    const customer = await newCustomer(request)
    const cart = await request.get(`${urls.api}/api/cart`, { headers: { Authorization: `Bearer ${customer.accessToken}` } })
    expect(cart.headers()['cache-control']).toContain('no-store')
  })
})

test.describe('security: untrusted text is never executed', () => {
  const hostile = `<img src=x onerror="window.__xss=1;alert('xss')">Hostile <b>name</b>`

  test('HTML in product data is displayed as text on every page that shows it', async ({ page, request }) => {
    failOnDialogs(page)
    const product = await createProduct(request, { name: hostile, stock: 5 })

    for (const path of [`/products/${product.sku}`, `/categories/${product.categorySlug}`]) {
      await page.goto(path)
      await expect(page.getByText('Hostile <b>name</b>').first()).toBeVisible() // the angle brackets are literally on screen
      expect(await page.locator('img[src="x"]').count()).toBe(0)                  // no element was created from the text
      expect(await page.evaluate(() => (window as unknown as { __xss?: number }).__xss)).toBeUndefined()
    }

    // and in the cart
    await page.goto(`/products/${product.sku}`)
    await page.getByRole('button', { name: 'Add to cart' }).click()
    await page.goto('/cart')
    await expect(page.getByText('Hostile <b>name</b>').first()).toBeVisible()
    expect(await page.evaluate(() => (window as unknown as { __xss?: number }).__xss)).toBeUndefined()
  })

  test('a search term is shown as text and nothing in the query string runs', async ({ page }) => {
    failOnDialogs(page)

    await page.goto('/products?q=' + encodeURIComponent('<script>window.__xss=1</script><img src=x onerror=alert(1)>'))

    await expect(page.getByRole('heading', { level: 1 })).toContainText('<script>')
    expect(await page.locator('script', { hasText: '__xss' }).count()).toBe(0)
    expect(await page.evaluate(() => (window as unknown as { __xss?: number }).__xss)).toBeUndefined()
  })

  test('a javascript: or off-site redirect after sign-in goes nowhere dangerous', async ({ page, request }) => {
    failOnDialogs(page)
    const customer = await newCustomer(request)

    for (const target of ['javascript:alert(1)', '//evil.example/x', 'https://evil.example']) {
      await page.goto(`/login?redirect=${encodeURIComponent(target)}`)
      await page.getByLabel('Email').fill(customer.email)
      await page.getByLabel('Password').fill(PASSWORD)
      await page.getByRole('button', { name: 'Sign in' }).click()
      await expect(page).toHaveURL(new RegExp(`^${urls.store.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}/?$`))
      await page.evaluate(() => localStorage.clear())
    }
  })
})

test.describe('security: the API refuses what it should', () => {
  test('forged or unsigned administrator tokens are rejected', async ({ request }) => {
    const claims = { sub: 'attacker', name: 'Mallory', roles: ['Admin'], iss: 'partsstore-dev', aud: 'partsstore-admin-dev', exp: Math.floor(Date.now() / 1000) + 3600 }

    const unsigned = `${b64url({ alg: 'none', typ: 'JWT' })}.${b64url(claims)}.` // "alg: none": claims accepted without a signature
    const wrongKey = `${b64url({ alg: 'HS256', typ: 'JWT' })}.${b64url(claims)}.${Buffer.from('not-the-real-signature').toString('base64url')}`
    const garbage = 'not.a.jwt'

    for (const token of [unsigned, wrongKey, garbage, '']) {
      const response = await request.get(`${urls.api}/api/admin/dashboard`, { headers: token ? { Authorization: `Bearer ${token}` } : {} })
      expect(response.status(), `token ${token.slice(0, 20)}…`).toBe(401)
    }
  })

  test("a customer's token is not an administrator's, and vice versa", async ({ request }) => {
    const customer = await newCustomer(request)
    const admin = (await (await request.post(`${urls.api}/api/dev/admin-token`)).json()) as { accessToken: string }

    expect((await request.get(`${urls.api}/api/admin/dashboard`, { headers: { Authorization: `Bearer ${customer.accessToken}` } })).status()).toBe(401)
    expect((await request.get(`${urls.api}/api/cart`, { headers: { Authorization: `Bearer ${admin.accessToken}` } })).status()).toBe(401)
  })

  test('SQL-injection strings are just text', async ({ request }) => {
    const attacks = [
      `' OR 1=1 --`,
      `'; DROP TABLE Products; --`,
      `" OR ""="`,
      `%' UNION SELECT name FROM sys.tables --`,
    ]
    for (const attack of attacks) {
      const search = await request.get(`${urls.api}/api/products`, { params: { q: attack } })
      expect(search.status(), attack).toBe(200)
      expect((await search.json()).totalCount, attack).toBe(0)

      const spec = await request.get(`${urls.api}/api/products`, { params: { spec: `socket:${attack}`, brand: attack, category: attack } })
      expect(spec.status(), attack).toBe(200)
      expect((await spec.json()).totalCount, attack).toBe(0)

      expect((await request.get(`${urls.api}/api/products/${encodeURIComponent(attack)}`)).status(), attack).toBe(404)
    }
    // the catalog is intact
    expect((await (await request.get(`${urls.api}/api/products`)).json()).totalCount).toBeGreaterThan(40)
  })

  test('sending more than you may: extra fields are ignored, malformed bodies are 400s, not 500s', async ({ request }) => {
    const customer = await newCustomer(request)
    const auth = { Authorization: `Bearer ${customer.accessToken}` }

    // mass assignment: an attacker-supplied owner / price / default flag is not honoured
    const address = await request.post(`${urls.api}/api/addresses`, {
      headers: auth,
      data: { fullName: 'A', line1: '1', city: 'C', postalCode: '1', countryCode: 'US', userId: 'someone-else', id: 99999, isAdmin: true },
    })
    expect(address.status()).toBe(201)
    expect((await address.json()).id).not.toBe(99999)
    const mine = await (await request.get(`${urls.api}/api/addresses`, { headers: auth })).json()
    expect(mine).toHaveLength(1)

    for (const body of ['{ not json', '[]', 'null', '"just a string"']) {
      const response = await request.post(`${urls.api}/api/addresses`, { headers: { ...auth, 'Content-Type': 'application/json' }, data: body })
      expect(response.status(), body).toBeLessThan(500)
    }
  })

  test('Identity cookies cannot be requested: the API is bearer-only', async ({ request }) => {
    const customer = await newCustomer(request)

    const response = await request.post(`${urls.api}/api/auth/login?useCookies=true`, { data: { email: customer.email, password: PASSWORD } })

    expect(response.status()).toBe(400)
    expect(response.headers()['set-cookie']).toBeUndefined()
  })

  test('a foreign website is not allowed to call the API from a browser', async ({ request }) => {
    const preflight = (origin: string) =>
      request.fetch(`${urls.api}/api/products`, { method: 'OPTIONS', headers: { Origin: origin, 'Access-Control-Request-Method': 'GET' } })

    expect((await preflight('https://evil.example')).headers()['access-control-allow-origin']).toBeUndefined()
    expect((await preflight(urls.store)).headers()['access-control-allow-origin']).toBe(urls.store)
  })

  test('errors do not reveal internals', async ({ request }) => {
    const response = await request.post(`${urls.api}/api/auth/login`, { headers: { 'Content-Type': 'application/json' }, data: '{ broken' })
    const body = await response.text()

    expect(response.status()).toBeLessThan(500)
    expect(body).not.toMatch(/Exception|StackTrace|at Microsoft|at System|\.cs:line/)
  })
})
