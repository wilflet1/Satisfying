# ==============================================================================
#  Satisfying - why did the port not open?
#
#  Runs the same UPnP search the game runs, from PowerShell. If this finds your
#  router and the game does not, the game or its firewall rule is the problem.
#  If this finds nothing either, UPnP is off on the router and no amount of
#  fiddling in the game will change that - forward the port by hand.
#
#  Run it with:
#    irm "https://raw.githubusercontent.com/wilflet1/Satisfying/refs/heads/claude/fps-multiplayer-movement-8a2ve0/tools/Test-PortForwarding.ps1" | iex
# ==============================================================================

$ErrorActionPreference = 'Continue'
$Port = 7777

function Head($text) { Write-Host ''; Write-Host "  $text" -ForegroundColor Cyan; Write-Host '  --------------------------------------------------------------' }
function Good($text) { Write-Host "  $text" -ForegroundColor Green }
function Bad($text)  { Write-Host "  $text" -ForegroundColor Yellow }
function Info($text) { Write-Host "  $text" -ForegroundColor DarkGray }

Head 'Network'

# The interface that actually carries traffic to the internet, not whichever
# virtual adapter happens to sort first.
# -ErrorAction does not cover a cmdlet that does not exist at all, so everything
# that touches the Windows networking cmdlets is wrapped.
function Quietly([scriptblock]$block) {
    try { & $block } catch { $null }
}

$route = Quietly { Get-NetRoute -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue |
                   Sort-Object RouteMetric, ifMetric | Select-Object -First 1 }

if (-not $route) {
    Bad 'Could not read the routing table. This script needs Windows PowerShell.'
    return
}

$gateway = $route.NextHop
$localIp = (Quietly { Get-NetIPAddress -InterfaceIndex $route.ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue |
                      Where-Object { $_.IPAddress -notlike '169.254.*' } | Select-Object -First 1 }).IPAddress
$adapter = (Quietly { Get-NetAdapter -InterfaceIndex $route.ifIndex -ErrorAction SilentlyContinue }).Name

if (-not $localIp) {
    Bad 'Could not work out this machine''s address on that interface.'
    return
}

Info "adapter   $adapter"
Info "local IP  $localIp"
Info "gateway   $gateway"

$others = @(Quietly { Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
                      Where-Object { $_.IPAddress -ne $localIp -and $_.IPAddress -ne '127.0.0.1' -and $_.IPAddress -notlike '169.254.*' } })
if ($others.Count -gt 0) {
    Info ("other adapters: " + (($others | ForEach-Object { $_.IPAddress }) -join ', '))
    Info 'the game binds to the one above, which is the one that matters'
}

# ---------------------------------------------------------------- UPnP
Head 'UPnP search'

