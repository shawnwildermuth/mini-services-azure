import type { Schemas } from '@/api/client'
import type { Tone } from './tone'

export type OrderStatus = Schemas['OrderStatus']

const labels: Record<OrderStatus, string> = {
  Pending: 'Awaiting payment',
  Paid: 'Paid — preparing shipment',
  Shipped: 'Shipped',
  PaymentFailed: 'Payment failed',
  Cancelled: 'Cancelled',
  Refunded: 'Refunded',
}

const tones: Record<OrderStatus, Tone> = {
  Pending: 'info',
  Paid: 'info',
  Shipped: 'success',
  PaymentFailed: 'error',
  Cancelled: 'neutral',
  Refunded: 'warning',
}

export const statusLabel = (status: OrderStatus) => labels[status]
export const statusTone = (status: OrderStatus) => tones[status]

/** Orders in these states can still change, so the order page keeps refreshing them. */
export const isInProgress = (status: OrderStatus) => status === 'Pending' || status === 'Paid'

/** How far along the happy path an order is: 1 = placed, 2 = paid, 3 = shipped. */
export function progressStep(status: OrderStatus): number {
  switch (status) {
    case 'Shipped':
      return 3
    case 'Paid':
      return 2
    default:
      return 1
  }
}
