$ErrorActionPreference = 'Stop'
$sourceBuild = Join-Path $PSScriptRoot 'build.ps1'
$projectRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $projectRoot ('.test-data/deployment-' + [Guid]::NewGuid().ToString('N'))
$originalManifest = '{"schemaVersion":1,"executable":"releases/working/Facet.exe"}'

function New-DeploymentCase([string]$Name) {
    $caseRoot = Join-Path $testRoot $Name
    $scripts = Join-Path $caseRoot 'scripts'
    $project = Join-Path $caseRoot 'impls/Facet'
    $deploy = Join-Path $caseRoot 'artifacts/Facet'
    foreach ($directory in @($scripts, $project, $deploy, (Join-Path $deploy 'releases/working'), (Join-Path $deploy 'releases/stale'), (Join-Path $deploy '.cli-stale'), (Join-Path $deploy '.launcher-stale'))) {
        [IO.Directory]::CreateDirectory($directory) | Out-Null
    }
    Copy-Item -LiteralPath $sourceBuild -Destination (Join-Path $scripts 'build.ps1')
    [IO.File]::WriteAllText((Join-Path $scripts 'check.ps1'), '# Synthetic checks already passed.')
    [IO.File]::WriteAllText((Join-Path $project 'Facet.csproj'), '<Project><PropertyGroup><Version>1.0.0</Version></PropertyGroup></Project>')
    [IO.File]::WriteAllText((Join-Path $deploy 'latest.json'), $originalManifest)
    [IO.File]::WriteAllText((Join-Path $deploy 'latest.previous.json'), 'PREVIOUS_MANIFEST')
    [IO.File]::WriteAllText((Join-Path $deploy 'Facet.Launcher.exe'), 'WORKING_LAUNCHER')
    [IO.File]::WriteAllText((Join-Path $deploy 'releases/working/Facet.exe'), 'WORKING_APP')
    [IO.File]::WriteAllText((Join-Path $deploy 'releases/working/untouched.txt'), 'KEEP_UNTIL_UNLOCKED')
    [IO.File]::WriteAllText((Join-Path $deploy 'releases/stale/Facet.exe'), 'STALE_APP')
    return [pscustomobject]@{ Root=$caseRoot; Build=(Join-Path $scripts 'build.ps1'); Deploy=$deploy; Manifest=(Join-Path $deploy 'latest.json') }
}

function Assert-OnlyLatest($Case) {
    $next = Get-Content -LiteralPath $Case.Manifest -Raw | ConvertFrom-Json
    $releases = @(Get-ChildItem -LiteralPath (Join-Path $Case.Deploy 'releases') -Directory)
    if ($next.schemaVersion -ne 1 -or $next.executable -notlike 'releases/1.0.0-*/Facet.exe' -or $releases.Count -ne 1 -or
        -not (Test-Path -LiteralPath (Join-Path $Case.Deploy $next.executable)) -or
        (Test-Path -LiteralPath (Join-Path $Case.Deploy 'latest.previous.json')) -or
        @(Get-ChildItem -LiteralPath $Case.Deploy -Directory | Where-Object { $_.Name -match '^\.(cli|launcher)-' }).Count) {
        throw 'Successful publish must retain only the committed release without previous manifests or staging.'
    }
}

