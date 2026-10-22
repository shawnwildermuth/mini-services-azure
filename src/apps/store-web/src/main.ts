import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import { router } from './router'
import { useTheme } from './composables/useTheme'
import './style.css'

useTheme().apply()

createApp(App).use(createPinia()).use(router).mount('#app')
