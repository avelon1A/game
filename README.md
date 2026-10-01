# Rilo — Squad Mind-Game Arena (4 × 4)

> 4 squads of 4. 5–15 minutes. One arena. Infinite decisions.

**Rilo** (codename *VEIL* in code, namespaces and folders) is a Unity 6.3 LTS (URP) squad game for Android and macOS,
with an authoritative .NET 8 server (matches, Gateway, friends/parties, voice relay, REST + SQLite) running on Oracle Cloud.
The arena, VFX, UI icons, sound and music are generated in code; the five heroes are Meshy models rigged by the Blender pipeline below.

## Play it now

```bash
open Builds/Mac/Rilo.app            # macOS
adb install -r Builds/Android/Rilo.apk   # Android (or send the APK to the phone)
```

The lobby opens in **ONLINE SQUADS** and connects to the cloud server automatically (see *Online* below).
**READY** queues your party; empty seats are filled with bots. **CHANGE MODE** → *Squad vs Bots* is offline practice
(lasts until the app is closed). Pick 5 / 10 / 15-minute matches in the lobby. In a match, **F9** skips 60 seconds (testing).
On phones: left thumb moves, right side looks, **FIRE** hold, **DASH / PULSE / DECOY**, hold **TALK** for voice; gyro aim in Settings.

| Key | Action |
|---|---|
| WASD | move (camera-relative) · Mouse: look/aim · Scroll: camera distance |
| LMB | blaster · Space jump · Shift sprint |
| Q / E / R | **Dash** / **Pulse** (reveal + pop decoys) / **Decoy** (a fake you that keeps running) |
| 1 / 2 / 3 | Market: speed boost / shield / key |
| V | push-to-talk (squad voice) · Tab players · Esc pause |

**How to win:** your **squad** places by the sum of its members' scores. Each squad gets a squad objective (+400,
visible only to that squad), and every player still has a hidden primary (+500) and secondary (+250) objective, e.g. Control the Tower,
Collect Cores, Unlock the Vault with 3 keys, Capture 2 locations, Finish with 70% energy, or Stop a specific enemy.
Squadmates share vision, can't damage each other and respawn near each other; zones are captured by squads.
Score also comes from resources, territory, eliminations (diminishing returns), survival and clever ability plays.
Nobody sees everything: vision is limited, Ruins hide you, the Reactor exposes you, gunfire pings the minimap,
Tower control grants periodic full sight, and the arena collapses toward the center in the last minutes.

## Online: parties, friends, voice

**Production server:** Oracle Cloud (Hyderabad) `144.24.139.103` — UDP 7779 Gateway · UDP 7777 matches · UDP 7778 voice ·
TCP 5080 REST (`http://144.24.139.103:5080/api/health`). Runs as the systemd service `rilo`.

**Which server the apps use** comes from the remote **boot config** — no new APK when the server moves:
<https://github.com/avelon1A/game/blob/main/config/boot.json> (read by every app at startup, cached on the device).

```bash
./Tools/boot/boot.sh show
./Tools/boot/boot.sh server udp://NEW-IP:7779      # move every installed app to a new server
./Tools/boot/boot.sh message "Tournament tonight"   # lobby announcement ("" clears)
./Tools/boot/boot.sh maintenance on|off             # pause / resume online play
./Tools/boot/boot.sh latestbuild 3 | minbuild 3     # "update available" / "update required" (below build 3)
./Tools/boot/boot.sh updateurl https://…            # target of Settings → UPDATE
```

Settings → **SERVER** empty = *AUTO* (boot config); typing an address pins that device to it. Bump `BootConfig.Build`
(`Client/Assets/Veil/Runtime/App/BootConfig.cs`, also the Android versionCode) for every APK you release.

**Local / LAN test server** (optional):

```bash
./Server/run-server.sh                 # TCP 5080 (REST + Gateway /ws) · UDP 7777 matches · UDP 7778 voice · UDP 7779 Gateway
./Server/run-server.sh --mm-wait 20    # wait up to 20 s for other parties before bots fill the match
```

Point a device at it in Settings → SERVER (e.g. `192.168.1.6` or `udp://192.168.1.6:7779`); clear the field to go back to AUTO.

In the game: the lobby puts you in your own room → **COPY CODE** (friends use **JOIN CODE**) or **INVITE FRIEND** →
everyone **READY** → the leader presses **START**.
Parties stay together as one squad; the matchmaker fills the other squads with queued parties, then bots.
**FRIENDS**: add by ID (`Name#1234`), accept/decline requests, see who's online / in a party / in a match, invite or join.
Leader controls: invite, kick, make leader, start / cancel. Leaving a match early → **REJOIN** (a bot plays for you meanwhile).
Voice: push-to-talk (**V**, or hold **TALK** on phones), open mic or off; mute / volume per squadmate; only your squad hears you.

