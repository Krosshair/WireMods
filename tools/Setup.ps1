<#
  One-time setup for publishing Wire's mods. Safe to run again: it only does what is missing.
    - Installs Git and GitHub CLI with winget if they are missing.
    - Runs "gh auth login" if you are not logged in (you log in yourself in the browser).
    - Asks for your Thunderstore, Hexium and Nexus tokens and stores them as your user
      environment variables (THUNDERSTORE_TOKEN, HEXIUM_TOKEN, NEXUSMODS_API_KEY).
      They never leave this PC except to the site they belong to.
    - Creates the Git repository and the GitHub repo "WireMods" and pushes it.
  Options: -ResetTokens to re-enter tokens, -Private to make the GitHub repo private.
#>
param([switch]$ResetTokens, [switch]$Private, [string]$RepoName = 'WireMods')
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
function Say($t, $c = 'Gray') { Write-Host $t -ForegroundColor $c }
function Refresh-Path { $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User') }

# 1. Tools
foreach ($t in @(@{cmd = 'git'; id = 'Git.Git' }, @{cmd = 'gh'; id = 'GitHub.cli' })) {
    if (-not (Get-Command $t.cmd -ErrorAction SilentlyContinue)) {
        Say "Installing $($t.id)..." 'Cyan'
        winget install --id $t.id -e --silent --accept-package-agreements --accept-source-agreements
        Refresh-Path
    }
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Say "Installing .NET SDK 8 (needed to build the mods)..." 'Cyan'
    winget install --id Microsoft.DotNet.SDK.8 -e --silent --accept-package-agreements --accept-source-agreements
    Refresh-Path
}

# 2. GitHub login (interactive, done by you)
& gh auth status 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) {
    Say "Log in to GitHub (a browser window opens)..." 'Cyan'
    & gh auth login --hostname github.com --git-protocol https --web
}
& gh auth setup-git | Out-Null

# 3. Tokens
$tokens = @(
    @{ name = 'THUNDERSTORE_TOKEN'; where = 'thunderstore.io > your team "Wire" > Service Accounts > Add service account, copy the token' },
    @{ name = 'HEXIUM_TOKEN';       where = 'hexium.gg > your team "Wire" > settings > API token (upload/publish permission)' },
    @{ name = 'NEXUSMODS_API_KEY';  where = 'https://www.nexusmods.com/settings/api-keys (personal API key)' }
)
foreach ($t in $tokens) {
    $existing = [Environment]::GetEnvironmentVariable($t.name, 'User')
    if ($existing -and -not $ResetTokens) { Say "$($t.name) already set." 'Green'; continue }
    Say "$($t.name): $($t.where)" 'Cyan'
    $sec = Read-Host "Paste $($t.name) (leave empty to skip)" -AsSecureString
    $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec))
    if ($plain) { [Environment]::SetEnvironmentVariable($t.name, $plain.Trim(), 'User'); Say "Saved $($t.name)." 'Green' }
}

# 4. Repository
Push-Location $Root
try {
    if (-not (Test-Path '.git')) { & git init -b main | Out-Null }
    if (-not (& git config user.name)) {
        $login = & gh api user --jq .login
        $id = & gh api user --jq .id
        & git config user.name $login
        & git config user.email "$id+$login@users.noreply.github.com"
    }
    & git add -A
    & git diff --cached --quiet
    if ($LASTEXITCODE -ne 0) {
        & git commit -q -m "Wire's mods: sources, release packages and publishing tools" `
            -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>" `
            -m "Claude-Session: https://claude.ai/code/session_01TEn8RJjHV7z1oSvo2tp1yt"
    }
    $remote = & git remote 2>$null
    if (-not $remote) {
        $vis = if ($Private) { '--private' } else { '--public' }
        & gh repo create $RepoName $vis --source . --push --description "Wire's Valheim mods"
    } else {
        & git push -u origin main
    }
    Say "Repository: $(& gh repo view --json url --jq .url)" 'Green'
} finally { Pop-Location }

Say ''
Say 'Setup done. Publish a mod with:  publish WiresRecycler   (add -DryRun to test first)' 'Green'
Say 'Open a NEW terminal first so the saved tokens are visible.' 'Yellow'
