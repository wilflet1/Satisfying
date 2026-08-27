#!/usr/bin/env bash
#
# Installs a built Satisfying dedicated server on a Linux box and keeps it running.
#
# On the machine you built on:
#     scp -r Builds/LinuxServer ubuntu@YOUR.SERVER.IP:~/satisfying
#     scp tools/deploy-server.sh  ubuntu@YOUR.SERVER.IP:~/
#
# The user name is whatever the image ships with and it is not the same everywhere:
# "ubuntu" on Ubuntu, "opc" on Oracle Linux, which is what Oracle Cloud gives you by
# default. Nothing else in here cares which distribution it is - the unit runs as
# $USER and the firewall step tries ufw, firewall-cmd and iptables in turn.
#
# On the server:
#     bash deploy-server.sh
#
# It creates a systemd unit, opens the firewall, starts the server and shows you
# the address to hand out. Re-running it upgrades in place.

set -euo pipefail

PORT="${PORT:-7777}"
NAME="${NAME:-$(hostname) duel}"
MAP="${MAP:-arena}"
BOTS="${BOTS:-1}"
INSTALL_DIR="${INSTALL_DIR:-$HOME/satisfying}"
BINARY="$INSTALL_DIR/SatisfyingServer"
SERVICE=/etc/systemd/system/satisfying.service

say() { printf '\n  \033[36m%s\033[0m\n' "$*"; }
warn() { printf '  \033[33m%s\033[0m\n' "$*"; }

say "Satisfying - dedicated server setup"

if [[ ! -f "$BINARY" ]]; then
    warn "No server binary at $BINARY"
    warn "Build one with:  Satisfying > Build > Linux dedicated server"
    warn "then copy Builds/LinuxServer to $INSTALL_DIR on this machine."
    exit 1
fi
chmod +x "$BINARY"

# ---------------------------------------------------------------- service
say "Writing $SERVICE"
sudo tee "$SERVICE" >/dev/null <<UNIT
[Unit]
Description=Satisfying dedicated server
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
User=$USER
WorkingDirectory=$INSTALL_DIR
# -batchmode -nographics: no window, no renderer. -noupnp: a cloud box has a
# public address already, and asking its gateway to forward anything is pointless.
ExecStart=$BINARY -batchmode -nographics -logfile $INSTALL_DIR/server.log \\
    -server -port $PORT -map $MAP -bots $BOTS -noupnp -servername "$NAME"
Restart=always
RestartSec=5
# The simulation is a fixed 64 Hz tick; it does not need a whole machine.
Nice=-5

[Install]
WantedBy=multi-user.target
UNIT

sudo systemctl daemon-reload
sudo systemctl enable --now satisfying
sudo systemctl restart satisfying

# ---------------------------------------------------------------- firewall
say "Opening UDP $PORT"
if command -v ufw >/dev/null 2>&1; then
    sudo ufw allow "$PORT"/udp || warn "ufw refused - open UDP $PORT yourself"
elif command -v firewall-cmd >/dev/null 2>&1; then
    sudo firewall-cmd --permanent --add-port="$PORT"/udp && sudo firewall-cmd --reload
else
    # Oracle Cloud and friends ship a locked-down iptables and no ufw.
    sudo iptables -I INPUT -p udp --dport "$PORT" -j ACCEPT || warn "could not add an iptables rule"
    if command -v netfilter-persistent >/dev/null 2>&1; then
        sudo netfilter-persistent save || true
    fi
fi

warn "Cloud providers also have their own firewall. Allow inbound UDP $PORT there:"
warn "  Oracle Cloud  - VCN > Security Lists > Add Ingress Rule"
warn "  AWS           - Security group > Inbound rules"
warn "  Hetzner / DO  - Networking > Firewalls"

# ---------------------------------------------------------------- report
sleep 2
say "Status"
systemctl --no-pager --lines=6 status satisfying || true

PUBLIC_IP="$(curl -s --max-time 5 https://api.ipify.org || echo 'YOUR.SERVER.IP')"
say "Anyone can join at:  $PUBLIC_IP:$PORT"
echo
echo "  In the game: type that into the address box and click join."
echo
echo "  logs      journalctl -u satisfying -f"
echo "  restart   sudo systemctl restart satisfying"
echo "  stop      sudo systemctl stop satisfying"
echo
