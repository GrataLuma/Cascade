# Grata Cascade demo — deployment guide

Kompletní postup pro nasazení Blazor WASM + SignalR demo aplikace na
veřejný VPS s HTTPS, systemd auto-restart a hardeningem. Čtyři fáze
A → D, odhad 60–90 min včetně provisioningu (bez čekání na DNS).

**Cílový stav po dokončení:** `https://gratacascade.com/` otevírá demo
přes Let's Encrypt cert, dva taby z různých sítí prochází full pair
flow, server běží jako systemd service s auto-restart.

---

## Předpoklady

- Lokální worktree: Windows / Linux / macOS s nainstalovaným .NET 9 SDK,
  SSH klientem (`ssh`, `scp`).
- Vlastní doména s přístupem do DNS panelu registrátora.
- SSH klíč k dispozici (`~/.ssh/id_ed25519.pub`).
- VPS account u libovolného cloud providera (Ubuntu 22.04+ image, min. 2 vCPU /
  2 GB RAM / 20 GB SSD).

---

## Fáze A — Provisioning (~10 min user-time + DNS propagace)

### A.1 VPS provider konzole

V panelu svého cloud providera (Hetzner / DigitalOcean / Vultr / OVH / ...):

1. Vytvoř nový server
2. Parametry:
   - Image: **Ubuntu 24.04 LTS** (`install.sh` testovaný na 24.04, funguje i 22.04)
   - Type: 2 vCPU / 4 GB RAM / 40 GB SSD postačí pro demo zátěž
   - Location: blízko cílové audience (EU pro CZ/EU reviewery, US East pro IACR US)
   - IPv4 ano, IPv6 volitelné
   - SSH Keys: import `~/.ssh/id_ed25519.pub` (nebo `ssh-keygen -t ed25519 -C "<descriptive-name>"`)
3. Po vytvoření zaznamenat **veřejnou IPv4 adresu** (např. `203.0.113.42`)

### A.2 DNS A-records

V panelu DNS registrátora (rozhraní se liší podle providera, princip stejný):

| Typ | Hostname | Hodnota | TTL |
|-----|----------|---------|-----|
| A   | (prázdné = apex `@`) | `<VPS_IP>` | 300 |
| A   | `www`    | `<VPS_IP>` | 300 |

Pozn.: pokud DNS běží u Cloudflare, **vypni proxy mode** (orange cloud → grey)
— jinak Let's Encrypt HTTP-01 challenge selže.

Ověření z lokálu (DNS propagace obvykle 5–30 min):

```bash
nslookup <your-domain> 8.8.8.8
nslookup www.<your-domain> 8.8.8.8
# Oba musí vrátit <VPS_IP>
```

---

## Fáze B — Bootstrap VPS (~15 min)

### B.1 První SSH login

```bash
ssh root@<VPS_IP>
# Accept fingerprint, login přes klíč automaticky.
```

### B.2 Upload + spuštění install.sh

Z lokálního repa (nový terminál, lokální shell):

```bash
scp deploy/install.sh root@<VPS_IP>:/root/
ssh root@<VPS_IP> 'bash /root/install.sh'
```

Skript provede 8 kroků (apt upgrade, user `deploy`, ufw, sshd harden,
unattended-upgrades, .NET 9 runtime, Caddy 2, app dir). Trvá ~5–10 min.

Pro náhled bez změn: `ssh root@<VPS_IP> 'bash /root/install.sh --dry-run'`.

### B.3 Validace nového usera

**KRITICKÉ:** Před zavřením root session ověřit, že `deploy@<IP>` funguje
v jiném terminálu. SSH klíče jsou importovány z `/root/.ssh/authorized_keys`.

```bash
ssh deploy@<VPS_IP>
# → musí pustit přes klíč, sudo bez hesla funguje
sudo systemctl status sshd
sudo ufw status
dotnet --info
caddy version
```

Po validaci `exit` z root session — `PermitRootLogin no` je v platnosti.

---

## Fáze C — Build + deploy aplikace (~10 min)

### C.1 Lokální publish

V repo rootu (Windows worktree):

```powershell
dotnet publish Demo/Demo.Server/Demo.Server.csproj `
  -c Release -r linux-x64 --self-contained false `
  -o publish/
```

Výstup ~30 MB v `publish/` (zahrnuje WASM bundle pod `wwwroot/_framework/`).

### C.2 Upload + symlink swap

```bash
ssh deploy@<VPS_IP> 'mkdir -p /tmp/grata-cascade'
scp -r publish/* deploy@<VPS_IP>:/tmp/grata-cascade/
scp deploy/grata-cascade.service deploy@<VPS_IP>:/tmp/
scp deploy/Caddyfile.example deploy@<VPS_IP>:/tmp/Caddyfile

ssh deploy@<VPS_IP> '
  sudo rsync -a --delete /tmp/grata-cascade/ /var/www/grata-cascade/
  sudo chown -R www-data:www-data /var/www/grata-cascade
  sudo mv /tmp/grata-cascade.service /etc/systemd/system/
  sudo mv /tmp/Caddyfile /etc/caddy/Caddyfile
  sudo systemctl daemon-reload
  sudo systemctl enable --now grata-cascade
  sudo systemctl reload caddy
  rm -rf /tmp/grata-cascade
'
```

### C.3 Sledovat Let's Encrypt cert issue

```bash
ssh deploy@<VPS_IP> 'sudo journalctl -u caddy -f'
# Hledat: "certificate obtained successfully" pro gratacascade.com a www
# Trvá ~30 s. Ctrl+C po úspěchu.
```

