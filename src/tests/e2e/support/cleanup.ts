import { urls } from './urls'

/**
 * Empties the dead-letter queues. Tests that simulate outages leave messages there on purpose; the admin page lists the
 * oldest 50 per queue, so without cleaning up, a later run's messages would eventually fall off the screen.
 * Only for the local/CI stack the suite runs against.
 */
export async function clearDeadLetters() {
  const tokenResponse = await fetch(`${urls.api}/api/dev/admin-token`, { method: 'POST' })
  if (!tokenResponse.ok) return // not a development API: nothing to clean
  const headers = { Authorization: `Bearer ${((await tokenResponse.json()) as { accessToken: string }).accessToken}` }

  const list = await fetch(`${urls.api}/api/admin/operations/dead-letters`, { headers })
  if (!list.ok) return
  for (const m of (await list.json()) as { queue: string; sequenceNumber: number }[])
    await fetch(`${urls.api}/api/admin/operations/dead-letters/${m.queue}/${m.sequenceNumber}`, { method: 'DELETE', headers })
}
