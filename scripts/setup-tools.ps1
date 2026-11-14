# Fetches the pinned yt-dlp.exe, which is not stored in git, and verifies it against tool-manifest.json.
$ErrorActionPreference = 'Stop'
$toolRoot = Join-Path (Split-Path -Parent $PSScriptRoot) 'impls/Facet.MusicPlugin/Tools'
# A checkout without the music plugin has nothing to fetch.
if (-not (Test-Path -LiteralPath $toolRoot)) { return }
$manifest = Get-Content -LiteralPath (Join-Path $toolRoot 'tool-manifest.json') -Raw | ConvertFrom-Json
$tool = Join-Path $toolRoot 'yt-dlp.exe'

function Test-Tool([string]$Path) {
    (Test-Path -LiteralPath $Path -PathType Leaf) -and (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -eq $manifest.sha256.ToUpperInvariant()
}

if (Test-Tool $tool) { return }
$url = 'https://github.com/yt-dlp/yt-dlp/releases/download/' + $manifest.version + '/yt-dlp.exe'
$pending = $tool + '.download'
Write-Host ('Downloading yt-dlp ' + $manifest.version + ' from ' + $url)
try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest -Uri $url -OutFile $pending -UseBasicParsing
    if (-not (Test-Tool $pending)) { throw 'Downloaded yt-dlp.exe failed SHA-256 verification.' }
    Move-Item -LiteralPath $pending -Destination $tool -Force
} finally {
    if (Test-Path -LiteralPath $pending) { Remove-Item -LiteralPath $pending -Force }
}
