# ==============================================================================
#  Satisfying - pull the latest and open it
#
#  For a copy you already have. If you have never downloaded it, run
#  Install-Satisfying.ps1 instead.
#
#  Run it with:
#    irm "https://raw.githubusercontent.com/wilflet1/Satisfying/refs/heads/claude/fps-multiplayer-movement-8a2ve0/tools/Update-Satisfying.ps1" | iex
# ==============================================================================

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# ---- change these if you like ------------------------------------------------
$Destination    = Join-Path $env:USERPROFILE 'Satisfying\new'
$UnityVersion   = '6000.3.17f1'
$OpenAfterwards = $true
# ------------------------------------------------------------------------------

$Branch = 'claude/fps-multiplayer-movement-8a2ve0'

# git writes ordinary progress to stderr, and PowerShell turns that into a
# terminating error when ErrorActionPreference is Stop. Run it with that relaxed
# and judge success by the exit code instead, which is what it is for.
function Invoke-Git {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $output = & git @Arguments 2>&1
    $code = $LASTEXITCODE
    $ErrorActionPreference = $previous

    return [pscustomobject]@{ Output = $output; ExitCode = $code; Text = ($output -join "`n") }
}

function Write-GitOutput {
    param($Result, [string]$Colour = 'DarkGray')
    foreach ($line in $Result.Output) { Write-Host ("    " + $line) -ForegroundColor $Colour }
}

Write-Host ''
Write-Host '  Satisfying - update' -ForegroundColor Cyan
Write-Host '  --------------------------------------------------------------'

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'git is not installed. Run Install-Satisfying.ps1 instead - it can fall back to a zip.'
}

# ------------------------------------------------------------------ 1. find the copy you have
$candidates = @($Destination)
$profileRoot = $env:USERPROFILE
if ($profileRoot) {
    $candidates += @(
        (Join-Path $profileRoot 'Satisfying'),
        (Join-Path $profileRoot 'Satisfying\Satisfying'),
        (Join-Path $profileRoot 'Satisfying\new'),
        (Join-Path $profileRoot 'Documents\Satisfying'),
        (Join-Path $profileRoot 'source\repos\Satisfying')
    )
}
$candidates = $candidates | Where-Object { $_ } | Select-Object -Unique

$projectPath = $null
foreach ($candidate in $candidates) {
    if (-not $candidate) { continue }
    if ((Test-Path (Join-Path $candidate 'ProjectSettings')) -and (Test-Path (Join-Path $candidate '.git'))) {
        $projectPath = (Resolve-Path $candidate).Path
        break
    }
}

if (-not $projectPath) {
    # A copy made from the zip has no .git, so there is nothing to pull into it.
    $zipCopy = $candidates | Where-Object { Test-Path (Join-Path $_ 'ProjectSettings') } | Select-Object -First 1
    Write-Host ''
    if ($zipCopy) {
        Write-Host "  $zipCopy is a copy of the project, but not a git clone -" -ForegroundColor Yellow
        Write-Host '  it came from the zip, so there is no history to pull into.' -ForegroundColor Yellow
        Write-Host '  Delete that folder and run Install-Satisfying.ps1 for a fresh copy.' -ForegroundColor Yellow
        Write-Host ''
        throw 'Nothing to update.'
    }
    Write-Host '  Could not find a clone of the project in any of these:' -ForegroundColor Yellow
    $candidates | ForEach-Object { Write-Host ("    " + $_) -ForegroundColor Yellow }
    Write-Host ''
    throw "No copy found. Set `$Destination at the top of this script, or run Install-Satisfying.ps1 for a fresh download."
}

Write-Host "  Project: $projectPath"
Push-Location $projectPath

