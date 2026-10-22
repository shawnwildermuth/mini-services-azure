import { createRouter, createWebHistory } from 'vue-router'
import { routes } from 'vue-router/auto-routes'
import { useAuthStore } from '@/stores/auth'

declare module 'vue-router' {
  interface RouteMeta {
    /** Set on everything under src/pages/(protected) by vite.config.ts. */
    requiresAuth?: boolean
  }
}

export const router = createRouter({
  history: createWebHistory(),
  routes,
  scrollBehavior: (_to, _from, saved) => saved ?? { top: 0 },
})

const GUEST_ONLY = new Set(['/login', '/register'])

router.beforeEach(async (to) => {
  const auth = useAuthStore()
  await auth.init()

  if (to.meta.requiresAuth && !auth.isSignedIn) {
    return { path: '/login', query: { redirect: to.fullPath } }
  }
  if (GUEST_ONLY.has(to.path) && auth.isSignedIn) return '/'
})

/** Only follow same-site redirects after login (guards against open redirects). */
export function safeRedirect(value: unknown): string {
  return typeof value === 'string' && value.startsWith('/') && !value.startsWith('//') ? value : '/'
}

if (import.meta.hot) {
  const { handleHotUpdate } = await import('vue-router/auto-routes')
  handleHotUpdate(router)
}