$found = @()
try {
    $socket = New-Object System.Net.Sockets.UdpClient([System.Net.IPEndPoint]::new([System.Net.IPAddress]::Parse($localIp), 0))
    $socket.Client.ReceiveTimeout = 400

    $search = "M-SEARCH * HTTP/1.1`r`nHOST: 239.255.255.250:1900`r`nMAN: `"ssdp:discover`"`r`nMX: 1`r`nST: ssdp:all`r`n`r`n"
    $bytes = [Text.Encoding]::ASCII.GetBytes($search)

    $multicast = [System.Net.IPEndPoint]::new([System.Net.IPAddress]::Parse('239.255.255.250'), 1900)
    $direct    = [System.Net.IPEndPoint]::new([System.Net.IPAddress]::Parse($gateway), 1900)

    [void]$socket.Send($bytes, $bytes.Length, $multicast)
    [void]$socket.Send($bytes, $bytes.Length, $direct)
    Info 'searching for 4 seconds...'

    $deadline = (Get-Date).AddSeconds(4)
    while ((Get-Date) -lt $deadline) {
        try {
            $from = [System.Net.IPEndPoint]::new([System.Net.IPAddress]::Any, 0)
            $data = $socket.Receive([ref]$from)
            $text = [Text.Encoding]::ASCII.GetString($data)
            $st = ($text -split "`r?`n" | Where-Object { $_ -match '^(ST|NT):' } | Select-Object -First 1)
            $found += [pscustomobject]@{ From = $from.Address.ToString(); What = $st }
        } catch { }   # nothing this moment; keep listening until the window closes
    }
    $socket.Close()
} catch {
    Bad "Could not open a socket: $($_.Exception.Message)"
}

$devices = $found | Sort-Object From -Unique
if ($devices.Count -eq 0) {
    Bad 'Nothing on the network answered at all.'
    Bad 'That is either UPnP switched off on the router, or Windows blocking the reply.'
} else {
    Good "$($devices.Count) device(s) answered:"
    $devices | ForEach-Object { Info ("  " + $_.From + "   " + $_.What) }

    $routerAnswered = $devices | Where-Object { $_.From -eq $gateway }
    if ($routerAnswered) {
        Good "Your router ($gateway) speaks UPnP - the game should be able to open the port."
        Good 'If the game still says no gateway answered, the firewall rule below is the problem.'
    } else {
        Bad "Your router ($gateway) did not answer, though other devices did."
        Bad 'So the network is fine and UPnP is switched off on the router itself.'
    }
}

# ---------------------------------------------------------------- firewall
Head 'Windows Firewall'

$rules = @(Quietly { Get-NetFirewallRule -ErrorAction SilentlyContinue |
                     Where-Object { $_.DisplayName -match 'Satisfying|Unity' } })
if ($rules.Count -eq 0) {
    Bad 'No rule for Unity or Satisfying.'
    Bad 'Inbound UDP is being dropped, which blocks players joining even with a port forward.'
    Info 'Fix it by allowing the app when Windows next asks, or run this as administrator:'
    Info "    New-NetFirewallRule -DisplayName 'Satisfying' -Direction Inbound -Protocol UDP -LocalPort $Port -Action Allow"
} else {
    $rules | ForEach-Object {
        $colour = if ($_.Action -eq 'Allow' -and $_.Enabled -eq 'True') { 'Green' } else { 'Yellow' }
        Write-Host ("  " + $_.DisplayName.PadRight(38) + " " + $_.Direction + "  " + $_.Action + "  " + $_.Profile) -ForegroundColor $colour
    }
}

$profileNow = (Quietly { Get-NetConnectionProfile -InterfaceIndex $route.ifIndex -ErrorAction SilentlyContinue }).NetworkCategory
if ($profileNow) {
    Info ""
    Info "This network is categorised as: $profileNow"
    if ($profileNow -eq 'Public') {
        Bad 'On a Public network Windows blocks far more inbound traffic.'
        Bad 'Set it to Private for your home network:'
        Info "    Set-NetConnectionProfile -InterfaceIndex $($route.ifIndex) -NetworkCategory Private"
    }
}

# ---------------------------------------------------------------- what to do
Head 'What to do'

Write-Host "  Forward UDP $Port to $localIp on your router at http://$gateway"
Write-Host '  and give that machine a DHCP reservation so the address stops moving.'
Write-Host ''
Write-Host '  Your public address (what people outside would connect to):'
try {
    $public = (Invoke-RestMethod -Uri 'https://api.ipify.org' -TimeoutSec 5)
    Good "    $public`:$Port"
    if ($public -match '^100\.(6[4-9]|[7-9][0-9]|1[0-1][0-9]|12[0-7])\.') {
        Bad '  That is a carrier-grade NAT address, not a real public one.'
        Bad '  Your ISP has you behind their own NAT: no port forward can work.'
        Bad '  Run a dedicated server instead - see docs/SERVER.md.'
    }
} catch {
    Bad '    could not reach api.ipify.org'
}
Write-Host ''
