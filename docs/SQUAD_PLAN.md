# VEIL — 4-Player Squad Multiplayer Plan

Status legend: ☐ todo · ◐ in progress · ☑ done

## Decisions (defaults taken — change here if needed)

| Question | Decision |
|---|---|
| Match format | **4 squads × 4 = 16 players.** Parties of 1–4; empty seats filled by queued players, then bots. 15-minute default, same 5 phases. |
| Voice | **Self-hosted**: Opus (Concentus, pure C#) over a UDP relay on the VEIL server, behind `IVoiceService` so Vivox can be swapped in later. |
| Accounts | **Device-bound handles** (`Name#1234`) now; Google/Apple sign-in later. |

## 1. Core gameplay with squads

| System | Squad version |
|---|---|
| Hidden objectives | Each player keeps a **secret personal objective**; each squad gets a **squad objective** only it can see. |
| Nemesis / Prevent | Targets are always on **other squads**. |
| Vision / info | Squadmates always visible to each other + **shared squad vision**; enemies stay per-viewer filtered (deception intact). |
| Deception | Enemies see your decoy as you; your squad sees it tagged "ally decoy". Ruins stealth hides from enemies only. |
| Abilities | Pulse reveals enemies to the whole squad. Dash/Decoy unchanged. |
| Zones | Tower/Vault/Reactor/Market/Ruins controlled by a **squad**; Market discount for that squad. |
| Combat | No friendly fire. Respawns as today. |
| Scoring | Individual score as today; **squad score = members' sum + squad objective bonus**; results show squad placement + MVP. |
| Progression | Team rating from squad placement with a small individual-performance modifier; XP/levels kept. |
| Bots | Squad-aware: follow squadmates, never target allies, fill empty squad seats. |

## 2. Networking architecture

```
Client ──HTTPS──▶  API        accounts, profiles, friends CRUD, match history      (TCP 5080)
Client ──WebSocket▶ Gateway   presence, party, invites, matchmaking push            (TCP 5080 /ws)
Client ──UDP──────▶ Match     authoritative 30 Hz sim, many MatchInstances          (UDP 7777)
Client ──UDP──────▶ Voice     Opus relay by channel (party:<id> / match:<id>:<sq>)  (UDP 7778)

Gateway → PartyService → Matchmaker → MatchAllocator → MatchInstance(s)
```

* **Today:** one `Veil.Server` process, in-memory bus, SQLite.
* **Later:** Redis/NATS bus, Postgres, allocator/fleet (Agones/Edgegap) — config change, not client change.
* **Signed match tickets** (HMAC: playerId, matchId, squad, slot, expiry) — match servers validate with no DB.
* **Reconnect:** slot held for the whole match, a bot pilots the character while you're away; Gateway presence says "in match X" → client shows **REJOIN**; ticket stays valid until match end.
* **Presence:** Offline · Online · InParty · InQueue · InMatch; heartbeat over WebSocket, offline after ~30 s silence; friends get pushes.
* **Gateway protocol:** JSON envelope `{ "t": type, "id": reqId, "d": data }`
  `hello`, `presence`, `friend.list|request|accept|decline|cancel|remove`, `party.create|join|leave|invite|invite.accept|invite.decline|ready|kick|promote|start|character`, pushes `party.state`, `party.invite`, `friends.state`, `mm.status`, `match.assigned {host,port,ticket}`, `voice.token`.
* **Matchmaker v1:** leader presses START → party goes to queue → matches are formed from queued parties into 4×4 (bot fill after a short wait). Later: region, MMR bands, backfill.
* **Server authority:** voice only forwarded inside the channel (never to enemy squads); invites only from friends or with a room code; only the leader can kick/promote/start.

## 3. Friends & party

* Handle `Name#1234`; add friend by handle.
* Friends list (online first, status dot + "In match · 6 min left"), **Incoming** (Accept/Decline) and **Outgoing** (Cancel) requests.
* **Create Room** → 6-char code; **Join Room** by code; **Invite** from friends list → toast with Accept/Decline (60 s expiry).
* Leader: invite, kick, transfer leadership, START (all ready). Leader leaves → next member promoted.
* **Squad member cards (4):** portrait, `name#tag`, character, ready tick, leader crown, ping bars, speaking ring / muted icon; empty slot = **+ INVITE**.

## 4. Voice (PUBG/Valorant style)

* Mic 16 kHz mono, 20 ms frames, VAD for indicators/open mic.
* Opus via Concentus (~20 kbps). UDP relay, unreliable; ~60–80 kbps down with 4 members.
* Jitter buffer ~80 ms, one streaming `AudioSource` per speaker.
* Modes **Push-to-talk (default)** / Open mic / Off; PTT key **V** (desktop), hold button on mobile HUD; mute mic, deafen, per-player mute + volume.
* Speaking rings on lobby cards and in-match squad bar.
* Android `RECORD_AUDIO` runtime permission; iOS microphone usage string + recording audio session.
* Risk: Unity `Microphone` has no echo cancellation → PTT default on phones.

## 5. UI

* PLAY tab: party panel (4 cards), room code, Create/Join/Leave, Ready/Unready, leader START + per-card menu (kick, promote, mute, volume).
* Friends drawer (top-bar button with badge): Friends / Requests / Add.
* HUD: squad bar (portrait, HP, speaking, respawn), allies coloured & always on minimap, squad objective row, mobile PTT button.
* Results: squad placement, squad score, individual stats, MVP.
* Settings → Voice: mode, PTT key, input meter/sensitivity, output volume.

## 6. Code map

| Area | Work |
|---|---|
| Sim (shared) | `GameConfig` (MaxPlayers 16, SquadSize 4, SquadCount 4), `Types` (`PlayerState.Squad`, squad objective), `MatchSim*` (no friendly fire, squad zones & scoring), `Visibility` (squad vision), `BotBrain` (squad aware), `Protocol`/`Snapshot` (squad ids, ticket hello, squad objective) |
| Server | `Gateway` (WebSocket), `PresenceService`, `FriendService`, `PartyService`, `Matchmaker`, `MatchInstance` (from `GameServer`), `TicketSigner`, `VoiceRelay`, DB schema, SelfTest + LoadTest |
| Client | `Net/GatewayClient`, `Social/*` models, `Voice/*` (capture, Opus, jitter, playback, `IVoiceService`), UI: `PartyPanel`, `SquadCard`, `FriendsDrawer`, `InviteToast`, `SquadHud`, voice settings |
| DB | `players` (+handle, tag, last_seen), `friendships`, `friend_requests`, `match_players` (+squad, placement); parties/invites in memory with TTL |

## 7. Milestones

| # | Milestone | Status |
|---|---|---|
| M1 | Foundations: git, handles, WebSocket Gateway, signed tickets, multiple match instances | ☐ |
| M2 | Friends + presence | ☐ |
| M3 | Party rooms: create/join code, invite, ready, leader controls, member cards + ping | ☐ |
| M4 | Squad gameplay: 4×4, shared vision, squad objective/scoring, squad HUD/results, squad-aware bots | ☐ |
| M5 | Party → match flow: START, matchmaker with bot fill, reconnect/REJOIN | ☐ |
| M6 | Voice: relay, Opus, PTT/open mic, mute/deafen, indicators, mobile permissions | ☐ |
| M7 | Hardening: load test (fake parties + voice), phone + Mac tests, drop/reconnect tests | ☐ |

Testing: `Veil.LoadTest` drives fake parties/friends over the Gateway; server `--selftest` covers friends/party/ticket/squad sim; voice loopback mode; scripted online autotest; real session Samsung + Mac.