try {
    # ------------------------------------------------------------------ 2. is the editor holding it
    if (Test-Path (Join-Path $projectPath 'Temp\UnityLockfile')) {
        $running = Get-Process -Name 'Unity' -ErrorAction SilentlyContinue
        if ($running) {
            Write-Host ''
            Write-Host '  Unity has this project open. Close it first - pulling script changes' -ForegroundColor Yellow
            Write-Host '  underneath a running editor is how you get a half-compiled mess.' -ForegroundColor Yellow
            Write-Host ''
            throw 'Close Unity and run this again.'
        }
    }

    # ------------------------------------------------------------------ 3. protect anything you changed
    $status = Invoke-Git status --porcelain
    $dirty = @($status.Output | Where-Object { $_ -and $_.ToString().Trim().Length -gt 0 })

    if ($dirty.Count -gt 0) {
        Write-Host ''
        Write-Host "  You have $($dirty.Count) local change(s):" -ForegroundColor Yellow
        $dirty | Select-Object -First 8 | ForEach-Object { Write-Host ("    " + $_) -ForegroundColor Yellow }
        if ($dirty.Count -gt 8) { Write-Host ("    ... and " + ($dirty.Count - 8) + " more") -ForegroundColor Yellow }
        Write-Host ''
        Write-Host '  Putting them on the git stash so the pull is clean. Nothing is thrown away.'

        $label = 'satisfying-update ' + (Get-Date -Format 'yyyy-MM-dd HH:mm')
        $r = Invoke-Git stash push -u -m $label
        if ($r.ExitCode -ne 0) { Write-GitOutput $r 'Red'; throw 'Could not stash your changes.' }

        Write-Host "  Stashed as: $label" -ForegroundColor Green
        Write-Host '  Get them back later with:  git stash pop' -ForegroundColor DarkGray
    }

    # ------------------------------------------------------------------ 4. pull
    Write-Host '  Fetching...'
    $r = Invoke-Git fetch origin $Branch
    if ($r.ExitCode -ne 0) { Write-GitOutput $r 'Red'; throw "git fetch failed with exit code $($r.ExitCode)." }

    $current = (Invoke-Git rev-parse --abbrev-ref HEAD).Text.Trim()
    if ($current -ne $Branch) {
        Write-Host "  Switching from $current to $Branch"
        $r = Invoke-Git checkout $Branch
        if ($r.ExitCode -ne 0) { Write-GitOutput $r 'Red'; throw "Could not switch to $Branch." }
    }

    $incoming = Invoke-Git log --oneline "HEAD..origin/$Branch"
    $commits = @($incoming.Output | Where-Object { $_ -and $_.ToString().Trim().Length -gt 0 })

    if ($commits.Count -eq 0) {
        Write-Host '  Already up to date.' -ForegroundColor Green
    } else {
        Write-Host ''
        Write-Host "  $($commits.Count) new commit(s):" -ForegroundColor Cyan
        $commits | ForEach-Object { Write-Host ("    " + $_) -ForegroundColor DarkGray }
        Write-Host ''

        $r = Invoke-Git merge --ff-only "origin/$Branch"
        if ($r.ExitCode -ne 0) {
            Write-GitOutput $r 'Yellow'
            Write-Host ''
            Write-Host '  Your copy has commits of its own, so it cannot fast forward.' -ForegroundColor Yellow
            Write-Host '  To throw yours away and match the branch exactly:' -ForegroundColor Yellow
            Write-Host "      git -C `"$projectPath`" reset --hard origin/$Branch" -ForegroundColor DarkGray
            Write-Host '  To keep them, merge or rebase by hand instead.' -ForegroundColor Yellow
            throw 'Not fast forwardable - nothing was changed.'
        }
        Write-Host '  Updated.' -ForegroundColor Green
    }

    $head = (Invoke-Git log -1 --pretty=format:'%h  %s').Text.Trim()
    Write-Host "  Now at: $head" -ForegroundColor Green
}
finally {
    Pop-Location
}

# ------------------------------------------------------------------ 5. open it
if ($OpenAfterwards) {
    $editorRoots = @('C:\Program Files\Unity\Hub\Editor')
    $secondary = Join-Path $env:APPDATA 'UnityHub\secondaryInstallPath.json'
    if (Test-Path $secondary) {
        try {
            $extra = Get-Content $secondary -Raw | ConvertFrom-Json
            if ($extra) { $editorRoots += [string]$extra }
        } catch { }
    }

    $unityExe = $null
    foreach ($root in $editorRoots) {
        $candidate = Join-Path $root (Join-Path $UnityVersion 'Editor\Unity.exe')
        if (Test-Path $candidate) { $unityExe = $candidate; break }
    }
    if (-not $unityExe) {
        foreach ($root in $editorRoots) {
            if (-not (Test-Path $root)) { continue }
            $alt = Get-ChildItem $root -Directory |
                   Where-Object { $_.Name -like '6000.*' } |
                   Sort-Object Name -Descending |
                   Select-Object -First 1
            if (-not $alt) { continue }
            $candidate = Join-Path $alt.FullName 'Editor\Unity.exe'
            if (Test-Path $candidate) { $unityExe = $candidate; $UnityVersion = $alt.Name; break }
        }
    }

    if ($unityExe) {
        Write-Host "  Opening with $UnityVersion ..." -ForegroundColor Cyan
        Start-Process -FilePath $unityExe -ArgumentList @('-projectPath', $projectPath) | Out-Null
    } else {
        Write-Host '  No Unity 6000.x found - open it from the Hub yourself.' -ForegroundColor Yellow
    }
}

Write-Host ''
Write-Host '  Done.' -ForegroundColor Cyan
Write-Host '  --------------------------------------------------------------'
Write-Host '  New since the last build: G for the gear menu (irons, red dot, holo),'
Write-Host '  F to bash with the stock - it breaks windows - and E to grab and drag'
Write-Host '  the crates. Heavier is slower, for the crate and for you.'
Write-Host ''
Write-Host '  Break a pane and listen: footsteps through it go from muffled to sharp.'
Write-Host ''
