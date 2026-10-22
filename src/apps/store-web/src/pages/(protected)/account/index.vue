<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import type { Schemas } from '@/api/client'
import AddressForm from '@/components/AddressForm.vue'
import ErrorAlert from '@/components/ErrorAlert.vue'
import { usePageTitle } from '@/composables/usePageTitle'
import { useAddressStore } from '@/stores/addresses'
import { useAuthStore } from '@/stores/auth'
import { useToastStore } from '@/stores/toast'
import { countryName } from '@/utils/countries'
import { carrierFor } from '@/utils/shipping'

usePageTitle('My account')

const auth = useAuthStore()
const addressStore = useAddressStore()
const toast = useToastStore()
const router = useRouter()

onMounted(() => addressStore.load())

// ----- addresses -----
const editingId = ref<number | 'new'>()

const toRequest = (a: Schemas['AddressDto']): Schemas['AddressRequest'] => ({ ...a })

async function saveAddress(request: Schemas['AddressRequest']) {
  const id = editingId.value
  const result = id === 'new' || id === undefined ? await addressStore.create(request) : await addressStore.update(id, request)
  if (!result.ok) return result.message
  toast.success('Address saved.')
  editingId.value = undefined
}

async function removeAddress(id: number) {
  if (!confirm('Delete this address?')) return
  const result = await addressStore.remove(id)
  if (result.ok) toast.success('Address deleted.')
  else toast.error(result.message)
}

// ----- password -----
const oldPassword = ref('')
const newPassword = ref('')
const passwordBusy = ref(false)
const passwordError = ref('')

async function changePassword() {
  passwordBusy.value = true
  passwordError.value = ''
  const result = await auth.changePassword(oldPassword.value, newPassword.value)
  passwordBusy.value = false

  if (!result.ok) {
    passwordError.value = result.message
    return
  }
  oldPassword.value = newPassword.value = ''
  toast.success('Your password has been changed.')
}

async function resend() {
  const result = await auth.resendConfirmation()
  if (result.ok) toast.info('Confirmation email sent.')
  else toast.error(result.message)
}

async function signOut() {
  await auth.logout()
  addressStore.reset()
  router.push('/')
}
</script>

<template>
  <div class="mx-auto max-w-3xl space-y-6">
    <h1 class="text-2xl font-bold">My account</h1>

    <section class="card card-border bg-base-100">
      <div class="card-body gap-2">
        <h2 class="card-title">Profile</h2>
        <p>
          {{ auth.email }}
          <span v-if="auth.isEmailConfirmed" class="badge badge-outline ml-2">✓ Verified</span>
          <span v-else class="badge badge-outline ml-2">Not verified</span>
        </p>
        <div class="card-actions">
          <RouterLink to="/account/orders" class="btn btn-sm btn-primary">My orders</RouterLink>
          <button v-if="!auth.isEmailConfirmed" class="btn btn-sm" @click="resend">Resend verification email</button>
          <button class="btn btn-sm btn-ghost" @click="signOut">Sign out</button>
        </div>
      </div>
    </section>

    <section class="card card-border bg-base-100">
      <div class="card-body gap-3">
        <div class="flex items-center justify-between">
          <h2 class="card-title">Addresses</h2>
          <button v-if="editingId === undefined" class="btn btn-sm btn-primary" @click="editingId = 'new'">Add address</button>
        </div>

        <AddressForm
          v-if="editingId === 'new'"
          :submit="saveAddress"
          @cancel="editingId = undefined"
        />

        <p v-if="addressStore.loaded && !addressStore.addresses.length && editingId !== 'new'" class="opacity-70">
          You have not saved any addresses yet.
        </p>

        <ul class="space-y-3">
          <li v-for="a in addressStore.addresses" :key="a.id" class="rounded-box border border-base-300 p-3">
            <AddressForm
              v-if="editingId === a.id"
              :initial="toRequest(a)"
              :submit="saveAddress"
              @cancel="editingId = undefined"
            />
            <div v-else class="flex flex-wrap items-start justify-between gap-3">
              <p class="text-sm">
                <span class="font-semibold">{{ a.fullName }}</span>
                <span v-if="a.isDefault" class="badge badge-ghost badge-xs ml-2">Default</span><br />
                {{ a.line1 }}<template v-if="a.line2">, {{ a.line2 }}</template><br />
                {{ a.city }}<template v-if="a.region">, {{ a.region }}</template> {{ a.postalCode }}<br />
                {{ countryName(a.countryCode) }} · ships via {{ carrierFor(a.countryCode) }}
              </p>
              <div class="flex gap-1">
                <button class="btn btn-sm btn-ghost" @click="editingId = a.id">Edit</button>
                <button class="btn btn-sm btn-ghost text-error" @click="removeAddress(a.id)">Delete</button>
              </div>
            </div>
          </li>
        </ul>
      </div>
    </section>

    <form class="card card-border bg-base-100" @submit.prevent="changePassword">
      <div class="card-body gap-3">
        <h2 class="card-title">Change password</h2>
        <ErrorAlert v-if="passwordError" :message="passwordError" />
        <label class="form-control">
          <span class="label-text mb-1">Current password</span>
          <input v-model="oldPassword" type="password" class="input w-full" required autocomplete="current-password" />
        </label>
        <label class="form-control">
          <span class="label-text mb-1">New password</span>
          <input v-model="newPassword" type="password" class="input w-full" required minlength="8" autocomplete="new-password" />
        </label>
        <div class="card-actions">
          <button class="btn btn-primary" type="submit" :disabled="passwordBusy">
            <span v-if="passwordBusy" class="loading loading-spinner loading-sm" />
            Change password
          </button>
        </div>
      </div>
    </form>
  </div>
</template>
