# VEIL — Development Plan

Maps the GDD milestones (§26) onto concrete work. ✅ = done in this build.

## Tooling
- ✅ Unity Hub + Unity 6000.3.25f1 LTS (Apple Silicon), URP
- ✅ .NET 8 SDK (server), LiteNetLib (UDP), SQLite
- Art: everything generated in code (procedural chibi characters, low-poly arena, VFX,
  synthesized SFX). Fonts: Lilita One + Chakra Petch (Google Fonts, OFL).

## Prototype 0 — Movement
- ✅ Chibi character (big head, strong silhouette, 5 outfit presets)
- ✅ Elevated third-person camera: smooth follow, zoom, collision, aim
- ✅ 150m × 150m arena: Tower, Vault, Reactor, Market, Ruins, canals, bridges, trees
- ✅ Run / sprint / jump / dash, procedural animation (idle, run, sprint, jump, fall, land,
  dash, hit, cast, death, victory)

## Prototype 1 — Core interaction
- ✅ Energy resource (orbs, Reactor charging, ability costs)
- ✅ Zones: capture & control (Tower, Reactor, Market, Ruins), Vault unlock with 3 keys
- ✅ Energy Cores, Keys, Market purchases
- ✅ Hidden objectives (primary + secondary)

## Prototype 2 — Mind game
- ✅ Dash, Pulse, Decoy
- ✅ Hidden information: vision radius, Ruins stealth, reveals
- ✅ Bots: Explorer, Collector, Hunter, Defender, Opportunist (perception-limited)

## Prototype 3 — Complete match
- ✅ Timer, 5 phases incl. collapsing arena, final-minute Tower bonus
- ✅ Scoring breakdown, results screen with podium, play again

## Prototype 4 — Multiplayer
- ✅ Authoritative .NET server (same sim), lobby, ready, countdown, bot fill
- ✅ Client prediction + interpolation, per-viewer snapshot filtering
- ✅ REST backend: guest profiles, stats, rating, leaderboard, match history
- ⏳ Reconnection handling (rejoin by token) — next (a bot currently takes over a disconnected player)

## Prototype 5 — Scale
- ✅ `Veil.LoadTest` headless clients for 5 / 10 / 15-player tests
- ⏳ Measure latency / CPU / bandwidth on real network; tune snapshot rate & delta compression

## Next steps after MVP
1. Playtest: does the player want to "play again" (GDD §33)? Tune numbers in `GameConfig`.
2. Replace procedural art with authored models (keep `CharacterRig` API, swap visuals).
3. Delta-compressed snapshots, reconnection, dedicated-server hosting, matchmaking by rating.
4. More abilities (Shield, EMP, Smoke, Trap…) only after the core loop is fun.
