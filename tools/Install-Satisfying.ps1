# ==============================================================================
#  Satisfying - one shot setup for Windows
#
#  Downloads the project and registers it with Unity Hub, then opens it.
#  Paste the whole thing into a normal PowerShell window - no admin needed.
# ==============================================================================

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# ---- change these if you like ------------------------------------------------
$Destination    = Join-Path $env:USERPROFILE 'Satisfying'
$UnityVersion   = '6000.3.17f1'
$OpenAfterwards = $true
# ------------------------------------------------------------------------------

$Owner  = 'wilflet1'
$Repo   = 'Satisfying'
$Branch = 'claude/fps-multiplayer-movement-8a2ve0'

Write-Host ''
Write-Host '  Satisfying - setup' -ForegroundColor Cyan
Write-Host '  --------------------------------------------------------------'

# ------------------------------------------------------------------ 1. get the code
$hasGit  = $null -ne (Get-Command git -ErrorAction SilentlyContinue)
$isThere = Test-Path (Join-Path $Destination 'ProjectSettings')

if ($isThere) {
    Write-Host "  Found an existing copy at $Destination"
    if ($hasGit -and (Test-Path (Join-Path $Destination '.git'))) {
        Write-Host '  Updating it from GitHub...'
        $out = & git -C $Destination fetch origin $Branch 2>&1
        $out = & git -C $Destination checkout $Branch 2>&1
        $out = & git -C $Destination pull --ff-only origin $Branch 2>&1
        if ($LASTEXITCODE -ne 0) { $out | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkYellow } }
    } else {
        Write-Host '  Not a git clone, so leaving it as it is.'
        Write-Host '  (Delete the folder and run this again for a clean copy.)'
    }
}
elseif ($hasGit) {
    Write-Host "  Cloning into $Destination ..."
    $out = & git clone --branch $Branch --single-branch "https://github.com/$Owner/$Repo.git" $Destination 2>&1
    if ($LASTEXITCODE -ne 0) {
        $out | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
        throw 'git clone failed.'
    }
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
    New-Item -ItemType Directory -Force -Path (Split-Path $Destination -Parent) | Out-Null
    Move-Item -Path $marker.Parent.FullName -Destination $Destination

    Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
    Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue
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

$hubWasRunning = $false
$hubProc = Get-Process -Name 'Unity Hub' -ErrorAction SilentlyContinue
if ($hubProc) {
    $hubWasRunning = $true
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

# Clone the shape of an entry the Hub wrote itself, so the schema always matches
# whatever Hub version is installed.
$template = $hub.data.PSObject.Properties | Select-Object -First 1
$entryKey = $projectPath
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
