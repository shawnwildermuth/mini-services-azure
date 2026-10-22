import { createRouter, createWebHistory } from 'vue-router'
import { routes } from 'vue-router/auto-routes'
import { onUnauthorized } from '@/auth'
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

router.beforeEach(async (to) => {
  const auth = useAuthStore()
  await auth.init()

  if (to.meta.requiresAuth && !auth.isSignedIn) {
    return { path: '/login', query: to.fullPath === '/' ? {} : { redirect: to.fullPath } }
  }
  // Already signed in (e.g. just returned from Microsoft): carry on to where they were headed.
  if (to.path === '/login' && auth.isSignedIn && auth.isAdmin) return safeRedirect(to.query.redirect)
})

// The API rejected our token (expired, revoked, or issued before the API restarted).
onUnauthorized(() => {
  const auth = useAuthStore()
  if (!auth.isSignedIn) return
  auth.expire()
  router.push({ path: '/login', query: { redirect: router.currentRoute.value.fullPath, expired: '1' } })
})

/** Only follow same-site redirects after login (guards against open redirects). */
export function safeRedirect(value: unknown): string {
  return typeof value === 'string' && value.startsWith('/') && !value.startsWith('//') ? value : '/'
}

if (import.meta.hot) {
  const { handleHotUpdate } = await import('vue-router/auto-routes')
  handleHotUpdate(router)
}
