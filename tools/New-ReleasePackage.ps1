<#
.SYNOPSIS
Builds openHUD, assembles the module folder and packs the release archive the
way the release workflow does, so a release can be reproduced and tested locally.

.DESCRIPTION
1. Reads id and version from module.template.json.
2. Runs tools\Build-Module.ps1 (unless -SkipBuild) to assemble artifacts\modules\openhud.
3. Zips that folder as <OutputDirectory>\openhud-<version>.zip (the archive holds
   one top-level folder, openhud\) and writes <zip>.sha256.
4. Extracts the archive into a scratch folder and verifies it against the
   manifest: every component of module.json is present and its SHA-256 equals
   the pin, and the archive holds exactly the files of the assembled folder.
Nothing is copied into an OMSI installation and nothing is published.

.PARAMETER OmsiLaunchRoot
The OmsiLaunch checkout to build against (passed to Build-Module.ps1).

.PARAMETER OutputDirectory
Where the archive and its checksum are written (default: artifacts\release).

.PARAMETER SkipBuild
Packs the module folder already assembled in artifacts\modules\openhud.

.PARAMETER SkipTests
Passed to Build-Module.ps1.

.PARAMETER PlatformToolset
Passed to the build script (C++ toolset override, for example v143).

.PARAMETER Tag
When given (the release workflow passes the pushed tag), it must equal v<version>.
#>
[CmdletBinding()]
param(
    [string]$OmsiLaunchRoot = '',
    [string]$OutputDirectory = '',
    [switch]$SkipBuild,
    [switch]$SkipTests,
    [string]$Tag = '',
    [string]$PlatformToolset = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$buildScript = Join-Path $PSScriptRoot 'Build-Module.ps1'
$templatePath = Join-Path $root 'module.template.json'
$moduleDir = Join-Path $root 'artifacts\modules\openhud'

$template = Get-Content -LiteralPath $templatePath -Raw | ConvertFrom-Json
$id = [string]$template.id
$version = [string]$template.version
if ($id -ne 'openhud') { throw "Unexpected module id '$id' in module.template.json." }
if ($version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') { throw "The manifest version '$version' is not a semantic version." }
if ($Tag -and $Tag -ne "v$version") { throw "The tag '$Tag' does not match the manifest version: expected 'v$version'." }

if (-not $SkipBuild) {
    $buildArguments = @{}
    if ($OmsiLaunchRoot) { $buildArguments.OmsiLaunchRoot = $OmsiLaunchRoot }
    if ($SkipTests) { $buildArguments.SkipTests = $true }
    if ($PlatformToolset) { $buildArguments.PlatformToolset = $PlatformToolset }
    & $buildScript @buildArguments
    if (-not $?) { throw 'The module build failed.' }
}
if (-not (Test-Path -LiteralPath (Join-Path $moduleDir 'module.json'))) { throw "No assembled module in $moduleDir. Run without -SkipBuild." }

if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root 'artifacts\release' }
$OutputDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$zip = Join-Path $OutputDirectory "$id-$version.zip"
$checksum = "$zip.sha256"
foreach ($old in @($zip, $checksum)) { if (Test-Path -LiteralPath $old) { Remove-Item -LiteralPath $old -Force } }

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $moduleDir -File -Recurse | Sort-Object FullName) {
        $relative = $file.FullName.Substring($moduleDir.Length).TrimStart('\', '/').Replace('\', '/')
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, "$id/$relative", [IO.Compression.CompressionLevel]::Optimal)
    }
} finally { $archive.Dispose() }
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($checksum, "$hash  $([IO.Path]::GetFileName($zip))`n", [Text.UTF8Encoding]::new($false))

# Verify the archive, not the folder it came from.
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('release-verify-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
try {
    [IO.Compression.ZipFile]::ExtractToDirectory($zip, $scratch)
    $extracted = Join-Path $scratch $id
    $manifestPath = Join-Path $extracted 'module.json'
    if (-not (Test-Path -LiteralPath $manifestPath)) { throw 'The archive has no openhud\module.json.' }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.id -ne $id -or $manifest.version -ne $version) { throw "module.json says $($manifest.id) $($manifest.version); expected $id $version." }
    foreach ($component in $manifest.components) {
        $path = Join-Path $extracted $component.path
        if (-not (Test-Path -LiteralPath $path)) { throw "The archive lacks the component $($component.path)." }
        $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($component.sha256 -notmatch '^[0-9a-f]{64}$') { throw "Component $($component.path) has no SHA-256 pin in module.json." }
        if ($actual -ne $component.sha256) { throw "Component $($component.path): pinned $($component.sha256) but the archive holds $actual." }
        Write-Host ("  pin ok  {0,-24} {1}" -f $component.path, $actual)
    }
    $expected = @(Get-ChildItem -LiteralPath $moduleDir -File -Recurse | ForEach-Object { $_.FullName.Substring($moduleDir.Length).TrimStart('\') } | Sort-Object)
    $found = @(Get-ChildItem -LiteralPath $extracted -File -Recurse | ForEach-Object { $_.FullName.Substring($extracted.Length).TrimStart('\') } | Sort-Object)
    if (($expected -join '|') -ne ($found -join '|')) { throw "The archive content differs from the assembled folder.`nexpected: $($expected -join ', ')`nfound: $($found -join ', ')" }
    foreach ($name in 'NOTICE.txt', 'LGPL-3.0.txt', 'GPL-3.0.txt') {
        if ($found -notcontains $name) { throw "The archive lacks $name." }
    }
} finally { Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue }

Write-Host "archive : $zip"
Write-Host "checksum: $checksum"
Write-Host "sha256  : $hash"
if ($env:GITHUB_OUTPUT) {
    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "version=$version"
    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "zip=$zip"
}
