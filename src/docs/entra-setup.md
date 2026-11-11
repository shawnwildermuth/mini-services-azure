# Setting up Microsoft Entra ID for the admin site

The admin site signs in with Microsoft Entra ID (MSAL in the browser) and calls the API with an access token. The
API accepts the token only if it was issued by your tenant for this app **and** carries the `Admin` app role.

This uses **one app registration** that is both the admin site (a single-page app) and the API's audience. It is the
simplest setup. (You can split it into two registrations later; the API accepts either audience form.)

## 1. Register the app

Entra admin center → **Identity → Applications → App registrations → New registration**

| Field | Value |
|---|---|
| Name | `Parts Store Admin` |
| Supported account types | *Accounts in this organizational directory only* (single tenant) |
| Redirect URI | Platform **Single-page application (SPA)**, `http://localhost:5174` |

After creating it, copy from the **Overview** page:

- **Application (client) ID** → `dd14b197-b7fe-4060-af11-bd73b6e63427`
- **Directory (tenant) ID** → `c260f41f-683e-425d-b5f5-256314c31dac`

When you deploy, add the production admin site URL as a second SPA redirect URI (**Authentication → Add URI**).
Redirect URIs must match exactly, including scheme and port, with no trailing slash.

## 2. Expose an API scope

**Expose an API**

1. Next to *Application ID URI*, choose **Add** and accept the default `api://<client-id>`.
2. **Add a scope**:
   - Scope name: `access_as_admin`
   - Who can consent: **Admins only**
   - Display names/descriptions: e.g. "Administer the Parts Store"
   - State: **Enabled**

The admin site will request the scope `api://<client-id>/access_as_admin`.

## 3. Create the `Admin` app role

**App roles → Create app role**

| Field | Value |
|---|---|
| Display name | `Admin` |
| Allowed member types | **Users/Groups** |
| Value | `Admin` (exactly; the API checks this string) |
| Description | Can manage the Parts Store |
| Do you want to enable this app role? | checked |

## 4. Use v2 access tokens (recommended)

**Manifest** (Microsoft Graph app manifest) → set `"requestedAccessTokenVersion": 2` under `api`, then **Save**.
The API accepts v1 and v2 tokens, but v2 is the current format.

## 5. Assign yourself the role

**Identity → Applications → Enterprise applications →** `Parts Store Admin` **→ Users and groups → Add user/group**,
pick yourself (or a security group of administrators) and the **Admin** role.

Optionally, under **Properties**, turn on **Assignment required** so only assigned users can sign in at all.

## 6. Grant consent

**API permissions → Add a permission → My APIs →** `Parts Store Admin` **→ Delegated →** `access_as_admin`, then
**Grant admin consent for \<your tenant\>**.

## 7. Configure the API

Set these for the API (environment variables / Azure App Service settings / user secrets):

```
AzureAd__TenantId = <tenant-id>
AzureAd__ClientId = <client-id>
```

## 8. Configure the admin site

In `src/apps/admin-web/public/env.js` (and for containers, the environment variables that generate it):

```js
window.__APP_CONFIG__ = {
  apiBaseUrl: 'http://localhost:5233',
  auth: {
    mode: 'msal',
    tenantId: '<tenant-id>',
    clientId: '<client-id>',
    apiScope: 'api://<client-id>/access_as_admin',
  },
}
```

Then run the API and the site (`npm run dev` in `src/apps/admin-web`, http://localhost:5174) and choose
**Sign in with Microsoft**.

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| `AADSTS50011` redirect URI mismatch | The SPA redirect URI in step 1 doesn't exactly match the site's URL. |
| `AADSTS65001` consent required | Step 6 (admin consent) hasn't been done. |
| "Access denied" screen after signing in | You aren't assigned the `Admin` role (step 5). Sign out and in again after assigning it so a new token is issued. |
| Sent back to the sign-in page with "session ended" | The API returned 401: check `AzureAd__TenantId`/`ClientId` match the registration and the token's audience. |
| Sign-in page says Microsoft sign-in "is not configured" | `tenantId`, `clientId` or `apiScope` is empty in `env.js`. |

## Local development without Entra

With `auth.mode: 'dev'` (the default in `public/env.js`) the site signs in through `POST /api/dev/admin-token`. The API
maps that endpoint **only** when it runs in the `Development` environment with `Admin:DevTokens:Enabled` set (as in
`appsettings.Development.json`); in any other environment it does not exist and its tokens are rejected. Switch the site
to `mode: 'msal'` once the registration above is done.
