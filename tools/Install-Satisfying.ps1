# ==============================================================================
#  Satisfying - one shot setup for Windows
#
#  Downloads the project and registers it with Unity Hub, then opens it.
#  Run it with:
#    irm "https://raw.githubusercontent.com/wilflet1/Satisfying/refs/heads/claude/fps-multiplayer-movement-8a2ve0/tools/Install-Satisfying.ps1" | iex
# ==============================================================================

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# ---- change these if you like ------------------------------------------------
$Destination    = Join-Path $env:USERPROFILE 'Satisfying\new'
$UnityVersion   = '6000.3.17f1'
$OpenAfterwards = $true
# ------------------------------------------------------------------------------

$Owner  = 'wilflet1'
$Repo   = 'Satisfying'
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

    return [pscustomobject]@{ Output = $output; ExitCode = $code }
}

function Write-GitOutput {
    param($Result, [string]$Colour = 'DarkGray')
    foreach ($line in $Result.Output) { Write-Host ("    " + $line) -ForegroundColor $Colour }
}

Write-Host ''
Write-Host '  Satisfying - setup' -ForegroundColor Cyan
Write-Host '  --------------------------------------------------------------'
Write-Host "  Target folder: $Destination"

# ------------------------------------------------------------------ 1. get the code
$hasGit       = $null -ne (Get-Command git -ErrorAction SilentlyContinue)
$hasProject   = Test-Path (Join-Path $Destination 'ProjectSettings')
$hasGitFolder = Test-Path (Join-Path $Destination '.git')

# Anything in the folder that is not a previous copy of this project is yours, and
# this script will not touch it.
$foreignFiles = @()
if ((Test-Path $Destination) -and -not $hasProject) {
    $foreignFiles = @(Get-ChildItem -Force -Path $Destination | Where-Object { $_.Name -ne '.git' })
}

if ($hasProject) {
    Write-Host '  Found an existing copy - updating it.'
    if ($hasGitFolder) {
        $r = Invoke-Git fetch origin $Branch
        if ($r.ExitCode -ne 0) { Write-GitOutput $r 'Yellow' }
        $r = Invoke-Git checkout $Branch
        if ($r.ExitCode -ne 0) { Write-GitOutput $r 'Yellow' }
        $r = Invoke-Git pull --ff-only origin $Branch
        if ($r.ExitCode -ne 0) { Write-GitOutput $r 'Yellow' }
        Write-Host '  Up to date.' -ForegroundColor Green
    } else {
        Write-Host '  Not a git clone, so leaving it alone. Delete it for a clean copy.'
    }
}
elseif ($foreignFiles.Count -gt 0) {
    Write-Host ''
    Write-Host "  $Destination already has files in it that are not this project:" -ForegroundColor Yellow
    $foreignFiles | Select-Object -First 8 | ForEach-Object { Write-Host ("    " + $_.Name) -ForegroundColor Yellow }
    Write-Host ''
    throw "Refusing to write over your files. Point `$Destination somewhere empty and run this again."
}
else {
    # A clone that died halfway leaves a .git with no working tree: start over.
    if ($hasGitFolder) {
        Write-Host '  Clearing out an unfinished download...'
        Remove-Item $Destination -Recurse -Force
    }

    if ($hasGit) {
        Write-Host '  Cloning from GitHub...'
        $parent = Split-Path $Destination -Parent
        if ($parent -and -not (Test-Path $parent)) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }

        $r = Invoke-Git clone --branch $Branch --single-branch "https://github.com/$Owner/$Repo.git" $Destination
        if ($r.ExitCode -ne 0) {
            Write-GitOutput $r 'Red'
            throw "git clone failed with exit code $($r.ExitCode)."
        }
        Write-GitOutput $r
    }
    else {
        Write-Host '  git is not installed - downloading the zip instead...'
        $zipPath = Join-Path $env:TEMP 'satisfying.zip'
        $tempDir = Join-Path $env:TEMP ('satisfying-' + [guid]::NewGuid().ToString('N'))
        $zipUrl  = "https://codeload.github.com/$Owner/$Repo/zip/refs/heads/$Branch"

        Invoke-WebRequest -UseBasicParsing -Uri $zipUrl -OutFile $zipPath
        Expand-Archive -Path $zipPath -DestinationPath $tempDir -Force

        # The zip nests everything one folder deep, so find the real project root.
        $marker = Get-ChildItem -Path $tempDir -Recurse -Directory -Filter 'ProjectSettings' | Select-Object -First 1
        if (-not $marker) { throw 'That download did not contain a Unity project.' }

        if (Test-Path $Destination) { Remove-Item $Destination -Recurse -Force }
        $parent = Split-Path $Destination -Parent
        if ($parent -and -not (Test-Path $parent)) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
        Move-Item -Path $marker.Parent.FullName -Destination $Destination

        Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
        Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

foreach ($needed in @('Assets', 'Packages', 'ProjectSettings')) {
    if (-not (Test-Path (Join-Path $Destination $needed))) {
        throw "$Destination is missing $needed - that is not a Unity project folder."
    }
}
$projectPath = (Resolve-Path $Destination).Path
Write-Host "  Project is at $projectPath" -ForegroundColor Green

# ------------------------------------------------------------------ 2. find the editor
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
    Write-Host "  Unity $UnityVersion is not installed - looking for another 6000.x ..." -ForegroundColor DarkYellow
    foreach ($root in $editorRoots) {
        if (-not (Test-Path $root)) { continue }
        $alt = Get-ChildItem $root -Directory |
               Where-Object { $_.Name -like '6000.*' } |
               Sort-Object Name -Descending |
               Select-Object -First 1
        if (-not $alt) { continue }
        $candidate = Join-Path $alt.FullName 'Editor\Unity.exe'
        if (Test-Path $candidate) {
            $unityExe = $candidate
            $UnityVersion = $alt.Name
            Write-Host "  Using $UnityVersion instead." -ForegroundColor DarkYellow
            break
        }
    }
}
if ($unityExe) { Write-Host "  Editor: $unityExe" -ForegroundColor Green }
else { Write-Host '  No Unity 6000.x found. Install one from the Hub, then re-run this.' -ForegroundColor Yellow }

