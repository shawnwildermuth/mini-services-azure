<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter, type LocationQueryRaw, type LocationQueryValue } from 'vue-router'
import { api, type Schemas } from '@/api/client'
import { usePageTitle } from '@/composables/usePageTitle'
import { useCatalogStore } from '@/stores/catalog'
import { specLabel } from '@/utils/format'
import AppPagination from './AppPagination.vue'
import ErrorAlert from './ErrorAlert.vue'
import ProductCard from './ProductCard.vue'

/**
 * Product list with filters. All filter state lives in the URL query (?q=&brand=&spec=socket:AM5...),
 * so results are shareable and the back button works. When `fixedCategory` is set (the category
 * page) the category comes from the path instead of the query.
 */
const props = defineProps<{ fixedCategory?: string }>()

const route = useRoute()
const router = useRouter()
const catalog = useCatalogStore()
catalog.loadCategories()

type QueryValue = LocationQueryValue | LocationQueryValue[] | undefined

const first = (value: QueryValue) => (Array.isArray(value) ? value[0] : value) || undefined
const all = (value: QueryValue) => (Array.isArray(value) ? value : value ? [value] : []).filter((v): v is string => !!v)

const category = computed(() => props.fixedCategory ?? first(route.query.category))
const q = computed(() => first(route.query.q))
const brand = computed(() => first(route.query.brand))
const minPrice = computed(() => first(route.query.minPrice))
const maxPrice = computed(() => first(route.query.maxPrice))
const sort = computed(() => first(route.query.sort) ?? 'name')
const specs = computed(() => all(route.query.spec))
const page = computed(() => Math.max(Number(first(route.query.page)) || 1, 1))

const result = ref<Schemas['PagedResultOfProductSummaryDto']>()
const facets = ref<Schemas['FacetsDto']>()
const loading = ref(true)
const error = ref('')

let requestId = 0

async function loadProducts() {
  const id = ++requestId
  loading.value = true
  error.value = ''
  try {
    const { data, response } = await api.GET('/api/products', {
      params: {
        query: {
          category: category.value,
          q: q.value,
          brand: brand.value,
          minPrice: minPrice.value ? Number(minPrice.value) : undefined,
          maxPrice: maxPrice.value ? Number(maxPrice.value) : undefined,
          spec: specs.value.length ? specs.value : undefined,
          sort: sort.value,
          page: page.value,
          pageSize: 12,
        },
      },
    })
    if (id !== requestId) return // a newer request superseded this one
    if (data) result.value = data
    else error.value = response.status === 429 ? 'Too many requests. Please wait a moment.' : 'We could not load products.'
  } catch {
    if (id === requestId) error.value = 'We could not reach the store. Check your connection and try again.'
  } finally {
    if (id === requestId) loading.value = false
  }
}

async function loadFacets() {
  facets.value = undefined
  const { data } = await api.GET('/api/products/facets', { params: { query: { category: category.value } } })
  facets.value = data
}

watch([category, q, brand, minPrice, maxPrice, sort, specs, page], loadProducts, { immediate: true })
watch(category, loadFacets, { immediate: true })

// ----- filter inputs (local drafts, applied on submit/change) -----
const qDraft = ref(q.value ?? '')
const minDraft = ref(minPrice.value ?? '')
const maxDraft = ref(maxPrice.value ?? '')
watch(q, (v) => (qDraft.value = v ?? ''))
watch(minPrice, (v) => (minDraft.value = v ?? ''))
watch(maxPrice, (v) => (maxDraft.value = v ?? ''))

function update(patch: Record<string, string | string[] | undefined>, keepPage = false) {
  const next: LocationQueryRaw = { ...route.query, ...patch }
  if (!keepPage && !('page' in patch)) delete next.page
  for (const key of Object.keys(next)) {
    const value = next[key]
    if (value == null || value === '' || (Array.isArray(value) && value.length === 0)) delete next[key]
  }
  router.push({ query: next })
}

const toggleSpec = (name: string, value: string, on: boolean) => {
  const set = new Set(specs.value)
  const key = `${name}:${value}`
  if (on) set.add(key)
  else set.delete(key)
  update({ spec: [...set] })
}

const hasFilters = computed(
  () => !!(q.value || brand.value || minPrice.value || maxPrice.value || specs.value.length || (!props.fixedCategory && category.value)),
)

function clearFilters() {
  router.push({ query: sort.value !== 'name' ? { sort: sort.value } : {} })
}

const heading = computed(() => {
  if (props.fixedCategory) return catalog.nameFor(props.fixedCategory)
  if (q.value) return `Results for “${q.value}”`
  return category.value ? catalog.nameFor(category.value) : 'All products'
})
usePageTitle(heading)

const sortOptions = [
  { value: 'name', label: 'Name (A–Z)' },
  { value: '-name', label: 'Name (Z–A)' },
  { value: 'price', label: 'Price (low to high)' },
  { value: '-price', label: 'Price (high to low)' },
]
</script>

