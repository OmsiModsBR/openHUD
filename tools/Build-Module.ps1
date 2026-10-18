<#
.SYNOPSIS
Builds the openHUD module and assembles it the way it is installed:
artifacts\modules\openhud\ with its module.json (every component pinned by its
SHA-256) and its files. Nothing is copied into an OMSI installation.

.PARAMETER OmsiLaunchRoot
The OmsiLaunch checkout the module builds against (default: the sibling
OmsiLaunch-spec-pack\OmsiLaunch).

.PARAMETER SkipTests
Builds and assembles without running the tests.
#>
[CmdletBinding()]
param(
    [string]$OmsiLaunchRoot = '',
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $root 'artifacts'
$extra = @()
if ($OmsiLaunchRoot) { $extra += "-p:OmsiLaunchRoot=$((Resolve-Path $OmsiLaunchRoot).Path.TrimEnd('\'))\" }
$omsiLaunch = if ($OmsiLaunchRoot) { (Resolve-Path $OmsiLaunchRoot).Path } else { Join-Path $root '..\OmsiLaunch-spec-pack\OmsiLaunch' }

function Find-MSBuild {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        $found = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
        if ($found) { return $found }
    }
    $fallback = 'C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\MSBuild.exe'
    if (Test-Path $fallback) { return $fallback }
    throw 'MSBuild (Visual Studio C++ build tools) was not found.'
}

function Invoke-Step([string]$What, [scriptblock]$Command) {
    Write-Host "== $What"
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$What failed (exit code $LASTEXITCODE)" }
}

function Write-Manifest([string]$Template, [string]$Directory) {
    $text = [IO.File]::ReadAllText($Template)
    $pinned = [regex]::Replace($text, '"path": "([^"]+)", "sha256": ""', {
        param($match)
        $file = Join-Path $Directory $match.Groups[1].Value
        if (-not (Test-Path -LiteralPath $file)) { throw "the manifest names $($match.Groups[1].Value), which is not in $Directory" }
        $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
        '"path": "' + $match.Groups[1].Value + '", "sha256": "' + $hash + '"'
    })
    [IO.File]::WriteAllText((Join-Path $Directory 'module.json'), $pinned, [Text.UTF8Encoding]::new($false))
}

$msbuild = Find-MSBuild

Invoke-Step 'openhud: draw layer (x86)' { & $msbuild (Join-Path $root 'src\OpenHud.Draw\OpenHud.Draw.vcxproj') -p:Configuration=Release -p:Platform=Win32 -nologo -v:minimal @extra }
if (-not $SkipTests) {
    Invoke-Step 'openhud: draw layer tests (x86)' { & $msbuild (Join-Path $root 'tests\native\OpenHud.Draw.Tests.vcxproj') -p:Configuration=Release -p:Platform=Win32 -nologo -v:minimal @extra }
    Invoke-Step 'openhud: draw layer tests: run' { & (Join-Path $artifacts 'bin\OpenHud.Draw.Tests\OpenHud.Draw.Tests.exe') --vectors (Join-Path $root 'tests\vectors') }
}
Invoke-Step 'openhud: managed component' { dotnet build (Join-Path $root 'src\OpenHud.Module\OpenHud.Module.csproj') -c Release -nologo -v:minimal @extra }
$module = Join-Path $artifacts 'modules\openhud'
if (Test-Path $module) { Remove-Item -LiteralPath $module -Recurse -Force }
New-Item -ItemType Directory -Path $module | Out-Null
$managed = Join-Path $artifacts 'bin\OpenHud.Module\Release\net6.0'
Copy-Item (Join-Path $managed 'OpenHud.dll') $module
Copy-Item (Join-Path $managed 'OpenHud.Core.dll') $module
Copy-Item (Join-Path $managed 'OpenHud.Pause.dll') $module
Copy-Item (Join-Path $artifacts 'bin\OpenHud.Draw\openhud-draw.dll') $module
# The module uses OmsiLaunch (LGPL-3.0-only): its notice and the license text go with it.
Copy-Item (Join-Path $root 'NOTICE.txt') $module
Copy-Item (Join-Path $omsiLaunch 'LICENSE') (Join-Path $module 'LGPL-3.0.txt')
Copy-Item (Join-Path $omsiLaunch 'COPYING') (Join-Path $module 'GPL-3.0.txt')
Write-Manifest (Join-Path $root 'module.template.json') $module
Write-Host "openhud: assembled in $module"
Get-ChildItem $module | ForEach-Object { Write-Host ("  {0,-24} {1,12:N0} bytes" -f $_.Name, $_.Length) }

if (-not $SkipTests) {
    Invoke-Step 'openhud: tests' {
        dotnet build (Join-Path $root 'tests\OpenHud.Tests\OpenHud.Tests.csproj') -c Release -nologo -v:minimal @extra
        if ($LASTEXITCODE -eq 0) { & (Join-Path $artifacts 'bin\OpenHud.Tests\Release\net6.0-windows\win-x64\OpenHud.Tests.exe') }
    }
}
Write-Host 'done'
