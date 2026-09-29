# VEIL — 15-Player Competitive Mind-Game Arena

> 15 players. 15 minutes. One arena. Infinite decisions.

A playable Unity 6.3 LTS (URP) prototype of the VEIL GDD, plus a small authoritative test server
and REST backend in .NET 8. Everything (characters, arena, VFX, UI icons, sound, music) is generated
in code, so no paid or binary art assets are needed.

## Play it now

```bash
open Builds/Mac/VEIL.app
```

Click through the title → **PLAY** tab → **READY** → a match against 14 bots starts after a 3-second countdown.
Pick 5 / 10 / 15-minute matches in the lobby. In a match, **F9** skips 60 seconds (for testing phases).

| Key | Action |
|---|---|
| WASD | move (camera-relative) · Mouse: look/aim · Scroll: camera distance |
| LMB | blaster · Space jump · Shift sprint |
| Q / E / R | **Dash** / **Pulse** (reveal + pop decoys) / **Decoy** (a fake you that keeps running) |
| 1 / 2 / 3 | Market: speed boost / shield / key |
| Tab | players · Esc pause |

**How to win:** each player gets a hidden primary (+500) and secondary (+250) objective, e.g. Control the Tower,
Collect Cores, Unlock the Vault with 3 keys, Capture 2 locations, Finish with 70% energy, or Stop a specific player.
Score also comes from resources, territory, eliminations (diminishing returns), survival and clever ability plays.
Nobody sees everything: vision is limited, Ruins hide you, the Reactor exposes you, gunfire pings the minimap,
Tower control grants periodic full sight, and the arena collapses toward the center in the last minutes.

## Online (test server)

```bash
./Server/run-server.sh                 # UDP 7777 + REST http://localhost:5080  (SQLite: Server/Veil.Server/veil.db)
./Server/run-server.sh --match-seconds 120 --players 15 --name "LAN Test"
```

In the game: **PLAY → ONLINE → CONNECT** (default host `127.0.0.1`; use the server machine's LAN IP for other PCs),
then **READY**. The match starts when every connected human is ready; empty slots are filled with server-side bots.
Results, XP, level and rating are stored by the backend (**LEADERBOARD** tab).

REST: `GET /api/health`, `POST /api/players/register`, `GET|PUT /api/players/{id}`, `GET /api/leaderboard`,
`GET /api/matches/recent`, `GET /api/servers`, `POST /api/matchmaking/join`.

## Develop

* **Unity:** Unity Hub → *Add project from disk* → `Client/` (Unity 6000.3.25f1). Open `Assets/Veil/Scenes/Main.unity`, press Play.
  The menu **VEIL → Setup Project** regenerates materials/scene/settings; **VEIL → Build macOS Player** builds to `Builds/Mac`.
* **Batch build:**
  ```bash
  /Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath Client -executeMethod Veil.EditorTools.BuildScript.BuildMac -logFile -
  ```
* **Server:** `cd Server && ~/.dotnet/dotnet build` (the server compiles the *same* simulation source files as the client).

### Tests

| Command | What it checks |
|---|---|
| `cd Server/Veil.Server && ~/.dotnet/dotnet run -c Release -- --selftest 300 2` | 2 headless 15-bot matches: map validity, all systems firing, tick cost, snapshot round-trip |
| `cd Server/Veil.LoadTest && ~/.dotnet/dotnet run -c Release -- 15` | 15 network clients complete a match (snapshot rate, bandwidth) |
| `VEIL.app/Contents/MacOS/VEIL -autotest -shotdir /tmp/shots` | full UI flow + autopilot match, screenshots of every screen |
| `… -autotest-scripted` | offline match through the real input → prediction path |
| `… -autotest-online 127.0.0.1` | REST register → UDP lobby → online match → results/XP |

Last verified results: tick 0.08 ms for 15 players (budget 33 ms), snapshots ≤ 620 B, 15 clients at 15 Hz ≈ 8 KB/s each,
online prediction error ≈ 6 mm average.

## Layout

```text
docs/        PLAN.md (milestones) · ARCHITECTURE.md (systems, networking, API)
Client/      Unity project — Assets/Veil/{Sim, Runtime, Editor, Shaders, Resources}
Server/      Veil.Server (game server + REST + SQLite) · Veil.LoadTest · run-server.sh
Builds/      macOS player (generated)
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

## Credits

Fonts: Lilita One and Chakra Petch (SIL Open Font License, Google Fonts). Networking: LiteNetLib (MIT).
Character generation: Hunyuan3D-2 (Tencent Hunyuan community license), MediaPipe (Apache 2.0), rembg (MIT), Blender (GPL; output assets are yours).
