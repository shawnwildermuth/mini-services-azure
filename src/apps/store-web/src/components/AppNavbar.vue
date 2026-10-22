<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useCartStore } from '@/stores/cart'
import { useCatalogStore } from '@/stores/catalog'
import { useToastStore } from '@/stores/toast'
import { useTheme } from '@/composables/useTheme'
import { categoryIcon } from '@/utils/categories'

const auth = useAuthStore()
const cart = useCartStore()
const catalog = useCatalogStore()
const toast = useToastStore()
const router = useRouter()
const { theme, toggle } = useTheme()

catalog.loadCategories()

const search = ref('')

// The visible badge shows the count, so the accessible name must contain it too (WCAG 2.5.3 label in name).
const cartLabel = computed(() =>
  cart.itemCount ? `Shopping cart, ${cart.itemCount} ${cart.itemCount === 1 ? 'item' : 'items'}` : 'Shopping cart',
)

function submitSearch() {
  const q = search.value.trim()
  router.push({ path: '/products', query: q ? { q } : {} })
}

// daisyUI dropdowns stay open while focused; blur to close after a selection.
function closeMenus() {
  ;(document.activeElement as HTMLElement | null)?.blur()
}

async function signOut() {
  closeMenus()
  await auth.logout()
  toast.info('You have been signed out.')
  router.push('/')
}
</script>

<template>
  <header class="navbar sticky top-0 z-30 gap-1 border-b border-base-300 bg-base-100/90 px-2 backdrop-blur sm:px-4">
    <div class="navbar-start gap-1 max-lg:w-auto max-lg:flex-1">
      <div class="dropdown lg:hidden">
        <div tabindex="0" role="button" class="btn btn-ghost btn-square" aria-label="Open menu">
          <svg xmlns="http://www.w3.org/2000/svg" class="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 6h16M4 12h16M4 18h8" />
          </svg>
        </div>
        <ul tabindex="0" class="menu dropdown-content z-40 mt-3 w-60 rounded-box bg-base-100 p-2 shadow">
          <li><RouterLink to="/products" @click="closeMenus">All products</RouterLink></li>
          <li v-for="c in catalog.categories" :key="c.slug">
            <RouterLink :to="`/categories/${c.slug}`" @click="closeMenus">
              {{ categoryIcon(c.slug) }} {{ c.name }}
            </RouterLink>
          </li>
        </ul>
      </div>
      <RouterLink to="/" class="btn btn-ghost px-2 text-xl font-bold">
        <span aria-hidden="true">🖥️</span>
        <!-- always in the accessibility tree; only visible from the sm breakpoint up -->
        <span class="max-sm:sr-only">Parts Store</span>
      </RouterLink>
    </div>

    <nav class="navbar-center hidden lg:flex">
      <ul class="menu menu-horizontal gap-1 px-1">
        <li><RouterLink to="/products">All products</RouterLink></li>
        <li>
          <details>
            <summary>Categories</summary>
            <ul class="z-40 w-56 rounded-box bg-base-100 p-2 shadow">
              <li v-for="c in catalog.categories" :key="c.slug">
                <RouterLink :to="`/categories/${c.slug}`" @click="closeMenus">
                  {{ categoryIcon(c.slug) }} {{ c.name }}
                </RouterLink>
              </li>
            </ul>
          </details>
        </li>
      </ul>
    </nav>

    <div class="navbar-end gap-1">
      <form class="hidden md:block" role="search" @submit.prevent="submitSearch">
        <label class="input input-sm w-48 lg:w-64">
          <svg class="h-4 w-4 opacity-50" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <circle cx="11" cy="11" r="8" /><path d="m21 21-4.3-4.3" />
          </svg>
          <input v-model="search" type="search" placeholder="Search parts" aria-label="Search parts" />
        </label>
      </form>

      <button class="btn btn-ghost btn-circle" :aria-label="`Switch to ${theme === 'dark' ? 'light' : 'dark'} theme`" @click="toggle">
        <span aria-hidden="true">{{ theme === 'dark' ? '☀️' : '🌙' }}</span>
      </button>

      <RouterLink to="/cart" class="btn btn-ghost btn-circle" :aria-label="cartLabel">
        <div class="indicator">
          <svg xmlns="http://www.w3.org/2000/svg" class="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M3 3h2l.4 2M7 13h10l4-8H5.4M7 13L5.4 5M7 13l-2.3 2.3c-.6.6-.2 1.7.7 1.7H17m0 0a2 2 0 100 4 2 2 0 000-4zm-8 2a2 2 0 11-4 0 2 2 0 014 0z" />
          </svg>
          <span v-if="cart.itemCount" class="badge indicator-item badge-primary badge-xs" aria-hidden="true">{{ cart.itemCount }}</span>
        </div>
      </RouterLink>

      <div v-if="auth.isSignedIn" class="dropdown dropdown-end">
        <div tabindex="0" role="button" class="btn btn-ghost btn-circle avatar placeholder" aria-label="Account menu">
          <div class="w-9 rounded-full bg-primary text-primary-content">
            <span class="text-sm uppercase">{{ auth.email?.charAt(0) }}</span>
          </div>
        </div>
        <ul tabindex="0" class="menu dropdown-content z-40 mt-3 w-60 rounded-box bg-base-100 p-2 shadow">
          <li class="menu-title truncate">{{ auth.email }}</li>
          <li><RouterLink to="/account" @click="closeMenus">My account</RouterLink></li>
          <li><RouterLink to="/account/orders" @click="closeMenus">My orders</RouterLink></li>
          <li><button @click="signOut">Sign out</button></li>
        </ul>
      </div>
      <RouterLink v-else to="/login" class="btn btn-primary btn-sm">Sign in</RouterLink>
    </div>
  </header>
</template>
