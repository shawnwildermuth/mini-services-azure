<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { api, type Schemas } from '@/api/client'
import AddToCartButton from '@/components/AddToCartButton.vue'
import ErrorAlert from '@/components/ErrorAlert.vue'
import StatusPill from '@/components/StatusPill.vue'
import { usePageTitle } from '@/composables/usePageTitle'
import { MAX_CART_QUANTITY } from '@/config'
import { categoryIcon } from '@/utils/categories'
import { money, specLabel } from '@/utils/format'

const route = useRoute('/products/[sku]')

const product = ref<Schemas['ProductDetailDto']>()
const availability = ref<Schemas['AvailabilityDto']>()
const status = ref<'loading' | 'ready' | 'notFound' | 'error'>('loading')
const quantity = ref(1)

usePageTitle(() => product.value?.name)

async function load() {
  status.value = 'loading'
  product.value = availability.value = undefined
  quantity.value = 1
  try {
    const sku = route.params.sku
    const [detail, stock] = await Promise.all([
      api.GET('/api/products/{sku}', { params: { path: { sku } } }),
      api.GET('/api/products/{sku}/availability', { params: { path: { sku } } }),
    ])
    if (detail.response.status === 404) {
      status.value = 'notFound'
      return
    }
    if (!detail.data) throw new Error('product request failed')
    product.value = detail.data
    availability.value = stock.data
    status.value = 'ready'
  } catch {
    status.value = 'error'
  }
}

watch(() => route.params.sku, load, { immediate: true })

const inStock = computed(() => availability.value?.inStock ?? false)
const maxQuantity = computed(() => Math.min(availability.value?.available ?? 1, MAX_CART_QUANTITY))
const productRef = computed(() =>
  product.value
    ? { sku: product.value.sku, name: product.value.name, brand: product.value.brand, price: product.value.price }
    : undefined,
)
</script>

<template>
  <div v-if="status === 'loading'" class="grid gap-8 md:grid-cols-2">
    <div class="skeleton h-80" />
    <div class="space-y-4">
      <div class="skeleton h-8 w-3/4" />
      <div class="skeleton h-6 w-1/4" />
      <div class="skeleton h-32" />
    </div>
  </div>

  <ErrorAlert v-else-if="status === 'error'" message="We could not load this product." retry @retry="load" />

  <div v-else-if="status === 'notFound'" class="rounded-box bg-base-200 p-10 text-center">
    <h1 class="text-2xl font-bold">Product not found</h1>
    <p class="py-2 opacity-70">It may have been discontinued.</p>
    <RouterLink to="/products" class="btn btn-primary btn-sm">Browse all products</RouterLink>
  </div>

  <article v-else-if="product && productRef" class="space-y-6">
    <nav class="breadcrumbs text-sm" aria-label="Breadcrumb">
      <ul>
        <li><RouterLink to="/products">Products</RouterLink></li>
        <li><RouterLink :to="`/categories/${product.categorySlug}`">{{ product.categoryName }}</RouterLink></li>
        <li>{{ product.name }}</li>
      </ul>
    </nav>

    <div class="grid gap-8 md:grid-cols-2">
      <div class="flex h-80 items-center justify-center rounded-box bg-base-200 text-9xl" aria-hidden="true">
        {{ categoryIcon(product.categorySlug) }}
      </div>

      <div class="space-y-4">
        <span class="badge badge-ghost">{{ product.brand }}</span>
        <h1 class="text-3xl font-bold">{{ product.name }}</h1>
        <p class="text-3xl font-semibold text-primary">{{ money(product.price) }}</p>

        <p>
          <StatusPill v-if="inStock" tone="success">
            In stock<template v-if="(availability?.available ?? 0) <= 5"> — only {{ availability?.available }} left</template>
          </StatusPill>
          <StatusPill v-else tone="error">Out of stock</StatusPill>
        </p>

        <p class="opacity-80">{{ product.description }}</p>

        <div class="flex flex-wrap items-center gap-3">
          <label v-if="inStock" class="flex items-center gap-2">
            <span class="text-sm">Qty</span>
            <select v-model.number="quantity" class="select select-sm w-20">
              <option v-for="n in maxQuantity" :key="n" :value="n">{{ n }}</option>
            </select>
          </label>
          <AddToCartButton :product="productRef" :quantity="quantity" :disabled="!inStock" />
        </div>
      </div>
    </div>

    <section>
      <h2 class="mb-2 text-xl font-bold">Specifications</h2>
      <div class="overflow-x-auto rounded-box border border-base-300">
        <table class="table">
          <tbody>
            <tr v-for="(value, name) in product.specs" :key="name">
              <th class="w-1/3">{{ specLabel(String(name)) }}</th>
              <td>{{ value }}</td>
            </tr>
            <tr>
              <th>SKU</th>
              <td class="font-mono text-sm">{{ product.sku }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>
  </article>
</template>
