# Rilo v2 — Extraction Plan

Goal: turn today's score-based match into the design-doc loop
**Objectives → Key events → Vault → Extraction → one squad wins**,
proved first in a small test arena (design doc §33), then scaled up.

> Rule for every milestone: playable on phone + Mac, bots can play it, server deployed, `docs/GAMEPLAY.md` updated.

---

## Milestone 0 — Groundwork (½ day) ✅ partly: tuning block in GameConfig; Extraction mode + boot.json tuning come with M2/M3
- New match mode **`Extraction`** next to the current score mode (old mode stays as fallback until v2 is fun).
- `GameConfig` values for the new mode in one block (timers, radii, re-entry) so tuning is one file.
- Remote tuning: the same values can be overridden from `boot.json` (no APK for balance changes).

## Milestone 1 — Downed, revive, spectate (1–2 days) ✅ done (v build 3)
| Feature | Rules (first values, tune in playtest) |
|---|---|
| Downed | 0 HP → downed, crawl slowly, 25 s bleed-out, can't shoot |
| Revive | teammate holds interact 4 s nearby; revived at 30 HP |
| Finish | enemies can finish a downed player; all squad downed = all eliminated |
| Respawn | eliminated → normal respawn timer (unlimited re-entry) |
| Spectate | while dead: follow teammates, switch with 1–4 / tap |
- Bots: revive nearby teammates, protect downed allies (Defender), finish downed enemies (Opportunist).
- Scoreboard tracks **revives** and **assists**.

## Milestone 2 — Objective chain + Secured Energy (2–3 days)
- Each squad progresses **independently** through the same chain:
  `Objective 1 → Objective 2 → Objective 3 → Key Event A + B → Vault Access`
- 3 objective types (readable, never hidden): **Hack Terminal** (hold 8 s), **Capture Zone** (hold area), **Collect** (bring N energy cores).
- Fairness: every squad gets the same objective types at equal travel time from its spawn (seeded mirrored placement).
- Energy: carried energy (60% kept on death, as today) + **Secured energy** (banked at objectives, never lost).
- Info events: completing an objective briefly reveals nearby enemies; other squads get "Squad X is hacking" alerts.
- HUD: current objective, progress bar, squad chain progress (●●●○○).

## Milestone 3 — Vault + Extraction = the win condition (2 days)
- Vault Access → channel the **central Vault** → **extraction point revealed to everyone**.
- Extraction: ~60 s, progresses only while **uncontested**; enemies inside = contested (paused); losing control pauses / slowly drains; another squad can take it.
- **Only the squad that reaches 100% wins** — match ends immediately.
- Final phase re-entry: long respawn timer (e.g. 20 s) to stop suicide waves.
- Time cap: if nobody extracts, the squad with most extraction progress / secured energy wins (no draws).
- Alerts: "EXTRACTION STARTED", "CONTESTED", "SQUAD B TOOK CONTROL".

## Milestone 4 — 100 × 100 m test arena (2 days)
- New compact map: 2 or 4 mirrored spawns, 1 terminal, 1 zone, 1 resource field, central Vault, 1 extraction pad, cover + rotation paths.
- Built from **Blender + free CC0 kits** (see Assets below).
- Bots learn the chain: rush objectives, contest Vault/extraction, retreat when outnumbered.

## Milestone 5 — Results + playtest (1 day + playtests)
- Victory / Defeat screens (extraction replay summary: who held it when).
- Scoreboard: Eliminations · Assists · Objectives · Revives (+ secured energy).
- Admin dashboard: extraction win times, contest counts → balance data.
- **Playtest gate:** is the loop fun? Tune timers via boot.json before scaling.

## Milestone 6+ — Scale up (after the gate)
- 400 × 400 m map with named regions (Rilo City, Hydro Plant, Dockyard, Ruins, Forest, Canyon, Outpost, Central Vault), phone-performance LODs + streaming.
- Solo mode queue; League tiers Rookie → Master on top of the current rating; premade-vs-random matchmaking.
- OTA content (Addressables + CDN) for heroes / map packs.
- Coins / Gems economy (architecture only, no monetization yet).

---

## Assets — cheapest first
| Need | First choice (free) | Meshy (credits) only if… |
|---|---|---|
| Terminal, vault, extraction pad, crates, barriers | Blender procedural / Kenney & Quaternius CC0 kits | a hero-quality centrepiece (Vault) looks too plain |
| Map buildings / props | Kenney City / Space kits, Quaternius, Poly Pizza CC0 | — |
| Downed / revive / crawl animations | Universal Animation Library (already in project) + Mixamo | — |
| New heroes | — | yes (rig + walk/run/idle in one request) |

Meshy usage rule: preview (low cost) first, refine only approved models, no texture re-runs without a reason.
API key lives only in `~/.rilo/meshy.key` (never in git).

## Order & rough time
M0 → M1 → M2 → M3 → M4 → M5 ≈ **2 weeks** to a playable extraction slice, then the playtest gate.
