const usd = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })
const dateTime = new Intl.DateTimeFormat('en-US', { dateStyle: 'medium', timeStyle: 'short' })

export const money = (amount: number) => usd.format(amount)
export const formatDateTime = (iso: string) => dateTime.format(new Date(iso))

/** "3 minutes ago", "2 days ago" — for queue ages and stuck-order hints. */
export function timeAgo(iso: string, now = Date.now()): string {
  const seconds = Math.max(Math.round((now - new Date(iso).getTime()) / 1000), 0)
  const units: [number, string][] = [[86400, 'day'], [3600, 'hour'], [60, 'minute']]
  for (const [size, name] of units) {
    if (seconds >= size) {
      const count = Math.floor(seconds / size)
      return `${count} ${name}${count === 1 ? '' : 's'} ago`
    }
  }
  return 'just now'
}

/** URL-safe slug from a display name: "Power Supplies" -> "power-supplies". */
export const slugify = (name: string) =>
  name.toLowerCase().normalize('NFKD').replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '')
