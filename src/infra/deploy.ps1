param(
  [string]$ResourceGroup = 'partsstore',
  [string]$Location = 'belgiumcentral'
)

az group create `
  --name $ResourceGroup `
  --location $Location

# A deleted Key Vault keeps its name reserved while soft-deleted, which blocks re-creating it. Purge this project's
# leftovers first. Vaults with purge protection can't be purged; those are reported and skipped (use keyVaultNameSalt).
$deleted = az keyvault list-deleted --resource-type vault -o json | ConvertFrom-Json
foreach ($vault in $deleted | Where-Object { $_.name -like 'kv-partsstore-*' }) {
  if ($vault.properties.purgeProtectionEnabled) {
    Write-Warning "Deleted vault '$($vault.name)' has purge protection (purgeable $($vault.properties.scheduledPurgeDate)); skipping."
    continue
  }
  Write-Host "Purging deleted vault '$($vault.name)'..."
  az keyvault purge --name $vault.name --location $vault.properties.location
}

az deployment group create `
  --resource-group $ResourceGroup `
  --parameters main.bicepparam
