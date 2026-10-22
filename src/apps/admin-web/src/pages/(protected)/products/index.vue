<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { api, type Schemas } from '@/api/client'
import AppPagination from '@/components/AppPagination.vue'
import ErrorAlert from '@/components/ErrorAlert.vue'
import StatusPill from '@/components/StatusPill.vue'
import { usePageTitle } from '@/composables/usePageTitle'
import { money } from '@/utils/format'

usePageTitle('Products')

const result = ref<Schemas['PagedResultOfAdminProductSummaryDto']>()
const categories = ref<Schemas['AdminCategoryDto'][]>([])
const loading = ref(true)
const error = ref('')

const search = ref('')
const category = ref('')
const status = ref('all')
const page = ref(1)

let requestId = 0
async function load() {
  const id = ++requestId
  loading.value = true
  error.value = ''
  try {
    const { data } = await api.GET('/api/admin/products', {
      params: { query: { q: search.value.trim() || undefined, category: category.value || undefined, status: status.value, page: page.value, pageSize: 15 } },
    })
    if (id !== requestId) return
    if (data) result.value = data
    else error.value = 'We could not load products.'
  } catch {
    if (id === requestId) error.value = 'We could not reach the API.'
  } finally {
    if (id === requestId) loading.value = false
  }
}

onMounted(async () => {
  categories.value = (await api.GET('/api/admin/categories')).data ?? []
  load()
})

// Any filter change starts from page 1; a page change keeps the filters.
watch([category, status], () => {
  page.value = 1
  load()
})
function applySearch() {
  page.value = 1
  load()
}
function goTo(p: number) {
  page.value = p
  load()
}
</script>

<template>
  <div class="space-y-4">
    <div class="flex flex-wrap items-center justify-between gap-2">
      <h1 class="text-2xl font-bold">Products</h1>
      <RouterLink to="/products/new" class="btn btn-primary btn-sm">+ New product</RouterLink>
    </div>

    <div class="flex flex-wrap gap-2">
      <form class="join" role="search" @submit.prevent="applySearch">
        <input v-model="search" type="search" class="input input-sm join-item w-56" placeholder="Search name, SKU, brand" aria-label="Search products" />
        <button class="btn btn-sm btn-neutral join-item" type="submit">Search</button>
      </form>
      <select v-model="category" class="select select-sm w-48" aria-label="Category">
        <option value="">All categories</option>
        <option v-for="c in categories" :key="c.id" :value="c.slug">{{ c.name }}</option>
      </select>
      <select v-model="status" class="select select-sm w-36" aria-label="Status">
        <option value="all">All statuses</option>
        <option value="active">Active</option>
        <option value="inactive">Inactive</option>
      </select>
    </div>

    <ErrorAlert v-if="error" :message="error" retry @retry="load" />
    <div v-else-if="loading && !result" class="skeleton h-64" />

    <template v-else-if="result">
      <div class="overflow-x-auto rounded-box border border-base-300 bg-base-100" :class="{ 'opacity-75': loading }">
        <table class="table">
          <thead>
            <tr><th>Product</th><th>SKU</th><th>Category</th><th class="text-right">Price</th><th class="text-right">Available</th><th>Status</th></tr>
          </thead>
          <tbody>
            <tr v-for="p in result.items" :key="p.id" class="hover:bg-base-200">
              <td>
                <RouterLink :to="`/products/${p.id}`" class="link-hover font-medium">{{ p.name }}</RouterLink>
                <div class="text-xs opacity-75">{{ p.brand }}</div>
              </td>
              <td class="font-mono text-xs">{{ p.sku }}</td>
              <td>{{ p.categoryName }}</td>
              <td class="text-right">{{ money(p.price) }}</td>
              <td class="text-right"><span :class="{ 'font-semibold text-error': p.available === 0 }">{{ p.available }}</span></td>
              <td><StatusPill :tone="p.isActive ? 'success' : 'neutral'">{{ p.isActive ? 'Active' : 'Inactive' }}</StatusPill></td>
            </tr>
            <tr v-if="!result.items.length"><td colspan="6" class="py-8 text-center opacity-70">No products match.</td></tr>
          </tbody>
        </table>
      </div>
      <div class="flex items-center justify-between">
        <p class="text-sm opacity-70">{{ result.totalCount }} {{ result.totalCount === 1 ? 'product' : 'products' }}</p>
        <AppPagination :page="result.page" :page-size="result.pageSize" :total="result.totalCount" @change="goTo" />
      </div>
    </template>
  </div>
</template>
