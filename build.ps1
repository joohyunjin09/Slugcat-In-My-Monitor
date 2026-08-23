param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$toolRoot = Join-Path $repoRoot '.tools\net48-reference'
$referenceAssembly = Join-Path $toolRoot 'build\.NETFramework\v4.8\mscorlib.dll'
$msbuild = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe'

if (-not (Test-Path -LiteralPath $msbuild)) {
    throw '.NET Framework MSBuild was not found.'
}

if (-not (Test-Path -LiteralPath $referenceAssembly)) {
    $downloadRoot = Join-Path $repoRoot '.tools'
    $packagePath = Join-Path $downloadRoot 'net48-reference.zip'
    New-Item -ItemType Directory -Force -Path $downloadRoot | Out-Null
    Write-Host 'Downloading Microsoft .NET Framework 4.8 reference assemblies (build-only)...'
    Invoke-WebRequest -UseBasicParsing -Uri 'https://www.nuget.org/api/v2/package/Microsoft.NETFramework.ReferenceAssemblies.net48/1.0.3' -OutFile $packagePath
    if (Test-Path -LiteralPath $toolRoot) {
        Remove-Item -LiteralPath $toolRoot -Recurse -Force
    }
    Expand-Archive -LiteralPath $packagePath -DestinationPath $toolRoot
    Remove-Item -LiteralPath $packagePath -Force
}

& $msbuild (Join-Path $repoRoot 'RainWorldDesktopPet.sln') /t:Build /p:Configuration=$Configuration /m /nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not $SkipTests) {
    & (Join-Path $repoRoot "artifacts\$Configuration\RainWorldDesktopPet.Tests.exe")
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
