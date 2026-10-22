import {
  createStandardPublicClientApplication,
  InteractionRequiredAuthError,
  type AccountInfo,
  type IPublicClientApplication,
} from '@azure/msal-browser'
import { rolesFromToken, type AdminUser, type AuthProvider } from './provider'

export interface MsalSettings {
  tenantId: string
  /** The admin site's application (client) id. */
  clientId: string
  /** The API scope to request, e.g. api://<client id>/access_as_admin. */
  apiScope: string
}

/** Microsoft Entra ID sign-in with the authorization code + PKCE flow (redirect). */
export class MsalAuthProvider implements AuthProvider {
  private app?: IPublicClientApplication
  private account: AccountInfo | null = null

  constructor(private readonly settings: MsalSettings) {}

  async init() {
    this.app = await createStandardPublicClientApplication({
      auth: {
        clientId: this.settings.clientId,
        authority: `https://login.microsoftonline.com/${this.settings.tenantId}`,
        redirectUri: window.location.origin,
        postLogoutRedirectUri: `${window.location.origin}/login`,
      },
      // Tokens live only as long as the tab does.
      cache: { cacheLocation: 'sessionStorage' },
    })

    // Finishes the sign-in when Microsoft has just redirected back to us.
    const result = await this.app.handleRedirectPromise()
    this.account = result?.account ?? this.app.getActiveAccount() ?? this.app.getAllAccounts()[0] ?? null
    if (!this.account) return null

    this.app.setActiveAccount(this.account)
    return this.userFor(await this.getToken())
  }

  async signIn() {
    await this.app!.loginRedirect({ scopes: [this.settings.apiScope] })
    return null // the browser is navigating to Microsoft
  }

  async signOut() {
    await this.app!.logoutRedirect({ account: this.account ?? undefined })
  }

  async forget() {
    await this.app?.clearCache({ account: this.account ?? undefined })
    this.account = null
  }

  async getToken() {
    if (!this.app || !this.account) return null
    const request = { scopes: [this.settings.apiScope], account: this.account }

    try {
      return (await this.app.acquireTokenSilent(request)).accessToken
    } catch (error) {
      if (error instanceof InteractionRequiredAuthError) {
        await this.app.acquireTokenRedirect(request) // e.g. the session or consent expired
        return null
      }
      throw error
    }
  }

  private userFor(token: string | null): AdminUser | null {
    if (!this.account) return null
    return { name: this.account.name ?? this.account.username, roles: token ? rolesFromToken(token) : [] }
  }
}
