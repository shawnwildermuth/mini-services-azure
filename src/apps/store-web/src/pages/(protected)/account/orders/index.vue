<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { api, type Schemas } from '@/api/client'
import AppPagination from '@/components/AppPagination.vue'
import ErrorAlert from '@/components/ErrorAlert.vue'
import OrderStatusBadge from '@/components/OrderStatusBadge.vue'
import { usePageTitle } from '@/composables/usePageTitle'
import { money } from '@/utils/format'

usePageTitle('My orders')

const route = useRoute()
const router = useRouter()

const orders = ref<Schemas['PagedResultOfOrderSummaryDto']>()
const loading = ref(true)
const error = ref('')

async function load() {
  loading.value = true
  error.value = ''
  const page = Math.max(Number(route.query.page) || 1, 1)
  try {
    const { data } = await api.GET('/api/orders', { params: { query: { page, pageSize: 10 } } })
    if (data) orders.value = data
    else error.value = 'We could not load your orders.'
  } catch {
    error.value = 'We could not reach the store. Check your connection and try again.'
  } finally {
    loading.value = false
  }
}

onMounted(load)

function goToPage(page: number) {
  router.push({ query: { page: String(page) } }).then(load)
}

const date = new Intl.DateTimeFormat('en-US', { dateStyle: 'medium', timeStyle: 'short' })
</script>

<template>
  <div class="mx-auto max-w-3xl space-y-4">
    <nav class="breadcrumbs text-sm" aria-label="Breadcrumb">
      <ul>
        <li><RouterLink to="/account">My account</RouterLink></li>
        <li>Orders</li>
      </ul>
    </nav>
    <h1 class="text-2xl font-bold">My orders</h1>

    <ErrorAlert v-if="error" :message="error" retry @retry="load" />
    <div v-else-if="loading && !orders" class="skeleton h-40" />

    <div v-else-if="orders && !orders.items.length" class="rounded-box bg-base-200 p-10 text-center">
      <p class="text-lg font-semibold">You have not placed any orders yet.</p>
      <RouterLink to="/products" class="btn btn-primary btn-sm mt-3">Start shopping</RouterLink>
    </div>

    <template v-else-if="orders">
      <ul class="space-y-3">
        <li v-for="o in orders.items" :key="o.id">
          <RouterLink :to="`/account/orders/${o.id}`" class="card card-border bg-base-100 transition-shadow hover:shadow-md">
            <div class="card-body flex-row flex-wrap items-center justify-between gap-3 p-4">
              <div>
                <p class="font-mono font-semibold">{{ o.orderNumber }}</p>
                <p class="text-sm opacity-70">{{ date.format(new Date(o.createdAt)) }} · {{ o.itemCount }} {{ o.itemCount === 1 ? 'item' : 'items' }}</p>
              </div>
              <div class="flex items-center gap-4">
                <OrderStatusBadge :status="o.status" />
                <span class="font-semibold">{{ money(o.total) }}</span>
              </div>
            </div>
          </RouterLink>
        </li>
      </ul>
      <div class="flex justify-center">
        <AppPagination :page="orders.page" :page-size="orders.pageSize" :total="orders.totalCount" @change="goToPage" />
      </div>
    </template>
  </div>
</template>
