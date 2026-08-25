# ==============================================================================
#  Satisfying - open a local Claude Code session on this project
#
#  What it does: finds your copy, pulls the branch, makes sure the permission
#  file is set up so you are not answering a prompt every ten seconds, writes
#  the handover brief, and starts Claude Code in the project folder.
#
#  Run it with:
#    irm "https://raw.githubusercontent.com/wilflet1/Satisfying/refs/heads/claude/fps-multiplayer-movement-8a2ve0/tools/Start-ClaudeSession.ps1" | iex
#
#  Or, from a copy you already have:
#    powershell -ExecutionPolicy Bypass -File tools\Start-ClaudeSession.ps1
#
#  Switches:
#    -Yolo        never ask permission for anything. Faster, and it means the
#                 session can run any command on your machine without checking.
#    -NoPull      work with what is on disk instead of fetching.
#    -BriefOnly   write the handover brief and stop, so you can paste it yourself.
# ==============================================================================

[CmdletBinding()]
param(
    [switch]$Yolo,
    [switch]$NoPull,
    [switch]$BriefOnly,
    [string]$Path
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$Branch = 'claude/fps-multiplayer-movement-8a2ve0'

function Say {
    param([string]$Text, [string]$Colour = 'Gray')
    Write-Host ("  " + $Text) -ForegroundColor $Colour
}

# git writes ordinary progress to stderr, and PowerShell turns that into a
# terminating error while ErrorActionPreference is Stop. Judge it by exit code.
function Invoke-Git {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $output = & git @Arguments 2>&1
    $code = $LASTEXITCODE
    $ErrorActionPreference = $previous

    return [pscustomobject]@{ Output = $output; ExitCode = $code; Text = ($output -join "`n") }
}

Write-Host ''
Write-Host '  Satisfying - local Claude session' -ForegroundColor Cyan
Write-Host '  --------------------------------------------------------------'

# ------------------------------------------------------------------ 1. find the project
$candidates = @()
if ($Path) { $candidates += $Path }
$candidates += @($PSScriptRoot, (Get-Location).Path)

$profileRoot = $env:USERPROFILE
if ($profileRoot) {
    $candidates += @(
        (Join-Path $profileRoot 'Satisfying\new'),
        (Join-Path $profileRoot 'Satisfying\Satisfying'),
        (Join-Path $profileRoot 'Satisfying'),
        (Join-Path $profileRoot 'Documents\Satisfying'),
        (Join-Path $profileRoot 'source\repos\Satisfying')
    )
}

$root = $null
foreach ($candidate in $candidates) {
    if (-not $candidate) { continue }

    # A script run from tools\ is one level down from the project.
    $walk = $candidate
    for ($i = 0; $i -lt 3 -and $walk; $i++) {
        if (Test-Path (Join-Path $walk 'Assets\_Project')) { $root = $walk; break }
        $walk = Split-Path $walk -Parent
    }
    if ($root) { break }
}

if (-not $root) {
    Write-Host ''
    Say 'Could not find the project.' 'Red'
    Say 'Run Install-Satisfying.ps1 first, or pass the folder:' 'Yellow'
    Say '  .\Start-ClaudeSession.ps1 -Path C:\path\to\Satisfying' 'Yellow'
    Write-Host ''
    return
}

$root = (Resolve-Path $root).Path
Say ("project   " + $root) 'DarkGray'

# ------------------------------------------------------------------ 2. pull
if (-not $NoPull -and (Get-Command git -ErrorAction SilentlyContinue) -and (Test-Path (Join-Path $root '.git'))) {
    Push-Location $root
    try {
        $dirty = (Invoke-Git status --porcelain).Text.Trim()
        if ($dirty) {
            Say 'you have local changes - stashing them so the pull is clean' 'Yellow'
            [void](Invoke-Git stash push -u -m 'Start-ClaudeSession')
            Say 'get them back later with:  git stash pop' 'DarkGray'
        }

        [void](Invoke-Git fetch origin $Branch)
        $checkout = Invoke-Git checkout -B $Branch ("origin/" + $Branch)
        if ($checkout.ExitCode -ne 0) {
            Say 'could not switch branch - carrying on with what is on disk' 'Yellow'
        } else {
            $head = (Invoke-Git log -1 --pretty=format:'%h  %s').Text
            Say ("head      " + $head) 'DarkGray'
        }
    } finally {
        Pop-Location
    }
} elseif ($NoPull) {
    Say 'skipping the pull' 'DarkGray'
}

# ------------------------------------------------------------------ 3. permissions
# The project settings file is committed and shared. Anything machine-specific
# goes in settings.local.json, which git ignores - that is the one to widen.
$claudeDir = Join-Path $root '.claude'
if (-not (Test-Path $claudeDir)) { [void](New-Item -ItemType Directory -Path $claudeDir) }

$localSettings = Join-Path $claudeDir 'settings.local.json'
if (-not (Test-Path $localSettings)) {
    $allow = @(
        'Read', 'Edit', 'Write', 'Glob', 'Grep',
        'Bash(dotnet *)', 'Bash(git *)',
        'Bash(Unity *)', 'Bash(*Unity.exe *)', 'Bash(*unity.exe *)',
        'Bash(adb *)', 'Bash(dir *)', 'Bash(ls *)', 'Bash(type *)', 'Bash(cat *)',
        'Bash(findstr *)', 'Bash(Get-Content *)', 'Bash(Select-String *)'
    )
    $settings = [ordered]@{ permissions = [ordered]@{ allow = $allow; deny = @() } }
    $settings | ConvertTo-Json -Depth 6 | Set-Content -Path $localSettings -Encoding UTF8
    Say 'wrote .claude\settings.local.json (git ignores it)' 'DarkGray'
} else {
    Say 'settings.local.json already there - leaving it alone' 'DarkGray'
}

# ------------------------------------------------------------------ 4. the brief
$briefPath = Join-Path $root 'HANDOVER.md'
if (-not (Test-Path $briefPath)) {
    Say 'HANDOVER.md is not in this checkout - pull again, it is committed' 'Yellow'
} else {
    Say ("brief     " + $briefPath) 'DarkGray'
}

if ($BriefOnly) {
    Write-Host ''
    Say 'Brief is at HANDOVER.md. Open a session yourself and say:' 'Green'
    Say '  read HANDOVER.md and work through it' 'White'
    Write-Host ''
    return
}

# ------------------------------------------------------------------ 5. launch
$claude = Get-Command claude -ErrorAction SilentlyContinue
if (-not $claude) {
    Write-Host ''
    Say 'Claude Code is not installed on this machine.' 'Red'
    Say 'Install it with one of these, then run this script again:' 'Yellow'
    Say '  irm https://claude.ai/install.ps1 | iex' 'White'
    Say '  npm install -g @anthropic-ai/claude-code' 'White'
    Write-Host ''
    return
}

Set-Location $root

$opening = 'Read HANDOVER.md and work through it in order. Start with step 1.'
$claudeArgs = @()
if ($Yolo) {
    $claudeArgs += '--dangerously-skip-permissions'
    Say 'permission prompts are OFF for this session' 'Yellow'
}
$claudeArgs += $opening

Write-Host ''
Say 'Starting Claude Code. It will read HANDOVER.md and begin.' 'Green'
Say 'If the session ends and you want it back:  claude --continue' 'DarkGray'
Write-Host ''

& claude @claudeArgs
