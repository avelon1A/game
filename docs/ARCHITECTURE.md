# VEIL — Technical Architecture

> Rule #1 (GDD §28): **Architecture first. Content second.** Every new feature must fit the layers below.

## 1. Big picture

```text
                ┌────────────────────────────────────────────────────────────┐
                │                 Veil.Sim  (pure C#, no Unity)              │
                │  Map · NavGrid · MatchSim · Abilities · Objectives ·        │
                │  Scoring · Phases · Bots · Visibility · Snapshot · Protocol │
                └───────────────▲───────────────────────────────▲────────────┘
                                │ same source files             │
        ┌───────────────────────┴───────────┐     ┌─────────────┴──────────────────────┐
        │   Unity Client (Client/)          │     │  Veil.Server (.NET 8, Server/)     │
        │                                   │     │                                    │
        │  LocalMatchDriver  (offline/bots) │     │  GameServer  – UDP (LiteNetLib)    │
        │  NetMatchDriver    (online)  ─────┼─UDP─┼─▶ authoritative MatchSim @30Hz     │
        │  Prediction + interpolation       │     │  Lobby / ready / countdown / bots  │
        │  Views: world, chibi characters,  │     │                                    │
        │  VFX, camera, minimap, audio      │     │  REST API – ASP.NET minimal API    │
        │  UI: menu, customize, lobby, HUD, │─HTTP┼─▶ profiles, stats, leaderboard,    │
        │      results                      │     │   match history, server list       │
        └───────────────────────────────────┘     │  SQLite (veil.db)                  │
                                                  └────────────────────────────────────┘
```

The **simulation is the game**. Unity only renders it and collects input. The server runs
the exact same simulation code, so offline play, online play and bots all obey the same rules.

## 2. Why a shared pure-C# simulation?

* **Authoritative from day one** (GDD §19): the server owns the state; clients send inputs only.
* **Hidden information is real**: the server sends each client only what that player can
  see (`Visibility.cs`). Decoys are sent exactly like players, so they are truly indistinguishable.
* **One rule set**: offline mode builds snapshots with the same `SnapshotBuilder` the server
  uses, so the client code path is identical online and offline.
* **Testable without Unity**: the server can run full 15-bot matches headless.

The simulation is 2.5D: movement/collision happen on the XZ plane (circles + oriented boxes),
jumping is a separate height axis (low walls are jumpable, tall walls are not).

## 3. Repository layout

```text
VEIL/
├── docs/                 PLAN.md, ARCHITECTURE.md
├── Client/               Unity 6.3 LTS project (URP)
│   └── Assets/Veil/
│       ├── Sim/          Veil.Sim.asmdef  (noEngineReferences = true)  ← shared with server
│       │   ├── Core/        Vec2, Rng, MathUtil
│       │   ├── Config/      GameConfig (all tunables in one place)
│       │   ├── Map/         MapData, ArenaMap (the 150m×150m arena), NavGrid (A*)
│       │   ├── Match/       MatchSim, PlayerState, entities, Objectives, Scoring, Phases
│       │   ├── Bots/        BotBrain (Explorer, Collector, Hunter, Defender, Opportunist)
│       │   └── Net/         Snapshot, SnapshotBuilder, Protocol, ByteWriter/Reader
│       ├── Runtime/      Veil.Runtime.asmdef (Unity)
│       │   ├── App/         GameApp (state machine), Profile, Settings, Bootstrap
│       │   ├── Match/       IMatchDriver, LocalMatchDriver, NetMatchDriver, ClientWorld,
│       │   │                Prediction, InputCollector
│       │   ├── View/        WorldBuilder, CharacterRig (procedural chibi), Pickup/Zone/FX
│       │   │                views, CameraRig, MaterialLib, MeshGen, Sfx
│       │   ├── UI/          UIKit (procedural UGUI), screens: MainMenu, Customize, Lobby,
│       │   │                Hud, Results, Pause
│       │   └── Net/         VeilNetClient (LiteNetLib), BackendApi (REST)
│       ├── Editor/       ProjectSetup (URP, materials, scene, build), BuildScript
│       └── Plugins/      LiteNetLib.dll
└── Server/
    ├── Veil.Server/      .NET 8: GameServer (UDP) + REST API + SQLite
    └── Veil.LoadTest/    headless fake clients to test 5 → 10 → 15 players
```

