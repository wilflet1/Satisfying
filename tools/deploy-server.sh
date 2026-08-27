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

# ---------------------------------------------------------------- will it even run here
#
# Unity's player links against a newer glibc than some perfectly current distributions ship, and
# when it does not match, systemd reports status=203/EXEC - which means "could not execute" and
# says nothing whatsoever about why. The binary is present, executable, the right architecture,
# and the error looks like a permissions problem. It is not.
#
# Oracle Linux 9 is the trap, because it is what Oracle Cloud offers you by default: glibc 2.34,
# against the 2.35 a Unity 6 player wants. Ask the binary what it needs and compare, so this is one
# line of output before anything is installed rather than an evening inside journalctl.
NEED=$(strings "$INSTALL_DIR/UnityPlayer.so" 2>/dev/null \
       | grep -oE 'GLIBC_2\.[0-9]+' | sort -uV | tail -1 | cut -d_ -f2)
HAVE=$(ldd --version 2>/dev/null | head -1 | grep -oE '[0-9]+\.[0-9]+$')

if [[ -n "$NEED" && -n "$HAVE" ]]; then
    if [[ "$(printf '%s\n%s\n' "$NEED" "$HAVE" | sort -V | tail -1)" != "$HAVE" ]]; then
        warn "This machine has glibc $HAVE and the server needs $NEED."
        warn ""
        warn "That is the whole problem - the binary is fine and this OS is too old for it."
        warn "Oracle Linux 9 ships 2.34 and is the default image on Oracle Cloud; Ubuntu 22.04"
        warn "ships exactly 2.35 and works. Recreate the instance with Ubuntu 22.04 or newer"
        warn "and run this again. Nothing has been installed."
        exit 1
    fi
    say "glibc $HAVE, server needs $NEED - fine"
fi

# SELinux gives systemd the same 203/EXEC for a different reason: a binary sitting in a home
# directory carries user_home_t, which a service is not allowed to execute. Relabel it rather than
# turning SELinux off, which is what every forum answer suggests and none of them should.
if command -v getenforce >/dev/null 2>&1 && [[ "$(getenforce)" == "Enforcing" ]]; then
    say "SELinux is enforcing - labelling the binary so systemd may run it"
    sudo chcon -R -t bin_t "$INSTALL_DIR" 2>/dev/null || warn "could not relabel; the service may not start"
fi

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

# WHICH firewall is a question with three answers and the wrong one is silent.
#
# Oracle Cloud's Ubuntu images have ufw INSTALLED AND INACTIVE, and carry their own iptables rules
# instead - ending in a REJECT-all that only TCP 22 gets past. Testing for ufw with `command -v`
# therefore found it, ran `ufw allow 7777/udp`, was told "Rules updated", exited 0, and changed
# nothing whatsoever. The port stayed shut, tcpdump showed the packets arriving - it taps the device
# before any filtering - and the server sat listening to traffic it was never handed. The player
# just sees nothing coming back, which is where this whole evening started.
#
# So: ufw only if ENABLED, firewalld only if RUNNING, iptables otherwise.
# "inactive" contains "active", so this match is anchored. Getting that wrong sent the whole thing
# down the ufw branch on a machine whose ufw is switched off, which is how the port stayed shut.
if command -v ufw >/dev/null 2>&1    && sudo ufw status 2>/dev/null | head -1 | grep -qiE '^Status:[[:space:]]*active$'; then
    sudo ufw allow "$PORT"/udp || warn "ufw refused - open UDP $PORT yourself"
elif command -v firewall-cmd >/dev/null 2>&1 && sudo firewall-cmd --state >/dev/null 2>&1; then
    sudo firewall-cmd --permanent --add-port="$PORT"/udp && sudo firewall-cmd --reload
else
    # Oracle Cloud and friends ship a locked-down iptables and an inactive ufw. INSERT rather than
    # append: their chain ends in a REJECT, and a rule added after a REJECT is a comment.
    if ! sudo iptables -C INPUT -p udp --dport "$PORT" -j ACCEPT >/dev/null 2>&1; then
        sudo iptables -I INPUT -p udp --dport "$PORT" -j ACCEPT || warn "could not add an iptables rule"
    fi
    # And keep it. An iptables rule lives in memory; without this the server is reachable until the
    # first reboot and then quietly is not, which is a horrible thing to debug weeks later.
    if command -v netfilter-persistent >/dev/null 2>&1; then
        sudo netfilter-persistent save >/dev/null 2>&1 || warn "could not persist the rule"
    elif [[ -d /etc/iptables ]]; then
        sudo sh -c "iptables-save > /etc/iptables/rules.v4" || warn "could not persist the rule"
    else
        warn "rule added but NOT saved - it will be lost on reboot"
    fi
fi

# Say whether it actually worked, rather than trusting a command that returned 0.
if sudo iptables -C INPUT -p udp --dport "$PORT" -j ACCEPT >/dev/null 2>&1; then
    say "UDP $PORT is open on this machine"
elif command -v ufw >/dev/null 2>&1 && sudo ufw status 2>/dev/null | grep -q "$PORT"; then
    say "UDP $PORT is open in ufw"
else
    warn "Could NOT confirm UDP $PORT is open here. Check: sudo iptables -L INPUT -n --line-numbers"
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