Architecture and roadmap: [docs/SQUAD_PLAN.md](docs/SQUAD_PLAN.md) · networking & boot config: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Deploy / operate the server

Full steps (creating the Oracle VM, ports, keys): [Server/deploy/ORACLE.md](Server/deploy/ORACLE.md).

```bash
./Server/deploy/deploy-oracle.sh 144.24.139.103 ~/.ssh/rilo_server.key opc   # build + upload + (re)start; keeps the DB
ssh -i ~/.ssh/rilo_server.key opc@144.24.139.103 'sudo journalctl -u rilo -f'   # live log
```

**Admin dashboard** (head users): <http://144.24.139.103:5080/admin> — live players, parties, matches, accounts, activity
chart and server health, refreshed every 5 s. Each admin has a personal key:

```bash
./Tools/admin/admin.sh add <name>      # new key → clipboard (and ~/.rilo/admin_keys); send it to that person privately
./Tools/admin/admin.sh remove <name>   # revoke
./Tools/admin/admin.sh list
```

The dashboard runs over plain HTTP: keys are only as private as the network. For real players add a domain + HTTPS.

New machine → run `deploy-oracle.sh NEW-IP …`, then `./Tools/boot/boot.sh server udp://NEW-IP:7779`.
The script detects x64 / ARM, installs `/opt/rilo`, opens the VM firewall, adds swap on small VMs and prints a health check.
The Oracle **security list** must allow UDP 7777-7779 and TCP 5080 (already set on `vcn-20260929-2318`).
Current VM runs on the free-trial credit (VM.Standard3.Flex): before the trial ends (~29 Oct 2026) either upgrade to
Pay As You Go or move to an Always Free Ampere A1 VM (deploy + `boot.sh server`).
REST: `GET /api/health`, `POST /api/players/register`, `GET|PUT /api/players/{id}`, `GET /api/leaderboard`,
`GET /api/matches/recent`, `GET /api/servers` · WebSocket Gateway: `/ws`.

## Develop

* **Unity:** Unity Hub → *Add project from disk* → `Client/` (Unity 6000.3.25f1). Open `Assets/Veil/Scenes/Main.unity`, press Play.
  The menu **VEIL → Setup Project** regenerates materials/scene/settings; **VEIL → Build macOS Player / Build Android APK** build to `Builds/`.
  App name, icon (`Assets/Veil/Icons/`, adaptive on Android) and version come from `ProjectSetup.cs`.
* **Batch builds:**
  ```bash
  /Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath Client -buildTarget StandaloneOSX -executeMethod Veil.EditorTools.BuildScript.BuildMac -logFile -
  /Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath Client -buildTarget Android -executeMethod Veil.EditorTools.BuildScript.BuildAndroid -logFile -
  ```
* **Phone UI:** text is scaled up on phones (`UIKit.Fs`); run the Mac build with `-mobile-ui` to preview the phone layout.
  Lobby micro animations live in `Runtime/UI/MicroFx.cs`.
* **Server:** `cd Server && ~/.dotnet/dotnet build` (the server compiles the *same* simulation source files as the client).

### Tests

| Command | What it checks |
|---|---|
| `cd Server/Veil.Server && ~/.dotnet/dotnet run -c Release -- --selftest 300 2` | 2 headless 4×4 matches: squads full, no friendly fire, allies always visible, tick cost, snapshot round-trip |
| `cd Server/Veil.LoadTest && ~/.dotnet/dotnet run -c Release` (server running with `--mm-wait 2`) | end-to-end: register → friends → party/invite/code/kick/promote → queue → squads → ticket join → voice relay (squad-only) → rejoin → results |
| `Rilo.app/Contents/MacOS/Rilo -autotest -shotdir /tmp/shots` (add `-mobile-ui` for the phone layout) | full UI flow + autopilot match, screenshots of every screen |
| `… -autotest-scripted` | offline match through the real input → prediction path |
| `… -autotest-online 127.0.0.1` | Gateway → room → queue → online squad match → results/XP → back in the party |
| `… -bootconfig file:///path/boot.json` | test a boot config (server switch, message, maintenance, update prompts) without publishing it |

Last verified results: tick ≈ 0.1 ms for 16 players in 4 squads (budget 33 ms), snapshots ≤ 720 B, squad flow test all green,
online prediction error ≈ 1 cm average.

