<script setup lang="ts">
import { ref, watch } from 'vue'
import { categoryIcon } from '@/utils/categories'

type Props = {
  src?: string | null
  alt: string
  categorySlug: string
  /** Tailwind size classes for the icon fallback, e.g. "text-6xl". */
  iconClass?: string
}

const props = withDefaults(defineProps<Props>(), { src: null, iconClass: 'text-6xl' })

// A broken or missing image falls back to the category icon instead of showing a broken-image glyph.
const failed = ref(false)
watch(() => props.src, () => (failed.value = false))
</script>

<template>
  <div class="flex size-full items-center justify-center bg-base-200">
    <img
      v-if="src && !failed"
      :src="src"
      :alt="alt"
      loading="lazy"
      class="size-full bg-white object-contain p-2"
      @error="failed = true"
    />
    <span v-else :class="iconClass" aria-hidden="true">{{ categoryIcon(categorySlug) }}</span>
  </div>
</template>
