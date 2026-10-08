# RILO heroes — roster and pipeline

## Current roster (2026-10-09)

| Slot (game id) | Model (Meshy download) | Idle | Run | Sprint | Store |
|---|---|---|---|---|---|
| Vanguard (`hero:0`) | Orange Vanguard | Idle 11 | own Running | own RunFast | free |
| Volt (`hero:1`) | Hazard Vanguard (yellow/black) | Idle 11 | own Running | Vanguard's RunFast (copied) | 600 |
| Lyra (`hero:2`) | Crimson Kunoichi (red/black) | Idle 11 | Vanguard's Running (copied) | Vanguard's RunFast (copied) | free |
| Nova (`hero:3`) | Arctic Cyber Valkyrie (white/black) | Idle 11 | own Running | own RunFast | 800 |
| Sol (`hero:4`) | Neon Sentinel (white/black robot) | Idle 11 | own Running | Vanguard's RunFast (copied) | 1200 |

Everything a hero lacks (jump, hits, punches, fall, dash, shoot, death, victory, downed/revive) comes from the shared
RILO pack (`Characters/_anim/rilo_anims.fbx`), then the Universal Animation Library.

Names / store descriptions in `Sim/Config/StoreCatalog.cs` and `Palette.Outfits` still describe the old designs — update
them when the new looks are final.

## Add or replace a hero

1. In Meshy, download the rigged character (biped, **GLB, Mixamo skeleton**, "with skin") together with its animations.
   Include at least **Idle 11**, **Walking**, **Running** and ideally **RunFast**.
2. Unzip into its own folder, then build the game FBX (mesh + rig + all clips, 2 m tall, facing −Y):

   ```
   Blender -b -P Tools/ai3d/blender/build_meshy_rigged.py -- <folder with *_withSkin.glb> <out dir> <hero> \
       idle_11=idle jump_run=jump runfast=sprint counterstrike=skill gun_hold_left_turn=gun_idle ...
   ```
   (`meshy_name=game_clip` pairs; defaults: walking→walk, running→run, run_03→run_alt, runfast→sprint, agree_gesture→victory.)
3. No RunFast in the download? Copy Vanguard's bone-by-bone (no humanoid retargeting):

   ```
   Blender -b -P Tools/ai3d/blender/copy_clips.py -- <hero>.fbx Characters/vanguard/vanguard.fbx <out>.fbx sprint
   ```
4. Copy `<hero>.fbx` and `<hero>_albedo.png` into `Client/Assets/Veil/Characters/<hero>/`. If the skeleton changed,
   reset the importer by replacing `<hero>.fbx.meta` with just `fileFormatVersion: 2` + its existing `guid:` line
   (keeps every reference).
5. New Meshy heroes keep their own bind pose as the humanoid reference: add the name to `OwnBindPose` in
   `Editor/CharacterBuilder.cs` (forcing the spine straight bent Lyra's runs far too much).
6. `Unity -batchmode -quit -projectPath Client -executeMethod Veil.EditorTools.CharacterBuilder.BuildAll`
7. Check: `-executeMethod Veil.EditorTools.PoseShots.Run` (every hero standing, front + side) and
   `PoseShots.RunCompare` (run / sprint frames next to Vanguard) → `/tmp/claude-501/poses`, `/tmp/claude-501/runs`.

## Why things are set up this way (lessons)

- **Retargeting between different Meshy skeletons bends spines and arms.** Clips made for a different rig
  (the old shared library idle / sprint) leaned heroes back with their arms behind them. Prefer clips made for the
  hero's own skeleton; when borrowing, copy bone-by-bone in Blender (`copy_clips.py`) rather than humanoid retargeting.
- **The humanoid T-pose reference must not rotate the hips** (`EnsureTPose`, "tpose-v4"): turning the hips swings both
  legs and tipped the pelvis on rigs whose spine joint sits ahead of the hips.
- **Standing correction** (`CharacterRig.Upright`): while standing, the spine and head are nudged upright, with a dead
  zone so an idle that is already straight is left alone.
- Meshy's rigging API names bones differently (`Spine02`, `neck`) from Meshy downloads (`mixamorig:*`);
  `add_own_idle.py` handles both.

## Tools (Tools/ai3d/blender)

| Script | Does |
|---|---|
| `build_meshy_rigged.py` | Meshy rigged GLBs → game FBX with all clips |
| `copy_clips.py` | copy clips bone-by-bone from one hero to another |
| `add_own_idle.py` | make a relaxed idle on a hero's own skeleton (shoulder weight smoothing, casual variant in history) |
| `make_stand_idle.py` | relaxed idle for the shared pack (Cyber Violet rig) |
| `fix_proportions.py` | lengthen arms / widen shoulders / scale hands & feet, keeps clips |
| `fix_hips.py` | move a misplaced Meshy hips joint in line with spine and legs |
| `optimize_hero.py` | clean + decimate a game hero to a triangle budget, keeps clips |
| `inspect_heroes.py` | proportion / mesh-health measurements for every hero |
| `render_reference.py` | clean front/side/back T-pose renders (Meshy multi-image input) |
| `rilo_characters_master.py` | full lineup pass: LOD0–3, test poses, presentation renders, VALIDATION.md (`Art/rilo_final`) |
| `build_meshy_vehicle.py` | Meshy vehicle GLB → FBX with a separate spinning rotor |
