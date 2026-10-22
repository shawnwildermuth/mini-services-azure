<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import ProductBrowser from '@/components/ProductBrowser.vue'

const route = useRoute('/categories/[slug]')
const slug = computed(() => route.params.slug)
</script>

<template>
  <ProductBrowser :key="slug" :fixed-category="slug" />
</template>
