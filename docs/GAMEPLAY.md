# Rilo — Current Gameplay

> 4 squads of 4 · 5 / 10 / 15-minute matches · one arena · outthink the other squads.

## 1. The match

- **16 players**: 4 squads × 4. Empty seats are filled by bots (Explorer, Collector, Hunter, Defender, Opportunist).
- **Arena**: 150 m × 150 m with a central **Tower**, plus **Reactor**, **Market**, **Vault**, **Ruins** and capturable zones.
- **Length**: 5, 10 or 15 minutes, chosen in the lobby by the squad leader.
- **Winning (extraction mode, default)**: the **first squad to complete the extraction wins** and the match ends immediately. If time runs out, squads rank by extraction progress, then route progress, then score.

### The route (every squad, same distances)
| Step | Task | Rule |
|---|---|---|
| Objective 1 | **Hack Terminal** | one **shared** terminal in the central plaza, own progress per squad; 14 s base, 1/2/3/4 hackers = 100/150/175/190 %; instability at 35 % and 67 % spawns Destroy / Stabilize / Override nodes (see `docs/HACK_TERMINAL.md`) |
| Objective 2 | **Capture Zone** | hold your zone (5 m) for 12 s, faster with more squadmates |
| Objective 3 | **Collect Cores** | your squad picks up 3 energy cores |
| Vault | **Central Vault** | channel at the centre (6 m) for 10 s |
| Extraction | **Hold the point** | 60 s, only squads that opened their Vault progress |

- Enemies near your site **pause** your progress (contest). Completing a step briefly reveals enemies near it.
- The first Vault opening **reveals the extraction point to everyone** (between two squads, away from the opener).
- Extraction: one squad inside = it progresses; two squads inside = **contested**, nobody progresses; leaving pauses it.
- After the reveal, re-entry takes **15 s** instead of 5 s. The collapse never closes over the extraction point.
- HUD: current step + distance, route dots and every rival squad's step, extraction status; gold beam = your site, big ring = extraction.

### Phases
| Phase | What changes |
|---|---|
| **Exploration** | Gather, scout. Damage is halved. |
| **Competition** | Full combat, zones contested. |
| **Manipulation** | Abilities and deception matter most. |
| **Collapse** | The arena closes towards the centre. |
| **Final** | Holding the Tower at the end gives **+300**. |

## 2. Heroes (cosmetic only, no stat differences)

| Hero | Look | Lobby |
|---|---|---|
| Vanguard | orange / white suit | standard idle |
| Volt | neon cyan techwear | idle + occasional pose |
| Lyra | violet cyber (female) | own idle + 3 idle actions |
| Nova | pink techwear (female) | idle + waves hello |
| Sol | gold / white paladin | idle + dance / flip |

All are Meshy-rigged models (own walk / run); other moves are retargeted from the animation library.
Pick in **Characters → HERO** plus a **glow colour**; bots get random heroes. New heroes: `Tools/ai3d/blender/build_meshy_rigged.py` (see its header).

## 3. Controls

| Action | PC / Mac | Phone |
|---|---|---|
| Move | WASD (camera-relative) | left thumb stick |
| Look / aim | mouse | right side drag (+ optional gyro) |
| Shoot | LMB | FIRE (hold) |
| Jump | Space | JUMP |
| Sprint | Shift | push the stick far |
| Dash | Q | DASH |
| Pulse | E | PULSE |
| Decoy | R | DECOY |
| Market buy | 1 / 2 / 3 | Market buttons |
| Voice | hold V | hold TALK |
| Pause / players | Esc / Tab | ❚❚ / LIST |

## 4. Combat

| Stat | Value |
|---|---|
| Health | 100, regenerates 7/s after 5 s without damage |
| Blaster | 11 damage per hit, 0.3 s between shots, instant hit (bolts cross the 30 m range in one tick) |
| Knockback | small push on hit |
| Respawn | 5 s after elimination (15 s once extraction is revealed) at **your squad's spawn**, then 2 s spawn protection. While dead you **spectate** squadmates (1–4 / click / tap to switch) |
| Assists | damaging an enemy within 8 s before their elimination: +30 |
| On death | you keep 60 % of your energy |
| Friendly fire | **off**: squadmates can't hurt each other |
| Noise | firing shows you on nearby enemies' minimaps for 1.5 s |

## 5. Energy & abilities

Energy: max 100, start 50. Gained from **orbs (+8)**, **cores (+15)** and the **Reactor (+7/s, +3/s more for its controller)**.

| Ability | Cost | Cooldown | Effect |
|---|---|---|---|
| **Dash** | 15 | 3 s | quick burst (25 m/s, 0.18 s) |
| **Pulse** | 25 | 10 s | reveals enemies within 24 m for 4 s, pops enemy decoys |
| **Decoy** | 30 | 14 s | a fake you that keeps running for 5 s |

Clever ability plays (a pulse that reveals someone, a decoy that gets shot) give **+30**.

## 6. Information & stealth

- You see **32 m** around you; walls block sight.
- **Squadmates share vision** and are always visible to each other.
- **Ruins** hide you beyond 7 m; the **Reactor** exposes you.
- Holding the **Tower** reveals everyone every 12 s for 3 s.

## 7. Map locations

| Place | Use |
|---|---|
| **Tower** | territory points, periodic full sight, +300 if held at the end |
| **Reactor** | fast energy |
| **Market** | buy speed boost (20), shield +40 (25), key (45); the controller pays 60 % |
| **Vault** | bring **3 keys**, channel 2.5 s → +200 resources (30 s cooldown) |
| **Zones** | capture in 6 s (as a squad); held zones give territory points |
| **Ruins** | stealth |

Pickups: up to 45 orbs, 6 cores (respawn 18 s), 6 keys (respawn 22 s, carry max 5).

## 8. Scoring

| Source | Points |
|---|---|
| **Primary objective** (secret, personal) | +500 |
| **Secondary objective** (secret, personal) | +250 |
| **Squad objective** (shared, split among the squad) | +400 |
| Elimination | +100, halved for each repeat on the same victim (min 25) |
| Orb / core / key | +2 / +20 / +10 |
| Ability play | +30 |
| Survival | up to +150, −30 per death |
| Territory | Tower 0.6/s, zones 0.3/s |
| Final Tower holder | +300 |

**Objectives** (each player gets a random primary and secondary, the squad gets one more):
Control the Tower (90 s) · Collect 5 cores · Raid the Vault (3 keys) · Capture 2 locations · Finish with ≥ 70 % energy · Stop a specific enemy (eliminate them twice).

## 9. Online & social

- The lobby opens in **Online Squads**: you are always in your own room.
- **Squad up**: COPY CODE → friends use JOIN CODE, or **INVITE FRIEND**. Add friends by `Name#1234`.
- Everyone presses **READY**, then the leader presses **START**. The matchmaker fills the other squads with parties, then bots.
- **Leader** can invite, kick, promote, start and cancel. Leaving mid-match → **REJOIN** (a bot plays for you meanwhile).
- **Voice chat**: push-to-talk, open mic or off; your squad only; per-player mute.
- **Practice**: CHANGE MODE → *Squad vs Bots* works offline.
- **Account**: guest by default; Sign in with Google (Android) keeps your progress across phones (needs the Google client id to be configured).

## 10. Progression

XP from score → levels; an Elo-style **rating** from placement; a leaderboard. Everything is cosmetic: no pay-to-win.

---
*Values come from `Client/Assets/Veil/Sim/Config/GameConfig.cs` — change them there (and redeploy the server) to rebalance.*
