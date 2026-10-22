<script setup lang="ts">
import type { Schemas } from '@/api/client'
import { categoryIcon } from '@/utils/categories'
import { money } from '@/utils/format'
import AddToCartButton from './AddToCartButton.vue'

defineProps<{ product: Schemas['ProductSummaryDto'] }>()
</script>

<template>
  <article class="card card-border bg-base-100 shadow-sm transition-shadow hover:shadow-md">
    <RouterLink :to="`/products/${product.sku}`" class="flex flex-col">
      <figure class="flex h-36 items-center justify-center bg-base-200 text-6xl" aria-hidden="true">
        {{ categoryIcon(product.categorySlug) }}
      </figure>
      <div class="card-body gap-1 pb-2">
        <span class="badge badge-ghost badge-sm">{{ product.brand }}</span>
        <h2 class="card-title text-base leading-snug">{{ product.name }}</h2>
        <p class="text-lg font-bold">{{ money(product.price) }}</p>
      </div>
    </RouterLink>
    <div class="card-actions px-6 pb-5">
      <AddToCartButton
        block
        :product="{ sku: product.sku, name: product.name, brand: product.brand, price: product.price }"
      />
    </div>
  </article>
</template>
