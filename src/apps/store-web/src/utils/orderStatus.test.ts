import { describe, expect, it } from 'vitest'
import { isInProgress, progressStep, statusLabel } from './orderStatus'

describe('order status helpers', () => {
  it('keeps refreshing only orders that can still change', () => {
    expect(isInProgress('Pending')).toBe(true)
    expect(isInProgress('Paid')).toBe(true)
    for (const done of ['Shipped', 'PaymentFailed', 'Cancelled', 'Refunded'] as const) expect(isInProgress(done)).toBe(false)
  })

  it('maps statuses onto the progress steps', () => {
    expect(progressStep('Pending')).toBe(1)
    expect(progressStep('Paid')).toBe(2)
    expect(progressStep('Shipped')).toBe(3)
  })

  it('has a customer-friendly label for every status', () => {
    expect(statusLabel('Pending')).toBe('Awaiting payment')
    expect(statusLabel('PaymentFailed')).toBe('Payment failed')
  })
})