Pokud cert flow selhává:
- `sudo journalctl -u caddy --no-pager | grep -i "challenge\|error"`
- Zkontrolovat `dig +short gratacascade.com` z VPS (musí vrátit vlastní IP)
- Ověřit `sudo ufw status` (port 80 musí být allowed)

---

## Fáze D — Verifikace + WAN test (~15 min)

### D.1 Acceptance criteria (z lokálu)

```bash
# 1) HTTPS landing
curl -I https://gratacascade.com/
# → HTTP/2 200, Server: Caddy, valid LE cert ze sekce TLS

# 2) WebSocket negotiate (SignalR)
curl -i 'https://gratacascade.com/hub/relay/negotiate?negotiateVersion=1' -X POST
# → HTTP/2 200, JSON s connectionId / availableTransports

# 3) www redirect
curl -I https://www.gratacascade.com/
# → HTTP/2 301 (nebo 308), Location: https://gratacascade.com/

# 4) systemd state
ssh deploy@<VPS_IP> 'systemctl status grata-cascade --no-pager'
# → "Active: active (running)", no recent crashes

# 5) Server logy
ssh deploy@<VPS_IP> 'journalctl -u grata-cascade -n 30 --no-pager'
# → "Now listening on: http://localhost:5000", "Application started"
```

### D.2 WAN browser test (manuálně, 2 sítě)

Cíl: ověřit, že pair flow funguje přes WAN, ne jen localhost loopback.

1. **Tab 1** — laptop wifi → `https://gratacascade.com/demo`
   - Public testbed banner viditelný (CZ+EN, podle aktuálního language toggle)
   - Connect → Lobby
2. **Tab 2** — mobile hotspot (ne stejná wifi!) → `https://gratacascade.com/demo`
   - Stejný banner, jiný language pokud chceš testovat
   - Connect → Lobby
3. Tab 1 sees Tab 2 v lobby → Pair → Tab 2 přijme
4. Oba kliknou Compute Final → K* zobrazen
5. Safety Number compare → match v obou tabech ✓
6. Disconnect → oba zpět do Lobby

### D.3 Final commit (po úspěšném WAN testu)

Z lokálního repa (commit 6 plánu):

```bash
# Editovat root README.md — přidat live URL badge / sekci na začátek
# (link na https://gratacascade.com/, krátký popis "Try it: pair with another tab")
git add README.md
git commit -m "docs: live URL badge — gratacascade.com je deployed"
git push origin claude/clever-stonebraker-28fc32  # nebo merge do main
```

---

## Re-deploy (pouze nová verze app, beze změn infra)

```bash
# Lokálně:
dotnet publish Demo/Demo.Server/Demo.Server.csproj -c Release -r linux-x64 --self-contained false -o publish/
ssh deploy@<VPS_IP> 'mkdir -p /tmp/grata-cascade'
scp -r publish/* deploy@<VPS_IP>:/tmp/grata-cascade/

ssh deploy@<VPS_IP> '
  sudo systemctl stop grata-cascade
  sudo rsync -a --delete /tmp/grata-cascade/ /var/www/grata-cascade/
  sudo chown -R www-data:www-data /var/www/grata-cascade
  sudo systemctl start grata-cascade
  rm -rf /tmp/grata-cascade
'
```

Outage cca 5 s (stop → rsync → start). Pokud by to mělo být zero-downtime,
přidat second backend port + Caddy load balance — out of scope pro academic
demo.

---

## Troubleshooting

| Symptom | Možná příčina | Řešení |
|---------|---------------|--------|
| Caddy cert flow selhává | port 80 zavřený, DNS nepropag. | `sudo ufw status`, `dig +short <doména>` z VPS |
| 502 Bad Gateway | grata-cascade service neběží | `sudo systemctl status grata-cascade`, `sudo journalctl -u grata-cascade -n 50` |
| WebSocket nepřipojí | reverse proxy timeout (default 60s OK) | `sudo journalctl -u caddy --no-pager | grep -i ws` |
| Banner se nezobrazuje na live | browser cache starého WASM | hard refresh (Ctrl+Shift+R), případně Caddy cache |
| systemd unit selže po deploy | špatný path k Demo.Server.dll | `ls /var/www/grata-cascade/Demo.Server.dll` |
| sshd se po restartu neuměl bootnout | špatný sshd config v /etc/ssh/sshd_config.d/ | rescue mode přes VPS provider konzoli |

---

## Migrace na jiný host

Tento postup funguje 1:1 pro libovolný Ubuntu 22.04+ VPS. Změny:

- **A.1** — adekvátní krok u jiného providera
- **A.2** — DNS panel jiného registrátora, jen update A-record na novou IP
- **B–D** — beze změn

`install.sh` je idempotentní; spuštění na čistém Ubuntu vždy provede totéž.

---

## Bezpečnostní poznámky

- SSH klíč jako root → primárně **nepoužívat** po fázi B.3. Pokud root klíč
  ztratíš, deploy klíč je dostupný (importován v B.2). Můžeš `sudo passwd -l root`
  pokud chceš úplně zakázat root login (nad rámec install.sh).
- TLS cert auto-renew: Caddy obnovuje 30 dní před expirací, automaticky.
  Žádný cron job k údržbě.
- `/var/www/grata-cascade` je writable jen pro `www-data` — Demo.Server
  běží pod tímto userem a nemůže si přepsat vlastní binárky.
- Logy v journald + `/var/log/caddy/gratacascade.access.log` (rotovaný).
  PII tu žádné nejsou (demo nepoužívá user accounts), ale logy jsou
  read-only pro `deploy` user (přes sudo dostupné).
