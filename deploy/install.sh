#!/usr/bin/env bash
# Grata Cascade demo — VPS bootstrap script.
#
# Usage:
#   1. scp deploy/install.sh root@<VPS_IP>:/root/
#   2. ssh root@<VPS_IP> 'bash /root/install.sh'
#
# Idempotentní — opakované spuštění je bezpečné, jen přeskočí už hotové kroky.
# Provede:
#   - apt update + upgrade
#   - vytvoření non-root usera 'deploy' s sudo + import root SSH klíčů
#   - ufw firewall (22/80/443 only)
#   - sshd hardening (no root login, key-only auth)
#   - unattended-upgrades pro automatické security patche
#   - .NET 9 ASP.NET runtime (Microsoft repo)
#   - Caddy 2 (oficiální Cloudsmith repo)
#   - mkdir /var/www/grata-cascade vlastněný www-data
#
# Po dokončení script vypíše finální status + next-steps pro upload aplikace.

set -euo pipefail

# ── 0. Sanity checks ────────────────────────────────────────────────────
if [[ $EUID -ne 0 ]]; then
    echo "ERROR: Tento script musí běžet jako root. Použij: sudo bash $0" >&2
    exit 1
fi

if ! command -v lsb_release >/dev/null 2>&1; then
    apt-get -y install lsb-release >/dev/null
fi

DISTRO=$(lsb_release -is)
RELEASE=$(lsb_release -rs)
if [[ "$DISTRO" != "Ubuntu" ]] || [[ "${RELEASE%%.*}" -lt 22 ]]; then
    echo "WARN: Otestováno na Ubuntu 22.04+. Detekováno: $DISTRO $RELEASE" >&2
    echo "      Pokračuje se, ale pokud něco selže, je to pravděpodobně tím." >&2
fi

DRY_RUN=0
for arg in "$@"; do
    case "$arg" in
        --dry-run) DRY_RUN=1 ;;
        -h|--help)
            grep -E '^# ' "$0" | sed 's/^# \?//'
            exit 0
            ;;
    esac
done

run() {
    if [[ $DRY_RUN -eq 1 ]]; then
        printf '   [dry-run] %s\n' "$*"
    else
        "$@"
    fi
}

step() { printf '\n→ %s\n' "$*"; }

# ── 1. APT update + base packages ───────────────────────────────────────
step "1/8 apt update + upgrade"
run apt-get -qq update
run apt-get -y -qq full-upgrade
run apt-get -y -qq install \
    ufw \
    unattended-upgrades \
    curl \
    ca-certificates \
    gnupg \
    rsync \
    sudo

# ── 2. Non-root user 'deploy' ───────────────────────────────────────────
step "2/8 vytvoření non-root usera 'deploy'"
if id -u deploy >/dev/null 2>&1; then
    echo "   uživatel 'deploy' už existuje — skip"
else
    run useradd -m -s /bin/bash -G sudo deploy
    # Sudo bez hesla pro deploy pohodlí; SSH key-only auth dělá stejně útok náročný.
    if [[ $DRY_RUN -eq 0 ]]; then
        echo "deploy ALL=(ALL) NOPASSWD:ALL" > /etc/sudoers.d/90-deploy
        chmod 440 /etc/sudoers.d/90-deploy
    fi
fi

# Import root authorized_keys do deploy (pokud root vůbec nějaké má)
if [[ -f /root/.ssh/authorized_keys ]]; then
    if [[ $DRY_RUN -eq 0 ]]; then
        mkdir -p /home/deploy/.ssh
        cp /root/.ssh/authorized_keys /home/deploy/.ssh/authorized_keys
        chown -R deploy:deploy /home/deploy/.ssh
        chmod 700 /home/deploy/.ssh
        chmod 600 /home/deploy/.ssh/authorized_keys
    fi
    echo "   SSH klíče importovány do /home/deploy/.ssh/authorized_keys"
else
    echo "   WARN: /root/.ssh/authorized_keys nenalezen — deploy user nedostane SSH klíče!" >&2
fi

# ── 3. UFW firewall ─────────────────────────────────────────────────────
step "3/8 ufw firewall (22/80/443)"
run ufw --force default deny incoming
run ufw --force default allow outgoing
run ufw allow OpenSSH
run ufw allow 80/tcp
run ufw allow 443/tcp
run ufw --force enable

# ── 4. SSHD harden ──────────────────────────────────────────────────────
step "4/8 sshd_config hardening"
SSHD_CONFIG=/etc/ssh/sshd_config.d/99-grata-cascade.conf
if [[ $DRY_RUN -eq 0 ]]; then
    cat > "$SSHD_CONFIG" <<'EOF'
# Grata Cascade demo deployment overrides — managed by deploy/install.sh
PermitRootLogin no
PasswordAuthentication no
KbdInteractiveAuthentication no
ChallengeResponseAuthentication no
UsePAM yes
X11Forwarding no
PrintMotd no
EOF
    # Validace před restartem — pokud konfigurace špatná, sshd by spadl.
    # /run/sshd musí existovat, jinak `sshd -t` selže (Ubuntu 24.04 quirk —
    # privilege separation dir se vytváří až při startu service, ne při testu).
    mkdir -p /run/sshd
    sshd -t || { echo "ERROR: sshd config invalid, soubor $SSHD_CONFIG zachován pro inspekci" >&2; exit 1; }
    systemctl reload ssh
