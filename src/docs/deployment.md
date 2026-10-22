# Deploying to Azure

Everything is created by `src/infra/main.bicep` and released by `.github/workflows/deploy.yml`. This page covers the
one-time setup, what each pipeline run does, and day-to-day operations.

> **Status:** the Bicep templates compile cleanly with the Bicep linter, the workflows pass `actionlint`, and the
> container images were built and exercised end to end locally (see *Testing the images locally*). The templates have
> **not yet been deployed to a real subscription**, so expect to fix small things on the first run (a region without
> Flex Consumption, a resource-provider registration, a quota). The troubleshooting list at the end covers the likely ones.

## What gets created

One resource group, one region, one environment.

```
                  Customers                          Staff
                      │                                │
            ┌─────────▼────────┐             ┌─────────▼────────┐
            │  store-web (nginx)│             │ admin-web (nginx) │      App Service for Containers
            └─────────┬────────┘             └─────────┬────────┘      (one Linux plan, three apps)
                      │         ┌──────────────────────┘
                      ▼         ▼
                ┌───────────────────┐   outbox    ┌──────────────┐  triggers  ┌───────────────────────┐
                │ API (.NET)         ├────────────►│ Service Bus   ├───────────►│ Function App           │
                └──┬─────┬─────┬────┘             │ order-placed   │            │ billing + fulfillment  │
                   │     │     │                  │ payment-captured│◄──────────┤ (Flex Consumption)     │
                   ▼     ▼     ▼                  └──────────────┘            └───────────┬───────────┘
              Azure SQL  Azure   Key Vault                                                │
              (Entra-    Managed (redis secret,                                           ▼
               only)     Redis   data-protection key)                                 Azure SQL
```

| Resource | Notes |
|---|---|
| Container Registry (Basic) | Images `store-api`, `store-web`, `admin-web`. No admin account; apps pull with managed identity. |
| App Service plan (Linux, B1) + 3 web apps | API, storefront, admin site. Health checks on `/health/live` and `/healthz`. |
| Function App (Flex Consumption, .NET 10) + storage | Scales to zero. Identity-based storage (no keys). |
| Azure SQL (S0) | **Entra-only authentication: there is no SQL password.** Auditing on; Microsoft Defender for SQL threat detection on by default (a per-server monthly charge; turn off with `sqlThreatDetection = false`). |
| Service Bus (Standard) | `order-placed`, `payment-captured` with duplicate detection and 5 deliveries before dead-lettering. Shared keys are disabled. |
| Azure Managed Redis (Balanced_B0) | Catalog cache. Connection string lives in Key Vault. |
| Key Vault | Redis connection string; RSA key that wraps the API's data-protection keys. RBAC mode, purge protection on. |
| Log Analytics + Application Insights | Telemetry from the API and the functions; 90-day retention; also receives the SQL audit log (sign-ins, permission and schema changes). |
| 3 user-assigned managed identities | `api`, `func`, `web`. All role assignments exist **before** any app starts. |

### Who can do what (least privilege)

| Identity | Permissions |
|---|---|
| `web` (storefront, admin site) | `AcrPull` on the registry |
| `api` | `AcrPull`; Key Vault Secrets User + Crypto User; Service Bus Data Sender + Receiver; SQL `db_datareader`/`db_datawriter` |
| `func` | Service Bus Data Sender + Receiver; Storage Blob/Queue/Table data roles on its own storage account; SQL `db_datareader`/`db_datawriter` |
| the pipeline | Contributor + User Access Administrator on the resource group; SQL Entra administrator (applies migrations) |

Nothing in this system uses an account key, a SAS token or a password.

## One-time setup

You need the Azure CLI (`az`), the GitHub CLI (`gh`), and rights to create app registrations and to assign roles.

### 1. Resource group

```bash
az group create --name rg-partsstore-prod --location eastus2   # choose a region that offers Flex Consumption + Azure Managed Redis
```

