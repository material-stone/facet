# Builds a portable zip: unzip anywhere and run Facet.Launcher.exe, with no .NET install and no installer (ADR 0087).
# Publishes from a temporary copy so the app-relative apphost never leaks into the obj folders build.ps1 uses.
param([switch]$SkipChecks)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

$outputRoot = Join-Path $projectRoot 'artifacts/portable'
$buildId = [Guid]::NewGuid().ToString('N').Substring(0, 8)
$work = Join-Path $outputRoot ('.work-' + $buildId)

function Publish([string]$Project, [string]$Output, [string]$Runtime, [string[]]$Extra = @()) {
    $arguments = @('publish', "impls/$Project/$Project.csproj", '-c', 'Release', '-o', $Output, '--nologo', '-m:1', '/nodeReuse:false', '-v', 'q') + $Extra
    if ($Runtime) { $arguments += @('--self-contained', 'false', '/p:AppHostDotNetSearch=AppRelative', "/p:AppHostRelativeDotNet=$Runtime") }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "$Project publish failed." }
}

Push-Location $projectRoot
try {
    if ($SkipChecks) { & (Join-Path $PSScriptRoot 'setup-tools.ps1') }
    else { & (Join-Path $PSScriptRoot 'check.ps1') }

    [xml]$appProject = Get-Content 'impls/Facet/Facet.csproj'
    $version = [string]$appProject.Project.PropertyGroup.Version
    if ($version -notmatch '^\d+\.\d+\.\d+') { throw 'Invalid application version.' }

    # The apps need one shared runtime; take it from the machine's dotnet install in its normal layout.
    $dotnetRoot = Split-Path -Parent (Get-Command dotnet).Source
    $desktop = @(Get-ChildItem (Join-Path $dotnetRoot 'shared/Microsoft.WindowsDesktop.App') -Directory | Where-Object { $_.Name -match '^10\.\d+\.\d+$' } | Sort-Object { [version]$_.Name } -Descending)
    if (-not $desktop.Count) { throw 'Microsoft.WindowsDesktop.App 10 runtime is not installed.' }
    $runtimeVersion = $desktop[0].Name

    $name = "Facet-$version-win-x64"
    $packageRoot = Join-Path $work $name
    $source = Join-Path $work 'source'
    New-Item -ItemType Directory -Force -Path $packageRoot, $source | Out-Null

    Copy-Item 'global.json', 'NuGet.Config' -Destination $source
    robocopy 'impls' (Join-Path $source 'impls') /E /XD bin obj /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw 'Source copy failed.' }
    $global:LASTEXITCODE = 0
    Push-Location $source
    try {
        $release = Join-Path $packageRoot 'releases/portable'
        Publish 'Facet' $release '../../runtime'
        foreach ($project in 'Facet.Launcher', 'Facet.Mcp', 'Facet.Cli') { Publish $project $packageRoot 'runtime' }
        # Plugins are optional: a checkout without plugin projects builds the plain app (ADR 0098).
        $bundled = Join-Path $packageRoot 'bundled-plugins'
        foreach ($plugin in @(@('Facet.MusicPlugin', 'music'), @('Facet.ConditionPlugin', 'condition'), @('Facet.SleepPlugin', 'sleep'), @('Facet.SchedulePlugin', 'schedule'), @('Facet.ClockPlugin', 'clock'), @('Facet.WeatherPlugin', 'weather'))) {
            if (Test-Path "impls/$($plugin[0])") { Publish $plugin[0] (Join-Path $bundled $plugin[1]) '' }
        }
        if (Test-Path 'impls/Facet.ChatPlugin') { Publish 'Facet.ChatPlugin' (Join-Path $bundled 'chat') '' @('-r', 'win-x64', '--self-contained', 'false') }
    } finally { Pop-Location }

    foreach ($part in @("host/fxr/$runtimeVersion", "shared/Microsoft.NETCore.App/$runtimeVersion", "shared/Microsoft.WindowsDesktop.App/$runtimeVersion")) {
        $target = Join-Path $packageRoot "runtime/$part"
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        Copy-Item -LiteralPath (Join-Path $dotnetRoot $part) -Destination $target -Recurse
    }
    foreach ($file in 'LICENSE.txt', 'ThirdPartyNotices.txt') { Copy-Item -LiteralPath (Join-Path $dotnetRoot $file) -Destination (Join-Path $packageRoot 'runtime') }
    Copy-Item 'LICENSE' -Destination (Join-Path $packageRoot 'LICENSE.txt')

    # Debug files, and the WinRT projection PluginHost always takes from the app.
    Get-ChildItem $packageRoot -Recurse -File -Include '*.pdb', 'DirectML.Debug.*' | Remove-Item -Force
    if (Test-Path -LiteralPath $bundled) {
        Get-ChildItem $bundled -Recurse -File -Include 'Microsoft.Windows.SDK.NET.dll', 'WinRT.Runtime.dll' | Remove-Item -Force
        [IO.File]::WriteAllText((Join-Path $bundled 'package-id.txt'), "$version-$buildId", [Text.UTF8Encoding]::new($false))
    }

    $manifest = @{ schemaVersion = 1; executable = 'releases/portable/Facet.exe' } | ConvertTo-Json
    [IO.File]::WriteAllText((Join-Path $packageRoot 'latest.json'), $manifest, [Text.UTF8Encoding]::new($false))
    $readme = @"
Facet $version (Windows x64)

실행: 압축을 풀고 Facet.Launcher.exe를 실행합니다. 설치와 .NET은 필요하지 않습니다.
위젯: 설정 → 플러그인 → 플러그인 받기에서 설치합니다. 스킨: 설정 → 스킨 → 스킨 받기.
기록: %LOCALAPPDATA%\MaterialStone\Facet 에 저장되며 이 Windows 사용자만 읽을 수 있습니다.
폴더를 옮긴 뒤에는 Codex·Claude에 등록한 Facet 도구(MCP)를 다시 등록하세요.
"@
    [IO.File]::WriteAllText((Join-Path $packageRoot 'README.txt'), $readme, [Text.UTF8Encoding]::new($true))

    # A partial package must fail here rather than on someone else's PC.
    $expected = @()
    if (Test-Path 'impls/Facet.ChatPlugin') { $expected += 'bundled-plugins/chat/onnxruntime.dll' }
    if (Test-Path 'impls/Facet.MusicPlugin') { $expected += 'bundled-plugins/music/Tools/yt-dlp.exe' }
    foreach ($file in @('Facet.Launcher.exe', 'Facet.Mcp.exe', 'Facet.Cli.exe', 'latest.json', 'releases/portable/Facet.exe', 'releases/portable/Facet.Plugins.dll', 'releases/portable/Microsoft.Windows.SDK.NET.dll', "runtime/host/fxr/$runtimeVersion/hostfxr.dll") + $expected) {
        if (-not (Test-Path -LiteralPath (Join-Path $packageRoot $file) -PathType Leaf)) { throw "Package is incomplete: $file" }
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = Join-Path $outputRoot "$name.zip"
    $pending = "$zip.$buildId.tmp"
    [IO.Compression.ZipFile]::CreateFromDirectory($packageRoot, $pending, [IO.Compression.CompressionLevel]::Optimal, $true)
    Move-Item -LiteralPath $pending -Destination $zip -Force
    Write-Host ('Ready: {0} ({1:N0} MB)' -f $zip, ((Get-Item -LiteralPath $zip).Length / 1MB))
} finally {
    Pop-Location
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
}
