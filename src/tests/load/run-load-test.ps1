<#
.SYNOPSIS
  Compares the catalog with the Redis cache ON and OFF under the same load (k6, in Docker).

.DESCRIPTION
  Needs the local infrastructure running with the database migrated and seeded:
      docker compose --profile apps up -d --build      (once, so the schema and the 50 sample products exist)
      docker compose --profile apps stop api store-web admin-web
  Then:  ./run-load-test.ps1
  The script seeds 5,000 extra products (seed-load-data.sql), starts the API image in Production mode twice
  (once per cache setting), runs k6 against each, and prints a side-by-side summary. Results land in ./results.

  Everything runs on one machine, so absolute numbers are only meaningful relative to each other.
#>
param(
  [int]$Vus = 50,
  [string]$Duration = '45s',
  [string]$SqlPassword = 'Dev!Passw0rd123',
  [string]$Network = 'partsstore_default'
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$src = Resolve-Path (Join-Path $here '../..')
$results = Join-Path $here 'results'
New-Item -ItemType Directory -Force $results | Out-Null

Write-Host '== seeding load-test products'
$sql = Get-Content (Join-Path $here 'seed-load-data.sql') -Raw
$sql | docker exec -i partsstore-sql-1 /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P $SqlPassword -d PartsStore -b | Out-Host

Write-Host '== building the API image'
docker build -q -f (Join-Path $src 'apps/Store.Api/Dockerfile') -t partsstore/api:load $src | Out-Null

function Start-Api([bool]$cacheEnabled) {
  docker rm -f load-api 2>&1 | Out-Null
  docker run -d --name load-api --network $Network `
    -e ASPNETCORE_ENVIRONMENT=Production `
    -e "ConnectionStrings__StoreDb=Server=sql;Database=PartsStore;User Id=sa;Password=$SqlPassword;TrustServerCertificate=True" `
    -e "ConnectionStrings__Redis=redis:6379" `
    -e "Catalog__Cache__Enabled=$($cacheEnabled.ToString().ToLower())" `
    -e "RateLimiting__GlobalPerMinute=100000000" `
    partsstore/api:load | Out-Null
  for ($i = 0; $i -lt 60; $i++) {
    $ready = docker run --rm --network $Network curlimages/curl -s -o /dev/null -w '%{http_code}' http://load-api:8080/health/ready
    if ($ready -eq '200') { return }
    Start-Sleep 2
  }
  throw 'the API did not become ready'
}

function Run-K6([string]$name, [string]$duration, [switch]$export) {
  $args = @('run', '--rm', '-i', '--network', $Network, '-v', "${here}:/load", 'grafana/k6', 'run', '--quiet',
    '-e', 'BASE_URL=http://load-api:8080', '-e', "VUS=$Vus", '-e', "DURATION=$duration")
  if ($export) { $args += "--summary-export=/load/results/$name.json" }
  $args += '/load/catalog.js'
  docker @args 2>&1 | Out-Host
}

foreach ($mode in @(@{ Name = 'cache-on'; Enabled = $true }, @{ Name = 'cache-off'; Enabled = $false })) {
  Write-Host "== $($mode.Name): starting API"
  Start-Api $mode.Enabled
  docker exec partsstore-redis-1 redis-cli FLUSHALL | Out-Null
  Write-Host "== $($mode.Name): warm-up (8s, not measured)"
  Run-K6 "warmup-$($mode.Name)" '8s'
  Write-Host "== $($mode.Name): measuring ($Vus VUs, $Duration)"
  Run-K6 $mode.Name $Duration -export
}
docker rm -f load-api 2>&1 | Out-Null

# ---- side-by-side summary ----
function Metric($file, $metric, $stat) { (Get-Content $file -Raw | ConvertFrom-Json).metrics.$metric.$stat }
$rows = foreach ($m in 'cache-off', 'cache-on') {
  $f = Join-Path $results "$m.json"
  [pscustomobject]@{
    Mode        = $m
    'req/s'     = [math]::Round((Metric $f 'http_reqs' 'rate'), 0)
    'median ms' = [math]::Round((Metric $f 'http_req_duration' 'med'), 1)
    'p95 ms'    = [math]::Round((Metric $f 'http_req_duration' 'p(95)'), 1)
    'p99 ms'    = [math]::Round((Metric $f 'http_req_duration' 'p(99)'), 1)
    'failed %'  = [math]::Round(100 * (Metric $f 'http_req_failed' 'value'), 3)
  }
}
Write-Host "`n== results ($Vus virtual users, $Duration each)"
$rows | Format-Table -AutoSize | Out-Host
$off = $rows | Where-Object Mode -eq 'cache-off'; $on = $rows | Where-Object Mode -eq 'cache-on'
Write-Host ("Throughput x{0:N1}, p95 latency {1:N1}x lower with the cache." -f ($on.'req/s' / $off.'req/s'), ($off.'p95 ms' / $on.'p95 ms'))
