param(
    [string]$Version = 'v0.1.0',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^v?\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$') {
    throw "Version must look like v0.1.0: $Version"
}
if (-not $Version.StartsWith('v')) { $Version = 'v' + $Version }

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$packageRoot = Join-Path $repoRoot 'artifacts\package'
$packageName = "Slugcat-In-My-Monitor-$Version-win-x64"
$stagingRoot = Join-Path $packageRoot $packageName
$archivePath = Join-Path $packageRoot ($packageName + '.zip')
$checksumPath = $archivePath + '.sha256'

if (-not $SkipBuild) {
    & (Join-Path $repoRoot 'build.ps1') -Configuration Release
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$releaseRoot = Join-Path $repoRoot 'artifacts\Release'
$requiredFiles = @(
    (Join-Path $releaseRoot 'RainWorldDesktopPet.exe'),
    (Join-Path $releaseRoot 'RainWorldDesktopPet.exe.config'),
    (Join-Path $repoRoot 'README.md'),
    (Join-Path $repoRoot 'LICENSE'),
    (Join-Path $repoRoot 'packaging\skins\README.txt')
)
foreach ($file in $requiredFiles) {
    if (-not (Test-Path -LiteralPath $file)) { throw "Required package file is missing: $file" }
}

New-Item -ItemType Directory -Force -Path $packageRoot | Out-Null
if (Test-Path -LiteralPath $stagingRoot) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path (Join-Path $stagingRoot 'skins') | Out-Null

Copy-Item -LiteralPath (Join-Path $releaseRoot 'RainWorldDesktopPet.exe') -Destination $stagingRoot
Copy-Item -LiteralPath (Join-Path $releaseRoot 'RainWorldDesktopPet.exe.config') -Destination $stagingRoot
Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md') -Destination $stagingRoot
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination $stagingRoot
Copy-Item -LiteralPath (Join-Path $repoRoot 'packaging\skins\README.txt') `
    -Destination (Join-Path $stagingRoot 'skins\README.txt')

Compress-Archive -Path (Join-Path $stagingRoot '*') -DestinationPath $archivePath -Force
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archivePath).Hash.ToLowerInvariant()
Set-Content -LiteralPath $checksumPath -Encoding ascii -NoNewline `
    -Value "$hash  $([IO.Path]::GetFileName($archivePath))"

Write-Host "Created $archivePath"
Write-Host "SHA-256 $hash"
