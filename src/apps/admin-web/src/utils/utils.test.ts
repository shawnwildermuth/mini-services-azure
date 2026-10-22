import { describe, expect, it } from 'vitest'
import { money, slugify, timeAgo } from './format'
import { canCancel, canRefund, statusLabel } from './orderStatus'

describe('format', () => {
  it('formats money', () => {
    expect(money(1234.5)).toBe('$1,234.50')
  })

  it('describes ages in the largest sensible unit', () => {
    const now = new Date('2026-10-22T12:00:00Z').getTime()
    const ago = (seconds: number) => timeAgo(new Date(now - seconds * 1000).toISOString(), now)

    expect(ago(5)).toBe('just now')
    expect(ago(60)).toBe('1 minute ago')
    expect(ago(150)).toBe('2 minutes ago')
    expect(ago(3600)).toBe('1 hour ago')
    expect(ago(3 * 86400)).toBe('3 days ago')
  })

  it('slugifies names the way the API requires', () => {
    expect(slugify('Power Supplies')).toBe('power-supplies')
    expect(slugify('  CPUs & GPUs!  ')).toBe('cpus-gpus')
    expect(slugify('Café Parts')).toBe('cafe-parts')
    expect(slugify('Multiple   spaces')).toMatch(/^[a-z0-9]+(-[a-z0-9]+)*$/)
  })
})

describe('order actions', () => {
  it('only lets unshipped orders be cancelled', () => {
    expect(canCancel('Pending')).toBe(true)
    expect(canCancel('Paid')).toBe(true)
    for (const s of ['Shipped', 'PaymentFailed', 'Cancelled', 'Refunded'] as const) expect(canCancel(s)).toBe(false)
  })

  it('only lets shipped orders be refunded', () => {
    expect(canRefund('Shipped')).toBe(true)
    for (const s of ['Pending', 'Paid', 'PaymentFailed', 'Cancelled', 'Refunded'] as const) expect(canRefund(s)).toBe(false)
  })

  it('labels every status', () => {
    expect(statusLabel('Refunded')).toBe('Refunded')
  })
})
