param(
  [string]$ResourceGroup = 'partsstore',
  [string]$Tag = 'latest',
  # Build only these images (default: all three), e.g.  -Image store-api  or  -Image store-web,admin-web
  [ValidateSet('store-api', 'store-web', 'admin-web')]
  [string[]]$Image = @('store-api', 'store-web', 'admin-web'),
  # Restart the web apps afterwards so they pull the freshly pushed images.
  [switch]$Restart
)

$ErrorActionPreference = 'Stop'
$src = Split-Path -Parent $PSScriptRoot

$registry = az acr list --resource-group $ResourceGroup --query '[0].name' -o tsv
if (-not $registry) { throw "No container registry found in resource group '$ResourceGroup'. Run deploy.ps1 first." }
$loginServer = az acr show --name $registry --query loginServer -o tsv

# Built with the local Docker daemon and pushed to the registry.
az acr login --name $registry
if ($LASTEXITCODE -ne 0) { throw 'Registry login failed.' }

$images = @(
  @{ Image = 'store-api'; Context = $src;                  Dockerfile = "$src/apps/Store.Api/Dockerfile" }
  @{ Image = 'store-web'; Context = "$src/apps/store-web"; Dockerfile = "$src/apps/store-web/Dockerfile" }
  @{ Image = 'admin-web'; Context = "$src/apps/admin-web"; Dockerfile = "$src/apps/admin-web/Dockerfile" }
)

foreach ($i in $images | Where-Object { $Image -contains $_.Image }) {
  $name = "$loginServer/$($i.Image):$Tag"

  Write-Host "Building $name..."
  docker build --file $i.Dockerfile --tag $name $i.Context
  if ($LASTEXITCODE -ne 0) { throw "Build of $($i.Image) failed." }

  Write-Host "Pushing $name..."
  docker push $name
  if ($LASTEXITCODE -ne 0) { throw "Push of $($i.Image) failed." }
}

if ($Restart) {
  # Each image runs in one web app, whose name contains the image's role (api, store, admin).
  $roles = @{ 'store-api' = '-api-'; 'store-web' = '-store-'; 'admin-web' = '-admin-' }
  $apps = az webapp list --resource-group $ResourceGroup --query '[].name' -o tsv
  foreach ($app in $apps | Where-Object { $name = $_; $Image | Where-Object { $name -like "*$($roles[$_])*" } }) {
    Write-Host "Restarting $app..."
    az webapp restart --resource-group $ResourceGroup --name $app
  }
}
