#!/usr/bin/env bash
# One-time hardening + Docker install for a fresh Ubuntu 22.04/24.04 VPS. Run as root:
#
#   bash harden.sh <admin-username> "<contents of your id_ed25519.pub>"
#
# After it finishes, KEEP THIS SESSION OPEN and confirm `ssh <admin-username>@<ip>` works from a
# new terminal before logging out - root and password logins are switched off below.
set -euo pipefail

ADMIN_USER="${1:-}"
ADMIN_KEY="${2:-}"
DEPLOY_USER=deploy

if [[ $EUID -ne 0 ]]; then echo "Run as root." >&2; exit 1; fi
if [[ -z "$ADMIN_USER" || -z "$ADMIN_KEY" ]]; then
  echo "Usage: bash harden.sh <admin-username> \"<ssh public key>\"" >&2; exit 1
fi
# Refuse to continue without a usable key - otherwise disabling passwords locks you out.
if ! ssh-keygen -l -f <(echo "$ADMIN_KEY") >/dev/null 2>&1; then
  echo "That doesn't look like a valid SSH public key (expected 'ssh-ed25519 AAAA...')." >&2; exit 1
fi

export DEBIAN_FRONTEND=noninteractive

echo "==> Updating packages"
apt-get update -q
apt-get full-upgrade -yq
apt-get install -yq ufw fail2ban python3-systemd unattended-upgrades ca-certificates curl git

echo "==> Admin user '$ADMIN_USER' (sudo, SSH key only)"
if ! id "$ADMIN_USER" &>/dev/null; then
  adduser --disabled-password --gecos "" "$ADMIN_USER"
  echo "Set a sudo password for $ADMIN_USER (used for sudo only, never for SSH login):"
  passwd "$ADMIN_USER"
fi
usermod -aG sudo "$ADMIN_USER"
install -d -m 700 -o "$ADMIN_USER" -g "$ADMIN_USER" "/home/$ADMIN_USER/.ssh"
echo "$ADMIN_KEY" > "/home/$ADMIN_USER/.ssh/authorized_keys"
chown "$ADMIN_USER:$ADMIN_USER" "/home/$ADMIN_USER/.ssh/authorized_keys"
chmod 600 "/home/$ADMIN_USER/.ssh/authorized_keys"

echo "==> Deploy user '$DEPLOY_USER' (docker, no sudo, no password)"
if ! id "$DEPLOY_USER" &>/dev/null; then
  adduser --disabled-password --gecos "" "$DEPLOY_USER"
fi
install -d -m 700 -o "$DEPLOY_USER" -g "$DEPLOY_USER" "/home/$DEPLOY_USER/.ssh"
install -d -o "$DEPLOY_USER" -g "$DEPLOY_USER" /opt/alertme

echo "==> SSH: keys only, no root, short leash on failed attempts"
# "00-" so it sorts first: sshd keeps the FIRST value it reads, and cloud images ship
# 50-cloud-init.conf with "PasswordAuthentication yes" that would otherwise win.
cat > /etc/ssh/sshd_config.d/00-hardening.conf <<EOF
PermitRootLogin no
PasswordAuthentication no
KbdInteractiveAuthentication no
PubkeyAuthentication yes
PermitEmptyPasswords no
MaxAuthTries 3
LoginGraceTime 20
X11Forwarding no
AllowAgentForwarding no
AllowUsers $ADMIN_USER $DEPLOY_USER
EOF
sshd -t
systemctl restart ssh

echo "==> Firewall: only SSH in (rate-limited)"
ufw default deny incoming
ufw default allow outgoing
ufw limit OpenSSH
ufw --force enable

echo "==> fail2ban: ban SSH brute-forcers for an hour"
cat > /etc/fail2ban/jail.local <<'EOF'
[sshd]
enabled  = true
backend  = systemd
maxretry = 5
findtime = 10m
bantime  = 1h
EOF
systemctl enable --now fail2ban
systemctl restart fail2ban

echo "==> Automatic security updates (reboots at 04:00 when a kernel update needs it)"
cat > /etc/apt/apt.conf.d/20auto-upgrades <<'EOF'
APT::Periodic::Update-Package-Lists "1";
APT::Periodic::Unattended-Upgrade "1";
EOF
cat > /etc/apt/apt.conf.d/52unattended-upgrades-local <<'EOF'
Unattended-Upgrade::Automatic-Reboot "true";
Unattended-Upgrade::Automatic-Reboot-Time "04:00";
EOF

echo "==> Docker (official repo)"
install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
chmod a+r /etc/apt/keyrings/docker.asc
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo "$VERSION_CODENAME") stable" \
  > /etc/apt/sources.list.d/docker.list
apt-get update -q
apt-get install -yq docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin

# Rotated logs (unbounded json logs fill the disk), containers keep running across daemon
# restarts/upgrades, and no container process can gain privileges via setuid binaries.
cat > /etc/docker/daemon.json <<'EOF'
{
  "log-driver": "json-file",
  "log-opts": { "max-size": "10m", "max-file": "3" },
  "live-restore": true,
  "no-new-privileges": true
}
EOF
systemctl enable docker
systemctl restart docker

# Docker group = root-equivalent. The deploy user gets it, but its only SSH key is pinned to the
# deploy script (see deploy/README.md), so it can't be used for an interactive shell.
usermod -aG docker "$DEPLOY_USER"
usermod -aG docker "$ADMIN_USER"

echo
echo "Done. Now, WITHOUT closing this session, open a new terminal and run:"
echo "  ssh $ADMIN_USER@$(hostname -I | awk '{print $1}')"
echo "Root login and password login are disabled from here on."
