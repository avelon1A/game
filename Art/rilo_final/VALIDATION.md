# RILO characters — validation

| Character | Height | Triangles LOD0/1/2/3 | Materials | Bones | UV | LOD | Non-manifold edges | Ready |
|---|---|---|---|---|---|---|---|---|
| VANGUARD | 1.8 m | 10351/10351/8999/2999 (src 10351) | 1 | 68 | yes | 4 | 139 | READY* |
| VOLT | 1.8 m | 59999/24999/9000/3000 (src 109438) | 1 | 66 | yes | 4 | 351 | READY* |
| LYRA | 1.74 m | 60000/25000/8999/3000 (src 96538) | 1 | 66 | yes | 4 | 386 | READY* |
| NOVA | 1.74 m | 59999/24999/9000/3612 (src 94703) | 1 | 58 | yes | 4 | 441 | READY* |
| SOL | 1.86 m | 59998/24999/9000/3056 (src 89320) | 1 | 66 | yes | 4 | 486 | READY* |

Characters: 5
Armatures: 5
Materials: 5
LOD meshes: 20
Cleanup: VANGUARD merged 11608 verts / 0 loose / 0 zero-area faces, VOLT merged 67671 verts / 0 loose / 0 zero-area faces, LYRA merged 61754 verts / 0 loose / 0 zero-area faces, NOVA merged 71284 verts / 0 loose / 0 zero-area faces, SOL merged 61138 verts / 0 loose / 0 zero-area faces
READY* = passes every check except open (non-manifold) edges, which are normal for Meshy single-shell clothing/hair; closing them needs manual work.

Remaining TODO:
- VANGUARD: single painted texture atlas — Skin/Hair/Metal/Rubber/Emissive split needs manual re-UV + texture separation
- VOLT: single painted texture atlas — Skin/Hair/Metal/Rubber/Emissive split needs manual re-UV + texture separation
- LYRA: single painted texture atlas — Skin/Hair/Metal/Rubber/Emissive split needs manual re-UV + texture separation
- NOVA: single painted texture atlas — Skin/Hair/Metal/Rubber/Emissive split needs manual re-UV + texture separation
- SOL: single painted texture atlas — Skin/Hair/Metal/Rubber/Emissive split needs manual re-UV + texture separation
- Body / Clothes / Hair / Accessories are one merged Meshy mesh per hero: separating them needs manual mesh selection + re-UV.
- Proportions: heights are standardised; limb/head proportions were not altered (would redesign the characters).
- Bone names kept as the shared Mixamo humanoid layout (Hips/Spine/…): renaming to Root/Pelvis/… would break every animation clip; Unity humanoid maps them as is.
