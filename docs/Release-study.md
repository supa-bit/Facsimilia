# Release study (17 Oct 2026)

The full study, with sources, is the page
https://claude.ai/artifact/V1aE1yknZ6w7JW322EAUWR. This is its summary.

## The short answer

- **There is a market**, small, loyal and picky. Deep historical strategy
  sells to experienced players who pay full price and play for hundreds of
  hours. Antiquity is underserved: Paradox stopped work on Imperator: Rome
  in 2022 and its fans kept it alive with the Invictus overhaul.
- **The game as designed can't be released.** The Ledger has 922
  features (334 done, 344 waiting on decisions), from a Stone Age opening
  to nuclear weapons. Code is not the bottleneck; computer realms that use
  every system, balance, long playtests and the first hour are.
- **Likely outcome** for a well-made slice with good marketing: a few
  thousand to a few tens of thousands of copies (Imperiums: Greek Wars
  ≈ 11k, Shadow Empire ≈ 58k, Terra Invicta ≈ 215k, all estimates).
- **Recommended path:** a trimmed release (300 BC to about 27 BC) in
  Early Access, the full game kept as a personal build from the same
  code, the Steam page up early, and perhaps the chef game first as the
  studio's practice release.

## Comparable games (estimates unless announced)

| Game | Team | Copies | Lesson |
|---|---|---|---|
| Europa Universalis V | Paradox | ≈ 610–650k | Don't compete on content volume |
| Imperator: Rome | Paradox | sold fine, players left | Antiquity fans want depth; revived by a mod |
| Terra Invicta | small team + Hooded Horse | ≈ 215k+ | Complexity sells with good presentation |
| Shadow Empire | one person, 4–5 years | ≈ 58k | Solo deep strategy can earn a living |
| Songs of Syx | tiny team | ≈ 198k | Long Early Access with steady updates |
| Dwarf Fortress (Steam) | two people + Kitfox | 1M+ | Depth sells once approachable |
| Manor Lords | mostly one person | 4M+ | Outlier: visual hook and devlogs |
| Ara: History Untold | Oxide / Xbox | ≈ 52k | Budget isn't depth |
| Total War: Pharaoh | Creative Assembly | ≈ 366k | Bronze Age setting alone doesn't sell |
| Imperiums: Greek Wars | small studio | ≈ 11k | The floor without a hook |

## Two builds, one project

Keep one project and two export presets in Godot (Project → Export):
**Release** and **Personal**. The Personal preset carries the custom
feature `personal` (Features tab); the game checks it with
`OS.HasFeature("personal")` and turns on everything not ready for sale.
Bug fixes reach both builds at once.

## Audience

Experienced grand strategy players (mostly men, about 25–55) who play
Europa Universalis, Crusader Kings, Imperator with Invictus, Old World,
Terra Invicta or Shadow Empire. Then history lovers, simulation lovers,
players who need readable text, and later Chinese-speaking players
(roughly a quarter to a third of Steam).

Pitch draft: "A grand strategy game where history pushes back. Start in
300 BC with the real kingdoms, peoples and land. Every other realm tries
to live out its own history. Only you can change it."

## What a release needs

- Game: a guided first game, computer realms that use every released
  system, balance, outside playtests, readable crash reports, saves that
  survive updates, player text in translation files (none today).
- Art and sound: music, sound effects, event pictures, logo, store art.
  The ElevenLabs voices and ChatGPT portrait parts must be declared on
  Steam; voices draw the most criticism.
- Legal and business: a credits screen (CC BY needs it), a Steam partner
  account ($100 per game, tax and bank forms), a checked studio name.
- Store: Steam page 6–12 months ahead, professional capsule art, trailer,
  demo for Next Fest. Aim for 7,000+ wishlists, 30,000+ for a strong
  launch.
- Publishers that fit: Hooded Horse, Slitherine / Matrix Games.

## Marketing, in order

Pick the hook and slice; Steam page; public devlogs (one system or one
historical moment per post); a Discord for playtesters; strategy
YouTubers and streamers; Next Fest with the demo; pitch publishers;
Early Access with steady updates.

## Studio brand

Players buy games, not studios; a studio name builds trust slowly.
Multi-genre studios (Klei) share a tone, not a look. The idea joining
both games: a **cabinet of curiosities** (Wunderkammer), each game one
strange, lovingly made object. Name candidates, none cleared yet:
Wunderkammer Games, Curio Cabinet, Marginalia. Check Steam, domains,
handles and the trademark register before choosing.

## The chef game

The genre is healthy (PlateUp!, one developer, 1.5M copies). Space Chef
(Blue Goo Games / Kwalee, October 2025) already covers hunting alien
ingredients and serving aliens: avoid the name and lead with the weird
customers and random events.
