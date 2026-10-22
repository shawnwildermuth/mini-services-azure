<script setup lang="ts">
import { statusLabel, statusTone, type OrderStatus } from '@/utils/orderStatus'
import StatusPill from './StatusPill.vue'

defineProps<{ status: OrderStatus }>()
</script>

<template>
  <StatusPill :tone="statusTone(status)">{{ statusLabel(status) }}</StatusPill>
</template>