<template>
  <div class="grid gap-6 lg:grid-cols-[16rem_1fr]">
    <!-- Filters: always open on large screens, collapsible on small ones -->
    <aside>
      <div class="collapse collapse-arrow rounded-box bg-base-200 lg:collapse-open">
        <input type="checkbox" class="lg:hidden" aria-label="Toggle filters" />
        <div class="collapse-title font-semibold">Filters</div>
        <div class="collapse-content space-y-4">
          <form class="join w-full" role="search" @submit.prevent="update({ q: qDraft.trim() })">
            <input v-model="qDraft" type="search" class="input input-sm join-item w-full" placeholder="Search" aria-label="Search" />
            <button class="btn btn-sm btn-neutral join-item" type="submit">Go</button>
          </form>

          <fieldset v-if="!fixedCategory" class="fieldset">
            <legend class="fieldset-legend">Category</legend>
            <select class="select select-sm w-full" aria-label="Category" :value="category ?? ''" @change="update({ category: ($event.target as HTMLSelectElement).value })">
              <option value="">All categories</option>
              <option v-for="c in catalog.categories" :key="c.slug" :value="c.slug">{{ c.name }}</option>
            </select>
          </fieldset>

          <fieldset v-if="facets?.brands.length" class="fieldset">
            <legend class="fieldset-legend">Brand</legend>
            <select class="select select-sm w-full" aria-label="Brand" :value="brand ?? ''" @change="update({ brand: ($event.target as HTMLSelectElement).value })">
              <option value="">All brands</option>
              <option v-for="b in facets.brands" :key="b" :value="b">{{ b }}</option>
            </select>
          </fieldset>

          <fieldset class="fieldset">
            <legend class="fieldset-legend">Price</legend>
            <form class="flex items-center gap-2" @submit.prevent="update({ minPrice: minDraft, maxPrice: maxDraft })">
              <input v-model="minDraft" type="number" min="0" class="input input-sm w-full" :placeholder="facets ? String(Math.floor(facets.minPrice)) : 'Min'" aria-label="Minimum price" />
              <span aria-hidden="true">–</span>
              <input v-model="maxDraft" type="number" min="0" class="input input-sm w-full" :placeholder="facets ? String(Math.ceil(facets.maxPrice)) : 'Max'" aria-label="Maximum price" />
              <button class="btn btn-sm btn-neutral" type="submit">Set</button>
            </form>
          </fieldset>

          <template v-if="category && facets">
            <fieldset v-for="(values, name) in facets.specs" :key="name" class="fieldset">
              <legend class="fieldset-legend">{{ specLabel(String(name)) }}</legend>
              <label v-for="value in values" :key="value" class="label cursor-pointer justify-start gap-2 text-sm">
                <input
                  type="checkbox"
                  class="checkbox checkbox-sm"
                  :checked="specs.includes(`${name}:${value}`)"
                  @change="toggleSpec(String(name), value, ($event.target as HTMLInputElement).checked)"
                />
                {{ value }}
              </label>
            </fieldset>
          </template>
          <p v-else-if="!category" class="text-xs opacity-75">Choose a category to filter by specifications.</p>

          <button v-if="hasFilters" class="btn btn-sm btn-ghost w-full" @click="clearFilters">Clear filters</button>
        </div>
      </div>
    </aside>

    <section aria-live="polite">
      <div class="mb-4 flex flex-wrap items-center justify-between gap-2">
        <div>
          <h1 class="text-2xl font-bold">{{ heading }}</h1>
          <p class="text-sm opacity-70">
            <template v-if="result">{{ result.totalCount }} {{ result.totalCount === 1 ? 'product' : 'products' }}</template>
            <span v-else class="skeleton inline-block h-4 w-24" />
          </p>
        </div>
        <label class="flex items-center gap-2 whitespace-nowrap text-sm">
          Sort by
          <select class="select select-sm" :value="sort" @change="update({ sort: ($event.target as HTMLSelectElement).value }, true)">
            <option v-for="o in sortOptions" :key="o.value" :value="o.value">{{ o.label }}</option>
          </select>
        </label>
      </div>

      <ErrorAlert v-if="error" :message="error" retry @retry="loadProducts" />

      <div v-else-if="loading && !result" class="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
        <div v-for="n in 6" :key="n" class="skeleton h-72" />
      </div>

      <div v-else-if="result && result.items.length === 0" class="rounded-box bg-base-200 p-10 text-center">
        <p class="text-lg font-semibold">No products match your filters.</p>
        <button v-if="hasFilters" class="btn btn-primary btn-sm mt-3" @click="clearFilters">Clear filters</button>
      </div>

      <template v-else-if="result">
        <div class="grid gap-4 transition-opacity sm:grid-cols-2 xl:grid-cols-3" :class="{ 'opacity-50': loading }">
          <ProductCard v-for="p in result.items" :key="p.sku" :product="p" />
        </div>
        <div class="mt-8 flex justify-center">
          <AppPagination :page="result.page" :page-size="result.pageSize" :total="result.totalCount" @change="update({ page: String($event) }, true)" />
        </div>
      </template>
    </section>
  </div>
</template>
