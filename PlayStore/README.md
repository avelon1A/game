# RILO — Google Play store graphics

| Asset | File | Play requirement |
|---|---|---|
| App icon | `icon/rilo_icon_512.png` | 512 × 512 PNG, full square (Play rounds the corners) |
| App icon (hi-res source) | `icon/rilo_icon_1024.png` | for other stores / marketing |
| Feature graphic | `feature-graphic/rilo_feature_1024x500.png` | 1024 × 500, no alpha |
| Phone screenshots | `screenshots/01…08_*.png` | 1920 × 1080 (16:9), 24-bit PNG, 2–8 allowed |

Upload order = file number (01 first — it is the one most people see).

## How they were made
- Icon art + title key art: Meshy text-to-image (`source/icon_art.png`, `Client/Assets/Veil/Resources/UI/title_keyart.png`).
- Screenshots: captured from the game at 1920×1080 with the phone layout (`Rilo -mobile-ui -autotest -shotdir …`,
  island shot with `-mapshot`) into `source/`.
- Layout, captions and logo: `swift make_store_art.swift` (run in this folder) — re-run after recapturing.
