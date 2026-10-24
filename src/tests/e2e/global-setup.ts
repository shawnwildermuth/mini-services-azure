import type { FullConfig } from '@playwright/test'
import { clearDeadLetters } from './support/cleanup'

/** Fails fast, with an instruction, when the stack under test isn't running, instead of 100 confusing timeouts. */
export default async function globalSetup(_config: FullConfig) {
  const store = process.env.STORE_URL ?? 'http://localhost:5173'
  const admin = process.env.ADMIN_URL ?? 'http://localhost:5174'
  const api = process.env.API_URL ?? 'http://localhost:5233'

  const probes: [string, string][] = [
    ['API', `${api}/health/ready`],
    ['storefront', `${store}/healthz`],
    ['admin site', `${admin}/healthz`],
  ]

  const down: string[] = []
  for (const [name, url] of probes) {
    try {
      const response = await fetch(url, { signal: AbortSignal.timeout(5000) })
      if (!response.ok) down.push(`${name} (${url}) answered ${response.status}`)
    } catch {
      down.push(`${name} (${url}) is not reachable`)
    }
  }
  if (down.length) {
    throw new Error(
      `The application stack is not running:\n  - ${down.join('\n  - ')}\n\n` +
        'Start it with:  cd src && docker compose --profile apps up -d --build\n' +
        'or, for the built images as in CI:  cd src && docker compose --profile apps up -d --build  plus  func start in src/apps/Store.Functions',
    )
  }

  await clearDeadLetters() // leftovers from earlier runs would crowd the failed-messages page

  // Orders only ship if the Function App is consuming the queues; check once with a throwaway order so a missing
  // Function App is one clear message rather than a dozen timeouts.
  const email = `probe-${Date.now()}@example.com`
  const json = { 'Content-Type': 'application/json' }
  await fetch(`${api}/api/auth/register`, { method: 'POST', headers: json, body: JSON.stringify({ email, password: 'Passw0rd!' }) })
  const login = (await (await fetch(`${api}/api/auth/login`, { method: 'POST', headers: json, body: JSON.stringify({ email, password: 'Passw0rd!' }) })).json()) as { accessToken: string }
  const auth = { ...json, Authorization: `Bearer ${login.accessToken}` }
  const address = (await (await fetch(`${api}/api/addresses`, { method: 'POST', headers: auth, body: JSON.stringify({ fullName: 'Probe', line1: '1 Main', city: 'Atlanta', postalCode: '30301', countryCode: 'US' }) })).json()) as { id: number }
  await fetch(`${api}/api/cart/items/MB-GIG-H610`, { method: 'PUT', headers: auth, body: JSON.stringify({ quantity: 1 }) })
  const order = (await (await fetch(`${api}/api/orders`, { method: 'POST', headers: auth, body: JSON.stringify({ addressId: address.id }) })).json()) as { id: string }

  const deadline = Date.now() + 45_000
  while (Date.now() < deadline) {
    const status = ((await (await fetch(`${api}/api/orders/${order.id}`, { headers: auth })).json()) as { status: string }).status
    if (status === 'Shipped') return
    await new Promise((r) => setTimeout(r, 1000))
  }
  throw new Error(
    'A test order was not paid and shipped within 45 seconds: the Function App is probably not running.\n' +
      'Start it with:  cd src/apps/Store.Functions && func start',
  )
}
