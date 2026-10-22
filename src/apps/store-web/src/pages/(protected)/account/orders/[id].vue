<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { api, type Schemas } from '@/api/client'
import ErrorAlert from '@/components/ErrorAlert.vue'
import OrderStatusBadge from '@/components/OrderStatusBadge.vue'
import { usePageTitle } from '@/composables/usePageTitle'
import { countryName } from '@/utils/countries'
import { money } from '@/utils/format'
import { isInProgress, progressStep } from '@/utils/orderStatus'

const route = useRoute('/(protected)/account/orders/[id]')

const order = ref<Schemas['OrderDetailDto']>()
const status = ref<'loading' | 'ready' | 'notFound' | 'error'>('loading')

usePageTitle(() => (order.value ? `Order ${order.value.orderNumber}` : 'Order'))

const POLL_MS = 4000
let timer: ReturnType<typeof setTimeout> | undefined
let disposed = false

async function load() {
  try {
    const { data, response } = await api.GET('/api/orders/{id}', { params: { path: { id: route.params.id } } })
    if (response.status === 404) {
      status.value = 'notFound'
      return
    }
    if (!data) throw new Error('order request failed')
    order.value = data
    status.value = 'ready'
  } catch {
    // Keep showing what we have if a background refresh fails.
    if (!order.value) status.value = 'error'
  }
  schedule()
}

// Payment and shipping happen asynchronously after checkout, so refresh while the order can still change.
function schedule() {
  clearTimeout(timer)
  if (!disposed && order.value && isInProgress(order.value.status)) timer = setTimeout(load, POLL_MS)
}

onMounted(load)
onBeforeUnmount(() => {
  disposed = true
  clearTimeout(timer)
})

const step = computed(() => (order.value ? progressStep(order.value.status) : 1))
const failed = computed(() => ['PaymentFailed', 'Cancelled', 'Refunded'].includes(order.value?.status ?? ''))
const date = new Intl.DateTimeFormat('en-US', { dateStyle: 'long', timeStyle: 'short' })
const day = new Intl.DateTimeFormat('en-US', { dateStyle: 'long' })
</script>

<template>
  <div class="mx-auto max-w-3xl space-y-4">
    <nav class="breadcrumbs text-sm" aria-label="Breadcrumb">
      <ul>
        <li><RouterLink to="/account">My account</RouterLink></li>
        <li><RouterLink to="/account/orders">Orders</RouterLink></li>
        <li>{{ order?.orderNumber ?? 'Order' }}</li>
      </ul>
    </nav>

    <div v-if="status === 'loading'" class="skeleton h-64" />
    <ErrorAlert v-else-if="status === 'error'" message="We could not load this order." retry @retry="load" />

    <div v-else-if="status === 'notFound'" class="rounded-box bg-base-200 p-10 text-center">
      <h1 class="text-2xl font-bold">Order not found</h1>
      <RouterLink to="/account/orders" class="btn btn-primary btn-sm mt-3">View your orders</RouterLink>
    </div>

    <template v-else-if="order">
      <div class="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h1 class="font-mono text-2xl font-bold">{{ order.orderNumber }}</h1>
          <p class="text-sm opacity-70">Placed {{ date.format(new Date(order.createdAt)) }}</p>
        </div>
        <OrderStatusBadge :status="order.status" />
      </div>

      <div v-if="order.status === 'PaymentFailed'" role="alert" class="alert alert-error alert-soft">
        We could not take payment for this order, so it will not ship.
      </div>
      <div v-else-if="order.status === 'Cancelled'" role="alert" class="alert alert-soft">This order was cancelled. Any payment has been refunded.</div>
      <div v-else-if="order.status === 'Refunded'" role="alert" class="alert alert-warning alert-soft">This order was refunded.</div>
      <ul v-else class="steps w-full" aria-label="Order progress">
        <li class="step" :class="{ 'step-primary': step >= 1 }">Order placed</li>
        <li class="step" :class="{ 'step-primary': step >= 2 }">Payment received</li>
        <li class="step" :class="{ 'step-primary': step >= 3 }">Shipped</li>
      </ul>
      <p v-if="!failed && order.status !== 'Shipped'" class="text-center text-sm opacity-75" aria-live="polite">
        This page updates automatically.
      </p>

      <section class="card card-border bg-base-100">
        <div class="card-body gap-2">
          <h2 class="card-title">Items</h2>
          <div class="overflow-x-auto">
            <table class="table">
              <tbody>
                <tr v-for="item in order.items" :key="item.sku">
                  <td>
                    <RouterLink :to="`/products/${item.sku}`" class="link-hover font-medium">{{ item.name }}</RouterLink>
                    <div class="text-xs opacity-75">{{ item.quantity }} × {{ money(item.unitPrice) }}</div>
                  </td>
                  <td class="text-right">{{ money(item.lineTotal) }}</td>
                </tr>
              </tbody>
            </table>
          </div>
          <dl class="ml-auto w-full max-w-xs space-y-1 text-sm">
            <div class="flex justify-between"><dt>Subtotal</dt><dd>{{ money(order.subtotal) }}</dd></div>
            <div class="flex justify-between"><dt>Shipping ({{ order.carrier }})</dt><dd>{{ money(order.shippingCost) }}</dd></div>
            <div class="flex justify-between text-base font-semibold"><dt>Total</dt><dd>{{ money(order.total) }}</dd></div>
          </dl>
        </div>
      </section>

      <section v-if="order.shipment" class="card card-border border-success/40 bg-base-100">
        <div class="card-body gap-1 text-sm">
          <h2 class="card-title">Shipment</h2>
          <dl class="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1">
            <dt class="opacity-70">Carrier</dt>
            <dd>{{ order.shipment.service }}</dd>
            <dt class="opacity-70">Tracking number</dt>
            <dd class="font-mono" data-test="tracking">{{ order.shipment.trackingNumber }}</dd>
            <dt class="opacity-70">Shipped</dt>
            <dd>{{ day.format(new Date(order.shipment.shippedAt)) }}</dd>
            <dt class="opacity-70">Estimated delivery</dt>
            <dd>{{ day.format(new Date(order.shipment.estimatedDelivery)) }}</dd>
          </dl>
        </div>
      </section>

      <section class="card card-border bg-base-100">
        <div class="card-body gap-1 text-sm">
          <h2 class="card-title">Ships to</h2>
          <p>
            {{ order.shipTo.name }}<br />
            {{ order.shipTo.line1 }}<template v-if="order.shipTo.line2">, {{ order.shipTo.line2 }}</template><br />
            {{ order.shipTo.city }}<template v-if="order.shipTo.region">, {{ order.shipTo.region }}</template> {{ order.shipTo.postalCode }}<br />
            {{ countryName(order.shipTo.countryCode) }}
          </p>
        </div>
      </section>
    </template>
  </div>
</template>
