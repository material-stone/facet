param([switch]$OverlayVisuals, [switch]$OverlayUi, [switch]$Launcher)

$ErrorActionPreference = 'Stop'

function Remove-DeploymentDirectory([string]$Path, [string]$Parent) {
    $target = [IO.Path]::GetFullPath($Path).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $boundary = [IO.Path]::GetFullPath($Parent).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if (-not ([IO.Path]::GetDirectoryName($target)).Equals($boundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'Cleanup target must be a direct child of its deployment directory.' }
    if (-not (Test-Path -LiteralPath $target)) { return }
    try {
        $item = Get-Item -LiteralPath $target -Force
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { Write-Warning ('Skipped linked deployment directory: ' + $target); return }
        $contents = @(Get-ChildItem -LiteralPath $target -Recurse -Force -ErrorAction Stop)
    } catch { Write-Warning ('Could not inspect deployment directory; cleanup will retry on the next build: ' + $target); return }
    if (@($contents | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) {
        Write-Warning ('Skipped linked deployment directory: ' + $target)
        return
    }
    $targetPrefix = $target + [IO.Path]::DirectorySeparatorChar
    foreach ($process in @(Get-Process -Name Facet -ErrorAction SilentlyContinue)) {
        try { $processPath = $process.Path } catch { $processPath = $null }
        if ($processPath -and $processPath.StartsWith($targetPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            Write-Warning ('Previous release is running; cleanup will retry on the next build: ' + $target)
            return
        }
    }
    $handles = [Collections.Generic.List[IO.FileStream]]::new()
    try {
        # Check every file before deleting any file from a potentially running release.
        foreach ($file in $contents | Where-Object { -not $_.PSIsContainer }) {
            $access = if ($file.Attributes -band [IO.FileAttributes]::ReadOnly) { [IO.FileAccess]::Read } else { [IO.FileAccess]::ReadWrite }
            $handles.Add([IO.File]::Open($file.FullName, [IO.FileMode]::Open, $access, [IO.FileShare]::None))
        }
    } catch {
        Write-Warning ('Deployment directory is locked; cleanup will retry on the next build: ' + $target)
        return
    } finally {
        foreach ($handle in $handles) { $handle.Dispose() }
    }
    try { Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction Stop }
    catch { Write-Warning ('Could not remove deployment directory; cleanup will retry on the next build: ' + $target + ' (' + $_.Exception.Message + ')') }
}

$projectRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$deployRoot = Join-Path $projectRoot 'artifacts/Facet'
$releasesRoot = Join-Path $deployRoot 'releases'
$sha = [Security.Cryptography.SHA256]::Create()
try { $lockHash = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes([IO.Path]::GetFullPath($deployRoot).ToUpperInvariant()))).Replace('-', '').Substring(0, 20) }
finally { $sha.Dispose() }
$buildLock = [Threading.Mutex]::new($false, ('Local\Facet-build-' + $lockHash))
$lockAcquired = $false
$committed = $false
$releasePath = $cliStage = $mcpStage = $launcherStage = $pendingPath = $null
Push-Location $projectRoot
try {
    try { $lockAcquired = $buildLock.WaitOne(0) } catch [Threading.AbandonedMutexException] { $lockAcquired = $true }
    if (-not $lockAcquired) { throw 'Another Facet deployment build is already running for this directory.' }
    foreach ($directory in @((Join-Path $projectRoot 'artifacts'), $deployRoot, $releasesRoot)) {
        if ((Test-Path -LiteralPath $directory) -and ((Get-Item -LiteralPath $directory -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Deployment directories cannot be links.' }
    }
    & (Join-Path $PSScriptRoot 'check.ps1') @PSBoundParameters
    [xml]$appProject = Get-Content (Join-Path $projectRoot 'impls/Facet/Facet.csproj')
    $appVersion = [string]$appProject.Project.PropertyGroup.Version
    if ($appVersion -notmatch '^\d+\.\d+\.\d+(?:[.-][A-Za-z0-9.-]+)?$') { throw 'Invalid application version.' }
    $buildId = [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $releaseName = $appVersion + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $buildId
    $releasePath = Join-Path $deployRoot ('releases/' + $releaseName)
    dotnet publish impls/Facet/Facet.csproj -c Release --self-contained false -o $releasePath --nologo -m:1 /nodeReuse:false
    if ($LASTEXITCODE -ne 0) { throw 'App publish failed. The previous release remains selected.' }
    $cliStage = Join-Path $deployRoot ('.cli-' + $buildId)
    dotnet publish impls/Facet.Cli/Facet.Cli.csproj -c Release --self-contained false -o $cliStage --nologo -m:1 /nodeReuse:false
    if ($LASTEXITCODE -ne 0) { throw 'CLI publish failed. The previous release remains selected.' }
    $mcpStage = Join-Path $deployRoot ('.mcp-' + $buildId)
    dotnet publish impls/Facet.Mcp/Facet.Mcp.csproj -c Release --self-contained false -o $mcpStage --nologo -m:1 /nodeReuse:false
    if ($LASTEXITCODE -ne 0) { throw 'MCP server publish failed. The previous release remains selected.' }
    $launcherStage = Join-Path $deployRoot ('.launcher-' + $buildId)
    dotnet publish impls/Facet.Launcher/Facet.Launcher.csproj -c Release --self-contained false -o $launcherStage --nologo -m:1 /nodeReuse:false
    if ($LASTEXITCODE -ne 0) { throw 'Launcher publish failed. The previous release remains selected.' }
    # The condition plugin ships the same way, to <data>/plugins/condition (ADR 0081).
    dotnet publish impls/Facet.ConditionPlugin/Facet.ConditionPlugin.csproj -c Release -o (Join-Path $deployRoot 'plugins/condition') --nologo -m:1 /nodeReuse:false
    if ($LASTEXITCODE -ne 0) { throw 'Condition plugin publish failed. The previous release remains selected.' }
    # The schedule plugin ships the same way, to <data>/plugins/schedule (ADR 0101).
    dotnet publish impls/Facet.SchedulePlugin/Facet.SchedulePlugin.csproj -c Release -o (Join-Path $deployRoot 'plugins/schedule') --nologo -m:1 /nodeReuse:false
    if ($LASTEXITCODE -ne 0) { throw 'Schedule plugin publish failed. The previous release remains selected.' }
    # The sleep plugin ships the same way, to <data>/plugins/sleep (ADR 0083).
    dotnet publish impls/Facet.SleepPlugin/Facet.SleepPlugin.csproj -c Release -o (Join-Path $deployRoot 'plugins/sleep') --nologo -m:1 /nodeReuse:false
    if ($LASTEXITCODE -ne 0) { throw 'Sleep plugin publish failed. The previous release remains selected.' }
    # The clock and weather plugins ship the same way, to <data>/plugins/clock and weather.
    foreach ($plugin in @(@('Facet.ClockPlugin', 'clock'), @('Facet.WeatherPlugin', 'weather'))) {
        dotnet publish "impls/$($plugin[0])/$($plugin[0]).csproj" -c Release -o (Join-Path $deployRoot "plugins/$($plugin[1])") --nologo -m:1 /nodeReuse:false
        if ($LASTEXITCODE -ne 0) { throw "$($plugin[0]) publish failed. The previous release remains selected." }
    }
    foreach ($file in @('Facet.exe', 'Facet.dll', 'Facet.Core.dll', 'Facet.Plugins.dll', 'Facet.deps.json', 'Facet.runtimeconfig.json')) {
        if (-not (Test-Path -LiteralPath (Join-Path $releasePath $file) -PathType Leaf)) { throw ('Published release is incomplete: ' + $file) }
    }
    foreach ($file in Get-ChildItem -LiteralPath $cliStage -File | Where-Object { $_.Name -like 'Facet.Cli*' -or $_.Name -like 'Facet.Core*' }) {
        $destination = Join-Path $deployRoot $file.Name
        # Facet.Core.dll is shared with a running Facet.Mcp; an identical locked copy needs no replacement.
        try { Copy-Item -LiteralPath $file.FullName -Destination $destination -Force -ErrorAction Stop }
        catch { Write-Warning ('Shared file is in use; restart the agent to load the new version: ' + $destination) }
    }
    # Facet.Mcp stays at a stable path so agent MCP registrations survive new releases (ADR 0069).
    foreach ($file in Get-ChildItem -LiteralPath $mcpStage -File | Where-Object { $_.Name -like 'Facet.Mcp*' -or $_.Name -like 'Facet.Core*' }) {
        $destination = Join-Path $deployRoot $file.Name
        try { Copy-Item -LiteralPath $file.FullName -Destination $destination -Force }
        catch { Write-Warning ('MCP server is in use; restart the agent to load the new version: ' + $destination) }
    }
    foreach ($file in Get-ChildItem -LiteralPath $launcherStage -File) {
        $destination = Join-Path $deployRoot $file.Name
        if ((Test-Path -LiteralPath $destination) -and ((Get-FileHash -LiteralPath $destination).Hash -eq (Get-FileHash -LiteralPath $file.FullName).Hash)) { continue }
        Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
    }
    $manifestPath = Join-Path $deployRoot 'latest.json'
    $pendingPath = Join-Path $deployRoot ('latest-' + $buildId + '.tmp')
    $manifest = @{ schemaVersion = 1; executable = 'releases/' + $releaseName + '/Facet.exe' } | ConvertTo-Json
    [IO.File]::WriteAllText($pendingPath, $manifest, [Text.UTF8Encoding]::new($false))
    if (Test-Path -LiteralPath $manifestPath) { [IO.File]::Replace($pendingPath, $manifestPath, [NullString]::Value) }
    else { [IO.File]::Move($pendingPath, $manifestPath) }
    $committed = $true
    $previousManifest = Join-Path $deployRoot 'latest.previous.json'
    if (Test-Path -LiteralPath $previousManifest) {
        try { Remove-Item -LiteralPath $previousManifest -Force } catch { Write-Warning ('Could not remove obsolete manifest: ' + $previousManifest) }
    }
    foreach ($oldRelease in Get-ChildItem -LiteralPath $releasesRoot -Directory -Force) {
        if (-not $oldRelease.FullName.Equals([IO.Path]::GetFullPath($releasePath), [StringComparison]::OrdinalIgnoreCase)) {
            Remove-DeploymentDirectory $oldRelease.FullName $releasesRoot
        }
    }
    foreach ($stage in Get-ChildItem -LiteralPath $deployRoot -Directory -Force | Where-Object { $_.Name -match '^\.(cli|mcp|launcher)-' }) {
        Remove-DeploymentDirectory $stage.FullName $deployRoot
    }
    Write-Host ('Ready: ' + (Join-Path $deployRoot 'Facet.Launcher.exe'))
    Write-Host ('Latest: ' + $releasePath)
} finally {
    try {
        if ($lockAcquired) {
            if (-not $committed -and $releasePath) { Remove-DeploymentDirectory $releasePath $releasesRoot }
            foreach ($stage in @($cliStage, $mcpStage, $launcherStage)) { if ($stage) { Remove-DeploymentDirectory $stage $deployRoot } }
            if ($pendingPath -and (Test-Path -LiteralPath $pendingPath)) { Remove-Item -LiteralPath $pendingPath -Force }
        }
    } finally {
        if ($lockAcquired) { $buildLock.ReleaseMutex() }
        $buildLock.Dispose()
        Pop-Location
    }
}