## 4. Simulation (Veil.Sim)

| System | File | Responsibility |
|---|---|---|
| Tick loop | `MatchSim.Tick()` | fixed 30 Hz: inputs → movement → abilities → combat → pickups → zones → objectives → phases |
| Movement | `Movement.cs` | accel, sprint, jump, dash, knockback, collision vs map. **Shared with client prediction.** |
| Abilities | `MatchSim.Abilities.cs` | Dash, Pulse, Decoy — energy cost + cooldown |
| Combat | `MatchSim.Combat.cs` | blaster projectiles, damage, elimination, respawn |
| Zones | `MatchSim.Zones.cs` | Tower, Reactor, Market, Ruins capture/control; Vault unlock with 3 keys |
| Objectives | `Objectives.cs` | hidden primary (+500) & secondary (+250) objectives per player |
| Scoring | `ScoreBreakdown` | Primary, Secondary, Resources, Territory, Eliminations, Survival, Bonus |
| Phases | `MatchPhases.cs` | Exploration → Competition → Manipulation → Collapse → Final (scaled to match length) |
| Visibility | `Visibility.cs` | vision radius, Ruins stealth, Pulse/Tower/Reactor reveals |
| Bots | `BotBrain.cs` | perceive (only what is visible) → score options → pick goal → path (A*) → act |

Events (`SimEvent`) are produced each tick (pulse, decoy, hit, elimination, capture, vault…)
and drive VFX, audio and the event feed.

## 5. Networking

* Transport: **LiteNetLib** (UDP). Channel 0 unreliable-sequenced (inputs, snapshots),
  channel 1 reliable-ordered (lobby, events, match start/end).
* Client → server: `Input{seq, move, yaw, buttons}` at 30 Hz, each packet carries the last
  3 inputs for loss tolerance.
* Server → client: `Snapshot` at 15 Hz, filtered per viewer (hidden information).
* **Client-side prediction** for the local player using the shared `Movement` code, with
  reconciliation on `lastProcessedInputSeq`. Remote avatars are interpolated ~100 ms behind.
* Server loop: lobby → all humans ready → 5 s countdown → fill with bots to 15 → match →
  results persisted → back to lobby.

## 6. Backend REST API (port 5080)

| Method | Route | Purpose |
|---|---|---|
| GET  | `/api/health` | liveness |
| POST | `/api/players/register` | guest account (name → id + token) |
| GET  | `/api/players/{id}` | profile, level, rating, stats |
| PUT  | `/api/players/{id}/appearance` | save customization |
| GET  | `/api/leaderboard` | top players by rating |
| GET  | `/api/matches/recent` | recent match results |
| GET  | `/api/servers` | game server list (matchmaking stub) |

Progression = mastery (GDD §22): XP from score, level curve, Elo-style rating from placement.
No pay-to-win: appearance is cosmetic only.

## 7. Client flow

```text
Boot → MainMenu ─┬─ Play (offline vs bots) ─▶ Lobby(local) ─▶ Match ─▶ Results ─▶ Play again
                 ├─ Play Online ────────────▶ Lobby(server) ─▶ Match ─▶ Results
                 ├─ Customize (outfit, hair, accessory, colors)
                 └─ Settings (match length, bots, mouse sensitivity, quality)
```

Everything visual is generated from code (primitives + procedural meshes + URP), so the
project has no binary scene/prefab churn and can be regenerated by `ProjectSetup`.

## 8. Controls

| Key | Action |
|---|---|
| WASD | move (camera-relative) |
| Mouse | look / aim |
| LMB | blaster |
| Space | jump |
| Shift | sprint |
| Q / E / R | Dash / Pulse / Decoy |
| F | interact (open Vault) |
| 1 / 2 / 3 | Market purchases (Speed, Shield, Key) |
| Tab | scoreboard · Esc: pause · Scroll: camera distance |
