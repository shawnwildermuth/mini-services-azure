// Runtime configuration. In containers this file is regenerated from environment variables at start.
//
// auth.mode = 'dev'  : local development only. Signs in through the API's development token endpoint, which the
//                      API only exposes when it runs in the Development environment.
// auth.mode = 'msal' : real Microsoft Entra ID sign-in. Fill in the values from the app registrations
//                      (see src/docs/entra-setup.md).
window.__APP_CONFIG__ = {
  apiBaseUrl: 'http://localhost:5233',
  auth: {
    mode: 'dev',
    tenantId: '',
    clientId: '', // the admin SPA's application (client) id
    apiScope: '', // e.g. api://<api-client-id>/access_as_admin
  },
}
