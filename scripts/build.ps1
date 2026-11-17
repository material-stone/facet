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
$releasePath = $launcherStage = $pendingPath = $null
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
    # The clock, weather and system monitor plugins ship the same way, to <data>/plugins/clock, weather and sysmon.
    foreach ($plugin in @(@('Facet.ClockPlugin', 'clock'), @('Facet.WeatherPlugin', 'weather'), @('Facet.SystemMonitorPlugin', 'sysmon'))) {
        dotnet publish "impls/$($plugin[0])/$($plugin[0]).csproj" -c Release -o (Join-Path $deployRoot "plugins/$($plugin[1])") --nologo -m:1 /nodeReuse:false
        if ($LASTEXITCODE -ne 0) { throw "$($plugin[0]) publish failed. The previous release remains selected." }
    }
    # The app runs plugins from <data>/plugins, not from this deployment, so a rebuild alone would not reach it.
    # Overwrite each first-party plugin the user already installed so the build is testable without a release.
    # plugin.json is kept: the installed version stays the registry's, so a later release still offers 업데이트.
    $dataPlugins = Join-Path $env:LOCALAPPDATA 'MaterialStone/Facet/plugins'
    foreach ($built in Get-ChildItem -LiteralPath (Join-Path $deployRoot 'plugins') -Directory) {
        $installed = Join-Path $dataPlugins $built.Name
        if (-not (Test-Path -LiteralPath (Join-Path $installed 'plugin.json'))) { continue }
        foreach ($file in Get-ChildItem -LiteralPath $built.FullName -File | Where-Object { $_.Name -ne 'plugin.json' }) {
            try { Copy-Item -LiteralPath $file.FullName -Destination $installed -Force -ErrorAction Stop }
            catch { Write-Warning ('Could not refresh installed plugin file; close Facet and rebuild: ' + (Join-Path $installed $file.Name)) }
        }
        Write-Host ('Refreshed installed plugin: ' + $built.Name)
    }
    # Skins work the same way: registry/skins/<folder> overwrites the installed skin with the same id in <data>/skins,
    # keeping the installed version in skin.json so a released update is still offered.
    $dataSkins = Join-Path $env:LOCALAPPDATA 'MaterialStone/Facet/skins'
    $installedSkins = @{}
    if (Test-Path -LiteralPath $dataSkins) {
        foreach ($folder in Get-ChildItem -LiteralPath $dataSkins -Directory) {
            $manifest = Join-Path $folder.FullName 'skin.json'
            if (-not (Test-Path -LiteralPath $manifest)) { continue }
            try { $installedSkins[[string](Get-Content -LiteralPath $manifest -Raw -Encoding UTF8 | ConvertFrom-Json).id] = $folder.FullName } catch { }
        }
    }
    $skinSources = Join-Path $projectRoot 'registry/skins'
    foreach ($source in @(if (Test-Path -LiteralPath $skinSources) { Get-ChildItem -LiteralPath $skinSources -Directory })) {
        $sourceManifest = Join-Path $source.FullName 'skin.json'
        if (-not (Test-Path -LiteralPath $sourceManifest)) { continue }
        $text = [IO.File]::ReadAllText($sourceManifest, [Text.Encoding]::UTF8)
        $id = [string]($text | ConvertFrom-Json).id
        $installed = $installedSkins[$id]
        if (-not $installed) { continue }
        try {
            $version = [string](Get-Content -LiteralPath (Join-Path $installed 'skin.json') -Raw -Encoding UTF8 | ConvertFrom-Json).version
            Copy-Item -Path (Join-Path $source.FullName '*') -Destination $installed -Recurse -Force -Exclude 'skin.json' -ErrorAction Stop
            $text = [regex]::new('"version"\s*:\s*"[^"]*"').Replace($text, '"version": "' + $version + '"', 1)
            [IO.File]::WriteAllText((Join-Path $installed 'skin.json'), $text, [Text.UTF8Encoding]::new($false))
            Write-Host ('Refreshed installed skin: ' + $id)
        } catch { Write-Warning ('Could not refresh installed skin; close Facet and rebuild: ' + $id) }
    }
    foreach ($file in @('Facet.exe', 'Facet.dll', 'Facet.Core.dll', 'Facet.Plugins.dll', 'Facet.deps.json', 'Facet.runtimeconfig.json')) {
        if (-not (Test-Path -LiteralPath (Join-Path $releasePath $file) -PathType Leaf)) { throw ('Published release is incomplete: ' + $file) }
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
    # .cli- and .mcp- stages are leftovers from releases that still shipped the CLI and MCP server (ADR 0105).
    foreach ($stage in Get-ChildItem -LiteralPath $deployRoot -Directory -Force | Where-Object { $_.Name -match '^\.(cli|mcp|launcher)-' }) {
        Remove-DeploymentDirectory $stage.FullName $deployRoot
    }
    Write-Host ('Ready: ' + (Join-Path $deployRoot 'Facet.Launcher.exe'))
    Write-Host ('Latest: ' + $releasePath)
} finally {
    try {
        if ($lockAcquired) {
            if (-not $committed -and $releasePath) { Remove-DeploymentDirectory $releasePath $releasesRoot }
            foreach ($stage in @($launcherStage)) { if ($stage) { Remove-DeploymentDirectory $stage $deployRoot } }
            if ($pendingPath -and (Test-Path -LiteralPath $pendingPath)) { Remove-Item -LiteralPath $pendingPath -Force }
        }
    } finally {
        if ($lockAcquired) { $buildLock.ReleaseMutex() }
        $buildLock.Dispose()
        Pop-Location
    }
}

