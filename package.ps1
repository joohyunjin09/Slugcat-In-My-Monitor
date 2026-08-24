param(
    [string]$Version = 'v0.1.0',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

if ($Version -ne 'continuous' -and
    $Version -notmatch '^v?\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$') {
    throw "Version must be continuous or look like v0.1.0: $Version"
}
if ($Version -ne 'continuous' -and -not $Version.StartsWith('v')) {
    $Version = 'v' + $Version
}

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$packageRoot = Join-Path $repoRoot 'artifacts\package'
$packageName = "SlugcatInMyMonitor-$Version-win-x64"
$stagingRoot = Join-Path $packageRoot $packageName
$archivePath = Join-Path $packageRoot ($packageName + '.zip')
$checksumPath = $archivePath + '.sha256'

if (-not $SkipBuild) {
    & (Join-Path $repoRoot 'build.ps1') -Configuration Release
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$releaseRoot = Join-Path $repoRoot 'artifacts\Release'
$requiredFiles = @(
    (Join-Path $releaseRoot 'SlugcatInMyMonitor.exe'),
    (Join-Path $releaseRoot 'SlugcatInMyMonitor.exe.config'),
    (Join-Path $releaseRoot 'SlugcatInMyMonitor.DirectComposition.dll'),
    (Join-Path $repoRoot 'README.md'),
    (Join-Path $repoRoot 'LICENSE')
)
foreach ($file in $requiredFiles) {
    if (-not (Test-Path -LiteralPath $file)) { throw "Required package file is missing: $file" }
}

New-Item -ItemType Directory -Force -Path $packageRoot | Out-Null
if (Test-Path -LiteralPath $stagingRoot) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $stagingRoot | Out-Null

Copy-Item -LiteralPath (Join-Path $releaseRoot 'SlugcatInMyMonitor.exe') -Destination $stagingRoot
Copy-Item -LiteralPath (Join-Path $releaseRoot 'SlugcatInMyMonitor.exe.config') -Destination $stagingRoot
Copy-Item -LiteralPath (Join-Path $releaseRoot 'SlugcatInMyMonitor.DirectComposition.dll') -Destination $stagingRoot
Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md') -Destination $stagingRoot
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination $stagingRoot

Compress-Archive -Path (Join-Path $stagingRoot '*') -DestinationPath $archivePath -Force
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archivePath).Hash.ToLowerInvariant()
Set-Content -LiteralPath $checksumPath -Encoding ascii -NoNewline `
    -Value "$hash  $([IO.Path]::GetFileName($archivePath))"

Write-Host "Created $archivePath"
Write-Host "SHA-256 $hash"
