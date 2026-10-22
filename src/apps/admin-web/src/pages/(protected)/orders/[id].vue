<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { api, type Schemas } from '@/api/client'
import { problemMessage } from '@/api/errors'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import ErrorAlert from '@/components/ErrorAlert.vue'
import StatusPill from '@/components/StatusPill.vue'
import OrderStatusBadge from '@/components/OrderStatusBadge.vue'
import { usePageTitle } from '@/composables/usePageTitle'
import { useToastStore } from '@/stores/toast'
import { formatDateTime, money } from '@/utils/format'
import { canCancel, canRefund } from '@/utils/orderStatus'

const route = useRoute('/(protected)/orders/[id]')
const toast = useToastStore()

const detail = ref<Schemas['AdminOrderDetailDto']>()
const status = ref<'loading' | 'ready' | 'notFound' | 'error'>('loading')
const action = ref<'cancel' | 'refund'>()
const busy = ref(false)
const error = ref('')

const order = computed(() => detail.value?.order)
usePageTitle(() => (order.value ? `Order ${order.value.orderNumber}` : 'Order'))

async function load() {
  status.value = 'loading'
  try {
    const { data, response } = await api.GET('/api/admin/orders/{id}', { params: { path: { id: route.params.id } } })
    if (response.status === 404) status.value = 'notFound'
    else if (!data) status.value = 'error'
    else {
      detail.value = data
      status.value = 'ready'
    }
  } catch {
    status.value = 'error'
  }
}
onMounted(load)

async function perform() {
  if (!action.value) return
  busy.value = true
  error.value = ''
  const path = { params: { path: { id: route.params.id } } }
  const { data, error: problem } =
    action.value === 'cancel' ? await api.POST('/api/admin/orders/{id}/cancel', path) : await api.POST('/api/admin/orders/{id}/refund', path)
  busy.value = false

  const done = action.value
  action.value = undefined
  if (!data) {
    error.value = problemMessage(problem, `We could not ${done} the order.`)
    await load() // the order may have moved on (e.g. shipped) while this page was open
    return
  }
  detail.value = data
  toast.success(done === 'cancel' ? 'Order cancelled.' : 'Order refunded.')
}
</script>

<template>
  <div class="mx-auto max-w-4xl space-y-4">
    <nav class="breadcrumbs text-sm" aria-label="Breadcrumb">
      <ul><li><RouterLink to="/orders">Orders</RouterLink></li><li>{{ order?.orderNumber ?? 'Order' }}</li></ul>
    </nav>

    <div v-if="status === 'loading' && !detail" class="skeleton h-96" />
    <ErrorAlert v-else-if="status === 'error'" message="We could not load this order." retry @retry="load" />
    <div v-else-if="status === 'notFound'" class="rounded-box bg-base-200 p-10 text-center">
      <h1 class="text-xl font-bold">Order not found</h1>
      <RouterLink to="/orders" class="btn btn-primary btn-sm mt-3">Back to orders</RouterLink>
    </div>

    <template v-else-if="detail && order">
      <div class="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 class="font-mono text-2xl font-bold">{{ order.orderNumber }}</h1>
          <p class="text-sm opacity-70">Placed {{ formatDateTime(order.createdAt) }} · {{ detail.customerEmail }}</p>
        </div>
        <div class="flex items-center gap-2">
          <OrderStatusBadge :status="order.status" />
          <button v-if="canCancel(order.status)" class="btn btn-sm btn-error btn-outline" @click="action = 'cancel'">Cancel order</button>
          <button v-if="canRefund(order.status)" class="btn btn-sm btn-warning btn-outline" @click="action = 'refund'">Refund order</button>
        </div>
      </div>

      <ErrorAlert v-if="error" :message="error" />

      <div class="grid gap-4 md:grid-cols-2">
        <section class="card card-border bg-base-100">
          <div class="card-body gap-1 text-sm">
            <h2 class="card-title">Payment</h2>
            <template v-if="detail.payment">
              <p>
                <StatusPill :tone="detail.payment.status === 'Captured' ? 'success' : detail.payment.status === 'Declined' ? 'error' : 'warning'">{{ detail.payment.status }}</StatusPill>
                {{ money(detail.payment.amount) }}
              </p>
              <p class="font-mono text-xs opacity-70">{{ detail.payment.providerReference }}</p>
              <p v-if="detail.payment.declineReason" class="text-error">Declined: {{ detail.payment.declineReason }}</p>
              <p class="opacity-70">{{ formatDateTime(detail.payment.createdAt) }}</p>
            </template>
            <p v-else class="opacity-70">No payment has been attempted yet.</p>
          </div>
        </section>

        <section class="card card-border bg-base-100">
          <div class="card-body gap-1 text-sm">
            <h2 class="card-title">Shipping</h2>
            <template v-if="order.shipment">
              <p>{{ order.shipment.service }}</p>
              <p class="font-mono">{{ order.shipment.trackingNumber }}</p>
              <p class="opacity-70">Shipped {{ formatDateTime(order.shipment.shippedAt) }}</p>
            </template>
            <p v-else class="opacity-70">Not shipped yet ({{ order.carrier }}).</p>
            <p class="pt-2">
              {{ order.shipTo.name }}<br />
              {{ order.shipTo.line1 }}<template v-if="order.shipTo.line2">, {{ order.shipTo.line2 }}</template><br />
              {{ order.shipTo.city }}<template v-if="order.shipTo.region">, {{ order.shipTo.region }}</template> {{ order.shipTo.postalCode }}, {{ order.shipTo.countryCode }}
            </p>
          </div>
        </section>
      </div>

      <section class="card card-border bg-base-100">
        <div class="card-body gap-2">
          <h2 class="card-title">Items</h2>
          <div class="overflow-x-auto">
            <table class="table">
              <thead><tr><th>Product</th><th class="text-right">Price</th><th class="text-right">Qty</th><th class="text-right">Total</th></tr></thead>
              <tbody>
                <tr v-for="item in order.items" :key="item.sku">
                  <td>{{ item.name }}<div class="font-mono text-xs opacity-75">{{ item.sku }}</div></td>
                  <td class="text-right">{{ money(item.unitPrice) }}</td>
                  <td class="text-right">{{ item.quantity }}</td>
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

      <ConfirmDialog
        :open="action === 'cancel'"
        title="Cancel this order?"
        confirm-label="Cancel order"
        tone="btn-error"
        :busy="busy"
        @confirm="perform"
        @cancel="action = undefined"
      >
        The reserved stock goes back on the shelf<template v-if="detail.payment?.status === 'Captured'"> and the {{ money(detail.payment.amount) }} payment is refunded</template>. This cannot be undone.
      </ConfirmDialog>

      <ConfirmDialog
        :open="action === 'refund'"
        title="Refund this order?"
        confirm-label="Refund order"
        tone="btn-warning"
        :busy="busy"
        @confirm="perform"
        @cancel="action = undefined"
      >
        {{ money(detail.payment?.amount ?? order.total) }} is returned to the customer. Stock is not put back; handle returned goods separately.
      </ConfirmDialog>
    </template>
  </div>
</template>
