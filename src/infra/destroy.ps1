param(
  [string]$ResourceGroup = 'partsstore'
)

# Prompts for confirmation before deleting. Add --no-wait to return immediately instead of waiting for the delete.
az group delete `
  --name $ResourceGroup
