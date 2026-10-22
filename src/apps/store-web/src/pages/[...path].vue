<script setup lang="ts">
import { usePageTitle } from '@/composables/usePageTitle'

usePageTitle('Page not found')
</script>

<template>
  <div class="hero py-16">
    <div class="hero-content text-center">
      <div>
        <p class="text-6xl" aria-hidden="true">🔌</p>
        <h1 class="mt-4 text-3xl font-bold">Page not found</h1>
        <p class="py-3 opacity-70">That page is unplugged — or never existed.</p>
        <RouterLink to="/" class="btn btn-primary">Back to the store</RouterLink>
      </div>
    </div>
  </div>
</template>
