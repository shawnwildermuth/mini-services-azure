<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { api, type Schemas } from '@/api/client'
import ProductCard from '@/components/ProductCard.vue'
import { usePageTitle } from '@/composables/usePageTitle'
import { useCatalogStore } from '@/stores/catalog'
import { categoryIcon } from '@/utils/categories'

usePageTitle(undefined)

const catalog = useCatalogStore()
catalog.loadCategories()

const featured = ref<Schemas['ProductSummaryDto'][]>([])
onMounted(async () => {
  const { data } = await api.GET('/api/products', { params: { query: { sort: '-price', pageSize: 4 } } })
  featured.value = data?.items ?? []
})
</script>

<template>
  <div class="space-y-12">
    <section class="hero rounded-box bg-base-200 py-12">
      <div class="hero-content text-center">
        <div class="max-w-xl">
          <h1 class="text-4xl font-bold">Build your next machine</h1>
          <p class="py-4">Processors, graphics cards, memory and everything in between. UPS across North America, DHL worldwide.</p>
          <RouterLink to="/products" class="btn btn-primary">Shop all parts</RouterLink>
        </div>
      </div>
    </section>

    <section>
      <h2 class="mb-4 text-2xl font-bold">Shop by category</h2>
      <div class="grid grid-cols-2 gap-3 md:grid-cols-4">
        <RouterLink
          v-for="c in catalog.categories"
          :key="c.slug"
          :to="`/categories/${c.slug}`"
          class="card card-border bg-base-100 transition-shadow hover:shadow-md"
        >
          <div class="card-body items-center gap-1 text-center">
            <span class="text-4xl" aria-hidden="true">{{ categoryIcon(c.slug) }}</span>
            <h3 class="font-semibold">{{ c.name }}</h3>
            <span class="text-xs opacity-75">{{ c.productCount }} products</span>
          </div>
        </RouterLink>
        <template v-if="!catalog.categories.length">
          <div v-for="n in 8" :key="n" class="skeleton h-32" />
        </template>
      </div>
    </section>

    <section v-if="featured.length">
      <h2 class="mb-4 text-2xl font-bold">Top of the line</h2>
      <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <ProductCard v-for="p in featured" :key="p.sku" :product="p" />
      </div>
    </section>
  </div>
</template>
