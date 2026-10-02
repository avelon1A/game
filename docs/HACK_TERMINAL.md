# RILO — Hack Terminal Gameplay Design

> **Core principle:** The objective is a tactical situation, not a button or progress bar.

## Implementation status (build 6)

| Design rule | In game |
|---|---|
| Terminal revealed to all, shared, own progress per squad | ✅ one terminal in the **central plaza** (tower core, low walls, 8 pillars, moat with 4 bridges) |
| No interaction button | ✅ enter the ring (6.5 m) |
| Squad speed 100 / 150 / 175 / 190 % | ✅ `GameConfig.HackSpeed` |
| Activity signal, not live position | ✅ "TERMINAL ACTIVITY DETECTED" to other squads (max every 12 s per squad) |
| Contest stops progress, never removes it | ✅ "TERMINAL CONTESTED" |
| Persistent squad progress | ✅ |
| Instability at ~35 % and ~67 % | ✅ |
| 3 node types: Destroy / Stabilize / Override | ✅ shoot (4 hits) / stand 3 s / hold 5 s (slips back when left) |
| Nodes placed by map layout, different each time | ✅ wave 1 inside the plaza between walls and pillars, wave 2 out past the bridges; random angles + shuffled types |
| Enemy pressure during instability | ✅ enemies can contest stand-nodes and the terminal |
| Completion: temporary information reward (~3 s) | ✅ nearby enemies revealed 3 s |
| Hack UI: progress, stability, contest, nodes, all squads | ✅ `Runtime/UI/HackPanel.cs` (bottom centre) |
| High ground / side buildings around the terminal | ⏳ needs the map pass (Milestone 4 arena) |

Code: `Sim/Match/MatchSim.Extraction.cs` (UpdateHack, UpdateNodes, SpawnInstability), values in `Sim/Config/GameConfig.cs`.

---

## 1. Objective Overview
Hack Terminal is an early-match objective. The squad must reach the terminal, decide how many players commit to hacking, protect it, react to instability events, split and reposition, and decide when to fight, defend, rotate, or disengage.

## 2. Announcement
`OBJECTIVE 1/3 — HACK TERMINAL`. The terminal location is revealed to all squads. Everyone attempts the same objective; each squad has its own progress.

## 3. Reaching the Terminal
The terminal sits in a real gameplay space: cover, 2–4 entrances, nearby high ground, alternative approaches, defensive and vulnerable positions, short and medium sightlines.

## 4. Starting the Hack
Entering the zone starts hacking automatically. Squad speed scaling: 1 = 100 %, 2 = 150 %, 3 = 175 %, 4 = 190 %. Options: full commitment (fast, vulnerable), split defense (2 hack, 2 watch), aggressive defense (1 hacks, 3 control).

## 5. Information / Noise
Starting the hack creates an information event ("TERMINAL ACTIVITY DETECTED"), never a continuous exact location.

## 6. Hack Progress
Visible progress bar and stability readout.

## 7. Contest
An enemy in the area → "TERMINAL CONTESTED": progress stops, existing progress is kept, whoever regains control continues.

## 8. Persistent Squad Progress
Leaving at 73 % keeps 73 %: leave, rotate, ambush, return, defend, force others away.

## 9–11. Terminal Instability & Stabilization Nodes
At ~35 % the hack pauses; three nodes activate in tactical locations so the squad must change formation and split up.

## 12. Node Types
Destroy (attack it), Stabilize (stay near it briefly), Override (hold).

## 13–14. Stabilization State / Completion
The terminal cannot progress until all nodes are resolved; enemies may still enter. Then "TERMINAL STABILIZED — HACK RESUMED" from the same progress.

## 15. Second Instability
At ~65–70 % ("SECONDARY SYSTEM FAILURE"), in different positions than the first.

## 16. Enemy Pressure During Instability
Squads may abandon nodes, regroup, fight first and stabilize later, or lose the terminal area.

## 17. Completion
"OBJECTIVE COMPLETE — HACK TERMINAL → NEXT: CAPTURE ZONE" and a short information reward (nearby enemies revealed ~3 s).

## 18–20. Flow / What it should and should not feel like
Not: walk to circle → wait → shoot 3 red balls → wait. Yes: choose approach → choose how many hack → someone is hacking → enemy detected → fight or disengage → instability → split → enemy attacks → finish nodes or fight → stabilized → regroup → finish → use the information → rotate.

## 21. Design Philosophy
The objective should generate decisions, not just actions ("I'll hack, you watch MID", "Node on high ground — I'll get it", "Don't chase, finish the terminal").

## 22. Unity Breakdown
HackTerminalObjective: zone, progress, contest, squad progress, activity signal, terminal state, stabilization (node, type, spawner), UI, audio, events. States: INACTIVE → ACTIVE → HACKING ⇄ CONTESTED → INSTABILITY → STABILIZATION → … → COMPLETE.

## 23. Initial Prototype Values
Base hack ~14 s · speeds 100/150/175/190 % · instability ~35 % and ~65–70 % · 3 nodes per instability · node distance by map · contest progress loss 0 · completion reveal ~3 s.

## 24. Non-Negotiable Rules
1. Not a passive waiting activity. 2. No interaction button. 3. More players faster, not linearly. 4. Enemies can contest. 5. Contest stops progress, doesn't destroy it. 6. Progress persists. 7. Instability forces tactical movement. 8. Nodes placed by map layout. 9. Instability creates vulnerability. 10. Node pattern not repeated. 11. Completion gives a temporary information advantage. 12. Encourage communication. 13. Use the environment. 14. Reusable for future objectives.

## 25. One-Sentence Definition
> Hack Terminal is a contested squad objective where players choose how to divide their team between hacking, defending, and stabilizing a terminal while other squads can interrupt and challenge their progress.
