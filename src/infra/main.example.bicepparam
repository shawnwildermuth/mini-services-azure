// Copy to main.bicepparam (git-ignored) and fill in. None of these values are secrets.
using './main.bicep'

param environmentName = 'prod'

// Who administers the SQL server (and applies migrations): an Entra group, user or the pipeline's service principal.
// Find an object id with:  az ad group show -g "<name>" --query id -o tsv
param sqlAdminObjectId = '00000000-0000-0000-0000-000000000000'
param sqlAdminLogin = 'partsstore-sql-admins'
param sqlAdminType = 'Group'

// The admin site's Entra app registration (docs/entra-setup.md, step 1).
param adminClientId = '00000000-0000-0000-0000-000000000000'

// Optional sizing (defaults shown)
// param appServiceSku = 'B1'
// param sqlSkuName = 'S0'
// param redisSku = 'Balanced_B0'
