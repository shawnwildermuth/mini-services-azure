<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { api, type Schemas } from '@/api/client'
import ErrorAlert from '@/components/ErrorAlert.vue'
import StatusPill from '@/components/StatusPill.vue'
import { usePageTitle } from '@/composables/usePageTitle'
import { money } from '@/utils/format'
import { allStatuses, statusLabel } from '@/utils/orderStatus'

usePageTitle('Dashboard')

const data = ref<Schemas['DashboardDto']>()
const failedMessages = ref<number | null | 'unavailable'>(null)
const error = ref('')

async function load() {
  error.value = ''
  try {
    const [dashboard, dlq] = await Promise.all([api.GET('/api/admin/dashboard'), api.GET('/api/admin/operations/dead-letters')])
    if (!dashboard.data) throw new Error('dashboard failed')
    data.value = dashboard.data
    failedMessages.value = dlq.data ? dlq.data.length : 'unavailable'
  } catch {
    error.value = 'We could not load the dashboard.'
  }
}

onMounted(load)

const inProgress = computed(() => (data.value?.ordersByStatus.Pending ?? 0) + (data.value?.ordersByStatus.Paid ?? 0))
</script>

<template>
  <div class="space-y-6">
    <h1 class="text-2xl font-bold">Dashboard</h1>

    <ErrorAlert v-if="error" :message="error" retry @retry="load" />
    <div v-else-if="!data" class="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
      <div v-for="n in 4" :key="n" class="skeleton h-28" />
    </div>

    <template v-else>
      <div v-if="data.stuckOrders > 0" role="alert" class="alert alert-warning">
        <span>
          <strong>{{ data.stuckOrders }}</strong> {{ data.stuckOrders === 1 ? 'order has' : 'orders have' }} been in progress for over 10 minutes.
          Check <RouterLink to="/operations/failed-messages" class="link font-semibold">failed messages</RouterLink>.
        </span>
      </div>

      <div class="stats stats-vertical w-full border border-base-300 bg-base-100 shadow-sm lg:stats-horizontal">
        <div class="stat">
          <div class="stat-title">Revenue (paid + shipped)</div>
          <div class="stat-value text-primary">{{ money(data.revenue) }}</div>
        </div>
        <div class="stat">
          <div class="stat-title">Orders in progress</div>
          <div class="stat-value">{{ inProgress }}</div>
          <div class="stat-desc">awaiting payment or shipment</div>
        </div>
        <div class="stat">
          <div class="stat-title">Shipped</div>
          <div class="stat-value">{{ data.ordersByStatus.Shipped }}</div>
        </div>
        <div class="stat">
          <div class="stat-title">Active products</div>
          <div class="stat-value">{{ data.activeProducts }}</div>
        </div>
      </div>

      <div class="grid gap-6 lg:grid-cols-2">
        <section class="card card-border bg-base-100">
          <div class="card-body gap-2">
            <h2 class="card-title">Orders by status</h2>
            <ul class="divide-y divide-base-300">
              <li v-for="s in allStatuses" :key="s">
                <RouterLink :to="{ path: '/orders', query: { status: s } }" class="flex justify-between py-2 hover:bg-base-200">
                  <span>{{ statusLabel(s) }}</span>
                  <span class="font-semibold">{{ data.ordersByStatus[s] ?? 0 }}</span>
                </RouterLink>
              </li>
            </ul>
          </div>
        </section>

        <div class="space-y-6">
          <section class="card card-border bg-base-100">
            <div class="card-body gap-2">
              <h2 class="card-title">Low stock</h2>
              <p v-if="!data.lowStock.length" class="text-sm opacity-70">Everything is comfortably stocked.</p>
              <ul v-else class="divide-y divide-base-300">
                <li v-for="item in data.lowStock" :key="item.productId" class="flex justify-between py-2 text-sm">
                  <RouterLink :to="`/products/${item.productId}`" class="link-hover">{{ item.name }}</RouterLink>
                  <StatusPill :tone="item.available === 0 ? 'error' : 'warning'">{{ item.available }} left</StatusPill>
                </li>
              </ul>
              <RouterLink to="/inventory?low=1" class="link text-sm">Manage inventory</RouterLink>
            </div>
          </section>

          <section class="card card-border bg-base-100">
            <div class="card-body gap-2">
              <h2 class="card-title">Failed messages</h2>
              <p v-if="failedMessages === 'unavailable'" class="text-sm opacity-70">Message queues are not connected in this environment.</p>
              <p v-else-if="failedMessages === 0" class="text-sm opacity-70">No messages are stuck in the dead-letter queues.</p>
              <p v-else class="text-sm">
                <strong>{{ failedMessages }}</strong> {{ failedMessages === 1 ? 'message needs' : 'messages need' }} attention.
              </p>
              <RouterLink to="/operations/failed-messages" class="link text-sm">View failed messages</RouterLink>
            </div>
          </section>
        </div>
      </div>
    </template>
  </div>
</template>
