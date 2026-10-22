<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { useRoute } from 'vue-router'
import { api, type Schemas } from '@/api/client'
import { problemMessage } from '@/api/errors'
import AppPagination from '@/components/AppPagination.vue'
import ErrorAlert from '@/components/ErrorAlert.vue'
import StatusPill from '@/components/StatusPill.vue'
import { usePageTitle } from '@/composables/usePageTitle'
import { useToastStore } from '@/stores/toast'

usePageTitle('Inventory')

const route = useRoute()
const toast = useToastStore()

const result = ref<Schemas['PagedResultOfInventoryItemDto']>()
const loading = ref(true)
const error = ref('')

const search = ref(typeof route.query.q === 'string' ? route.query.q : '')
const lowOnly = ref(route.query.low === '1')
const page = ref(1)

// Edited quantities by product id; an entry exists only while it differs from what is saved.
const drafts = reactive<Record<number, number>>({})
const saving = ref<number>()

async function load() {
  loading.value = true
  error.value = ''
  try {
    const { data } = await api.GET('/api/admin/inventory', {
      params: { query: { q: search.value.trim() || undefined, lowStock: lowOnly.value || undefined, page: page.value, pageSize: 20 } },
    })
    if (data) {
      result.value = data
      for (const key of Object.keys(drafts)) delete drafts[Number(key)]
    } else error.value = 'We could not load inventory.'
  } catch {
    error.value = 'We could not reach the API.'
  } finally {
    loading.value = false
  }
}
onMounted(load)

function refilter() {
  page.value = 1
  load()
}

function edit(item: Schemas['InventoryItemDto'], value: string) {
  const quantity = Number(value)
  if (!Number.isInteger(quantity) || quantity < 0 || quantity === item.quantityOnHand) delete drafts[item.productId]
  else drafts[item.productId] = quantity
}

async function save(item: Schemas['InventoryItemDto']) {
  const quantity = drafts[item.productId]
  if (quantity === undefined) return

  saving.value = item.productId
  const { data, error: problem } = await api.PUT('/api/admin/inventory/{productId}', {
    params: { path: { productId: item.productId } },
    body: { quantityOnHand: quantity },
  })
  saving.value = undefined

  if (!data) {
    toast.error(problemMessage(problem, 'We could not update the stock.'))
    return
  }
  Object.assign(item, data)
  delete drafts[item.productId]
  toast.success(`${item.name}: ${data.quantityOnHand} on hand.`)
}
</script>

<template>
  <div class="space-y-4">
    <h1 class="text-2xl font-bold">Inventory</h1>

    <div class="flex flex-wrap items-center gap-3">
      <form class="join" role="search" @submit.prevent="refilter">
        <input v-model="search" type="search" class="input input-sm join-item w-56" placeholder="Search name or SKU" aria-label="Search inventory" />
        <button class="btn btn-sm btn-neutral join-item" type="submit">Search</button>
      </form>
      <label class="label cursor-pointer gap-2 text-sm">
        <input v-model="lowOnly" type="checkbox" class="checkbox checkbox-sm" @change="refilter" />
        Low stock only
      </label>
    </div>

    <ErrorAlert v-if="error" :message="error" retry @retry="load" />
    <div v-else-if="loading && !result" class="skeleton h-64" />

    <template v-else-if="result">
      <div class="overflow-x-auto rounded-box border border-base-300 bg-base-100" :class="{ 'opacity-75': loading }">
        <table class="table">
          <thead>
            <tr><th>Product</th><th class="text-right">Reserved</th><th class="text-right">Available</th><th class="w-56">On hand</th></tr>
          </thead>
          <tbody>
            <tr v-for="item in result.items" :key="item.productId" class="hover:bg-base-200">
              <td>
                <RouterLink :to="`/products/${item.productId}`" class="link-hover font-medium">{{ item.name }}</RouterLink>
                <div class="font-mono text-xs opacity-75">{{ item.sku }}</div>
              </td>
              <td class="text-right">{{ item.quantityReserved }}</td>
              <td class="text-right">
                <StatusPill :tone="item.available === 0 ? 'error' : item.available <= 5 ? 'warning' : 'neutral'">{{ item.available }}</StatusPill>
              </td>
              <td>
                <form class="join" @submit.prevent="save(item)">
                  <input
                    type="number"
                    min="0"
                    class="input input-sm join-item w-24"
                    :value="drafts[item.productId] ?? item.quantityOnHand"
                    :aria-label="`On hand for ${item.name}`"
                    @input="edit(item, ($event.target as HTMLInputElement).value)"
                  />
                  <button class="btn btn-sm btn-primary join-item" type="submit" :disabled="drafts[item.productId] === undefined || saving === item.productId">
                    <span v-if="saving === item.productId" class="loading loading-spinner loading-xs" />
                    Save
                  </button>
                </form>
              </td>
            </tr>
            <tr v-if="!result.items.length"><td colspan="4" class="py-8 text-center opacity-70">Nothing to show.</td></tr>
          </tbody>
        </table>
      </div>
      <div class="flex items-center justify-between">
        <p class="text-sm opacity-70">Reserved units are held for open orders and can't be taken off the shelf.</p>
        <AppPagination :page="result.page" :page-size="result.pageSize" :total="result.totalCount" @change="(p) => { page = p; load() }" />
      </div>
    </template>
  </div>
</template>
