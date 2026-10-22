import createClient, { type Middleware } from 'openapi-fetch'
import { authProvider, reportUnauthorized } from '@/auth'
import { apiBaseUrl } from '@/config'
import type { components, paths } from './schema'

export type Schemas = components['schemas']

const authMiddleware: Middleware = {
  async onRequest({ request }) {
    const token = await authProvider.getToken()
    if (token) request.headers.set('Authorization', `Bearer ${token}`)
    return request
  },

  onResponse({ response }) {
    // Expired, revoked, or (in development) issued by a previous run of the API: sign in again.
    if (response.status === 401) reportUnauthorized()
    return response
  },
}

export const api = createClient<paths>({ baseUrl: apiBaseUrl })
api.use(authMiddleware)
