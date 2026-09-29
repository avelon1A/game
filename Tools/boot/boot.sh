#!/usr/bin/env bash
# Change the live boot config that every installed Rilo app reads at startup (no new APK needed).
# It edits config/boot.json, commits and pushes; apps pick it up on their next launch (usually within a minute).
#   ./Tools/boot/boot.sh show
#   ./Tools/boot/boot.sh server udp://1.2.3.4:7779      # move everyone to a new server
#   ./Tools/boot/boot.sh message "Double XP this weekend!"   # lobby announcement ("" clears it)
#   ./Tools/boot/boot.sh maintenance on|off              # pause / resume online play (message explains)
#   ./Tools/boot/boot.sh minbuild 3                      # builds below 3 must update to play online
#   ./Tools/boot/boot.sh latestbuild 3                   # builds below 3 see "new version available"
#   ./Tools/boot/boot.sh updateurl https://...           # where Settings → UPDATE goes
set -euo pipefail
cd "$(dirname "$0")/../.."
FILE=config/boot.json
cmd="${1:-show}"; val="${2:-}"
if [ "$cmd" = show ]; then cat "$FILE"; echo; exit 0; fi
python3 - "$FILE" "$cmd" "$val" <<'PY'
import json, sys
path, cmd, val = sys.argv[1:4]
d = json.load(open(path))
key = {"server": "server", "message": "message", "maintenance": "maintenance", "minbuild": "minBuild",
       "latestbuild": "latestBuild", "updateurl": "updateUrl"}.get(cmd)
if key is None: sys.exit(f"unknown setting '{cmd}'")
if key == "maintenance": val = val.lower() in ("on", "true", "1", "yes")
elif key in ("minBuild", "latestBuild"): val = int(val)
d[key] = val
json.dump(d, open(path, "w"), indent=2); open(path, "a").write("\n")
print(json.dumps(d, indent=2))
PY
git add "$FILE"
git commit -q -m "Boot config: $cmd ${val:-(cleared)}"
git push -q origin HEAD
echo "Published. Apps use it on their next launch."
