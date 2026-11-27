# Midnight Club: Los Santos – UI design system

Built with the ui-ux-pro-max skill (design-system search: racing / gaming, glassmorphism style, Sports pairing
Barlow Condensed + Barlow → mapped to GTA's ChaletComprimeCologne (display) + ChaletLondon (body)).

## Style
- **Glassmorphism**: translucent surfaces over the blurred world (menus use GTA's MenuMGIn blur), 1 px hairline
  border, 1 px top highlight (light source), layered depth. Rounded corners everywhere.
- **Light and dark theme** (Settings > Display > Theme: Auto / Dark / Light). Auto: light 08:00-19:00 in-game.
- One shape language: radius 6 / 10 / 14 px (1080p), pills for capsules, chips and bars.
- Colour is used for meaning and category, never alone: icon badges, chips, focus rings, progress.

## Tokens (UI/Kit.cs, class Th)
| Token | Dark | Light |
|---|---|---|
| Glass | rgba(18,20,28,.77) | rgba(246,247,250,.84) |
| GlassStrong | rgba(20,22,31,.91) | rgba(250,251,253,.93) |
| Line | rgba(255,255,255,.16) | rgba(10,14,30,.13) |
| Text | #F5F6FA | #0E1016 |
| Sub | #ACB1C2 | #464C5C |
| Faint | #707588 | #828796 |
| Track | rgba(255,255,255,.24) | rgba(10,14,30,.18) |

Accents: Red #FF3B5C (primary, races, matches the speedometer), Cyan #22B8F0 (garage), Amber #FFB020 (career),
Green #3DDC97 (world), Violet #8B7CFF (settings). Text on accents: white, dark on amber / green (Th.On).
Contrast: body text ≥ 4.5:1 on glass in both themes.

## Components (Kit)
Round, Outline, Glass, Pill, Bar, Chip, Badge, Focus (2 px accent ring), Toggle, Prompts (button bar with real
keycaps / pad buttons, follows the last input device), KeyHint, Interact (context prompt). Rounded corners are
anti-aliased textures (q_*, qo*_*), bodies are GTA rects, so text drawn afterwards stays on top.

## Rules
- Focus is always visible: accent pill / ring + brighter glass, not colour alone.
- Motion: 150-350 ms ease-out, exits faster than entries; lists move with a damped highlight.
- Max 8 rows per list, a detail card explains the selected item.
- Button prompts: keycaps for keyboard, coloured face buttons / bumpers / d-pad for controllers.
- The speedometer is the exception: pixel-exact to the reference (docs: proto/speedo.py).
