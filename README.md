# VEIL — Squad Mind-Game Arena (4 × 4)

> 4 squads of 4. 15 minutes. One arena. Infinite decisions.

A playable Unity 6.3 LTS (URP) prototype of the VEIL GDD, plus a small authoritative test server
and REST backend in .NET 8. Everything (characters, arena, VFX, UI icons, sound, music) is generated
in code, so no paid or binary art assets are needed.

## Play it now

```bash
open Builds/Mac/VEIL.app
```

Click through the title → **PLAY** tab → **VS BOTS** → **READY** → your squad (you + 3 bots) plays 3 bot squads.
Pick 5 / 10 / 15-minute matches in the lobby. In a match, **F9** skips 60 seconds (for testing phases).

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

## Online (test server): parties, friends, voice

```bash
./Server/run-server.sh                 # TCP 5080 (REST + Gateway /ws) · UDP 7777 (matches) · UDP 7778 (voice)
./Server/run-server.sh --mm-wait 20    # wait up to 20 s for other parties before bots fill the match
```

In the game: **PLAY → ONLINE** (phones find the server on your Wi-Fi automatically; on PCs type its LAN IP) →
**CREATE ROOM** → share the 6-letter code or **FRIENDS → INVITE** → everyone **READY** → the leader presses **START**.
Parties stay together as one squad; the matchmaker fills the other squads with queued parties, then bots.
**FRIENDS**: add by ID (`Name#1234`), accept/decline requests, see who's online / in a party / in a match, invite or join.
Leader controls: invite, kick, make leader, start / cancel. Leaving a match early → **REJOIN** (a bot plays for you meanwhile).
Voice: push-to-talk (**V**, or hold **TALK** on phones), open mic or off; mute / volume per squadmate; only your squad hears you.

Architecture and roadmap: [docs/SQUAD_PLAN.md](docs/SQUAD_PLAN.md).
REST: `GET /api/health`, `POST /api/players/register`, `GET|PUT /api/players/{id}`, `GET /api/leaderboard`,
`GET /api/matches/recent`, `GET /api/servers` · WebSocket Gateway: `/ws`.

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
| `cd Server/Veil.Server && ~/.dotnet/dotnet run -c Release -- --selftest 300 2` | 2 headless 4×4 matches: squads full, no friendly fire, allies always visible, tick cost, snapshot round-trip |
| `cd Server/Veil.LoadTest && ~/.dotnet/dotnet run -c Release` (server running with `--mm-wait 2`) | end-to-end: register → friends → party/invite/code/kick/promote → queue → squads → ticket join → voice relay (squad-only) → rejoin → results |
| `VEIL.app/Contents/MacOS/VEIL -autotest -shotdir /tmp/shots` | full UI flow + autopilot match, screenshots of every screen |
| `… -autotest-scripted` | offline match through the real input → prediction path |
| `… -autotest-online 127.0.0.1` | Gateway → room → queue → online squad match → results/XP → back in the party |

Last verified results: tick ≈ 0.1 ms for 16 players in 4 squads (budget 33 ms), snapshots ≤ 720 B, squad flow test all green,
online prediction error ≈ 1 cm average.

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
