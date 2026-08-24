param(
    [string]$RainWorldRoot = 'C:\Program Files (x86)\Steam\steamapps\common\Rain World',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

# A net48 executable cannot be reflection-loaded reliably by PowerShell 7/CoreCLR.
# Relaunch under the inbox .NET Framework host when this script is called from pwsh.
if ($PSVersionTable.PSEdition -ne 'Desktop') {
    $windowsPowerShell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    if (-not (Test-Path -LiteralPath $windowsPowerShell)) {
        throw 'Windows PowerShell 5.1 is required for the net48 embedded-atlas smoke test.'
    }
    $relaunchArguments = @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $MyInvocation.MyCommand.Path,
        '-RainWorldRoot', $RainWorldRoot, '-Configuration', $Configuration
    )
    if ($SkipBuild) { $relaunchArguments += '-SkipBuild' }
    & $windowsPowerShell @relaunchArguments
    exit $LASTEXITCODE
}

$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not $SkipBuild) {
    & (Join-Path $repoRoot 'build.ps1') -Configuration $Configuration -SkipTests
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$assemblyPath = Join-Path $repoRoot "artifacts\$Configuration\SlugcatInMyMonitor.exe"
if (-not (Test-Path -LiteralPath $assemblyPath)) {
    throw "Build output was not found: $assemblyPath"
}
if (-not (Test-Path -LiteralPath (Join-Path $RainWorldRoot 'RainWorld_Data\resources.assets'))) {
    throw "Rain World resources.assets was not found under: $RainWorldRoot"
}

[void][Reflection.Assembly]::LoadFrom($assemblyPath)
$installation = New-Object RainWorldDesktopPet.RainWorld.RainWorldInstallation -ArgumentList $RainWorldRoot
$loader = New-Object RainWorldDesktopPet.RainWorld.RainWorldAssetLoader -ArgumentList $installation
$atlasSet = $loader.TryLoadPlayerAtlas()
Write-Host $loader.Status
if ($null -eq $atlasSet) { throw 'The player atlas loader returned null.' }

try {
    if ($atlasSet.AtlasCount -lt 2) {
        throw "Expected the base and MSC atlases, but loaded $($atlasSet.AtlasCount)."
    }

    $required = @('BodyA', 'HipsA', 'HeadA0', 'FaceA0', 'PlayerArm0', 'LegsA0')
    foreach ($name in $required) {
        $sprite = $null
        if (-not $atlasSet.TryGet($name, [ref]$sprite)) { throw "Missing base sprite: $name" }
        if ($sprite.Atlas.Image.Width -ne 464 -or $sprite.Atlas.Image.Height -ne 512) {
            throw "Unexpected base atlas dimensions for ${name}: $($sprite.Atlas.Image.Width)x$($sprite.Atlas.Image.Height)"
        }
    }

    # HeadC0 is an MSC-owned frame and therefore a useful RGBA32/override smoke sample.
    # It is not treated as Gourmand's head selection.
    $mscSprite = $null
    if (-not $atlasSet.TryGet('HeadC0', [ref]$mscSprite)) { throw 'Missing MSC sprite: HeadC0' }
    if ($mscSprite.Atlas.Image.Width -ne 367 -or $mscSprite.Atlas.Image.Height -ne 245) {
        throw "HeadC0 did not resolve to rainworldmsc: $($mscSprite.Atlas.Image.Width)x$($mscSprite.Atlas.Image.Height)"
    }

    # These are the v1.11.8 MSC frames used for both tail growth and the
    # physical held/thrown needle. A procedural stand-in must not pass here.
    $spearmasterFrames = @{
        BioSpear1 = @(20, 134, 7, 53)
        BioSpear2 = @(29, 134, 7, 53)
        BioSpear3 = @(38, 134, 6, 53)
    }
    foreach ($name in $spearmasterFrames.Keys) {
        $sprite = $null
        if (-not $atlasSet.TryGet($name, [ref]$sprite)) { throw "Missing Spearmaster sprite: $name" }
        if (-not $sprite.Atlas.ImagePath.EndsWith('#rainworldmsc', [StringComparison]::OrdinalIgnoreCase)) {
            throw "$name did not come from the embedded MSC atlas: $($sprite.Atlas.ImagePath)"
        }
        $expected = $spearmasterFrames[$name]
        $actual = $sprite.Element.Frame
        if ($actual.X -ne $expected[0] -or $actual.Y -ne $expected[1] -or
            $actual.Width -ne $expected[2] -or $actual.Height -ne $expected[3]) {
            throw "Unexpected $name frame geometry: $actual"
        }
    }

    $tinyStar = $null
    if (-not $atlasSet.TryGet('tinyStar', [ref]$tinyStar)) { throw 'Missing Spearmaster sprite: tinyStar' }
    $tinyStarFrame = $tinyStar.Element.Frame
    if (-not $tinyStar.Atlas.ImagePath.EndsWith('#rainWorld', [StringComparison]::OrdinalIgnoreCase) -or
        $tinyStarFrame.X -ne 450 -or $tinyStarFrame.Y -ne 495 -or
        $tinyStarFrame.Width -ne 3 -or $tinyStarFrame.Height -ne 3) {
        throw "Unexpected tinyStar source or frame geometry: $($tinyStar.Atlas.ImagePath) $tinyStarFrame"
    }

    # A geometry-only test can pass with an incorrectly decoded transparent texture.
    # Sample BodyA's decoded frame as a small pixel-level orientation/payload check.
    $body = $null
    [void]$atlasSet.TryGet('BodyA', [ref]$body)
    if (-not $body.Atlas.ImagePath.EndsWith('#rainWorld', [StringComparison]::OrdinalIgnoreCase)) {
        throw "BodyA did not come from the embedded original atlas: $($body.Atlas.ImagePath)"
    }
    $frame = $body.Element.Frame
    if ($frame.X -ne 358 -or $frame.Y -ne 50 -or $frame.Width -ne 14 -or $frame.Height -ne 19) {
        throw "Unexpected BodyA frame geometry: $frame"
    }
    $opaquePixels = 0
    for ($y = $frame.Top; $y -lt $frame.Bottom; $y++) {
        for ($x = $frame.Left; $x -lt $frame.Right; $x++) {
            if ($body.Atlas.Image.GetPixel($x, $y).A -gt 0) { $opaquePixels++ }
        }
    }
    if ($opaquePixels -ne 222) {
        throw "Unexpected BodyA opaque-pixel signature: $opaquePixels (expected 222 for v1.11.8)."
    }

    $mscFrame = $mscSprite.Element.Frame
    if (-not $mscSprite.Atlas.ImagePath.EndsWith('#rainworldmsc', [StringComparison]::OrdinalIgnoreCase)) {
        throw "HeadC0 did not come from the embedded MSC atlas: $($mscSprite.Atlas.ImagePath)"
    }
    $mscOpaquePixels = 0
    for ($y = $mscFrame.Top; $y -lt $mscFrame.Bottom; $y++) {
        for ($x = $mscFrame.Left; $x -lt $mscFrame.Right; $x++) {
            if ($mscSprite.Atlas.Image.GetPixel($x, $y).A -gt 0) { $mscOpaquePixels++ }
        }
    }
    if ($mscOpaquePixels -ne 163) {
        throw "Unexpected MSC HeadC0 opaque-pixel signature: $mscOpaquePixels (expected 163 for v1.11.8)."
    }

    Write-Host "PASS: $($atlasSet.AtlasCount) embedded atlases; BodyA opaque pixels=$opaquePixels; MSC HeadC0 opaque pixels=$mscOpaquePixels; BioSpear1/2/3 and tinyStar frames verified."
}
finally {
    $atlasSet.Dispose()
}
