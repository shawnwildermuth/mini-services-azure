<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { api, type Schemas } from '@/api/client'
import ErrorAlert from './ErrorAlert.vue'

type Product = Schemas['AdminProductDto']

const props = defineProps<{
  /** Present when editing; absent when creating. */
  product?: Product
  /** Returns an error message, or nothing on success. */
  submit: (values: Schemas['CreateProductRequest']) => Promise<string | void>
}>()
const emit = defineEmits<{ saved: [] }>()

const isNew = !props.product
const categories = ref<Schemas['AdminCategoryDto'][]>([])
onMounted(async () => {
  categories.value = (await api.GET('/api/admin/categories')).data ?? []
  if (isNew && !form.categoryId) form.categoryId = categories.value[0]?.id ?? 0
})

const form = reactive({
  sku: props.product?.sku ?? '',
  name: props.product?.name ?? '',
  brand: props.product?.brand ?? '',
  description: props.product?.description ?? '',
  price: props.product?.price ?? 0,
  categoryId: props.product?.categoryId ?? 0,
  isActive: props.product?.isActive ?? true,
  initialStock: 0,
  specs: (props.product?.specs ?? []).map((s) => ({ ...s })),
})

const busy = ref(false)
const error = ref('')

// Suggestions keep spec names consistent with the ones the storefront filters on.
const commonSpecs = ['socket', 'cores', 'tdp', 'formFactor', 'memoryType', 'type', 'capacity', 'speed', 'interface', 'chipset', 'memory', 'wattage', 'efficiency', 'modular', 'color', 'size', 'sockets']

const addSpec = () => form.specs.push({ name: '', value: '' })
const removeSpec = (index: number) => form.specs.splice(index, 1)

async function onSubmit() {
  busy.value = true
  error.value = ''
  try {
    const message = await props.submit({
      ...form,
      specs: form.specs.filter((s) => s.name.trim() || s.value.trim()),
    })
    if (message) error.value = message
    else emit('saved')
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <form class="grid gap-4 md:grid-cols-2" @submit.prevent="onSubmit">
    <label v-if="isNew" class="form-control">
      <span class="label-text mb-1">SKU</span>
      <input v-model="form.sku" class="input w-full font-mono" required maxlength="63" pattern="[A-Za-z0-9][A-Za-z0-9._\-]{1,62}" />
      <span class="label-text-alt mt-1 opacity-75">Letters, digits, . _ - · cannot be changed later</span>
    </label>
    <div v-else class="form-control">
      <span class="label-text mb-1">SKU</span>
      <p class="input w-full items-center font-mono opacity-70">{{ form.sku }}</p>
    </div>

    <label class="form-control">
      <span class="label-text mb-1">Category</span>
      <select v-model.number="form.categoryId" class="select w-full" required>
        <option v-for="c in categories" :key="c.id" :value="c.id">{{ c.name }}</option>
      </select>
    </label>

    <label class="form-control md:col-span-2">
      <span class="label-text mb-1">Name</span>
      <input v-model="form.name" class="input w-full" required maxlength="256" />
    </label>

    <label class="form-control">
      <span class="label-text mb-1">Brand</span>
      <input v-model="form.brand" class="input w-full" required maxlength="128" />
    </label>

    <label class="form-control">
      <span class="label-text mb-1">Price (USD)</span>
      <input v-model.number="form.price" type="number" class="input w-full" required min="0.01" max="100000" step="0.01" />
    </label>

    <label class="form-control md:col-span-2">
      <span class="label-text mb-1">Description</span>
      <textarea v-model="form.description" class="textarea h-24 w-full" maxlength="2000" />
    </label>

    <label v-if="isNew" class="form-control">
      <span class="label-text mb-1">Initial stock</span>
      <input v-model.number="form.initialStock" type="number" class="input w-full" min="0" max="100000" />
    </label>

    <label class="label cursor-pointer justify-start gap-3 md:col-span-2">
      <input v-model="form.isActive" type="checkbox" class="toggle toggle-success" />
      <span>
        <span class="font-medium">{{ form.isActive ? 'Active' : 'Inactive' }}</span>
        <span class="block text-xs text-base-content/80">Inactive products are hidden from the storefront but keep their order history.</span>
      </span>
    </label>

    <fieldset class="md:col-span-2">
      <legend class="mb-2 font-semibold">Specifications</legend>
      <datalist id="spec-names"><option v-for="n in commonSpecs" :key="n" :value="n" /></datalist>
      <p v-if="!form.specs.length" class="text-sm opacity-75">No specifications. Add some so customers can filter by them.</p>
      <div v-for="(spec, i) in form.specs" :key="i" class="mb-2 flex gap-2">
        <input v-model="spec.name" list="spec-names" class="input input-sm w-2/5" placeholder="name (e.g. socket)" :aria-label="`Specification ${i + 1} name`" maxlength="64" />
        <input v-model="spec.value" class="input input-sm w-3/5" placeholder="value (e.g. AM5)" :aria-label="`Specification ${i + 1} value`" maxlength="128" />
        <button type="button" class="btn btn-ghost btn-sm" :aria-label="`Remove specification ${i + 1}`" @click="removeSpec(i)">✕</button>
      </div>
      <button type="button" class="btn btn-sm" @click="addSpec">+ Add specification</button>
    </fieldset>

    <ErrorAlert v-if="error" :message="error" class="md:col-span-2" />

    <div class="flex gap-2 md:col-span-2">
      <button class="btn btn-primary" type="submit" :disabled="busy">
        <span v-if="busy" class="loading loading-spinner loading-sm" />
        {{ isNew ? 'Create product' : 'Save changes' }}
      </button>
      <RouterLink to="/products" class="btn btn-ghost">Cancel</RouterLink>
    </div>
  </form>
</template>
