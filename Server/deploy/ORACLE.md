# Rilo server on Oracle Cloud (Always Free)

## 1. Create the VM (Oracle console)
Compute → Instances → **Create instance**
- Image: **Canonical Ubuntu 24.04** (or 22.04)
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
./Server/deploy/deploy-oracle.sh <public-ip> ~/Downloads/<your-key>.key ubuntu
```
Installs `/opt/rilo`, a `rilo` systemd service (auto-restarts, starts on boot) and opens the VM's own firewall.
Re-run the same command to update the server; the player database is kept.

## 4. Point the game at it
`Client/Assets/Veil/Resources/server_default.txt` = `udp://<public-ip>:7779`, rebuild the apps.

## Useful
```bash
ssh -i <key> ubuntu@<ip> 'sudo journalctl -u rilo -f'     # live log
ssh -i <key> ubuntu@<ip> 'sudo systemctl restart rilo'
curl http://<ip>:5080/api/health
```
