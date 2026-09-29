#!/usr/bin/env bash
# FREE character pipeline (all local on this Mac):
#   concept cutout (masks/<name>.png) -> Hunyuan3D-2mini shape (MPS) -> MediaPipe 2D pose -> Blender:
#   cleanup, texture bake from the concept art, skeleton, skin weights, 11 animations -> FBX -> Unity prefab -> game build.
# Usage: ./build_characters.sh [names...]      (default: vanguard pixie shade nova bolt)
#        SKIP_SHAPE=1 ./build_characters.sh    (reuse existing meshes, only re-rig/re-texture)
set -euo pipefail
cd "$(dirname "$0")"
NAMES=("${@:-vanguard pixie shade nova bolt}")
[ $# -eq 0 ] && NAMES=(vanguard pixie shade nova bolt)
if [ -z "${SKIP_SHAPE:-}" ]; then
  (cd ../Hunyuan3D-2 && PYTORCH_ENABLE_MPS_FALLBACK=1 .venv/bin/python gen_local.py "${NAMES[@]}")
fi
.venvpose/bin/python pose2d.py "${NAMES[@]}" || true
for n in "${NAMES[@]}"; do
  /Applications/Blender.app/Contents/MacOS/Blender -b -P blender/build_character.py -- "$n" --preview 2>&1 | grep -E "\[veil\]" || true
done
U="/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity"
"$U" -batchmode -quit -projectPath ../../Client -executeMethod Veil.EditorTools.BuildScript.BuildMac -logFile ../../Client/Logs/build.log
grep -E "\[VEIL\]" ../../Client/Logs/build.log || true
echo "Done. Previews in Tools/ai3d/renders, game in Builds/Mac/VEIL.app"
