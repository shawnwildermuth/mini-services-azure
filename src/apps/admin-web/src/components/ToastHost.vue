<script setup lang="ts">
import { useToastStore } from '@/stores/toast'

const toast = useToastStore()
const kinds = { success: 'alert-success', error: 'alert-error', info: 'alert-info' } as const
</script>

<template>
  <div class="toast toast-end toast-bottom z-50" aria-live="polite">
    <div
      v-for="t in toast.toasts"
      :key="t.id"
      class="alert cursor-pointer shadow-lg"
      :class="kinds[t.kind]"
      role="status"
      @click="toast.dismiss(t.id)"
    >
      <span>{{ t.message }}</span>
    </div>
  </div>
</template>