fi
echo "   IMPORTANT: před odhlášením otestuj nový login z jiné session:"
echo "              ssh deploy@<IP>  (přes import. klíč)"

# ── 5. unattended-upgrades ──────────────────────────────────────────────
step "5/8 unattended-upgrades pro auto security patche"
if [[ $DRY_RUN -eq 0 ]]; then
    echo 'APT::Periodic::Update-Package-Lists "1";'    > /etc/apt/apt.conf.d/20auto-upgrades
    echo 'APT::Periodic::Unattended-Upgrade "1";'     >> /etc/apt/apt.conf.d/20auto-upgrades
    systemctl enable --now unattended-upgrades >/dev/null
fi

# ── 6. .NET 9 ASP.NET Core runtime ──────────────────────────────────────
step "6/8 .NET 9 ASP.NET Core runtime"
if [[ -x /usr/bin/dotnet ]] && /usr/bin/dotnet --list-runtimes 2>/dev/null | grep -q "Microsoft.AspNetCore.App 9\."; then
    echo "   .NET 9 runtime už nainstalován — skip"
else
    if [[ $DRY_RUN -eq 0 ]]; then
        # Microsoft oficiální dotnet-install.sh — stahuje tarball přímo, bypasuje
        # apt repo issues. packages-microsoft-prod.deb na Ubuntu 24.04 buď neexistuje
        # nebo má špatný repo path (.NET 9 není v Ubuntu base repo, jen .NET 8).
        # dotnet-install.sh je oficiální a stable cross-distro.
        curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
        chmod +x /tmp/dotnet-install.sh
        /tmp/dotnet-install.sh --runtime aspnetcore --channel 9.0 \
            --install-dir /usr/share/dotnet --no-path
        ln -sf /usr/share/dotnet/dotnet /usr/bin/dotnet
        rm -f /tmp/dotnet-install.sh
        # Sanity check
        echo "   nainstalované runtimes:"
        /usr/bin/dotnet --list-runtimes | sed 's/^/     /'
    else
        echo "   [dry-run] would download dotnet-install.sh + install ASP.NET 9 runtime"
    fi
fi

# ── 7. Caddy 2 ──────────────────────────────────────────────────────────
step "7/8 Caddy 2 (HTTPS reverse proxy)"
if command -v caddy >/dev/null 2>&1; then
    echo "   Caddy už nainstalován — skip"
else
    if [[ $DRY_RUN -eq 0 ]]; then
        curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' \
            | gpg --dearmor -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
        curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' \
            | tee /etc/apt/sources.list.d/caddy-stable.list >/dev/null
        apt-get -qq update
        apt-get -y -qq install caddy
        # Caddy spawne defaultní service; my ji v Fázi C přepíšeme na náš Caddyfile.
    else
        echo "   [dry-run] would install Caddy from Cloudsmith repo"
    fi
fi

# ── 8. Aplikační adresář ────────────────────────────────────────────────
step "8/8 /var/www/grata-cascade"
if [[ $DRY_RUN -eq 0 ]]; then
    mkdir -p /var/www/grata-cascade
    chown www-data:www-data /var/www/grata-cascade
    chmod 755 /var/www/grata-cascade
fi

# ── Finále ──────────────────────────────────────────────────────────────
cat <<'EOF'

═══════════════════════════════════════════════════════════════════════
  Bootstrap hotov. Co dál (z lokálního Windows worktree):

  1) Lokální publish:
       dotnet publish Demo/Demo.Server/Demo.Server.csproj `
         -c Release -r linux-x64 --self-contained false -o publish/

  2) Upload + setup (SSH jako 'deploy', NE root):
       scp -r publish/* deploy@<IP>:/tmp/grata-cascade/
       scp deploy/grata-cascade.service deploy@<IP>:/tmp/
       scp deploy/Caddyfile.example deploy@<IP>:/tmp/Caddyfile

       ssh deploy@<IP> '
         sudo rsync -a --delete /tmp/grata-cascade/ /var/www/grata-cascade/
         sudo chown -R www-data:www-data /var/www/grata-cascade
         sudo mv /tmp/grata-cascade.service /etc/systemd/system/
         sudo mv /tmp/Caddyfile /etc/caddy/Caddyfile
         sudo systemctl daemon-reload
         sudo systemctl enable --now grata-cascade
         sudo systemctl reload caddy
       '

  3) Sledovat Let's Encrypt cert issue:
       ssh deploy@<IP> 'sudo journalctl -u caddy -f'

  4) Smoke test:
       curl -I https://gratacascade.com/
       → HTTP/2 200, Server: Caddy, valid LE cert

  Detaily v deploy/README.md.
═══════════════════════════════════════════════════════════════════════
EOF
