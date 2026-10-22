/** Base URL of the API, injected at runtime through /env.js (see public/env.js). */
export const apiBaseUrl = window.__APP_CONFIG__?.apiBaseUrl ?? ''

/** Matches the API's per-line cart limit. */
export const MAX_CART_QUANTITY = 20
