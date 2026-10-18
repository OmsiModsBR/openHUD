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
3. Every page under docs\ (except docs\pt-BR\) is listed in docs\README.md.
4. docs\pt-BR\ mirrors docs\ (every page except scene-protocol.md, which stays English-only): the same headings (level
   and order) each with its explicit English anchor line, identical code blocks, the l10n banner pointing to the English
   page with its current SHA-256, the language line in both READMEs, every page listed in docs\pt-BR\README.md, and
   every setting of module.template.json in docs\pt-BReference\settings.md with its type, default and bounds.
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
    foreach ($a in [regex]::Matches($text, '<a id="([^"]+)"></a>')) { $set.Add($a.Groups[1].Value.ToLowerInvariant()) | Out-Null }
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
$ptRoot = Join-Path $docs 'pt-BR'
$englishPages = @(Get-ChildItem -Path $docs -Recurse -Filter *.md | Where-Object { -not $_.FullName.StartsWith($ptRoot + '\') })
foreach ($file in $englishPages) {
    if ($file.FullName -eq (Join-Path $docs 'README.md')) { continue }
    $relative = $file.FullName.Substring($docs.Length + 1).Replace('\', '/')
    if (-not $index.Contains("($relative)")) { Fail "docs\README.md does not list $relative" }
}

# ---- 4. Brazilian Portuguese tree
function Read-Lf([string]$path) { return ([IO.File]::ReadAllText($path) -replace "`r`n", "`n") }
function Get-Sha256Text([string]$text) {
    $sha = [Security.Cryptography.SHA256]::Create()
    $bytes = (New-Object Text.UTF8Encoding($false)).GetBytes($text)
    return (($sha.ComputeHash($bytes) | ForEach-Object { $_.ToString('x2') }) -join '')
}
function Get-Headings([string]$text) {
    $plain = [regex]::Replace($text, '(?ms)^(```|~~~).*?^\1[^\r\n]*$', '')
    $list = New-Object System.Collections.Generic.List[object]
    $seen = @{}
    foreach ($m in [regex]::Matches($plain, '(?m)^(#{1,6})[ \t]+(.+?)[ \t]*#*[ \t]*\r?$')) {
        $title = [regex]::Replace($m.Groups[2].Value, '\[([^\]]*)\]\([^)]*\)', '$1') -replace '`', ''
        $slug = [regex]::Replace($title.ToLowerInvariant(), '[^\p{L}\p{N} _-]', '') -replace ' ', '-'
        if ($seen.ContainsKey($slug)) { $seen[$slug]++; $slug = "$slug-$($seen[$slug])" } else { $seen[$slug] = 0 }
        $list.Add([pscustomobject]@{ Level = $m.Groups[1].Value.Length; Slug = $slug }) | Out-Null
    }
    return ,$list
}
function Get-CodeBlocks([string]$text) {
    return @([regex]::Matches($text, '(?ms)^(```|~~~)[^\r\n]*\r?\n.*?^\1[^\r\n]*$') | ForEach-Object { $_.Value })
}

$pageCount = 0
if (-not (Test-Path -LiteralPath $ptRoot)) { Fail 'docs\pt-BR does not exist' }
else {
    $ptPages = @(Get-ChildItem -Path $ptRoot -Recurse -Filter *.md)
    foreach ($page in $ptPages) {
        $relative = $page.FullName.Substring($ptRoot.Length + 1).Replace('\', '/')
        if (-not (Test-Path -LiteralPath (Join-Path $docs $relative))) { Fail "pt-BR/$relative has no English page" }
    }
    foreach ($english in $englishPages) {
        if ($english.FullName -eq (Join-Path $docs 'scene-protocol.md')) { continue }
        $relative = $english.FullName.Substring($docs.Length + 1).Replace('\', '/')
        $ptPath = Join-Path $ptRoot $relative
        if (-not (Test-Path -LiteralPath $ptPath)) { Fail "pt-BR/$relative is missing"; continue }
        $pageCount++
        $enText = Read-Lf $english.FullName
        $ptText = Read-Lf $ptPath
        $label = "pt-BR/$relative"

        # banner: source path and the current hash of the English page
        $depth = ($relative.Split('/').Count)
        $banner = [regex]::Match($ptText, '<!-- l10n: source=(\S+) source-sha256=([0-9a-f]{64}) -->\n> [^\n]*?\]\(([^)]+)\)')
        if (-not $banner.Success) { Fail "$label has no l10n banner" }
        else {
            if ($banner.Groups[1].Value -ne $relative) { Fail "$label banner source is '$($banner.Groups[1].Value)', expected '$relative'" }
            if ($banner.Groups[2].Value -ne (Get-Sha256Text $enText)) { Fail "$label is out of date: the English page changed since the banner hash was recorded" }
            $expectedLink = ('../' * $depth) + $relative
            if ($banner.Groups[3].Value -ne $expectedLink) { Fail "$label banner links to '$($banner.Groups[3].Value)', expected '$expectedLink'" }
        }

        # headings: same count and levels; each heading but the title carries the English anchor line before it
        $enHeadings = Get-Headings $enText
        $ptHeadings = Get-Headings $ptText
        if ($enHeadings.Count -ne $ptHeadings.Count) { Fail "$label has $($ptHeadings.Count) headings, English has $($enHeadings.Count)" }
        else {
            for ($i = 0; $i -lt $enHeadings.Count; $i++) {
                if ($enHeadings[$i].Level -ne $ptHeadings[$i].Level) { Fail "$label heading $($i + 1) has level $($ptHeadings[$i].Level), English has $($enHeadings[$i].Level)"; break }
            }
            $ptPlain = [regex]::Replace($ptText, '(?ms)^(```|~~~).*?^\1[^\r\n]*$', '')
            $ptAnchored = @([regex]::Matches($ptPlain, '(?m)^<a id="([^"]+)"></a>\n#{2,6}[ \t]+\S') | ForEach-Object { $_.Groups[1].Value })
            $expectedAnchors = @(); for ($i = 1; $i -lt $enHeadings.Count; $i++) { $expectedAnchors += $enHeadings[$i].Slug }
            if (($ptAnchored -join '|') -ne ($expectedAnchors -join '|')) { Fail "$label : the English anchor lines before the headings do not match the English headings" }
        }

        # code blocks: identical
        $enBlocks = Get-CodeBlocks $enText
        $ptBlocks = Get-CodeBlocks $ptText
        if ($enBlocks.Count -ne $ptBlocks.Count) { Fail "$label has $($ptBlocks.Count) code blocks, English has $($enBlocks.Count)" }
        else {
            for ($i = 0; $i -lt $enBlocks.Count; $i++) {
                if ($enBlocks[$i] -ne $ptBlocks[$i]) { Fail "$label code block $($i + 1) differs from the English page"; break }
            }
        }
    }

    # language line in both READMEs
    if (-not ([IO.File]::ReadAllText((Join-Path $docs 'README.md'))).Contains('(pt-BR/README.md)')) { Fail 'docs\README.md has no language line linking pt-BR/README.md' }
    if (-not ([IO.File]::ReadAllText((Join-Path $ptRoot 'README.md'))).Contains('[English](../README.md)')) { Fail 'docs\pt-BR\README.md has no language line linking ../README.md' }

    # index
    $ptIndex = [IO.File]::ReadAllText((Join-Path $ptRoot 'README.md'))
    foreach ($page in $ptPages) {
        if ($page.FullName -eq (Join-Path $ptRoot 'README.md')) { continue }
        $relative = $page.FullName.Substring($ptRoot.Length + 1).Replace('\', '/')
        if (-not $ptIndex.Contains("($relative)")) { Fail "docs\pt-BR\README.md does not list $relative" }
    }

    # settings
    $ptSettingsPath = Join-Path $ptRoot 'reference\settings.md'
    if (Test-Path -LiteralPath $ptSettingsPath) {
        $ptSettings = [IO.File]::ReadAllText($ptSettingsPath)
        $ptAnchors = Get-Anchors $ptSettingsPath
        $typeNames = @{ integer = 'inteiro'; boolean = 'booleano'; text = 'texto'; choice = 'escolha' }
        $and = ' a '; $or = ' ou '; $upTo = "at$([char]0xE9) "; $chars = ' caracteres'
        foreach ($setting in $template.settings) {
            $key = $setting.key
            $row = ([regex]::Matches($ptSettings, '(?m)^\| `' + [regex]::Escape($key) + '` \|(.*)$') | Select-Object -First 1)
            if (-not $row) { Fail "pt-BR settings.md: no summary row for '$key'"; continue }
            $cells = @($row.Groups[1].Value.Split('|') | ForEach-Object { $_.Trim() })
            if ($cells[0] -ne $typeNames[[string]$setting.type]) { Fail "pt-BR settings.md: '$key' type is '$($cells[0])', expected '$($typeNames[[string]$setting.type])'" }
            $default = $setting.default
            if ($default -is [bool]) { $default = $default.ToString().ToLowerInvariant() }
            $expectedDefault = if ($setting.type -eq 'text' -and "$default" -eq '') { '(vazio)' } else { '`' + $default + '`' }
            if ($cells[1] -ne $expectedDefault) { Fail "pt-BR settings.md: '$key' default is '$($cells[1])', manifest says '$expectedDefault'" }
            if ($setting.type -eq 'integer') {
                $expectedRange = "$($setting.min)$and$($setting.max)" + $(if ($setting.auto) { $or + '`auto`' } else { '' })
                if ($cells[2] -ne $expectedRange) { Fail "pt-BR settings.md: '$key' range is '$($cells[2])', manifest says '$expectedRange'" }
            }
            elseif ($setting.type -eq 'choice') {
                $expectedChoices = ($setting.choices | ForEach-Object { '`' + $_ + '`' }) -join ', '
                if ($cells[2] -ne $expectedChoices) { Fail "pt-BR settings.md: '$key' choices are '$($cells[2])', manifest says '$expectedChoices'" }
            }
            elseif ($setting.type -eq 'text') {
                $expectedLength = "$upTo$($setting.max_length)$chars"
                if ($cells[2] -ne $expectedLength) { Fail "pt-BR settings.md: '$key' length is '$($cells[2])', manifest says '$expectedLength'" }
            }
            if (-not $ptAnchors.Contains($key)) { Fail "pt-BR settings.md: no section '### $key'" }
        }
    }
}
Write-Host "pt-BR pages checked: $pageCount"

if ($failures.Count -gt 0) { Write-Host "$($failures.Count) problem(s)"; exit 1 }
Write-Host 'documentation checks passed'
exit 0
