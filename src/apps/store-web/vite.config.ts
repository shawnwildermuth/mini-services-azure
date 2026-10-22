import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vitest/config'
import vue from '@vitejs/plugin-vue'
import VueRouter from 'vue-router/vite'
import tailwindcss from '@tailwindcss/vite'

export default defineConfig({
  plugins: [
    // File-based routing: every file under src/pages becomes a route. Must come before vue().
    VueRouter({
      // Pages inside the (protected) group need a signed-in customer (checked in src/router.ts).
      beforeWriteFiles(root) {
        for (const route of root) {
          if (route.isPassThrough && route.name === '/(protected)') {
            route.addToMeta({ requiresAuth: true })
          }
        }
      },
    }),
    vue(),
    tailwindcss(),
  ],
  resolve: {
    alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
  },
  server: { port: 5173, strictPort: true },
  test: {
    environment: 'jsdom',
    include: ['src/**/*.test.ts'],
  },
})
