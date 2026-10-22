const config = window.__APP_CONFIG__ ?? {}

/** Base URL of the API, injected at runtime through /env.js (see public/env.js). */
export const apiBaseUrl = config.apiBaseUrl ?? ''

export const authConfig = {
  mode: config.auth?.mode ?? 'msal',
  tenantId: config.auth?.tenantId ?? '',
  clientId: config.auth?.clientId ?? '',
  apiScope: config.auth?.apiScope ?? '',
}

/** The app role (assigned in Entra) that grants access to the administration API. */
export const ADMIN_ROLE = 'Admin'
