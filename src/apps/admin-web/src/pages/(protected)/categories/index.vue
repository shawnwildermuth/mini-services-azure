<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { api, type Schemas } from '@/api/client'
import { problemMessage } from '@/api/errors'
import ErrorAlert from '@/components/ErrorAlert.vue'
import { usePageTitle } from '@/composables/usePageTitle'
import { useToastStore } from '@/stores/toast'
import { slugify } from '@/utils/format'

usePageTitle('Categories')

const toast = useToastStore()
const categories = ref<Schemas['AdminCategoryDto'][]>()
const error = ref('')

async function load() {
  error.value = ''
  const { data } = await api.GET('/api/admin/categories')
  if (data) categories.value = data
  else error.value = 'We could not load categories.'
}
onMounted(load)

const name = ref('')
const slug = ref('')
const slugTouched = ref(false)
const busy = ref(false)
const formError = ref('')

// Suggest a slug from the name until the admin edits the slug themselves.
watch(name, (value) => {
  if (!slugTouched.value) slug.value = slugify(value)
})

async function create() {
  busy.value = true
  formError.value = ''
  const { data, error: problem } = await api.POST('/api/admin/categories', { body: { name: name.value, slug: slug.value } })
  busy.value = false

  if (!data) {
    formError.value = problemMessage(problem, 'We could not create the category.')
    return
  }
  toast.success(`${data.name} created.`)
  name.value = slug.value = ''
  slugTouched.value = false
  await load()
}
</script>

<template>
  <div class="space-y-6">
    <h1 class="text-2xl font-bold">Categories</h1>

    <form class="card card-border bg-base-100" @submit.prevent="create">
      <div class="card-body gap-3">
        <h2 class="card-title text-base">Add a category</h2>
        <div class="grid gap-3 sm:grid-cols-[1fr_1fr_auto] sm:items-end">
          <label class="form-control">
            <span class="label-text mb-1">Name</span>
            <input v-model="name" class="input w-full" required maxlength="128" />
          </label>
          <label class="form-control">
            <span class="label-text mb-1">Slug (used in URLs)</span>
            <input v-model="slug" class="input w-full font-mono" required maxlength="64" pattern="[a-z0-9]+(-[a-z0-9]+)*" @input="slugTouched = true" />
          </label>
          <button class="btn btn-primary" type="submit" :disabled="busy">
            <span v-if="busy" class="loading loading-spinner loading-sm" />
            Add
          </button>
        </div>
        <ErrorAlert v-if="formError" :message="formError" />
      </div>
    </form>

    <ErrorAlert v-if="error" :message="error" retry @retry="load" />
    <div v-else-if="!categories" class="skeleton h-48" />
    <div v-else class="overflow-x-auto rounded-box border border-base-300 bg-base-100">
      <table class="table">
        <thead><tr><th>Name</th><th>Slug</th><th class="text-right">Products</th><th /></tr></thead>
        <tbody>
          <tr v-for="c in categories" :key="c.id" class="hover:bg-base-200">
            <td class="font-medium">{{ c.name }}</td>
            <td class="font-mono text-xs">{{ c.slug }}</td>
            <td class="text-right">
              <RouterLink :to="{ path: '/products', query: { category: c.slug } }" class="link-hover">{{ c.productCount }}</RouterLink>
            </td>
            <td class="text-right"><RouterLink :to="`/categories/${c.id}`" class="btn btn-ghost btn-sm">Edit</RouterLink></td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>
