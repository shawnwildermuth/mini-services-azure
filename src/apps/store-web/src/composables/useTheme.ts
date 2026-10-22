import { ref } from 'vue'

type Theme = 'light' | 'dark'
const KEY = 'store.theme'

function initialTheme(): Theme {
  try {
    const saved = localStorage.getItem(KEY)
    if (saved === 'light' || saved === 'dark') return saved
  } catch {
    // ignore
  }
  return window.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
}

const theme = ref<Theme>(initialTheme())

function apply() {
  document.documentElement.setAttribute('data-theme', theme.value)
}

export function useTheme() {
  function toggle() {
    theme.value = theme.value === 'dark' ? 'light' : 'dark'
    try {
      localStorage.setItem(KEY, theme.value)
    } catch {
      // ignore
    }
    apply()
  }
  return { theme, toggle, apply }
}
