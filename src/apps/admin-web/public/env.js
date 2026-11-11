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
    tenantId: 'c260f41f-683e-425d-b5f5-256314c31dac',
    clientId: 'dd14b197-b7fe-4060-af11-bd73b6e63427', // the admin SPA's application (client) id
    apiScope: 'api://dd14b197-b7fe-4060-af11-bd73b6e63427/access_as_admin', // e.g. api://<api-client-id>/access_as_admin
  },
}