### 2. The pipeline's identity (GitHub → Azure with OIDC, no stored secret)

```bash
APP_ID=$(az ad app create --display-name partsstore-deploy --query appId -o tsv)
az ad sp create --id "$APP_ID"
SP_OBJECT_ID=$(az ad sp show --id "$APP_ID" --query id -o tsv)

# Trust workflows that run in this repository's "production" environment
az ad app federated-credential create --id "$APP_ID" --parameters '{
  "name": "github-production",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:<owner>/<repo>:environment:production",
  "audiences": ["api://AzureADTokenExchange"]
}'

SCOPE=$(az group show --name rg-partsstore-prod --query id -o tsv)
az role assignment create --assignee "$APP_ID" --role Contributor                 --scope "$SCOPE"
az role assignment create --assignee "$APP_ID" --role "User Access Administrator" --scope "$SCOPE"  # lets Bicep create role assignments
```

### 3. The admin site's Entra app registration

Follow [`entra-setup.md`](entra-setup.md) steps 1–6 and keep the **client id** and **tenant id**. You will add the production
redirect URI after the first deployment (step 6 below), because the site's URL isn't known until it exists.

### 4. Repository settings

In GitHub: **Settings → Environments → New environment → `production`** (add required reviewers if you want a manual
approval before each release). Then set these repository variables (they are identifiers, not secrets):

```bash
gh variable set AZURE_CLIENT_ID        --body "$APP_ID"
gh variable set AZURE_TENANT_ID        --body "$(az account show --query tenantId -o tsv)"
gh variable set AZURE_SUBSCRIPTION_ID  --body "$(az account show --query id -o tsv)"
gh variable set AZURE_RESOURCE_GROUP   --body rg-partsstore-prod
gh variable set ENVIRONMENT_NAME       --body prod
gh variable set SQL_ADMIN_OBJECT_ID    --body "$SP_OBJECT_ID"      # the pipeline is the SQL administrator...
gh variable set SQL_ADMIN_LOGIN        --body partsstore-deploy
gh variable set SQL_ADMIN_TYPE         --body Application          # ...or use a Group that contains it plus your admins
gh variable set ADMIN_CLIENT_ID        --body "<admin app client id from step 3>"
```

### 5. First deployment