## Layout

```text
docs/        PLAN.md (milestones) · ARCHITECTURE.md (systems, networking, API) · SQUAD_PLAN.md (squads, friends, voice)
config/      boot.json — live remote config read by every installed app (server, message, maintenance, builds)
Client/      Unity project — Assets/Veil/{Sim, Runtime, Editor, Shaders, Resources, Icons, Characters}
Server/      Veil.Server (game server + Gateway + voice + REST + SQLite) · Veil.LoadTest · run-server.sh · deploy/ (Oracle)
Tools/       ai3d/ (character pipeline) · boot/boot.sh (edit the boot config)
Builds/      Mac / Android players, server publishes (generated, not in git)
```

`Client/Assets/Veil/Sim` is pure C# with no Unity references. It is **the game rules**, shared by the offline client,
the server and the load test. Tune gameplay numbers in `Sim/Config/GameConfig.cs`.

## Characters (free AI + Blender pipeline)

The five heroes are generated from the concept art, entirely free and on this Mac:

1. `Tools/ai3d/masks/*.png` — characters cut out of the concept art (rembg, anime model)
2. **Hunyuan3D-2mini** (Tencent, open source) runs locally on Apple Silicon → 3D shape
3. **MediaPipe** pose → arm joints; mesh analysis → legs, neck, crotch
4. **Blender** (headless script `Tools/ai3d/blender/build_character.py`): cleanup, decimate, UVs, bakes a 2K texture
   by projecting the concept art onto the front (full detail) + edge-sampled colours for the back, builds the skeleton,
   automatic skin weights (+ arm/hip cleanup), keyframes 11 animations (idle, walk, run, sprint, jump, fall, dash,
   shoot, hit, death, victory), exports FBX
5. Unity `VEIL → Build Character Prefabs` → animator controller (locomotion blend tree, air, dash, death, victory,
   upper-body shoot/hit layer) and prefabs in `Resources/Characters`

Regenerate everything (or just some characters):

```bash
./Tools/ai3d/build_characters.sh                 # all five
SKIP_SHAPE=1 ./Tools/ai3d/build_characters.sh    # keep meshes, redo texture/rig/animations
```

Add a new character: put a cutout PNG in `Tools/ai3d/masks/<name>.png` (plus `input/<name>_clean.png`), run the script
with `<name>`, and map it in `CharacterRig.ModelNames`. If no model exists, the game falls back to the procedural rig.

### Using a finished textured model (e.g. from Meshy)

A ready-made textured GLB keeps its own texture and is only rigged + animated by the pipeline:

```bash
cd Tools/ai3d
/Applications/Blender.app/Contents/MacOS/Blender -b -P blender/render_model_front.py -- source_models/shade_meshy.glb shade
.venvpose/bin/python pose2d.py shade_model      # or hand-mark joints/shade_model.json if detection fails (Shade: hand-marked)
/Applications/Blender.app/Contents/MacOS/Blender -b -P blender/build_character.py -- shade --src source_models/shade_meshy.glb --preview
```

Meshy FBX zips: `blender -b -P blender/fbx_to_glb.py -- <model.fbx> <model_texture.png> source_models/<name>_meshy.glb` first.
Glow: `--glow cyan|pink|ring|lime|none` (Shade cyan, Pixie pink, Nova ring, Bolt lime, Vanguard none). All five heroes use Meshy models.
Rigging repairs for welded multi-piece meshes run automatically (proxy weights, firm arm/body split, ribbon removal); textured
characters render double-sided so removed seams read as shadow. The model must face -Y (front) with Z up, arms down. A glow map is derived from its bright cyan/blue texels; Unity gives
textured models more toon lighting (`<name>_textured.txt` marker). Shade uses `Tools/ai3d/source_models/shade_meshy.glb`.

## Credits

Blasters: Kenney Blaster Kit (CC0), re-centred on import by `Editor/WeaponImport.cs`.
Fonts: Lilita One and Chakra Petch (SIL Open Font License, Google Fonts). Networking: LiteNetLib (MIT).
Heroes (Ranger, Huntress, Warden, Scout, Drifter, Wanderer): Quaternius (CC0) — Modular Character Outfits Fantasy, Universal Base Characters, Universal Animation Library, reshaped to stylized proportions by `Tools/ai3d/blender/build_heroes.py`. The earlier Meshy heroes are archived in `Tools/ai3d/meshy_heroes_archive`.
Character generation: Hunyuan3D-2 (Tencent Hunyuan community license), MediaPipe (Apache 2.0), rembg (MIT), Blender (GPL; output assets are yours).
