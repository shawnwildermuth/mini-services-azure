<script setup lang="ts">
defineProps<{ message: string; retry?: boolean }>()
defineEmits<{ retry: [] }>()
</script>

<template>
  <div role="alert" class="alert alert-error alert-soft">
    <span>{{ message }}</span>
    <button v-if="retry" class="btn btn-sm" @click="$emit('retry')">Try again</button>
  </div>
</template>
