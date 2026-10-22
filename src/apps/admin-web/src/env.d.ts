/// <reference types="vite/client" />

interface AppConfig {
  apiBaseUrl?: string
  auth?: {
    mode?: 'dev' | 'msal'
    tenantId?: string
    clientId?: string
    apiScope?: string
  }
}

interface Window {
  __APP_CONFIG__?: AppConfig
}
