# Keyart (production notes, 2026-10-27)

Generated with Qwen-Image 2.1 after the cropped header was judged ugly.
Text is NOT baked into the render — stamped afterwards with PIL for
sharpness.

- **Model:** `qwen_image_21_uncensored_7B` (Wan2GP daemon, port 8743)
- **LoRA:** `Qwen-Image-2.1-viggle-turbo-v0.2.1-6step-lora-r256` (6 steps, guidance 1, solver viggle_v02)
- **Resolution:** 1344x768 • **Seed:** 7
- **Prompt:** "Cinematic wide shot of a luxurious dark boardroom at night,
  floor-to-ceiling windows overlooking a glittering city skyline, a huge
  glowing situation wall covered with business charts, maps, pinned photos
  and connecting strings, teal and warm amber accent lighting, mahogany
  conference table in foreground with scattered documents, moody premium
  strategy-game keyart, ultra detailed, no text, no words, no letters,
  no watermark"
- **Type:** Bebas Neue (title) + Montserrat (tagline/chips) — `/tmp/fonts`
  is not persistent; re-pull from Google Fonts if re-stamping is needed.
- **Icon:** exact dimensions from `PhoneButtonInstaller.CreateIcon`
  (teal #1A8C8C, block-pixel B).

## Files

| File | Use |
|---|---|
| `keyart-original-1344x768.jpg` | Raw render, textless ORIGINAL — do not lose |
| `keyart-cover-1344x768.jpg` | Cover: icon + title + tagline + 6 chips → Steam preview + Nexus gallery head |
| `keyart-nexus-header-1300x372.png` | Nexus header size (icon + title + tagline + tab row) |

## Language rule (2026-10-27)

No needling-the-developer language: phrases like "everything the game
doesn't show you" or "vanilla apps never tell you" are OUT. Use instead:
"sits alongside the vanilla apps", "brings the numbers you check every
day into one place". Workshop/store/branding copy was converted to match.
