import type { Schemas } from '@/api/client'
import type { Tone } from './tone'

export type OrderStatus = Schemas['OrderStatus']

export const allStatuses: OrderStatus[] = ['Pending', 'Paid', 'Shipped', 'PaymentFailed', 'Cancelled', 'Refunded']

const labels: Record<OrderStatus, string> = {
  Pending: 'Awaiting payment',
  Paid: 'Paid — awaiting shipment',
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

/** Orders that have not shipped can be cancelled (stock is released and any payment refunded). */
export const canCancel = (status: OrderStatus) => status === 'Pending' || status === 'Paid'

/** Only shipped orders are refunded; unshipped ones are cancelled instead. */
export const canRefund = (status: OrderStatus) => status === 'Shipped'