Push to `main` (or run the **Deploy** workflow manually). The first run creates everything; the web apps briefly fail to
start (their `:latest` images don't exist until the *images* job finishes) and then come up when the *release* job rolls them
to the new images. Later runs are rolling updates.

### 6. Finish the admin sign-in

When the first run is done, take the admin site URL from the *Provision infrastructure* job outputs
(`https://app-partsstore-prod-admin-<suffix>.azurewebsites.net`) and add it as a **Single-page application** redirect URI on the
app registration (**Authentication → Add URI**). Assign yourself the `Admin` role (entra-setup.md step 5), and sign in.

## What a pipeline run does

| Job | Does |
|---|---|
| **Test** | Builds and tests the .NET solution, type-checks/tests/builds both Vue apps, compiles the Bicep, builds the three images. Also fails if the committed OpenAPI document or generated API clients are stale. |
| **Provision infrastructure** | `az deployment group create` of `main.bicep`. Idempotent; apps keep their current image. |
| **Build ×3** | `az acr build` (inside Azure, no Docker on the runner). Tags `:<commit sha>` and `:latest`. |
| **Migrate database** | Opens the SQL firewall to the runner for the duration, applies EF Core migrations with a self-contained bundle, creates/refreshes the database users for the API and function identities, closes the firewall. Schema changes land **before** the new code starts. |
| **Release** | Points each web app at `:<commit sha>`, deploys the Function App package, then smoke-tests `/health/ready` (database + Redis through the API's managed identity), `/healthz` on both front ends, and that the storefront's `env.js` names the API. |

### Rolling back

Re-run the **Deploy** workflow from an earlier commit (Actions → Deploy → *Run workflow* on that ref), or point an app at an old
image immediately:

```bash
az webapp config set -g rg-partsstore-prod -n <api app> --linux-fx-version "DOCKER|<registry>.azurecr.io/store-api:<old sha>"
```

Migrations are forward-only. Write them so the previous release still works against the new schema (add columns/tables first,
remove them in a later release).

## Operating it

* **Failed orders / stuck messages**: the admin site's *Failed messages* page lists dead-lettered messages (5 failed deliveries
  or unprocessable) and lets you replay or discard them. The dashboard flags orders stuck for over 10 minutes.
* **Logs and traces**: Application Insights → *Transaction search* / *Failures*. The API and both functions report to it.
* **Rotating the Redis key**: regenerate it in the portal, then re-run the Bicep deployment (it rewrites the Key Vault secret); apps
  pick up the new value on restart.
* **Scaling**: raise `appServiceSku` (e.g. `P0v3`) and, for more than one instance, nothing else: token-signing keys live in SQL
  and every instance shares them. Redis and Service Bus are shared services.
* **Cost**: the defaults (B1 plan, Basic ACR, S0 SQL, Balanced_B0 Redis, Standard Service Bus, Flex Consumption at idle) are the
  small end of each service. Redis and SQL dominate; check the Azure pricing calculator for your region before leaving it running.

## Testing the images locally

```powershell
cd src
docker compose --profile apps up -d --build      # SQL, Redis, Service Bus emulator, Azurite + the 3 images
cd apps/Store.Functions; func start              # the Function App isn't containerised
```

Storefront http://localhost:5173, admin http://localhost:5174 (development sign-in), API http://localhost:5233.
A different SQL port is available via `SQL_PORT` if 1433 is taken.

## Security notes and next steps

The full review (scanners, code review, attack tests, accepted risks) is in [security-review.md](security-review.md); how the catalog cache performs is in [performance.md](performance.md).

Already in place: Entra-only SQL, identity-based access to Service Bus/Storage/Key Vault/ACR, no shared keys, non-root containers,
HTTPS-only with TLS 1.2, strict Content-Security-Policy and security headers on both front ends, per-IP rate limiting (the
containers honour `X-Forwarded-*` so the real client IP is used), customer token keys persisted in SQL and wrapped by Key Vault.

Reasonable next hardening steps (not done here, each is a deliberate scope decision):

* Private endpoints + VNet integration for SQL, Service Bus, Key Vault and Redis, with public access off.
* Azure Front Door/WAF in front of the web apps; custom domains and managed certificates.
* Separate staging environment (a second resource group with a different `environmentName`) and deployment slots on a Standard+ plan.
* Zone redundancy / geo-replication for SQL, Service Bus and Redis if the availability target calls for it.

## Troubleshooting the first deployment

| Symptom | Likely cause |
|---|---|
| `AuthorizationFailed` creating role assignments | The pipeline identity lacks *User Access Administrator* on the resource group (step 2). |
| `MissingSubscriptionRegistration` | Register the provider once: `az provider register -n Microsoft.App` (Flex Consumption) and `-n Microsoft.Cache`. |
| Region errors for Flex Consumption or Managed Redis | Pick a region that offers both (`az functionapp list-flexconsumption-locations`), and recreate the resource group there. |
| Web apps show "Application Error" after the first run | Images not pulled yet: wait for the *Release* job, or restart the apps. |
| `/health/ready` reports the database unhealthy | The *Migrate database* job's grant step didn't run, or the identity name in SQL differs: check `SELECT name FROM sys.database_principals`. |
| Redis unhealthy, API log shows a Key Vault error | The Key Vault reference couldn't resolve: the API identity needs *Key Vault Secrets User*, and the secret must exist (it is written by the Redis module). |
| Admin sign-in loops back to login with "session ended" | API `AzureAd__ClientId`/`TenantId` don't match the registration, or the user hasn't been assigned the `Admin` role. |
