# Posts a version's changelog to Nexus again (the publish script's own changelog step, on its own).
# Usage: powershell -ExecutionPolicy Bypass -File tools\NexusChangelog.ps1 -Mod WiresAcoustics -Version 0.5.0
param([Parameter(Mandatory)][string]$Mod, [Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$Config = Get-Content -Raw -Path (Join-Path $PSScriptRoot 'mods.json') | ConvertFrom-Json
$ModCfg = $Config.mods.$Mod
if (-not $ModCfg -or -not $ModCfg.nexus_mod_id) { throw "No nexus_mod_id for $Mod in tools\mods.json" }
$key = [Environment]::GetEnvironmentVariable('NEXUSMODS_API_KEY', 'Process')
if (-not $key) { $key = [Environment]::GetEnvironmentVariable('NEXUSMODS_API_KEY', 'User') }
if (-not $key) { throw 'NEXUSMODS_API_KEY not set' }

# the version's section, up to the next heading of the same or a higher level; one entry per line, no markdown
$lines = Get-Content -Path (Join-Path $root "$Mod\package\CHANGELOG.md") -Encoding UTF8
$start = -1
for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -match "^#+.*\b$([regex]::Escape($Version))\b") { $start = $i; break } }
if ($start -lt 0) { throw "No $Version section in CHANGELOG.md" }
$level = ([regex]::Match($lines[$start], '^#+')).Length
$out = @()
for ($i = $start + 1; $i -lt $lines.Count; $i++) {
    $m = [regex]::Match($lines[$i], '^(#+) ')
    if ($m.Success -and $m.Groups[1].Length -le $level) { break }
    if ($m.Success -or -not $lines[$i].Trim()) { continue }
    $out += ($lines[$i] -replace '^\s*[-*]\s+', '')
}
$text = $out -join "`n"
Write-Host $text
Write-Host ""
if ((Read-Host "Post this as the $Version changelog of $Mod on Nexus? (y/n)") -ne 'y') { return }

$api = 'https://api.nexusmods.com/v3'
$h = @{ apikey = $key; 'User-Agent' = 'WireMods-Publish' }
$info = Invoke-RestMethod -Uri "$api/games/$($Config.community)/mods/$($ModCfg.nexus_mod_id)" -Headers $h
Invoke-RestMethod -Uri "$api/mods/$($info.data.id)/changelogs" -Method Post -Headers $h -ContentType 'application/json' `
    -Body (@{ version = $Version; changelog = $text } | ConvertTo-Json) | Out-Null
Write-Host "Posted."