$global:deploymentPublishCall = 0
$global:deploymentFailAt = 0
$global:deploymentIncomplete = $false
function global:dotnet {
    $global:deploymentPublishCall++
    $outputIndex = [Array]::IndexOf($args, '-o')
    if ($outputIndex -lt 0) { throw 'Expected publish output.' }
    $outputPath = [string]$args[$outputIndex + 1]
    [IO.Directory]::CreateDirectory($outputPath) | Out-Null
    [IO.File]::WriteAllText((Join-Path $outputPath 'partial-build.txt'), 'Synthetic publish output.')
    if ($args[1] -like '*Facet.Cli*') { $files = @('Facet.Cli.exe', 'Facet.Core.dll') }
    elseif ($args[1] -like '*Facet.Launcher*') { $files = @('Facet.Launcher.exe') }
    else { $files = @('Facet.exe', 'Facet.dll', 'Facet.Core.dll', 'Facet.Plugins.dll', 'Facet.deps.json', 'Facet.runtimeconfig.json') }
    foreach ($name in $files) {
        if (-not ($global:deploymentIncomplete -and $name -eq 'Facet.Core.dll')) { [IO.File]::WriteAllText((Join-Path $outputPath $name), 'NEXT_' + $name) }
    }
    $global:LASTEXITCODE = if ($global:deploymentPublishCall -eq $global:deploymentFailAt) { 1 } else { 0 }
}

