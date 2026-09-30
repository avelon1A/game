# Rilo — Fortnite-style costume heroes (Meshy prompts)

Original designs (no real Fortnite / PUBG skins). Use **Meshy → Text to 3D**, Art style **Cartoon / Stylized**,
Symmetry **on**, Pose **A-pose**, then **Texture** (PBR on), then **Rig** (humanoid) → **Download FBX**.

Add to every prompt (style block):
> stylized 3D game character, Fortnite style, full body, standing A-pose, arms slightly away from the body, feet apart,
> empty hands, clean game-ready topology, vibrant hand-painted textures, bright readable colours, head slightly large

Negative prompt (all):
> realistic, photorealistic, gun in hand, weapon, pedestal, base, text, logo, cape, long flowing cloth, fused arms

| # | Name | Prompt (put before the style block) |
|---|---|---|
| 1 | **Blaze** — assault trooper | young male soldier, orange and black tactical jacket, rounded combat helmet with orange goggles pushed up, chest rig with pouches, black cargo pants with knee pads, chunky combat boots, fingerless gloves, confident smile |
| 2 | **Frost** — arctic scout | female scout, white and ice-blue winter camo parka with fur-lined hood down, light-blue face scarf around the neck, small backpack, white snow boots, grey gloves, short silver hair |
| 3 | **Viper** — stealth operative | female operative, dark green tactical bodysuit with neon green light strips, black beanie, thigh holster, knee pads, black sneakers, green visor glasses, ponytail |
| 4 | **Rook** — combat medic | broad male medic, red and white armored vest with a white cross, white helmet with red stripe, bandolier with med kits, grey cargo pants, heavy boots, beard |
| 5 | **Neon** — cyber runner | teen runner, purple cropped jacket with glowing cyan seams, holographic visor, black shorts over leggings, chunky white sneakers, pink undercut hair |
| 6 | **Dune** — desert ranger | male ranger, sand-coloured poncho-free desert jacket, tan helmet with cloth cover, brown shemagh around the neck, ammo belt, khaki pants, desert boots, sunglasses |

Costume variants (same hero, different outfit): re-run the prompt with a colour swap, e.g.
"*Blaze* … **blue and white** tactical jacket …" — each download becomes an extra skin.

After downloading, put the FBX files in `~/Downloads` and tell Claude the names; they get the Universal Animation Library
walk / jog / sprint / aim through Unity humanoid retargeting (see `blender/build_ranger.py` for the animation set).
