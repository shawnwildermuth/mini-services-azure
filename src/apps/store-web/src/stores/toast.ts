import { ref } from 'vue'
import { defineStore } from 'pinia'

export interface Toast {
  id: number
  kind: 'success' | 'error' | 'info'
  message: string
}

export const useToastStore = defineStore('toast', () => {
  const toasts = ref<Toast[]>([])
  let nextId = 1

  function dismiss(id: number) {
    toasts.value = toasts.value.filter((t) => t.id !== id)
  }

  function push(kind: Toast['kind'], message: string, durationMs = 4000) {
    const id = nextId++
    toasts.value.push({ id, kind, message })
    setTimeout(() => dismiss(id), durationMs)
  }

  return {
    toasts,
    dismiss,
    success: (message: string) => push('success', message),
    error: (message: string) => push('error', message, 6000),
    info: (message: string) => push('info', message),
  }
})
