<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{ page: number; pageSize: number; total: number }>()
const emit = defineEmits<{ change: [page: number] }>()

const pageCount = computed(() => Math.max(Math.ceil(props.total / props.pageSize), 1))

// Current page plus a window around it, with the first and last pages always reachable.
const pages = computed(() => {
  const set = new Set([1, pageCount.value])
  for (let p = props.page - 1; p <= props.page + 1; p++) if (p >= 1 && p <= pageCount.value) set.add(p)
  return [...set].sort((a, b) => a - b)
})
</script>

<template>
  <nav v-if="pageCount > 1" class="join" aria-label="Pagination">
    <button class="btn join-item" :disabled="page <= 1" aria-label="Previous page" @click="emit('change', page - 1)">«</button>
    <template v-for="(p, i) in pages" :key="p">
      <button v-if="i > 0 && p - (pages[i - 1] ?? 0) > 1" class="btn btn-disabled join-item" tabindex="-1">…</button>
      <button
        class="btn join-item"
        :class="{ 'btn-active btn-primary': p === page }"
        :aria-current="p === page ? 'page' : undefined"
        @click="emit('change', p)"
      >
        {{ p }}
      </button>
    </template>
    <button class="btn join-item" :disabled="page >= pageCount" aria-label="Next page" @click="emit('change', page + 1)">»</button>
  </nav>
</template>
