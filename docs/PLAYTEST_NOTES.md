# Playtest notes — 2026-10-08 (Mac build, scripted bot match)

## Mission / objectives
1. **Nobody finished anything.** The match ended "TIME UP · NO SQUAD EXTRACTED", with every squad still on OBJ 1/3 (hack 0%). The chain (hack → capture → collect → vault → extract) is too long for the match length and the map size. Fix: shorten the chain or the travel, or speed up the hack, so a good squad reaches extraction in about 60–70% of the match.
2. **The first target is 173 m away at spawn.** Players spend the opening just running. Spawn squads closer to their own terminal, or add a fast travel option (glider or jump pad).
3. **Too many targets at once:** enemy terminal, central terminal, Snow Vault, Market, Ruins. A new player can't tell which one matters. Show only the current objective strongly and fade the others.
4. **"TIME UP" is a flat ending.** Add sudden death or overtime: when time runs out, the squad with the most progress gets a final 30 s to extract.

## Gameplay
5. **Camera clips into the rock.** A large cliff filled half the screen for several seconds. The third-person camera needs collision pull-in, or obstacles between the camera and the player should fade out.
6. **The player looks small on screen**, and the gun on the back is hard to read. Move the camera a little closer and lower while running.
7. **Very little combat happened** (one elimination each in the whole match). The squads barely meet. Use the objective layout to pull squads into shared areas earlier.
8. **No feedback when a goal makes progress.** Add a sound, a progress ring on the target, and a short "+score" pop.

## UI / HUD
9. **Labels overlap.** "▼ ENEMY TERMINAL 173 m" sits on top of the squad list, and the waypoint pills overlap the compass. Clamp world markers away from HUD panels.
10. **The objectives panel (right side) is dense.** It has three cards with tiny grey text ("Enemy 0% · 173 m | Centre 0% · 97 m"). Collapse it to one line for the current goal and let the player tap to expand.
11. **The kill feed is tiny and hidden under the minimap** ("Orion eliminated Pixel"). Move it, make it bigger, and show a hero icon.
12. **Two different scores on the HUD and results:** the HUD said "SCORE 2" while the results said 284. Use one scoring system.
13. **The results screen contradicts itself:** the top right says "MVP · SQUAD C Nova 790", while my own card is tagged MVP and my hero is shown. Label them "Match MVP" vs "Squad MVP", or show only one.
14. **Results "1/1/0" is unclear.** Write it as "1 KILL · 1 DEATH · 0 ASSIST".
15. **Ability labels ("DASH 10", "PULSE 25") are tiny and red** above the buttons. Show the cooldown inside the button instead.
16. **The phase bar ("EXPLORATION", "COMPETITION") has no timer or explanation.** Show "Competition in 0:20" so players know what's coming.

## Suggested order
High impact first: 1, 5, 9, 12–13, then 2, 3, 10, then the rest.
