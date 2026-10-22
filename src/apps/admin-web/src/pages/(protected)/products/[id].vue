<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { api, type Schemas } from '@/api/client'
import { problemMessage } from '@/api/errors'
import ErrorAlert from '@/components/ErrorAlert.vue'
import ProductForm from '@/components/ProductForm.vue'
import { usePageTitle } from '@/composables/usePageTitle'
import { useToastStore } from '@/stores/toast'

const route = useRoute('/(protected)/products/[id]')
const toast = useToastStore()

const product = ref<Schemas['AdminProductDto']>()
const status = ref<'loading' | 'ready' | 'notFound' | 'error'>('loading')
usePageTitle(() => product.value?.name ?? 'Product')

async function load() {
  status.value = 'loading'
  try {
    const { data, response } = await api.GET('/api/admin/products/{id}', { params: { path: { id: Number(route.params.id) } } })
    if (response.status === 404) status.value = 'notFound'
    else if (!data) status.value = 'error'
    else {
      product.value = data
      status.value = 'ready'
    }
  } catch {
    status.value = 'error'
  }
}
onMounted(load)

async function save(values: Schemas['CreateProductRequest']) {
  const { sku: _sku, initialStock: _stock, ...body } = values // SKU and initial stock only apply when creating
  const { data, error } = await api.PUT('/api/admin/products/{id}', { params: { path: { id: Number(route.params.id) } }, body })
  if (!data) return problemMessage(error, 'We could not save the product.')
  product.value = data
  toast.success('Product saved.')
}
</script>

<template>
  <div class="mx-auto max-w-3xl space-y-4">
    <nav class="breadcrumbs text-sm" aria-label="Breadcrumb">
      <ul><li><RouterLink to="/products">Products</RouterLink></li><li>{{ product?.name ?? 'Edit' }}</li></ul>
    </nav>

    <div v-if="status === 'loading'" class="skeleton h-96" />
    <ErrorAlert v-else-if="status === 'error'" message="We could not load this product." retry @retry="load" />
    <div v-else-if="status === 'notFound'" class="rounded-box bg-base-200 p-10 text-center">
      <h1 class="text-xl font-bold">Product not found</h1>
      <RouterLink to="/products" class="btn btn-primary btn-sm mt-3">Back to products</RouterLink>
    </div>

    <template v-else-if="product">
      <div class="flex flex-wrap items-center justify-between gap-2">
        <h1 class="text-2xl font-bold">{{ product.name }}</h1>
        <RouterLink :to="`/inventory?q=${product.sku}`" class="btn btn-sm">
          Stock: {{ product.quantityOnHand }} on hand · {{ product.quantityReserved }} reserved
        </RouterLink>
      </div>
      <div class="card card-border bg-base-100">
        <div class="card-body"><ProductForm :key="product.id" :product="product" :submit="save" /></div>
      </div>
    </template>
  </div>
</template>
