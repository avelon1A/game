#!/usr/bin/env bash
# Generate the 5 VEIL characters with Meshy, then rebuild the game with them.
#   ./generate_characters.sh --dry-run        (free: checks key, shows animations + credit cost)
#   ./generate_characters.sh --only vanguard  (one character first)
#   ./generate_characters.sh                  (all five)
set -euo pipefail
cd "$(dirname "$0")"
.venv/bin/python meshy_pipeline.py "$@"
[[ " $* " == *" --dry-run "* ]] && exit 0
U="/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity"
"$U" -batchmode -quit -projectPath ../../Client -executeMethod Veil.EditorTools.BuildScript.BuildMac -logFile ../../Client/Logs/build.log
grep -E "\[VEIL\]" ../../Client/Logs/build.log || true
echo "Game rebuilt: open ../../Builds/Mac/VEIL.app"
