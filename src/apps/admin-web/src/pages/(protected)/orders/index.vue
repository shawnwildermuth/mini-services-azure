<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { api, type Schemas } from '@/api/client'
import AppPagination from '@/components/AppPagination.vue'
import ErrorAlert from '@/components/ErrorAlert.vue'
import OrderStatusBadge from '@/components/OrderStatusBadge.vue'
import { usePageTitle } from '@/composables/usePageTitle'
import { formatDateTime, money } from '@/utils/format'
import { allStatuses, statusLabel } from '@/utils/orderStatus'

usePageTitle('Orders')

const route = useRoute()

const result = ref<Schemas['PagedResultOfAdminOrderSummaryDto']>()
const loading = ref(true)
const error = ref('')

const status = ref(typeof route.query.status === 'string' ? route.query.status : '')
const search = ref('')
const page = ref(1)

let requestId = 0
async function load() {
  const id = ++requestId
  loading.value = true
  error.value = ''
  try {
    const { data } = await api.GET('/api/admin/orders', {
      params: { query: { status: status.value || undefined, q: search.value.trim() || undefined, page: page.value, pageSize: 20 } },
    })
    if (id !== requestId) return
    if (data) result.value = data
    else error.value = 'We could not load orders.'
  } catch {
    if (id === requestId) error.value = 'We could not reach the API.'
  } finally {
    if (id === requestId) loading.value = false
  }
}

onMounted(load)
watch(status, () => {
  page.value = 1
  load()
})
function applySearch() {
  page.value = 1
  load()
}
</script>

<template>
  <div class="space-y-4">
    <h1 class="text-2xl font-bold">Orders</h1>

    <div class="flex flex-wrap gap-2">
      <form class="join" role="search" @submit.prevent="applySearch">
        <input v-model="search" type="search" class="input input-sm join-item w-64" placeholder="Order number or customer email" aria-label="Search orders" />
        <button class="btn btn-sm btn-neutral join-item" type="submit">Search</button>
      </form>
      <select v-model="status" class="select select-sm w-52" aria-label="Status">
        <option value="">All statuses</option>
        <option v-for="s in allStatuses" :key="s" :value="s">{{ statusLabel(s) }}</option>
      </select>
    </div>

    <ErrorAlert v-if="error" :message="error" retry @retry="load" />
    <div v-else-if="loading && !result" class="skeleton h-64" />

    <template v-else-if="result">
      <div class="overflow-x-auto rounded-box border border-base-300 bg-base-100" :class="{ 'opacity-75': loading }">
        <table class="table">
          <thead>
            <tr><th>Order</th><th>Placed</th><th>Customer</th><th class="text-right">Items</th><th class="text-right">Total</th><th>Status</th></tr>
          </thead>
          <tbody>
            <tr v-for="o in result.items" :key="o.id" class="hover:bg-base-200">
              <td><RouterLink :to="`/orders/${o.id}`" class="link-hover font-mono font-medium">{{ o.orderNumber }}</RouterLink></td>
              <td class="whitespace-nowrap text-sm">{{ formatDateTime(o.createdAt) }}</td>
              <td class="text-sm">{{ o.customerEmail }}</td>
              <td class="text-right">{{ o.itemCount }}</td>
              <td class="text-right font-medium">{{ money(o.total) }}</td>
              <td><OrderStatusBadge :status="o.status" /></td>
            </tr>
            <tr v-if="!result.items.length"><td colspan="6" class="py-8 text-center opacity-70">No orders match.</td></tr>
          </tbody>
        </table>
      </div>
      <div class="flex items-center justify-between">
        <p class="text-sm opacity-70">{{ result.totalCount }} {{ result.totalCount === 1 ? 'order' : 'orders' }}</p>
        <AppPagination :page="result.page" :page-size="result.pageSize" :total="result.totalCount" @change="(p) => { page = p; load() }" />
      </div>
    </template>
  </div>
</template>
