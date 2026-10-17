<#
.SYNOPSIS
  Build, package and publish one Wire mod to GitHub, Thunderstore, Hexium and Nexus Mods.

.EXAMPLE
  .\publish.cmd WiresRecycler                 # publish what is in package\manifest.json
  .\publish.cmd WiresRecycler -Version 1.0.3  # set the manifest version first (only that field)
  .\publish.cmd WiresRecycler -DryRun         # build, pack and check everything, upload nothing
  .\publish.cmd WiresRecycler -Sites thunderstore,hexium

.NOTES
  Tokens come from user environment variables (set once with tools\Setup.ps1):
    THUNDERSTORE_TOKEN, HEXIUM_TOKEN, NEXUSMODS_API_KEY.   GitHub uses the GitHub CLI login (gh auth login).
  Never edits README.md, icon.png or the manifest description. -Version only rewrites "version_number".
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)][string]$Mod,
    [string]$Version,
    [string[]]$Sites = @('github', 'thunderstore', 'hexium', 'nexus'),
    [string]$Changelog,
    [switch]$DryRun,
    [switch]$SkipBuild,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$Sites = @($Sites | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$Root = Split-Path -Parent $PSScriptRoot
$Config = Get-Content -Raw -Path (Join-Path $PSScriptRoot 'mods.json') | ConvertFrom-Json
$Results = [ordered]@{}

function Say($text, $color = 'Gray') { Write-Host $text -ForegroundColor $color }
function Fail($text) { Say "ERROR: $text" 'Red'; exit 1 }
function EnvToken($name) {
    $v = [Environment]::GetEnvironmentVariable($name, 'Process')
    if (-not $v) { $v = [Environment]::GetEnvironmentVariable($name, 'User') }
    return $v
}
function VersionParts($v) { return [version]($v + ('.0' * [Math]::Max(0, 2 - ($v.Split('.').Count - 1)))) }

# ---------- mod + manifest ----------
$ModCfg = $Config.mods.$Mod
if (-not $ModCfg) { Fail "Unknown mod '$Mod'. Known: $(($Config.mods.PSObject.Properties.Name) -join ', ')" }
if (-not $ModCfg.publish -and -not $Force) { Fail "$Mod has publish=false in tools\mods.json ($($ModCfg.note)). Use -Force to override." }
$ModDir = Join-Path $Root $Mod
$PkgDir = Join-Path $ModDir 'package'
$SrcDir = Join-Path $ModDir 'src'
$ManifestPath = Join-Path $PkgDir 'manifest.json'
foreach ($f in @('manifest.json', 'README.md', 'icon.png')) {
    if (-not (Test-Path (Join-Path $PkgDir $f))) { Fail "package\$f is missing (Thunderstore and Hexium require it)." }
}

$OldVer = ((Get-Content -Raw -Path $ManifestPath -Encoding UTF8) | ConvertFrom-Json).version_number
if ($Version) {
    $bytes = [IO.File]::ReadAllBytes($ManifestPath)
    $text = [Text.Encoding]::UTF8.GetString($bytes)
    $new = [regex]::Replace($text, '("version_number"\s*:\s*")[^"]*(")', "`${1}$Version`${2}", 1)
    if (-not $DryRun) { [IO.File]::WriteAllBytes($ManifestPath, [Text.Encoding]::UTF8.GetBytes($new)) }
    else { Say "(dry run) would set manifest version_number to $Version" }
}
$Manifest = (Get-Content -Raw -Path $ManifestPath -Encoding UTF8) | ConvertFrom-Json
$PkgName = $Manifest.name
$Ver = if ($Version) { $Version } else { $Manifest.version_number }
Say "== $Mod ($PkgName) $Ver ==" 'Cyan'

# Source version constant sanity check (warning only).
$srcHits = Get-ChildItem -Path $SrcDir -Filter *.cs -Recurse | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\|/(bin|obj)/' } |
    Select-String -Pattern 'Version\s*=\s*"(\d+\.\d+\.\d+)"' | ForEach-Object { $_.Matches[0].Groups[1].Value } | Select-Object -Unique
# With -Version, also bump the plugin's own version constant (Version = "x.y.z") if it matched the old manifest version.
if ($Version -and $srcHits -and ($srcHits -contains $OldVer) -and ($OldVer -ne $Version)) {
    $files = Get-ChildItem -Path $SrcDir -Filter *.cs -Recurse | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\|/(bin|obj)/' } |
        Where-Object { Select-String -Path $_.FullName -Pattern "Version\s*=\s*`"$([regex]::Escape($OldVer))`"" -Quiet }
    foreach ($f in $files) {
        if ($DryRun) { Say "(dry run) would set Version = `"$Version`" in $($f.Name)"; continue }
        $t = [IO.File]::ReadAllText($f.FullName)
        $t = [regex]::Replace($t, "(Version\s*=\s*`")$([regex]::Escape($OldVer))(`")", "`${1}$Version`${2}")
        [IO.File]::WriteAllText($f.FullName, $t, (New-Object Text.UTF8Encoding($false)))
        Say "Set plugin version to $Version in $($f.Name)"
    }
    if (-not $DryRun) { $srcHits = @($srcHits | ForEach-Object { if ($_ -eq $OldVer) { $Version } else { $_ } }) }
}
if ($srcHits -and ($srcHits -notcontains $Ver) -and -not ($DryRun -and $Version)) {
    Say "WARNING: source declares version $($srcHits -join ', ') but manifest says $Ver." 'Yellow'
}

# ---------- changelog ----------
if (-not $Changelog) {
    $cl = Join-Path $PkgDir 'CHANGELOG.md'
    if (Test-Path $cl) {
        $lines = Get-Content -Path $cl -Encoding UTF8
        $start = -1
        for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -match "^#+.*\b$([regex]::Escape($Ver))\b") { $start = $i; break } }
        if ($start -ge 0) {
            $body = @()
            for ($i = $start + 1; $i -lt $lines.Count -and $lines[$i] -notmatch '^#+ '; $i++) { $body += $lines[$i] }
            $Changelog = ($body -join "`n").Trim()
        }
    }
}
if (-not $Changelog) { $Changelog = "Version $Ver" }

# ---------- build ----------
if (-not $SkipBuild) {
    Say "Building..."
    Push-Location $SrcDir
    try {
        $out = & dotnet build -c Release 2>&1
        if ($LASTEXITCODE -ne 0) { $out | Select-Object -Last 30 | ForEach-Object { Say $_ 'Red' }; Fail 'Build failed.' }
        $warn = ($out | Select-String -Pattern '(\d+) Warning\(s\)' | Select-Object -Last 1)
        Say "Build OK. $warn" 'Green'
    } finally { Pop-Location }
    foreach ($dll in Get-ChildItem -Path $PkgDir -Filter *.dll) {
        $built = Join-Path $SrcDir "bin\Release\net472\$($dll.Name)"
        if (-not (Test-Path $built)) { Fail "Built $($dll.Name) not found at $built" }
        if (-not $DryRun) { Copy-Item $built $dll.FullName -Force }
    }
}

# ---------- package ----------
$OutDir = Join-Path $Root '_releases'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$Zip = Join-Path $OutDir "$PkgName-$Ver.zip"
if (Test-Path $Zip) { Remove-Item $Zip -Force }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$za = [IO.Compression.ZipFile]::Open($Zip, 'Create')
try {
    foreach ($f in Get-ChildItem -Path $PkgDir -File) {
        $srcFile = $f.FullName
        if ($DryRun -and $f.Extension -eq '.dll' -and -not $SkipBuild) {
            $b = Join-Path $SrcDir "bin\Release\net472\$($f.Name)"; if (Test-Path $b) { $srcFile = $b }
        }
        if ($f.Name -eq 'manifest.json' -and $DryRun -and $Version) {
            $e = $za.CreateEntry('manifest.json'); $w = New-Object IO.StreamWriter($e.Open(), (New-Object Text.UTF8Encoding($false)))
            $w.Write([regex]::Replace((Get-Content -Raw $srcFile -Encoding UTF8), '("version_number"\s*:\s*")[^"]*(")', "`${1}$Version`${2}", 1)); $w.Close(); continue
        }
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($za, $srcFile, $f.Name) | Out-Null
    }
} finally { $za.Dispose() }
Say "Packed $Zip ($([Math]::Round((Get-Item $Zip).Length / 1KB)) KB)" 'Green'

# ---------- Thunderstore / Hexium (same API) ----------
function Publish-TsLike($site, $api, $team, $tokenName, $categories) {
    try {
        $info = Invoke-RestMethod -Uri "$api/package/$team/$PkgName/" -Method Get
        $latest = $info.latest.version_number
        if ($latest -and ((VersionParts $Ver) -le (VersionParts $latest))) { return "skipped: $Ver is not newer than published $latest" }
        Say "[$site] published $latest -> $Ver"
    } catch {
        $code = $_.Exception.Response.StatusCode.value__
        if ($code -eq 404) {
            if (-not $Force) { return "skipped: $PkgName is not on $site yet (run with -Force to create it)" }
        } else { return "FAILED version check: $($_.Exception.Message)" }
    }
    if ($DryRun) { return 'ok (dry run, nothing uploaded)' }
    $token = EnvToken $tokenName
    if (-not $token) { return "skipped: $tokenName not set (run tools\Setup.ps1)" }
    $h = @{ Authorization = "Bearer $token" }
    $bytes = [IO.File]::ReadAllBytes($Zip)
    $init = Invoke-RestMethod -Uri "$api/usermedia/initiate-upload/" -Method Post -Headers $h -ContentType 'application/json' `
        -Body (@{ filename = [IO.Path]::GetFileName($Zip); file_size_bytes = $bytes.Length } | ConvertTo-Json)
    $uuid = $init.user_media.uuid
    try {
        $parts = @()
        foreach ($p in $init.upload_urls) {
            $chunk = New-Object byte[] $p.length
            [Array]::Copy($bytes, [long]$p.offset, $chunk, 0, [long]$p.length)
            $r = Invoke-WebRequest -Uri $p.url -Method Put -Body $chunk -UseBasicParsing
            $parts += @{ ETag = [string]($r.Headers['ETag']); PartNumber = $p.part_number }
        }
        Invoke-RestMethod -Uri "$api/usermedia/$uuid/finish-upload/" -Method Post -Headers $h -ContentType 'application/json' `
            -Body (@{ parts = $parts } | ConvertTo-Json -Depth 5) | Out-Null
        $body = @{
            author_name          = $team
            categories           = @()
            communities          = @($Config.community)
            community_categories = @{ $Config.community = @($categories) }
            has_nsfw_content     = $false
            upload_uuid          = $uuid
        }
        Invoke-RestMethod -Uri "$api/submission/submit/" -Method Post -Headers $h -ContentType 'application/json' `
            -Body ($body | ConvertTo-Json -Depth 5) | Out-Null
        return 'published'
    } catch {
        try { Invoke-RestMethod -Uri "$api/usermedia/$uuid/abort-upload/" -Method Post -Headers $h -ContentType 'application/json' -Body '{}' | Out-Null } catch {}
        return "FAILED: $($_.Exception.Message) $($_.ErrorDetails.Message)"
    }
}

# ---------- Nexus Mods (official v3 upload API, same flow as Nexus-Mods/upload-action) ----------
function Publish-Nexus {
    if (-not $ModCfg.nexus_mod_id) { return 'skipped: no nexus_mod_id in tools\mods.json' }
    $key = EnvToken 'NEXUSMODS_API_KEY'
    if (-not $key) { return 'skipped: NEXUSMODS_API_KEY not set (run tools\Setup.ps1)' }
    $api = 'https://api.nexusmods.com/v3'
    $h = @{ apikey = $key; 'User-Agent' = 'WireMods-Publish' }
    try {
        $modInfo = Invoke-RestMethod -Uri "$api/games/$($Config.community)/mods/$($ModCfg.nexus_mod_id)" -Headers $h
        $fileId = $ModCfg.nexus_file_id
        if (-not $fileId) {
            $files = (Invoke-RestMethod -Uri "$api/mods/$($modInfo.data.id)/files" -Headers $h).data.mod_files | Where-Object { $_.is_active }
            if (@($files).Count -ne 1) { return "skipped: mod has $(@($files).Count) active files; set nexus_file_id in tools\mods.json" }
            $fileId = @($files)[0].id
        }
        if ($DryRun) { return "ok (dry run): would add version $Ver to file $fileId" }
        $size = (Get-Item $Zip).Length
        $up = (Invoke-RestMethod -Uri "$api/uploads/multipart" -Method Post -Headers $h -ContentType 'application/json' `
                -Body (@{ filename = [IO.Path]::GetFileName($Zip); size_bytes = "$size" } | ConvertTo-Json)).data
        $bytes = [IO.File]::ReadAllBytes($Zip); $parts = @()
        for ($i = 0; $i -lt $up.part_presigned_urls.Count; $i++) {
            $off = [long]$i * [long]$up.part_size_bytes; $len = [Math]::Min([long]$up.part_size_bytes, $bytes.Length - $off)
            $chunk = New-Object byte[] $len; [Array]::Copy($bytes, $off, $chunk, 0, $len)
            $r = Invoke-WebRequest -Uri $up.part_presigned_urls[$i] -Method Put -Body $chunk -ContentType 'application/octet-stream' -UseBasicParsing
            $parts += "  <Part>`n    <PartNumber>$($i + 1)</PartNumber>`n    <ETag>$(([string]$r.Headers['ETag']).Trim('"'))</ETag>`n  </Part>"
        }
        Invoke-WebRequest -Uri $up.complete_presigned_url -Method Post -ContentType 'application/xml' -UseBasicParsing `
            -Body "<CompleteMultipartUpload>`n$($parts -join "`n")`n</CompleteMultipartUpload>" | Out-Null
        Invoke-RestMethod -Uri "$api/uploads/$($up.id)/finalise" -Method Post -Headers $h -ContentType 'application/json' | Out-Null
        $state = ''; $delay = 2
        for ($a = 0; $a -lt 60 -and $state -ne 'available'; $a++) {
            Start-Sleep -Seconds $delay; $delay = [Math]::Min($delay * 1.5, 30)
            $state = (Invoke-RestMethod -Uri "$api/uploads/$($up.id)" -Headers $h).data.state
        }
        if ($state -ne 'available') { return 'FAILED: Nexus did not finish processing the upload in time' }
        $body = @{
            upload_id = $up.id; name = $(if ($modInfo.data.name) { $modInfo.data.name } else { $Manifest.name }); version = $Ver
            file_category = 'main'; archive_existing_file = $true; update_mod_version = $true
        }
        Invoke-RestMethod -Uri "$api/mod-files/$fileId/versions" -Method Post -Headers $h -ContentType 'application/json' `
            -Body ($body | ConvertTo-Json) | Out-Null
        Invoke-RestMethod -Uri "$api/mods/$($modInfo.data.id)/changelogs" -Method Post -Headers $h -ContentType 'application/json' `
            -Body (@{ version = $Ver; changelog = $Changelog } | ConvertTo-Json) | Out-Null
        return 'published'
    } catch { return "FAILED: $($_.Exception.Message) $($_.ErrorDetails.Message)" }
}

# ---------- GitHub (commit, tag, release with the zip) ----------
function Publish-GitHub {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) { return 'skipped: git not installed (run tools\Setup.ps1)' }
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { return 'skipped: GitHub CLI not installed (run tools\Setup.ps1)' }
    Push-Location $Root
    try {
        $tag = "$Mod-v$Ver"
        if ((& git tag --list $tag)) { return "skipped: tag $tag already exists" }
        if ($DryRun) { return "ok (dry run): would commit, tag $tag and create a GitHub release" }
        & git add -A "$Mod" tools | Out-Null
        & git diff --cached --quiet; if ($LASTEXITCODE -ne 0) { & git commit -q -m "$Mod $Ver" | Out-Null }
        & git tag $tag; & git push -q origin HEAD; & git push -q origin $tag
        $notes = Join-Path $env:TEMP "wire-notes-$PkgName.md"; Set-Content -Path $notes -Value $Changelog -Encoding UTF8
        & gh release create $tag $Zip --title "$($Manifest.name) $Ver" --notes-file $notes | Out-Null
        if ($LASTEXITCODE -ne 0) { return 'FAILED: gh release create' }
        return "published (release $tag)"
    } catch { return "FAILED: $($_.Exception.Message)" }
    finally { Pop-Location }
}

foreach ($s in $Sites) {
    switch ($s.ToLower()) {
        'thunderstore' { $Results[$s] = if ($ModCfg.thunderstore) { Publish-TsLike 'thunderstore' 'https://thunderstore.io/api/experimental' $Config.thunderstore_namespace 'THUNDERSTORE_TOKEN' $ModCfg.thunderstore_categories } else { 'skipped: not a Thunderstore mod (tools\mods.json)' } }
        'hexium'       { $Results[$s] = if ($ModCfg.hexium) { Publish-TsLike 'hexium' 'https://hexium.gg/api/experimental' $Config.hexium_team 'HEXIUM_TOKEN' @() } else { 'skipped: hexium=false in tools\mods.json' } }
        'nexus'        { $Results[$s] = Publish-Nexus }
        'github'       { $Results[$s] = Publish-GitHub }
        default        { $Results[$s] = 'unknown site' }
    }
}

Say ''
Say "Summary for $PkgName $Ver$(if ($DryRun) { ' (DRY RUN)' })" 'Cyan'
foreach ($k in $Results.Keys) {
    $v = $Results[$k]; $c = if ($v -like 'FAILED*') { 'Red' } elseif ($v -like 'skipped*') { 'Yellow' } else { 'Green' }
    Say ("  {0,-13} {1}" -f $k, $v) $c
}
if ($Results.Values | Where-Object { $_ -like 'FAILED*' }) { exit 1 }
