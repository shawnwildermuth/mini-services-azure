<script setup lang="ts">
import type { Tone } from '@/utils/tone'

/**
 * A status label: high-contrast neutral text with a coloured dot. Coloured text on a tinted background (daisyUI's
 * "soft" badges) does not reach WCAG AA contrast in these themes, and colour alone is a poor signal anyway.
 */
defineProps<{ tone: Tone }>()

const dot: Record<Tone, string> = {
  success: 'bg-success',
  error: 'bg-error',
  warning: 'bg-warning',
  info: 'bg-info',
  neutral: 'bg-neutral',
}
</script>

<template>
  <span class="badge badge-ghost gap-1.5 whitespace-nowrap">
    <span class="inline-block size-2 shrink-0 rounded-full" :class="dot[tone]" aria-hidden="true" />
    <slot />
  </span>
</template>