# ------------------------------------------------------------------ 3. tell the Hub about it
$hubDir  = Join-Path $env:APPDATA 'UnityHub'
$hubJson = Join-Path $hubDir 'projects-v1.json'
$hubExe  = 'C:\Program Files\Unity Hub\Unity Hub.exe'

$hubProc = Get-Process -Name 'Unity Hub' -ErrorAction SilentlyContinue
if ($hubProc) {
    Write-Host '  Closing Unity Hub so its project list can be edited...'
    $hubProc | Stop-Process -Force
    Start-Sleep -Seconds 2
}

if (Test-Path $hubJson) {
    Copy-Item $hubJson "$hubJson.backup" -Force
    $hub = Get-Content $hubJson -Raw | ConvertFrom-Json
} else {
    New-Item -ItemType Directory -Force -Path $hubDir | Out-Null
    $hub = [pscustomobject]@{ schema_version = 'v1'; data = [pscustomobject]@{} }
}
if (-not $hub.PSObject.Properties['data']) {
    $hub | Add-Member -NotePropertyName 'data' -NotePropertyValue ([pscustomobject]@{}) -Force
}

# Clone the shape of an entry the Hub wrote itself, so this matches whatever Hub
# version is installed rather than assuming a schema.
$template   = $hub.data.PSObject.Properties | Select-Object -First 1
$entryKey   = $projectPath
$folderPath = Split-Path $projectPath -Parent

if ($template) {
    $entry = $template.Value | ConvertTo-Json -Depth 10 | ConvertFrom-Json
    if ([string]$template.Value.path -match '/') {
        $entryKey   = $projectPath.Replace('\', '/')
        $folderPath = $folderPath.Replace('\', '/')
    }
} else {
    $entry = [pscustomobject]@{
        title                = ''
        lastModified         = 0
        isCustomEditor       = $false
        path                 = ''
        containingFolderPath = ''
        version              = ''
        architecture         = 'x86_64'
        isFavorite           = $false
    }
}

$entry | Add-Member -NotePropertyName 'title'                -NotePropertyValue 'Satisfying' -Force
$entry | Add-Member -NotePropertyName 'path'                 -NotePropertyValue $entryKey -Force
$entry | Add-Member -NotePropertyName 'containingFolderPath' -NotePropertyValue $folderPath -Force
$entry | Add-Member -NotePropertyName 'version'              -NotePropertyValue $UnityVersion -Force
$entry | Add-Member -NotePropertyName 'isFavorite'           -NotePropertyValue $false -Force
$entry | Add-Member -NotePropertyName 'isCustomEditor'       -NotePropertyValue $false -Force
$entry | Add-Member -NotePropertyName 'lastModified' `
                    -NotePropertyValue ([int64][DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()) -Force

$hub.data | Add-Member -NotePropertyName $entryKey -NotePropertyValue $entry -Force

# No BOM: the Hub parses this with Node, which chokes on one.
$json = $hub | ConvertTo-Json -Depth 12
[System.IO.File]::WriteAllText($hubJson, $json, (New-Object System.Text.UTF8Encoding($false)))
Write-Host '  Added to the Unity Hub project list.' -ForegroundColor Green

# ------------------------------------------------------------------ 4. open it
if (Test-Path $hubExe) { Start-Process -FilePath $hubExe | Out-Null }

if ($OpenAfterwards -and $unityExe) {
    Write-Host '  Opening the project (first import takes a few minutes)...' -ForegroundColor Cyan
    Start-Process -FilePath $unityExe -ArgumentList @('-projectPath', $projectPath) | Out-Null
}

Write-Host ''
Write-Host '  Done.' -ForegroundColor Cyan
Write-Host '  --------------------------------------------------------------'
Write-Host '  The Hierarchy looks almost empty before you press Play - that is'
Write-Host '  correct. The arena, weapons, UI and audio are all built in code.'
Write-Host ''
Write-Host '  Press Play, then: name -> pick a map -> host a duel.'
Write-Host '  Esc for the menu, and "add a training bot" to have something to shoot.'
Write-Host '  Satisfying > Playtest > Launch a second player  for a real 1v1.'
Write-Host ''
Write-Host '  WASD, Shift sprint, Q/E lean, Alt+Q/Alt+E slow lean,'
Write-Host '  Alt+A/Alt+D side step, sprint+tap C to slide, Space to vault,'
Write-Host '  V blind fire, wheel speed dial, F1 tuning panel.'
Write-Host ''
