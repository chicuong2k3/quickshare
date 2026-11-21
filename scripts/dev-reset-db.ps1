# Resets the local QuickShare dev database and re-baselines it.
#
# Why: the migration chain on master cannot run against an empty database (duplicate
# drop/add/create operations in 20261110105148_… and 20261116100332_SyncModelAfterDevMerge), so
# `dotnet run` fails inside MigrateAsync. This rebuilds the schema from the current EF model and
# stamps every migration id as applied, which is what startup needs to see.
#
# DESTRUCTIVE: drops the `quickshare` database. Any local dev data in it is gone.
#
# Usage: powershell -NoProfile -File scripts/dev-reset-db.ps1

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$database = "quickshare"
$psql = Get-ChildItem "C:\Program Files\PostgreSQL\*\bin\psql.exe" -ErrorAction SilentlyContinue |
    Sort-Object { [int]($_.Directory.Parent.Name) } -Descending |
    Select-Object -First 1 -ExpandProperty FullName

if (-not $psql) {
    throw "psql.exe not found under C:\Program Files\PostgreSQL\*\bin"
}

Write-Host "Stopping running dotnet processes so no connection blocks the drop..."
Get-Process dotnet -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

Write-Host "Dropping and recreating database '$database'..."
& $psql -h localhost -U postgres -q -c "DROP DATABASE IF EXISTS $database"
if ($LASTEXITCODE -ne 0) {
    throw "DROP DATABASE failed. Close any client still connected to $database."
}
& $psql -h localhost -U postgres -q -c "CREATE DATABASE $database"
if ($LASTEXITCODE -ne 0) {
    throw "CREATE DATABASE failed."
}

Write-Host "Building schema from the EF model and stamping migration history..."
& dotnet run --project (Join-Path $repoRoot "tools/db-baseline")
if ($LASTEXITCODE -ne 0) {
    throw "Baseline tool failed."
}

Write-Host ""
Write-Host "Database ready. Start the app with:"
Write-Host "  dotnet run --project src/QuickShare"