try {
    foreach ($failedPublish in @(1, 2, 3, 0)) {
        $case = New-DeploymentCase ('publish-' + $failedPublish)
        $global:deploymentPublishCall = 0
        $global:deploymentFailAt = $failedPublish
        $failed = $false
        try { & $case.Build }
        catch { if ($_.Exception.Message -notlike '*publish failed*') { throw }; $failed = $true }
        if ($failedPublish -eq 0) {
            if ($failed) { throw 'Successful publish unexpectedly failed.' }
            Assert-OnlyLatest $case
            Write-Host 'PASS successful publish commits atomically and removes every older release, backup manifest and stage'
        } else {
            if (-not $failed -or [IO.File]::ReadAllText($case.Manifest) -cne $originalManifest -or
                [IO.File]::ReadAllText((Join-Path $case.Deploy 'Facet.Launcher.exe')) -cne 'WORKING_LAUNCHER' -or
                @(Get-ChildItem -LiteralPath (Join-Path $case.Deploy 'releases') -Directory).Count -ne 2 -or
                @(Get-ChildItem -LiteralPath $case.Deploy -Directory | Where-Object { $_.Name -match '^\.(cli|launcher)-' }).Count -ne 2) {
                throw 'Failed publish changed previous deployment or left new partial output.'
            }
            Write-Host ('PASS publish failure ' + $failedPublish + ' preserves old deployment and removes new partial output')
        }
    }
    $global:deploymentFailAt = 0

    $case = New-DeploymentCase 'readonly-release'
    $readonlyFile = Join-Path $case.Deploy 'releases/working/Facet.exe'
    [IO.File]::SetAttributes($readonlyFile, [IO.FileAttributes]::ReadOnly)
    & $case.Build
    Assert-OnlyLatest $case
    Write-Host 'PASS obsolete read-only output is removed without treating it as a running release'

    $case = New-DeploymentCase 'locked-release'
    $working = Join-Path $case.Deploy 'releases/working'
    $locked = [IO.File]::Open((Join-Path $working 'Facet.exe'), [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        & $case.Build
        if (-not (Test-Path -LiteralPath (Join-Path $working 'untouched.txt')) -or (Test-Path -LiteralPath (Join-Path $case.Deploy 'releases/stale'))) {
            throw 'Locked old release must remain whole while other old releases are removed.'
        }
    } finally { $locked.Dispose() }
    & $case.Build
    Assert-OnlyLatest $case
    Write-Host 'PASS locked release remains intact and is automatically removed by the next build after unlock'

    $case = New-DeploymentCase 'incomplete-release'
    $global:deploymentIncomplete = $true
    $failed = $false
    try { & $case.Build } catch { if ($_.Exception.Message -notlike 'Published release is incomplete:*') { throw }; $failed = $true }
    finally { $global:deploymentIncomplete = $false }
    if (-not $failed -or [IO.File]::ReadAllText($case.Manifest) -cne $originalManifest -or @(Get-ChildItem -LiteralPath (Join-Path $case.Deploy 'releases') -Directory).Count -ne 2) { throw 'Incomplete release replaced a working deployment.' }
    Write-Host 'PASS incomplete publish is rejected before committing or removing previous releases'

    $case = New-DeploymentCase 'commit-failure'
    $locked = [IO.File]::Open($case.Manifest, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $failed = $false
    try { & $case.Build } catch { $failed = $true }
    finally { $locked.Dispose() }
    if (-not $failed -or [IO.File]::ReadAllText($case.Manifest) -cne $originalManifest -or @(Get-ChildItem -LiteralPath (Join-Path $case.Deploy 'releases') -Directory).Count -ne 2 -or @(Get-ChildItem -LiteralPath $case.Deploy -Filter 'latest-*.tmp').Count) { throw 'Commit failure did not preserve previous selection and remove partial output.' }
    Write-Host 'PASS manifest commit failure preserves previous releases and removes uncommitted output'

    $case = New-DeploymentCase 'concurrent-build'
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes([IO.Path]::GetFullPath($case.Deploy).ToUpperInvariant()))).Replace('-', '').Substring(0, 20) }
    finally { $sha.Dispose() }
    $helperPath = Join-Path $case.Root 'hold-build-lock.ps1'
    [IO.File]::WriteAllText($helperPath, @'
param($MutexName, $Ready, $Stop)
$gate = [Threading.Mutex]::new($false, $MutexName)
try {
    if (-not $gate.WaitOne(0)) { exit 1 }
    [IO.File]::WriteAllText($Ready, 'ready')
    $timeout = [Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-Path -LiteralPath $Stop) -and $timeout.ElapsedMilliseconds -lt 10000) { Start-Sleep -Milliseconds 50 }
    $gate.ReleaseMutex()
} finally { $gate.Dispose() }
'@)
    $ready = Join-Path $case.Root 'lock-ready.txt'
    $stop = Join-Path $case.Root 'release-lock.txt'
    $holder = Start-Process powershell -WindowStyle Hidden -PassThru -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $helperPath + '"'), ('"Local\Facet-build-' + $hash + '"'), ('"' + $ready + '"'), ('"' + $stop + '"'))
    try {
        $timeout = [Diagnostics.Stopwatch]::StartNew()
        while (-not (Test-Path -LiteralPath $ready) -and -not $holder.HasExited -and $timeout.ElapsedMilliseconds -lt 5000) { Start-Sleep -Milliseconds 50 }
        if (-not (Test-Path -LiteralPath $ready)) { throw 'Build lock helper did not start.' }
        $global:deploymentPublishCall = 0
        $failed = $false
        try { & $case.Build } catch { if ($_.Exception.Message -notlike 'Another Facet deployment build*') { throw }; $failed = $true }
        if (-not $failed -or $global:deploymentPublishCall -ne 0 -or [IO.File]::ReadAllText($case.Manifest) -cne $originalManifest) { throw 'Concurrent build was allowed to alter the deployment.' }
    } finally {
        [IO.File]::WriteAllText($stop, 'stop')
        if (-not $holder.WaitForExit(5000)) { $holder.Kill(); $holder.WaitForExit() }
        $holder.Dispose()
    }
    Write-Host 'PASS concurrent build is rejected before publishing or altering the selected release'

    $case = New-DeploymentCase 'linked-release'
    $outside = Join-Path $case.Root 'outside-releases'
    [IO.Directory]::CreateDirectory($outside) | Out-Null
    [IO.File]::WriteAllText((Join-Path $outside 'keep.txt'), 'OUTSIDE_CLEANUP_BOUNDARY')
    $junction = Join-Path $case.Deploy 'releases/linked'
    New-Item -ItemType Junction -Path $junction -Target $outside | Out-Null
    try {
        & $case.Build
        if (-not (Test-Path -LiteralPath (Join-Path $outside 'keep.txt')) -or -not (Test-Path -LiteralPath $junction)) { throw 'Cleanup crossed a directory link.' }
    } finally { [IO.Directory]::Delete($junction) }
    Assert-OnlyLatest $case
    Write-Host 'PASS cleanup skips directory links and preserves files outside the releases boundary'
} finally { Remove-Item Function:\dotnet }
