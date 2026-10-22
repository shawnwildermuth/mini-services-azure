<script setup lang="ts">
import { useRouter } from 'vue-router'
import { api, type Schemas } from '@/api/client'
import { problemMessage } from '@/api/errors'
import ProductForm from '@/components/ProductForm.vue'
import { usePageTitle } from '@/composables/usePageTitle'
import { useToastStore } from '@/stores/toast'

usePageTitle('New product')

const router = useRouter()
const toast = useToastStore()

async function create(values: Schemas['CreateProductRequest']) {
  const { data, error } = await api.POST('/api/admin/products', { body: values })
  if (!data) return problemMessage(error, 'We could not create the product.')
  toast.success(`${data.name} created.`)
  router.push(`/products/${data.id}`)
}
</script>

<template>
  <div class="mx-auto max-w-3xl space-y-4">
    <nav class="breadcrumbs text-sm" aria-label="Breadcrumb">
      <ul><li><RouterLink to="/products">Products</RouterLink></li><li>New</li></ul>
    </nav>
    <h1 class="text-2xl font-bold">New product</h1>
    <div class="card card-border bg-base-100"><div class="card-body"><ProductForm :submit="create" /></div></div>
  </div>
</template>
