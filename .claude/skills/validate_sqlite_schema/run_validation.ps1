# Checking script behind the validate_sqlite_schema skill.
# Scratch pass: applies SchemaDefinitions.sql to a throwaway db and proves the
# full C# code path (10k-player round-trip, FK integrity, day-advance benchmark).
# Live pass: read-only structural audit of the actual save file, if present.
param(
    [string]$LiveDb,
    [int]$Players = 10000
)

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
$project = Join-Path $repoRoot "Tools\SchemaValidator"

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -eq $dotnet) { $dotnet = "C:\Program Files\dotnet\dotnet.exe" } else { $dotnet = $dotnet.Source }

Write-Host "=== Scratch validation (schema + C# layer) ===" -ForegroundColor Cyan
& $dotnet run --project $project -c Release -- --players $Players
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not $LiveDb) { $LiveDb = Join-Path $repoRoot "dirt_and_diamonds.db" }
if (Test-Path $LiveDb) {
    Write-Host "=== Live audit: $LiveDb ===" -ForegroundColor Cyan
    & $dotnet run --project $project -c Release -- --live $LiveDb
    exit $LASTEXITCODE
}

Write-Host "No live database at $LiveDb - skipped live audit." -ForegroundColor Yellow
exit 0
