#!/usr/bin/env bash
# Deploy the Rilo game server to an Oracle Cloud (or any Ubuntu / Oracle Linux) VM over SSH.
#   ./Server/deploy/deploy-oracle.sh <public-ip> <ssh-private-key> [ssh-user]
# Re-run it to update the server; the player database (/opt/rilo/data/veil.db) is kept.
set -euo pipefail
IP="${1:?usage: deploy-oracle.sh <public-ip> <ssh-key> [user]}"
KEY="${2:?ssh private key path}"
USER_="${3:-opc}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
SSH=(ssh -i "$KEY" -o StrictHostKeyChecking=accept-new -o ConnectTimeout=15 "$USER_@$IP")
chmod 600 "$KEY"

echo "==> checking the VM"
ARCH=$("${SSH[@]}" uname -m)
case "$ARCH" in
  aarch64|arm64) RID=linux-arm64 ;;
  x86_64) RID=linux-x64 ;;
  *) echo "unsupported CPU: $ARCH"; exit 1 ;;
esac
echo "    $ARCH -> $RID"

echo "==> building the server ($RID)"
export PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
OUT="$ROOT/Builds/server-$RID"
rm -rf "$OUT"
dotnet publish "$ROOT/Server/Veil.Server" -c Release -r "$RID" --self-contained true -o "$OUT" | tail -1
COPYFILE_DISABLE=1 tar -C "$OUT" -czf "$ROOT/Builds/rilo-server-$RID.tgz" .

echo "==> uploading"
scp -i "$KEY" -o StrictHostKeyChecking=accept-new "$ROOT/Builds/rilo-server-$RID.tgz" "$USER_@$IP:/tmp/rilo-server.tgz"
# non-secret server settings (e.g. VEIL_GOOGLE_CLIENT_ID) — committed in Server/deploy/server.env
scp -i "$KEY" -o StrictHostKeyChecking=accept-new "$ROOT/Server/deploy/server.env" "$USER_@$IP:/tmp/rilo-server.env"

echo "==> installing the service + opening the firewall"
"${SSH[@]}" 'sudo bash -s' <<'REMOTE'
set -euo pipefail
# small VMs (E2.1.Micro = 1 GB RAM): add 2 GB swap once so the .NET server never gets OOM-killed
if [ "$(awk '/MemTotal/{print $2}' /proc/meminfo)" -lt 2000000 ] && ! swapon --show | grep -q /swapfile; then
  (fallocate -l 2G /swapfile || dd if=/dev/zero of=/swapfile bs=1M count=2048) 2>/dev/null
  chmod 600 /swapfile && mkswap /swapfile >/dev/null && swapon /swapfile
  grep -q '^/swapfile' /etc/fstab || echo '/swapfile none swap sw 0 0' >> /etc/fstab
fi
id rilo >/dev/null 2>&1 || useradd --system --home /opt/rilo --shell /usr/sbin/nologin rilo
mkdir -p /opt/rilo/server /opt/rilo/data
systemctl stop rilo 2>/dev/null || true
rm -rf /opt/rilo/server/*
tar -C /opt/rilo/server -xzf /tmp/rilo-server.tgz
chmod +x /opt/rilo/server/Veil.Server
# match-ticket signing secret: created once, kept across updates
[ -f /opt/rilo/secret.env ] || echo "VEIL_TICKET_SECRET=$(head -c 32 /dev/urandom | base64 | tr -d '/+=')" > /opt/rilo/secret.env
chmod 600 /opt/rilo/secret.env
install -m 644 /tmp/rilo-server.env /opt/rilo/server.env
chown -R rilo:rilo /opt/rilo

cat > /etc/systemd/system/rilo.service <<'UNIT'
[Unit]
Description=Rilo game server (gateway 7779/udp, match 7777/udp, voice 7778/udp, REST 5080/tcp)
After=network-online.target
Wants=network-online.target

[Service]
User=rilo
WorkingDirectory=/opt/rilo/data
EnvironmentFile=/opt/rilo/secret.env
EnvironmentFile=-/opt/rilo/server.env
# small VMs: workstation GC + conserve memory (ASP.NET defaults to server GC, which reserves far more RAM)
Environment=DOTNET_gcServer=0
Environment=DOTNET_GCConserveMemory=7
ExecStart=/opt/rilo/server/Veil.Server --name "Rilo" --db /opt/rilo/data/veil.db
Restart=always
RestartSec=3
LimitNOFILE=65536

[Install]
WantedBy=multi-user.target
UNIT

# OS firewall (Oracle's Ubuntu images reject everything except SSH by default)
if command -v firewall-cmd >/dev/null 2>&1 && systemctl is-active --quiet firewalld; then
  firewall-cmd --permanent --add-port=7777-7779/udp --add-port=5080/tcp >/dev/null
  firewall-cmd --reload >/dev/null
else
  iptables -C INPUT -p udp --dport 7777:7779 -j ACCEPT 2>/dev/null || iptables -I INPUT 1 -p udp --dport 7777:7779 -j ACCEPT
  iptables -C INPUT -p tcp --dport 5080 -j ACCEPT 2>/dev/null || iptables -I INPUT 1 -p tcp --dport 5080 -j ACCEPT
  if command -v netfilter-persistent >/dev/null 2>&1; then netfilter-persistent save >/dev/null 2>&1 || true
  elif [ -d /etc/iptables ]; then iptables-save > /etc/iptables/rules.v4; fi
fi

systemctl daemon-reload
systemctl enable --now rilo >/dev/null 2>&1
sleep 3
systemctl is-active rilo
journalctl -u rilo -n 8 --no-pager | sed 's/^/    /'
REMOTE

echo "==> checking from here"
if curl -fsS --max-time 8 "http://$IP:5080/api/health"; then echo; echo "Server is up: udp://$IP:7779"; echo "Moved to a new machine? Point every installed app at it:  ./Tools/boot/boot.sh server udp://$IP:7779"
else echo "REST port 5080 not reachable yet: add the Ingress rules in the Oracle console (see Server/deploy/ORACLE.md)"; fi
