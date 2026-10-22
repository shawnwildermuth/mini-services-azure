<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { api, type Schemas } from '@/api/client'
import { problemMessage } from '@/api/errors'
import AddressForm from '@/components/AddressForm.vue'
import ErrorAlert from '@/components/ErrorAlert.vue'
import { usePageTitle } from '@/composables/usePageTitle'
import { useAddressStore } from '@/stores/addresses'
import { useCartStore } from '@/stores/cart'
import { useToastStore } from '@/stores/toast'
import { countryName } from '@/utils/countries'
import { money } from '@/utils/format'

usePageTitle('Checkout')

const cart = useCartStore()
const addressStore = useAddressStore()
const toast = useToastStore()
const router = useRouter()

const selectedId = ref<number>()
const adding = ref(false)
const quote = ref<Schemas['QuoteDto']>()
const placing = ref(false)
const error = ref('')

// One key per checkout visit: if a request is retried (double click, flaky network) the server
// returns the order it already created instead of creating a second one.
const idempotencyKey = crypto.randomUUID()

onMounted(async () => {
  await Promise.all([addressStore.load(), cart.load()])
  const addresses = addressStore.addresses
  selectedId.value = (addresses.find((a) => a.isDefault) ?? addresses[0])?.id
  adding.value = addresses.length === 0
})

const selected = computed(() => addressStore.addresses.find((a) => a.id === selectedId.value))
const hasStockProblem = computed(() => cart.lines.some((l) => l.available !== null && l.quantity > l.available))

// Shipping depends on the destination, so re-price whenever the address or cart changes.
watch(
  [selectedId, () => cart.subtotal],
  async () => {
    quote.value = undefined
    if (selectedId.value === undefined || !cart.lines.length) return
    const { data } = await api.GET('/api/checkout/quote', { params: { query: { addressId: selectedId.value } } })
    quote.value = data
  },
  { immediate: true },
)

async function createAddress(request: Schemas['AddressRequest']) {
  const result = await addressStore.create(request)
  if (!result.ok) return result.message
  selectedId.value = [...addressStore.addresses].sort((a, b) => b.id - a.id)[0]?.id
  adding.value = false
}

async function placeOrder() {
  if (selectedId.value === undefined || placing.value) return
  placing.value = true
  error.value = ''
  try {
    const { data, error: problem, response } = await api.POST('/api/orders', {
      params: { header: { 'Idempotency-Key': idempotencyKey } },
      body: { addressId: selectedId.value },
    })

    if (data) {
      await cart.load() // the server emptied the cart
      toast.success(`Order ${data.orderNumber} placed.`)
      router.push(`/account/orders/${data.id}`)
      return
    }

    if (response.status === 409) await cart.load() // show the up-to-date availability
    error.value =
      response.status === 429 ? 'Too many requests. Please wait a moment and try again.' : problemMessage(problem, 'We could not place your order.')
  } catch {
    error.value = 'We could not reach the store. Check your connection and try again.'
  } finally {
    placing.value = false
  }
}
</script>

<template>
  <div class="space-y-4">
    <h1 class="text-2xl font-bold">Checkout</h1>

    <div v-if="!cart.lines.length && !cart.loading" class="rounded-box bg-base-200 p-10 text-center">
      <p class="text-lg font-semibold">Your cart is empty.</p>
      <RouterLink to="/products" class="btn btn-primary btn-sm mt-3">Start shopping</RouterLink>
    </div>

    <div v-else class="grid gap-6 lg:grid-cols-[1fr_22rem]">
      <div class="space-y-6">
        <section class="card card-border bg-base-100">
          <div class="card-body gap-3">
            <h2 class="card-title">1. Shipping address</h2>

            <div v-if="addressStore.addresses.length && !adding" class="grid gap-3 sm:grid-cols-2">
              <label
                v-for="a in addressStore.addresses"
                :key="a.id"
                class="flex cursor-pointer gap-3 rounded-box border p-3"
                :class="selectedId === a.id ? 'border-primary bg-primary/5' : 'border-base-300'"
              >
                <input v-model="selectedId" type="radio" class="radio radio-primary radio-sm mt-1" name="address" :value="a.id" />
                <span class="text-sm">
                  <span class="font-semibold">{{ a.fullName }}</span>
                  <span v-if="a.isDefault" class="badge badge-ghost badge-xs ml-2">Default</span><br />
                  {{ a.line1 }}<template v-if="a.line2">, {{ a.line2 }}</template><br />
                  {{ a.city }}<template v-if="a.region">, {{ a.region }}</template> {{ a.postalCode }}<br />
                  {{ countryName(a.countryCode) }}
                </span>
              </label>
            </div>

            <button v-if="addressStore.addresses.length && !adding" class="btn btn-ghost btn-sm w-fit" @click="adding = true">
              + Use a new address
            </button>

            <AddressForm
              v-if="adding"
              submit-label="Save and use this address"
              :submit="createAddress"
              @cancel="adding = false"
            />
          </div>
        </section>

        <section class="card card-border bg-base-100">
          <div class="card-body gap-3">
            <h2 class="card-title">2. Review items</h2>
            <ul class="divide-y divide-base-300">
              <li v-for="line in cart.lines" :key="line.sku" class="flex justify-between gap-4 py-2 text-sm">
                <span>{{ line.quantity }} × {{ line.name }}</span>
                <span class="font-medium">{{ money(line.lineTotal) }}</span>
              </li>
            </ul>
            <RouterLink to="/cart" class="link text-sm">Edit cart</RouterLink>
            <div v-if="hasStockProblem" role="alert" class="alert alert-warning alert-soft">
              Some items are no longer available in the quantity you chose. Please update your cart.
            </div>
          </div>
        </section>
      </div>

      <aside class="card card-border h-fit bg-base-100">
        <div class="card-body gap-3">
          <h2 class="card-title">Order summary</h2>
          <dl class="space-y-1 text-sm">
            <div class="flex justify-between">
              <dt>Subtotal</dt>
              <dd>{{ money(quote?.subtotal ?? cart.subtotal) }}</dd>
            </div>
            <div class="flex justify-between">
              <dt>
                Shipping
                <span v-if="quote" class="badge badge-neutral badge-sm">{{ quote.carrier }}</span>
              </dt>
              <dd v-if="quote">{{ money(quote.shippingCost) }}</dd>
              <dd v-else class="opacity-75">{{ selected ? '…' : 'Choose an address' }}</dd>
            </div>
            <div class="divider my-1" />
            <div class="flex justify-between text-base font-semibold">
              <dt>Total</dt>
              <dd>{{ quote ? money(quote.total) : '—' }}</dd>
            </div>
          </dl>

          <ErrorAlert v-if="error" :message="error" />

          <button class="btn btn-primary" :disabled="!quote || hasStockProblem || placing" @click="placeOrder">
            <span v-if="placing" class="loading loading-spinner loading-sm" />
            Place order
          </button>
          <p class="text-xs opacity-75">Payments are simulated; no card is charged.</p>
        </div>
      </aside>
    </div>
  </div>
</template>
