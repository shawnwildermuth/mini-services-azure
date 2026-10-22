import createClient, { type Middleware } from 'openapi-fetch'
import { apiBaseUrl } from '@/config'
import type { components, paths } from './schema'
import { getAccessToken, hasRefreshToken, refreshSession } from './session'

export type Schemas = components['schemas']

// Anonymous auth calls where a 401 means "bad credentials", not "expired token".
const NO_REFRESH_PATHS = new Set([
  '/api/auth/login',
  '/api/auth/register',
  '/api/auth/refresh',
  '/api/auth/forgotPassword',
  '/api/auth/resetPassword',
])

// Request bodies are streams, so keep a copy of each request to replay it after a token refresh.
const replayCopies = new Map<string, Request>()

const authMiddleware: Middleware = {
  onRequest({ request, id }) {
    replayCopies.set(id, request.clone())
    const token = getAccessToken()
    if (token) request.headers.set('Authorization', `Bearer ${token}`)
    return request
  },

  async onResponse({ request, response, id }) {
    const copy = replayCopies.get(id)
    replayCopies.delete(id)

    const path = new URL(request.url).pathname
    if (response.status !== 401 || !copy || NO_REFRESH_PATHS.has(path) || !hasRefreshToken()) return response
    if (!(await refreshSession())) return response

    copy.headers.set('Authorization', `Bearer ${getAccessToken()}`)
    return fetch(copy)
  },

  onError({ id }) {
    replayCopies.delete(id)
  },
}

export const api = createClient<paths>({ baseUrl: apiBaseUrl })
api.use(authMiddleware)
