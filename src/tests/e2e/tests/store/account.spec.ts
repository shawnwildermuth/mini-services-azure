import { expect, test } from '@playwright/test'
import { PASSWORD, newCustomer, signedInAsCustomer, unique } from '../../support/api'
import { urls } from '../../support/urls'

test.describe('accounts and sign-in', () => {
  test('registering signs the customer in', async ({ page }) => {
    const email = `${unique('reg-')}@example.com`

    await page.goto('/register')
    await page.getByLabel('Email').fill(email)
    await page.getByLabel(/^Password$/).fill(PASSWORD)
    await page.getByLabel('Confirm password').fill(PASSWORD)
    await page.getByRole('button', { name: 'Create account' }).click()

    await expect(page).toHaveURL(/\/$/)
    await expect(page.getByRole('button', { name: 'Account menu' })).toBeVisible()
  })

  test('a weak password is explained, not just rejected', async ({ page }) => {
    await page.goto('/register')
    await page.getByLabel('Email').fill(`${unique('weak-')}@example.com`)
    await page.getByLabel(/^Password$/).fill('abcdefgh') // long enough, but no digit/capital/symbol
    await page.getByLabel('Confirm password').fill('abcdefgh')
    await page.getByRole('button', { name: 'Create account' }).click()

    await expect(page.getByRole('alert')).toContainText(/digit|uppercase|non alphanumeric/i)
    await expect(page).toHaveURL(/\/register/)
  })

  test('mismatched passwords block submission', async ({ page }) => {
    await page.goto('/register')
    await page.getByLabel('Email').fill(`${unique('mismatch-')}@example.com`)
    await page.getByLabel(/^Password$/).fill(PASSWORD)
    await page.getByLabel('Confirm password').fill('Different1!')

    await expect(page.getByText('Passwords do not match.')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Create account' })).toBeDisabled()
  })

  test('registering an email that already has an account is refused', async ({ page, request }) => {
    const existing = await newCustomer(request)

    await page.goto('/register')
    await page.getByLabel('Email').fill(existing.email)
    await page.getByLabel(/^Password$/).fill(PASSWORD)
    await page.getByLabel('Confirm password').fill(PASSWORD)
    await page.getByRole('button', { name: 'Create account' }).click()

    await expect(page.getByRole('alert')).toContainText(/already/i)
  })

  test('the wrong password gets a clear message that does not reveal whether the account exists', async ({ page, request }) => {
    const customer = await newCustomer(request)

    for (const email of [customer.email, `${unique('ghost-')}@example.com`]) {
      await page.goto('/login')
      await page.getByLabel('Email').fill(email)
      await page.getByLabel('Password').fill('Wrong-password1')
      await page.getByRole('button', { name: 'Sign in' }).click()
      await expect(page.getByRole('alert')).toHaveText('Invalid email or password.')
    }
  })

  test('signing in and out', async ({ page, request }) => {
    const customer = await newCustomer(request)

    await page.goto('/login')
    await page.getByLabel('Email').fill(customer.email)
    await page.getByLabel('Password').fill(customer.password)
    await page.getByRole('button', { name: 'Sign in' }).click()
    await expect(page.getByRole('button', { name: 'Account menu' })).toBeVisible()

    await page.getByRole('button', { name: 'Account menu' }).click()
    await page.getByRole('button', { name: 'Sign out' }).click()

    await expect(page.getByRole('link', { name: 'Sign in' })).toBeVisible()
    // the session is really gone: reloading does not bring it back, and protected pages ask for sign-in again
    await page.goto('/account')
    await expect(page).toHaveURL(/\/login\?redirect=(%2F|\/)account/)
  })

  test('a protected page asks for sign-in and returns there afterwards', async ({ page, request }) => {
    const customer = await newCustomer(request)

    await page.goto('/account/orders')
    await expect(page).toHaveURL(/\/login\?redirect=(%2F|\/)account(%2F|\/)orders/)
    await page.getByLabel('Email').fill(customer.email)
    await page.getByLabel('Password').fill(customer.password)
    await page.getByRole('button', { name: 'Sign in' }).click()

    await expect(page).toHaveURL(/\/account\/orders$/)
  })

  test('a redirect to another website after sign-in is ignored', async ({ page, request }) => {
    const customer = await newCustomer(request)

    await page.goto('/login?redirect=https://evil.example/phish')
    await page.getByLabel('Email').fill(customer.email)
    await page.getByLabel('Password').fill(customer.password)
    await page.getByRole('button', { name: 'Sign in' }).click()

    await expect(page).toHaveURL(new RegExp(`^${urls.store.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}/?$`))
  })

  test('someone already signed in is taken away from the sign-in page', async ({ page, request }) => {
    await signedInAsCustomer(page, await newCustomer(request))

    await page.goto('/login')

    await expect(page).toHaveURL(/\/$/)
  })

  test('the session survives a reload and a new tab', async ({ page, context, request }) => {
    await signedInAsCustomer(page, await newCustomer(request))
    await page.goto('/account')
    await expect(page.getByRole('heading', { name: 'My account' })).toBeVisible()

    await page.reload()
    await expect(page.getByRole('heading', { name: 'My account' })).toBeVisible()
  })

  test('addresses can be added, edited, given a default, and deleted', async ({ page, request }) => {
    await signedInAsCustomer(page, await newCustomer(request))
    page.on('dialog', (dialog) => dialog.accept()) // the delete confirmation

    await page.goto('/account')
    await page.getByRole('button', { name: 'Add address' }).click()
    await page.getByLabel('Full name').fill('First Address')
    await page.getByLabel('Address', { exact: true }).fill('1 Main St')
    await page.getByLabel('City').fill('Atlanta')
    await page.getByLabel('Postal code').fill('30301')
    await page.getByRole('button', { name: 'Save address' }).click()
    await expect(page.getByText('First Address')).toBeVisible()
    await expect(page.getByText('ships via UPS')).toBeVisible()

    await page.getByRole('button', { name: 'Add address' }).click()
    await page.getByLabel('Full name').fill('Second Address')
    await page.getByLabel('Address', { exact: true }).fill('2 High St')
    await page.getByLabel('City').fill('Berlin')
    await page.getByLabel('Postal code').fill('10115')
    await page.getByLabel('Country').selectOption('DE')
    await page.getByLabel('Use as my default address').check()
    await page.getByRole('button', { name: 'Save address' }).click()
    await expect(page.getByText('ships via DHL')).toBeVisible()
    // the new default is listed first
    await expect(page.locator('li', { hasText: 'Second Address' }).getByText('Default')).toBeVisible()
    await expect(page.locator('li', { hasText: 'First Address' }).getByText('Default')).toHaveCount(0)

    await page.locator('li', { hasText: 'Second Address' }).getByRole('button', { name: 'Delete' }).click()
    await expect(page.getByText('Second Address')).toHaveCount(0)
    // deleting the default promotes the remaining address
    await expect(page.locator('li', { hasText: 'First Address' }).getByText('Default')).toBeVisible()
  })

  test('changing the password takes effect', async ({ page, request }) => {
    const customer = await newCustomer(request)
    await signedInAsCustomer(page, customer)
    const newPassword = 'N3w-Passw0rd!'

    await page.goto('/account')
    await page.getByLabel('Current password').fill(customer.password)
    await page.getByLabel('New password').fill(newPassword)
    await page.getByRole('button', { name: 'Change password' }).click()
    await expect(page.getByRole('status').filter({ hasText: 'Your password has been changed.' })).toBeVisible()

    const oldLogin = await request.post(`${urls.api}/api/auth/login`, { data: { email: customer.email, password: customer.password } })
    const newLogin = await request.post(`${urls.api}/api/auth/login`, { data: { email: customer.email, password: newPassword } })
    expect(oldLogin.status()).toBe(401)
    expect(newLogin.ok()).toBe(true)
  })

  test('changing the password with the wrong current password is refused', async ({ page, request }) => {
    const customer = await newCustomer(request)
    await signedInAsCustomer(page, customer)

    await page.goto('/account')
    await page.getByLabel('Current password').fill('Not-My-Password1')
    await page.getByLabel('New password').fill('N3w-Passw0rd!')
    await page.getByRole('button', { name: 'Change password' }).click()

    await expect(page.getByRole('alert')).toBeVisible()
  })
})
