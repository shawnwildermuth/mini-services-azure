<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import AdminShell from '@/components/AdminShell.vue'
import ToastHost from '@/components/ToastHost.vue'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
const route = useRoute()
const router = useRouter()

// Until the router has settled (sign-in redirect handled, session restored) show nothing rather than flash the login page.
const ready = ref(false)
router.isReady().then(() => (ready.value = true))

/** Signed in with Microsoft but without the Admin role: the API would refuse every call, so say so up front. */
const accessDenied = computed(() => auth.isSignedIn && !auth.isAdmin)
const showChrome = computed(() => auth.isSignedIn && auth.isAdmin && route.path !== '/login')
</script>

<template>
  <div v-if="!ready" class="flex min-h-screen items-center justify-center">
    <span class="loading loading-spinner loading-lg text-primary" aria-label="Loading" />
  </div>

  <template v-else>
    <main v-if="accessDenied" class="hero min-h-screen bg-base-200">
      <div class="hero-content text-center">
        <div class="max-w-md">
          <p class="text-5xl" aria-hidden="true">⛔</p>
          <h1 class="mt-3 text-2xl font-bold">Access denied</h1>
          <p class="py-3 opacity-80">
            {{ auth.user?.name }} is signed in, but has not been assigned the <strong>Admin</strong> role for the Parts Store.
            Ask an administrator to assign it, then sign in again.
          </p>
          <button class="btn btn-primary" @click="auth.signOut()">Sign out</button>
        </div>
      </div>
    </main>

    <!-- One RouterView, always in the same spot, so the page isn't remounted when the chrome appears or disappears. -->
    <AdminShell v-else :chrome="showChrome"><RouterView /></AdminShell>

    <ToastHost />
  </template>
</template>
