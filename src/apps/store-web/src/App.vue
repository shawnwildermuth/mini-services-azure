<script setup lang="ts">
import AppNavbar from '@/components/AppNavbar.vue'
import AppFooter from '@/components/AppFooter.vue'
import ToastHost from '@/components/ToastHost.vue'
</script>

<template>
  <div class="flex min-h-screen flex-col bg-base-100 text-base-content">
    <AppNavbar />
    <main class="container mx-auto w-full max-w-7xl flex-1 px-4 py-6">
      <RouterView />
    </main>
    <AppFooter />
    <ToastHost />
  </div>
</template>
