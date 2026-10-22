<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { api, type Schemas } from '@/api/client'
import { problemMessage } from '@/api/errors'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import ErrorAlert from '@/components/ErrorAlert.vue'
import { usePageTitle } from '@/composables/usePageTitle'
import { useToastStore } from '@/stores/toast'

const route = useRoute('/(protected)/categories/[id]')
const router = useRouter()
const toast = useToastStore()

const category = ref<Schemas['AdminCategoryDto']>()
const status = ref<'loading' | 'ready' | 'notFound'>('loading')
const name = ref('')
const slug = ref('')
const busy = ref(false)
const error = ref('')
const confirmDelete = ref(false)
usePageTitle(() => category.value?.name ?? 'Category')

onMounted(async () => {
  const { data } = await api.GET('/api/admin/categories')
  const found = data?.find((c) => c.id === Number(route.params.id))
  if (!found) {
    status.value = 'notFound'
    return
  }
  category.value = found
  name.value = found.name
  slug.value = found.slug
  status.value = 'ready'
})

async function save() {
  busy.value = true
  error.value = ''
  const { data, error: problem } = await api.PUT('/api/admin/categories/{id}', {
    params: { path: { id: Number(route.params.id) } },
    body: { name: name.value, slug: slug.value },
  })
  busy.value = false

  if (!data) {
    error.value = problemMessage(problem, 'We could not save the category.')
    return
  }
  category.value = data
  toast.success('Category saved.')
}

async function remove() {
  busy.value = true
  const { response, error: problem } = await api.DELETE('/api/admin/categories/{id}', { params: { path: { id: Number(route.params.id) } } })
  busy.value = false
  confirmDelete.value = false

  if (!response.ok) {
    error.value = problemMessage(problem, 'We could not delete the category.')
    return
  }
  toast.success('Category deleted.')
  router.push('/categories')
}
</script>

<template>
  <div class="mx-auto max-w-xl space-y-4">
    <nav class="breadcrumbs text-sm" aria-label="Breadcrumb">
      <ul><li><RouterLink to="/categories">Categories</RouterLink></li><li>{{ category?.name ?? 'Edit' }}</li></ul>
    </nav>

    <div v-if="status === 'loading'" class="skeleton h-56" />
    <div v-else-if="status === 'notFound'" class="rounded-box bg-base-200 p-10 text-center">
      <h1 class="text-xl font-bold">Category not found</h1>
      <RouterLink to="/categories" class="btn btn-primary btn-sm mt-3">Back to categories</RouterLink>
    </div>

    <template v-else-if="category">
      <h1 class="text-2xl font-bold">{{ category.name }}</h1>
      <form class="card card-border bg-base-100" @submit.prevent="save">
        <div class="card-body gap-3">
          <ErrorAlert v-if="error" :message="error" />
          <label class="form-control">
            <span class="label-text mb-1">Name</span>
            <input v-model="name" class="input w-full" required maxlength="128" />
          </label>
          <label class="form-control">
            <span class="label-text mb-1">Slug</span>
            <input v-model="slug" class="input w-full font-mono" required maxlength="64" pattern="[a-z0-9]+(-[a-z0-9]+)*" />
            <span class="label-text-alt mt-1 text-warning">Changing the slug changes this category's storefront URL.</span>
          </label>
          <div class="card-actions justify-between">
            <button class="btn btn-primary" type="submit" :disabled="busy">Save</button>
            <button
              class="btn btn-ghost text-error"
              type="button"
              :disabled="category.productCount > 0"
              :title="category.productCount > 0 ? 'Move or deactivate its products first' : ''"
              @click="confirmDelete = true"
            >
              Delete
            </button>
          </div>
          <p v-if="category.productCount > 0" class="text-xs opacity-75">
            {{ category.productCount }} products use this category, so it cannot be deleted.
          </p>
        </div>
      </form>

      <ConfirmDialog :open="confirmDelete" title="Delete this category?" confirm-label="Delete" tone="btn-error" :busy="busy" @confirm="remove" @cancel="confirmDelete = false">
        <strong>{{ category.name }}</strong> will be removed from the storefront.
      </ConfirmDialog>
    </template>
  </div>
</template>
