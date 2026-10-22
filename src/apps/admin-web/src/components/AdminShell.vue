<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

/**
 * The page frame (sidebar + top bar). The slot is always rendered in the same place and the chrome is only
 * shown/hidden (v-show), so signing out or visiting the login page never remounts the current page.
 */
defineProps<{ chrome: boolean }>()

const auth = useAuthStore()
const router = useRouter()
const open = ref(false)

const links = [
  { to: '/', label: 'Dashboard', icon: '📊' },
  { to: '/orders', label: 'Orders', icon: '📦' },
  { to: '/products', label: 'Products', icon: '🖥️' },
  { to: '/categories', label: 'Categories', icon: '🗂️' },
  { to: '/inventory', label: 'Inventory', icon: '🏷️' },
  { to: '/operations/failed-messages', label: 'Failed messages', icon: '⚠️' },
] as const

async function signOut() {
  await auth.signOut()
  router.push('/login')
}
</script>

<template>
  <div class="drawer" :class="{ 'lg:drawer-open': chrome }">
    <input id="admin-drawer" v-model="open" type="checkbox" class="drawer-toggle" aria-label="Toggle navigation" />

    <div class="drawer-content flex min-h-screen flex-col">
      <header v-show="chrome" class="navbar sticky top-0 z-20 border-b border-base-300 bg-base-100/90 px-4 backdrop-blur">
        <div class="navbar-start">
          <label for="admin-drawer" class="btn btn-ghost btn-square lg:hidden" aria-label="Open navigation">
            <svg xmlns="http://www.w3.org/2000/svg" class="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 6h16M4 12h16M4 18h8" />
            </svg>
          </label>
        </div>
        <div class="navbar-end gap-2">
          <span class="hidden text-sm opacity-70 sm:inline">{{ auth.user?.name }}</span>
          <button class="btn btn-ghost btn-sm" @click="signOut">Sign out</button>
        </div>
      </header>

      <main class="w-full flex-1" :class="{ 'mx-auto max-w-7xl p-4 lg:p-6': chrome }">
        <slot />
      </main>
    </div>

    <div v-show="chrome" class="drawer-side z-30">
      <!-- Click-away area for the mobile drawer; the "Close menu" button below is the accessible equivalent. -->
      <label for="admin-drawer" class="drawer-overlay" aria-hidden="true" />
      <aside class="flex min-h-full w-64 flex-col bg-base-200">
        <button type="button" class="btn btn-ghost btn-sm m-2 self-end lg:hidden" @click="open = false">Close menu</button>
        <div class="p-4 text-lg font-bold">🖥️ Parts Store <span class="badge badge-primary badge-sm align-middle">Admin</span></div>
        <ul class="menu w-full grow gap-1 px-2">
          <li v-for="l in links" :key="l.to">
            <RouterLink :to="l.to" exact-active-class="menu-active" :active-class="l.to === '/' ? '' : 'menu-active'" @click="open = false">
              <span aria-hidden="true">{{ l.icon }}</span> {{ l.label }}
            </RouterLink>
          </li>
        </ul>
        <p class="p-4 text-xs opacity-75">Signed in as {{ auth.user?.name }}</p>
      </aside>
    </div>
  </div>
</template>
