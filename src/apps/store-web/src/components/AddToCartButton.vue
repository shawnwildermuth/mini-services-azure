<script setup lang="ts">
import { ref } from 'vue'
import { useCartStore, type ProductRef } from '@/stores/cart'
import { useToastStore } from '@/stores/toast'

const props = withDefaults(defineProps<{ product: ProductRef; quantity?: number; disabled?: boolean; block?: boolean }>(), {
  quantity: 1,
})

const cart = useCartStore()
const toast = useToastStore()
const busy = ref(false)

async function add() {
  busy.value = true
  try {
    const result = await cart.add(props.product, props.quantity)
    if (result.ok) toast.success(`${props.product.name} added to your cart.`)
    else toast.error(result.message)
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <button class="btn btn-primary" :class="{ 'btn-block': block }" :disabled="disabled || busy" @click.prevent="add">
    <span v-if="busy" class="loading loading-spinner loading-sm" />
    {{ disabled ? 'Out of stock' : 'Add to cart' }}
  </button>
</template>
