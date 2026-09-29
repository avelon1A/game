# Rilo server on Oracle Cloud

**Current server:** `144.24.139.103` · VM.Standard3.Flex (1 OCPU / 8 GB, trial credit) · Oracle Linux 9 · user `opc` · key `~/.ssh/rilo_server.key`.
Subnet `subnet-20260929-2317` in `vcn-20260929-2318` (security list already allows the game ports).
Free Ampere A1 was *out of capacity* in Hyderabad; E2.1.Micro (1 GB) is too small (it froze).

## 1. Create the VM (Oracle console)
Compute → Instances → **Create instance**
- Image: **Canonical Ubuntu 24.04** or Oracle Linux 9 (user `ubuntu` / `opc`)
- Shape: **VM.Standard.A1.Flex** (Ampere, 1 OCPU / 6 GB is plenty) — or VM.Standard.E2.1.Micro
- Networking: keep "Assign a public IPv4 address" on
- SSH keys: **Generate a key pair for me → Save private key** (e.g. `~/Downloads/ssh-key-2026-09-29.key`)

## 2. Open the game ports (Oracle console)
Instance → Subnet → Security list (Default Security List) → **Add Ingress Rules**, source CIDR `0.0.0.0/0`:

| Protocol | Destination port | What |
|---|---|---|
| UDP | 7777-7779 | match, voice, gateway |
| TCP | 5080 | REST / health (optional) |

## 3. Deploy from the Mac
```bash
./Server/deploy/deploy-oracle.sh <public-ip> <private-key> <opc|ubuntu>
# current: ./Server/deploy/deploy-oracle.sh 144.24.139.103 ~/.ssh/rilo_server.key opc
```
Installs `/opt/rilo`, a `rilo` systemd service (auto-restarts, starts on boot) and opens the VM's own firewall.
Re-run the same command to update the server; the player database is kept.

## 4. Point the game at it
`./Tools/boot/boot.sh server udp://<public-ip>:7779` — every installed app switches on its next launch (no rebuild).
(`Client/Assets/Veil/Resources/server_default.txt` is only the fallback built into new APKs.)

## Useful
```bash
ssh -i <key> opc@<ip> 'sudo journalctl -u rilo -f'     # live log
ssh -i <key> opc@<ip> 'sudo systemctl restart rilo'
curl http://<ip>:5080/api/health
```

## Moving the server (no new APK)
Every app reads `config/boot.json` from this GitHub repo at startup (cached on the device). After deploying to a new machine:
```bash
./Tools/boot/boot.sh server udp://<new-ip>:7779
```
Other switches: `boot.sh message "…"`, `boot.sh maintenance on|off`, `boot.sh minbuild N`, `boot.sh latestbuild N`, `boot.sh updateurl <link>`, `boot.sh show`.
Players who typed a server in Settings keep it; an empty SERVER field = AUTO (boot config).
