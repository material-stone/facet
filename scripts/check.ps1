param([switch]$OverlayVisuals, [switch]$OverlayUi, [switch]$Launcher)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet'
Push-Location $projectRoot
try {
    powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'check-deployment.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Deployment checks failed.' }
    node scripts/check-world-overlaps.js
    if ($LASTEXITCODE -ne 0) { throw 'World voxel overlap check failed.' }
    dotnet run --project impls/Facet.Checks/Facet.Checks.csproj --no-launch-profile
    if ($LASTEXITCODE -ne 0) { throw 'Core checks failed.' }
    dotnet build impls/Facet.Cli/Facet.Cli.csproj --nologo -m:1 /nodeReuse:false
    if ($LASTEXITCODE -ne 0) { throw 'Reminder CLI build failed.' }
    dotnet build impls/Facet.Mcp/Facet.Mcp.csproj --nologo -m:1 /nodeReuse:false
    if ($LASTEXITCODE -ne 0) { throw 'Agent MCP server build failed.' }
    dotnet build impls/Facet.JournalChecks/Facet.JournalChecks.csproj --nologo -m:1 /nodeReuse:false
    if ($LASTEXITCODE -ne 0) { throw 'Journal check build failed.' }
    $journalArguments = @()
    if ($OverlayVisuals) { $journalArguments += '--overlay-visuals' }
    if ($OverlayUi) { $journalArguments += '--overlay-ui' }
    if ($Launcher) { $journalArguments += '--launcher' }
    dotnet run --project impls/Facet.JournalChecks/Facet.JournalChecks.csproj --no-build --no-launch-profile -- @journalArguments
    if ($LASTEXITCODE -ne 0) { throw 'Journal integration checks failed.' }
} finally { Pop-Location }
