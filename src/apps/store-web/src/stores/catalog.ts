import { ref } from 'vue'
import { defineStore } from 'pinia'
import { api, type Schemas } from '@/api/client'

/** Categories change rarely, so they are loaded once and shared by the navbar, home and filters. */
export const useCatalogStore = defineStore('catalog', () => {
  const categories = ref<Schemas['CategoryDto'][]>([])
  let loadPromise: Promise<void> | null = null

  function loadCategories(): Promise<void> {
    loadPromise ??= api
      .GET('/api/categories')
      .then(({ data }) => {
        categories.value = data ?? []
      })
      .catch(() => {
        loadPromise = null // allow a retry on the next call
      })
    return loadPromise
  }

  const nameFor = (slug: string) => categories.value.find((c) => c.slug === slug)?.name ?? slug

  return { categories, loadCategories, nameFor }
})
