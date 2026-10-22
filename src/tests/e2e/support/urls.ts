/** Where the applications under test are (override with environment variables, e.g. in CI). */
export const urls = {
  store: process.env.STORE_URL ?? 'http://localhost:5173',
  admin: process.env.ADMIN_URL ?? 'http://localhost:5174',
  api: process.env.API_URL ?? 'http://localhost:5233',
}
