<script setup lang="ts">
import { ref, watch } from 'vue'

const props = defineProps<{
  open: boolean
  title: string
  confirmLabel: string
  /** Style of the confirm button, e.g. 'btn-error' for destructive actions. */
  tone?: string
  busy?: boolean
}>()
const emit = defineEmits<{ confirm: []; cancel: [] }>()

const dialog = ref<HTMLDialogElement>()

// Drive the native <dialog> (focus trap, Esc to close) from the `open` prop.
watch(
  () => props.open,
  (open) => {
    if (open && !dialog.value?.open) dialog.value?.showModal()
    if (!open && dialog.value?.open) dialog.value.close()
  },
  { flush: 'post' },
)
</script>

<template>
  <dialog ref="dialog" class="modal" @close="emit('cancel')">
    <div class="modal-box">
      <h3 class="text-lg font-bold">{{ title }}</h3>
      <div class="py-3"><slot /></div>
      <div class="modal-action">
        <button class="btn btn-ghost" type="button" :disabled="busy" @click="emit('cancel')">Keep as is</button>
        <button class="btn" :class="tone ?? 'btn-primary'" type="button" :disabled="busy" @click="emit('confirm')">
          <span v-if="busy" class="loading loading-spinner loading-sm" />
          {{ confirmLabel }}
        </button>
      </div>
    </div>
    <form method="dialog" class="modal-backdrop"><button aria-label="Close">close</button></form>
  </dialog>
</template>
