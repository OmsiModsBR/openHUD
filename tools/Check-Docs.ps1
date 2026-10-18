<#
.SYNOPSIS
Checks the openHUD documentation.

.DESCRIPTION
Run from anywhere; paths are relative to the repository root.
1. Every relative link (and #anchor) in docs\**\*.md, README.md and CHANGELOG.md resolves.
   docs\architecture.md and docs\scene-protocol.md are owned by other authors: a link to
   architecture.md that cannot resolve because the file is not there yet is a warning, not a failure.
2. Every setting of module.template.json is in docs\reference\settings.md with its type, default
   and range, and has its own '### `key`' section.
3. Every page under docs\ is listed in docs\README.md.
Nothing is written. Exit code 0 when every check passes, 1 otherwise.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$docs = Join-Path $root 'docs'
$failures = New-Object System.Collections.Generic.List[string]
function Fail([string]$message) { $failures.Add($message) | Out-Null; Write-Host "FAIL  $message" }
$pending = @('docs\architecture.md')

$files = @(Get-ChildItem -Path $docs -Recurse -Filter *.md | ForEach-Object { $_.FullName })
foreach ($extra in @('README.md', 'CHANGELOG.md')) {
    $path = Join-Path $root $extra
    if (Test-Path -LiteralPath $path) { $files += $path }
}

function Strip-Code([string]$text) {
    $text = [regex]::Replace($text, '(?ms)^(```|~~~).*?^\1[^\r\n]*$', '')
    return [regex]::Replace($text, '`[^`\r\n]*`', '')
}

$anchorCache = @{}
function Get-Anchors([string]$path) {
    if ($anchorCache.ContainsKey($path)) { return $anchorCache[$path] }
    $set = New-Object System.Collections.Generic.HashSet[string]
    $seen = @{}
    $text = [IO.File]::ReadAllText($path)
    $text = [regex]::Replace($text, '(?ms)^(```|~~~).*?^\1[^\r\n]*$', '')
    foreach ($m in [regex]::Matches($text, '(?m)^#{1,6}[ \t]+(.+?)[ \t]*#*[ \t]*\r?$')) {
        $title = $m.Groups[1].Value
        $title = [regex]::Replace($title, '\[([^\]]*)\]\([^)]*\)', '$1')
        $title = $title -replace '`', ''
        $slug = $title.ToLowerInvariant()
        $slug = [regex]::Replace($slug, '[^\p{L}\p{N} _-]', '')
        $slug = $slug -replace ' ', '-'
        if ($seen.ContainsKey($slug)) { $seen[$slug]++; $slug = "$slug-$($seen[$slug])" } else { $seen[$slug] = 0 }
        $set.Add($slug) | Out-Null
    }
    $anchorCache[$path] = $set
    return $set
}

# ---- 1. links
$linkCount = 0
foreach ($file in $files) {
    $dir = Split-Path $file -Parent
    $text = Strip-Code ([IO.File]::ReadAllText($file))
    foreach ($m in [regex]::Matches($text, '(?<!!)\[[^\]]*\]\(([^)\s]+)(?:\s+"[^"]*")?\)')) {
        $target = $m.Groups[1].Value
        if ($target -match '^(https?:|mailto:)') { continue }
        $linkCount++
        $path = $target; $anchor = $null
        $hash = $target.IndexOf('#')
        if ($hash -ge 0) { $path = $target.Substring(0, $hash); $anchor = $target.Substring($hash + 1) }
        $relative = $file.Substring($root.Length + 1)
        if ($path -eq '') { $resolved = $file }
        else {
            $resolved = [IO.Path]::GetFullPath((Join-Path $dir ([Uri]::UnescapeDataString($path))))
            if (-not (Test-Path -LiteralPath $resolved)) {
                $rel = $resolved.Substring($root.Length + 1)
                if ($pending -contains $rel) { Write-Host "WARN  $relative -> $target (owned by another author, not there yet)"; continue }
                Fail "$relative -> $target (not found)"; continue
            }
        }
        if ($anchor -and $resolved -like '*.md' -and (Test-Path -LiteralPath $resolved -PathType Leaf)) {
            if (-not (Get-Anchors $resolved).Contains($anchor.ToLowerInvariant())) { Fail "$relative -> $target (no such heading)" }
        }
    }
}
Write-Host "links checked: $linkCount in $($files.Count) files"

# ---- 2. settings
$template = Get-Content -LiteralPath (Join-Path $root 'module.template.json') -Raw | ConvertFrom-Json
$settingsPath = Join-Path $docs 'reference\settings.md'
$settingsText = [IO.File]::ReadAllText($settingsPath)
$anchors = Get-Anchors $settingsPath
foreach ($setting in $template.settings) {
    $key = $setting.key
    $row = ([regex]::Matches($settingsText, '(?m)^\| `' + [regex]::Escape($key) + '` \|(.*)$') | Select-Object -First 1)
    if (-not $row) { Fail "settings.md: no summary row for '$key'"; continue }
    $cells = @($row.Groups[1].Value.Split('|') | ForEach-Object { $_.Trim() })
    if ($cells[0] -ne $setting.type) { Fail "settings.md: '$key' type is '$($cells[0])', manifest says '$($setting.type)'" }
    $default = $setting.default
    if ($default -is [bool]) { $default = $default.ToString().ToLowerInvariant() }
    $expectedDefault = '`' + $default + '`'
    if ($cells[1] -ne $expectedDefault) { Fail "settings.md: '$key' default is '$($cells[1])', manifest says '$expectedDefault'" }
    if ($setting.type -eq 'integer') {
        $expectedRange = "$($setting.min) to $($setting.max)"
        if ($cells[2] -ne $expectedRange) { Fail "settings.md: '$key' range is '$($cells[2])', manifest says '$expectedRange'" }
    }
    elseif ($setting.type -eq 'choice') {
        $expectedChoices = ($setting.choices | ForEach-Object { '`' + $_ + '`' }) -join ', '
        if ($cells[2] -ne $expectedChoices) { Fail "settings.md: '$key' choices are '$($cells[2])', manifest says '$expectedChoices'" }
    }
    elseif ($setting.type -eq 'boolean') {
        if ($cells[2] -ne '`true` or `false`') { Fail "settings.md: '$key' range is '$($cells[2])'" }
    }
    elseif ($setting.type -eq 'text') {
        $expectedLength = "up to $($setting.max_length) characters"
        if ($cells[2] -ne $expectedLength) { Fail "settings.md: '$key' length is '$($cells[2])', manifest says '$expectedLength'" }
    }
    if (-not $anchors.Contains($key)) { Fail "settings.md: no section '### $key'" }
}
Write-Host "settings checked: $(@($template.settings).Count)"

# ---- 3. index
$index = [IO.File]::ReadAllText((Join-Path $docs 'README.md'))
foreach ($file in Get-ChildItem -Path $docs -Recurse -Filter *.md) {
    if ($file.FullName -eq (Join-Path $docs 'README.md')) { continue }
    $relative = $file.FullName.Substring($docs.Length + 1).Replace('\', '/')
    if (-not $index.Contains("($relative)")) { Fail "docs\README.md does not list $relative" }
}

if ($failures.Count -gt 0) { Write-Host "$($failures.Count) problem(s)"; exit 1 }
Write-Host 'documentation checks passed'
exit 0
