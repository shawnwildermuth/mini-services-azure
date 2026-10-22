<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import type { Schemas } from '@/api/client'
import { countries } from '@/utils/countries'
import { carrierFor } from '@/utils/shipping'
import ErrorAlert from './ErrorAlert.vue'

type AddressRequest = Schemas['AddressRequest']

const props = defineProps<{
  initial?: Partial<AddressRequest>
  submitLabel?: string
  /** Returns an error message, or nothing on success. */
  submit: (address: AddressRequest) => Promise<string | void>
}>()
const emit = defineEmits<{ saved: []; cancel: [] }>()

const form = reactive({
  fullName: props.initial?.fullName ?? '',
  line1: props.initial?.line1 ?? '',
  line2: props.initial?.line2 ?? '',
  city: props.initial?.city ?? '',
  region: props.initial?.region ?? '',
  postalCode: props.initial?.postalCode ?? '',
  countryCode: props.initial?.countryCode ?? 'US',
  phone: props.initial?.phone ?? '',
  isDefault: props.initial?.isDefault ?? false,
})

const busy = ref(false)
const error = ref('')
const carrier = computed(() => carrierFor(form.countryCode))

async function onSubmit() {
  busy.value = true
  error.value = ''
  try {
    const message = await props.submit({
      ...form,
      line2: form.line2 || null,
      region: form.region || null,
      phone: form.phone || null,
    })
    if (message) error.value = message
    else emit('saved')
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <form class="grid gap-3 sm:grid-cols-2" @submit.prevent="onSubmit">
    <label class="form-control sm:col-span-2">
      <span class="label-text mb-1">Full name</span>
      <input v-model="form.fullName" class="input w-full" required maxlength="128" autocomplete="name" />
    </label>
    <label class="form-control sm:col-span-2">
      <span class="label-text mb-1">Address</span>
      <input v-model="form.line1" class="input w-full" required maxlength="200" autocomplete="address-line1" />
    </label>
    <label class="form-control sm:col-span-2">
      <span class="label-text mb-1">Apartment, suite, etc. (optional)</span>
      <input v-model="form.line2" class="input w-full" maxlength="200" autocomplete="address-line2" />
    </label>
    <label class="form-control">
      <span class="label-text mb-1">City</span>
      <input v-model="form.city" class="input w-full" required maxlength="100" autocomplete="address-level2" />
    </label>
    <label class="form-control">
      <span class="label-text mb-1">State / province</span>
      <input v-model="form.region" class="input w-full" maxlength="100" autocomplete="address-level1" />
    </label>
    <label class="form-control">
      <span class="label-text mb-1">Postal code</span>
      <input v-model="form.postalCode" class="input w-full" required maxlength="20" autocomplete="postal-code" />
    </label>
    <label class="form-control">
      <span class="label-text mb-1">Country</span>
      <select v-model="form.countryCode" class="select w-full" required autocomplete="country">
        <option v-for="c in countries" :key="c.code" :value="c.code">{{ c.name }}</option>
      </select>
    </label>
    <label class="form-control sm:col-span-2">
      <span class="label-text mb-1">Phone (optional)</span>
      <input v-model="form.phone" type="tel" class="input w-full" maxlength="32" autocomplete="tel" />
    </label>

    <p class="text-sm opacity-70 sm:col-span-2">
      Orders to this address ship with <span class="badge badge-neutral badge-sm">{{ carrier }}</span>.
    </p>

    <label class="label cursor-pointer justify-start gap-2 sm:col-span-2">
      <input v-model="form.isDefault" type="checkbox" class="checkbox checkbox-sm" />
      <span>Use as my default address</span>
    </label>

    <ErrorAlert v-if="error" :message="error" class="sm:col-span-2" />

    <div class="flex gap-2 sm:col-span-2">
      <button class="btn btn-primary" type="submit" :disabled="busy">
        <span v-if="busy" class="loading loading-spinner loading-sm" />
        {{ submitLabel ?? 'Save address' }}
      </button>
      <button class="btn btn-ghost" type="button" @click="emit('cancel')">Cancel</button>
    </div>
  </form>
</template>
