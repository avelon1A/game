# RILO changelog

## 2026-10-08 → 2026-10-09

### Match rules
- **No time limit.** After the planned length the match goes to **OVERTIME** and runs until a squad escapes.
  The Vault is still required (protocol 15 → 17).
- **Helicopter extraction.** Only squads that opened their Vault see where the helicopter is (others only see it up close,
  within 70 m) and only they can use it. No unlock delay; hold the zone 25 s to board; the last 5 s is BOARDING.
  The helicopter lands for the first eligible squad and stays landed.
- **Escape cinematic** (~10 s): run-up behind the squad, door close-up as it lifts, drone shot over the island,
  locked camera as it banks out to sea, letterbox + VICTORY / SQUAD X ESCAPED, then results.

### Gameplay (protocol 16)
- Pickups: **shotgun** (8 shells, 7 pellets), **SMG** (45), **grenades** (G). Fists faster, silent, heavy knockback.
- **Squad pings** (Z / T / middle mouse / PING): enemy, go here, need help, loot.
- **Tags:** fallen players drop a tag; a squadmate standing on it 2.5 s redeploys them (respawn 10 s otherwise).
- **Squad combos:** Pulse shields nearby allies; dashing beside an ally gives both a speed burst.
- **World events:** supply drop, hack surge (×2 progress), bounty on the top player.
- **Comeback sabotage:** the last squad, 2+ steps behind, jams the leader once.
- **"Your terminal is under attack"** alert with a DEFEND marker.
- **Map:** 16 jump pads on the spoke roads, auto-vault over low cover, crates around every capture pad / Vault.
- **Feel:** damage numbers, hit ticks, damage-direction arrows, squad-wipe slow-mo (offline), finale countdown + music.

### Map screen (protocol 18)
- Tap the minimap to open the full map (the phone look pad no longer swallows the tap); M on desktop.
- Tap the island to drop a squad **GO HERE** marker; a **route line** leads to your marker / objective / helicopter
  (ground dashes, minimap, map). The map shows loot, squadmates' tags, jump pads, pings and alerts.

### Progression
- Ranked tiers from rating (Bronze → Master), hero levels (coins per level), 3 daily missions paying coins.

### Heroes (see docs/CHARACTERS.md)
- All five heroes replaced with new Meshy models, each standing in **Idle 11**; Lyra / Volt / Sol use Vanguard's run
  or sprint copied bone-by-bone. Posture fixes: T-pose reference no longer tips the pelvis, own bind pose for new
  Meshy rigs, standing upright correction. Blender toolset for idles, proportions, clip copying, inspection, LODs.

### UI
- **Phones:** menus inside the safe area (Dynamic Island), HACK button and circuit puzzle above the touch controls.
- **Lobby:** drag to turn your hero, pinch / scroll to zoom (squad view can't zoom in past the 4-hero framing);
  three "+" invite spots; squad list hides the right cards; **player cards** wider with a colour strip
  (you / squadmate / bot), marquee names, level tag, and a layout pass so they never overlap; 5/10/15 MIN selector removed.
- **Characters:** hero centred in the free space, locked heroes / gun skins preview in 3D with an UNLOCK bar,
  texts fit their chips.
- **Settings:** rebuilt as one scrolling panel (Account, Audio, Voice chat, Controls, Graphics & display, Server),
  fixed-height rows, nothing overflows.
- **How to Play:** centred pop-up with CLOSE (tap outside closes too), updated rules.
- **Results:** camera cuts straight to the MVP (no glide that looked like shaking).

### Not done yet
- Hero names / store descriptions still describe the old designs.
- New heroes' colours look harsher in the toon shader than in Blender (shader tuning per hero).
- Roaming robot boss (world event) and a separate ranked queue.
