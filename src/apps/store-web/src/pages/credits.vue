<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { usePageTitle } from '@/composables/usePageTitle'

type Credit = {
  skus: string[]
  file: string
  title: string
  author: string
  license: string
  licenseUrl: string
  source: string
}

usePageTitle('Image credits')

const credits = ref<Credit[]>([])

onMounted(async () => {
  try {
    const response = await fetch('/images/products/credits.json')
    if (response.ok) credits.value = await response.json()
  } catch {
    // The page still renders its intro without the list.
  }
})
</script>

<template>
  <div class="mx-auto max-w-3xl space-y-4">
    <h1 class="text-3xl font-bold">Image credits</h1>
    <p class="opacity-80">
      Sample product photos come from Wikimedia Commons and are used under the licenses below.
      Products without a photo show a category icon.
    </p>
    <ul class="space-y-3">
      <li v-for="credit in credits" :key="credit.file" class="rounded-box border border-base-300 p-4 text-sm">
        <a :href="credit.source" class="link font-semibold" target="_blank" rel="noopener">{{ credit.title }}</a>
        by {{ credit.author }} ·
        <a :href="credit.licenseUrl" class="link" target="_blank" rel="noopener">{{ credit.license }}</a>
      </li>
    </ul>
  </div>
</template>
