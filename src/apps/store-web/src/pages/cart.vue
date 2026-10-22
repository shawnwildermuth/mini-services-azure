<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { usePageTitle } from '@/composables/usePageTitle'
import { MAX_CART_QUANTITY } from '@/config'
import { useAuthStore } from '@/stores/auth'
import { useCartStore, type CartLine } from '@/stores/cart'
import { useToastStore } from '@/stores/toast'
import { categoryIcon } from '@/utils/categories'
import { money } from '@/utils/format'

usePageTitle('Your cart')

const auth = useAuthStore()
const cart = useCartStore()
const toast = useToastStore()
const router = useRouter()

const busySku = ref<string>()

const maxFor = (line: CartLine) => Math.min(line.available ?? MAX_CART_QUANTITY, MAX_CART_QUANTITY)

async function setQuantity(line: CartLine, quantity: number) {
  busySku.value = line.sku
  const result = await cart.setQuantity(
    { sku: line.sku, name: line.name, brand: line.brand, price: line.unitPrice },
    quantity,
  )
  if (!result.ok) toast.error(result.message)
  busySku.value = undefined
}

async function remove(line: CartLine) {
  busySku.value = line.sku
  const result = await cart.remove(line.sku)
  if (!result.ok) toast.error(result.message)
  busySku.value = undefined
}

function checkout() {
  router.push(auth.isSignedIn ? '/checkout' : { path: '/login', query: { redirect: '/checkout' } })
}
</script>

<template>
  <div class="space-y-4">
    <h1 class="text-2xl font-bold">Your cart</h1>

    <div v-if="cart.loading && !cart.lines.length" class="skeleton h-40" />

    <div v-else-if="!cart.lines.length" class="rounded-box bg-base-200 p-10 text-center">
      <p class="text-lg font-semibold">Your cart is empty.</p>
      <RouterLink to="/products" class="btn btn-primary btn-sm mt-3">Start shopping</RouterLink>
    </div>

    <div v-else class="grid gap-6 lg:grid-cols-[1fr_20rem]">
      <div class="overflow-x-auto rounded-box border border-base-300">
        <table class="table">
          <thead>
            <tr>
              <th>Product</th>
              <th class="text-right">Price</th>
              <th>Qty</th>
              <th class="text-right">Total</th>
              <th><span class="sr-only">Remove</span></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="line in cart.lines" :key="line.sku">
              <td>
                <RouterLink :to="`/products/${line.sku}`" class="link-hover font-medium">{{ line.name }}</RouterLink>
                <div class="text-xs opacity-75">{{ line.brand }}</div>
                <div v-if="line.available !== null && line.quantity > line.available" class="text-xs text-error">
                  Only {{ line.available }} available now.
                </div>
              </td>
              <td class="text-right">{{ money(line.unitPrice) }}</td>
              <td>
                <select
                  class="select select-sm w-20"
                  :value="line.quantity"
                  :disabled="busySku === line.sku"
                  :aria-label="`Quantity for ${line.name}`"
                  @change="setQuantity(line, Number(($event.target as HTMLSelectElement).value))"
                >
                  <option v-for="n in Math.max(maxFor(line), line.quantity)" :key="n" :value="n">{{ n }}</option>
                </select>
              </td>
              <td class="text-right font-medium">{{ money(line.lineTotal) }}</td>
              <td class="text-right">
                <button class="btn btn-ghost btn-sm" :disabled="busySku === line.sku" :aria-label="`Remove ${line.name}`" @click="remove(line)">✕</button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <aside class="card card-border h-fit bg-base-100">
        <div class="card-body gap-3">
          <h2 class="card-title">Order summary</h2>
          <div class="flex justify-between">
            <span>Subtotal ({{ cart.itemCount }} {{ cart.itemCount === 1 ? 'item' : 'items' }})</span>
            <span class="font-semibold">{{ money(cart.subtotal) }}</span>
          </div>
          <p class="text-xs opacity-75">Shipping is calculated at checkout.</p>
          <p v-if="!auth.isSignedIn" class="text-xs opacity-75">
            Sign in to confirm stock and current prices. Your cart will be kept.
          </p>
          <button class="btn btn-primary" @click="checkout">
            {{ auth.isSignedIn ? 'Proceed to checkout' : 'Sign in to check out' }}
          </button>
        </div>
      </aside>
    </div>
  </div>
</template>
