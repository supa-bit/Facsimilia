# Facsimilia roadmap

The living checklist for the project: what's done, what's next, and what's
waiting on a decision. Updated every time something is finished or a gap is
found. The detailed design for each system lives in MECHANICS.md; this file
only tracks progress and order.

**How to read it:** `[x]` done · `[ ]` to do · **Needs your decision** items
block the work that depends on them.

---

## Right now

Small, cheap, and they protect everything built after them.

- [x] **Merge the work branch into `master`** (pull request
      supa-bit/Facsimilia#1, merged 25 Sep 2026). The one-click
      "Assets release" button in the Actions tab now works too.

## Build order (from MECHANICS.md)

Each step is verified on the real game before the next begins.

1. [x] **Provinces**: the `province_id` layer, ~290 provinces from real
       300 BC regions, drawn on the map with names, click to inspect (area,
       people), province brush to redraw your own, new provinces, rename
2. [~] **Density fields and population**
   - [x] HYDE 3.2.1 population data imported (300 BC derived, 0 AD–2017 AD)
   - [x] Population engine: history blend, historical ceiling, player legacy
         fade, capital bonuses, resettlement, save/load
   - [x] Calibrated to real founded-city growth on the real grid
   - [x] Region mask: the 38 ancient geographic regions baked onto the
         population grid, with neighbours and sea lanes; the Regions map
         overlay (button or R key) and each province's region in its panel
   - [x] Growth drivers groundwork: each region grows at its own
         historical rate plus bounded factor effects, people move between
         neighbouring regions (capped, never created or destroyed), and
         the calibration was refitted on it (`μ` 0.0625 → 0.065)
   - [x] Balance-test harness: all six tests from MECHANICS.md pass on the
         real grid (plus the founded-city calibration)
   - [ ] Wire gameplay into the growth factors and attraction as each
         system is built (economy, tech, war, resources); each must pass
         the balance tests
   - [ ] Events that strike a region (epidemics, famines): the engine can
         apply them; nothing triggers them yet
   - [x] The land layer: terrain, climate, soil, water, 300 BC habitat
         and resources for every place, from real data cleared for a
         commercial game, with hand fixes for 300 BC; shown in the Map
         view menu with a legend
   - [x] Crop yields: a suitability profile for each of 31 crops, trees,
         herds and fisheries; carrying capacity from food replaces the 3x
         stand-in; "Crops and food" map views
   - [ ] Yearly harvests from the weather draw, feeding the food-security
         growth factor
   - [ ] Renewable stocks (soil nutrients, forests, forage, fish) and the
         yearly weather draw
   - [ ] Goods and recipes, shown by family, with the economy (step 4);
         labour from the population, as history had it
3. [ ] **Settlements and territory rules**: population clusters, city names
       by percentile (with hysteresis and ruins), unorganized-land limits,
       the Regions map overlay
4. [ ] **Economy**: realm treasury from province sums, taxes, upkeep
5. [ ] **Military and technology**: armies, Might score, basic tech
6. [ ] **Integration**: how conquered people come to accept a new ruler
7. [ ] **Conquest by painting**: the floating Might panel while painting,
       confirm, win or lose per province. This is where it becomes a game.

## Also needed before it's a full game

- [ ] **Other realms act** (AI): their own choices with history as a pull;
      expand, go to war, react to the player
- [~] **Map views**: regions and 84 land and crop views (done), population heatmap
      (the data already exists), political (today's view)
- [~] **Click a realm or place** to see its details (provinces done;
      realms and cities still to come)
- [ ] **Family tree view**: the player's ruler, heir and court
- [ ] **A goal**: whatever is decided below
- [ ] **Sound and music**
- [ ] **Tutorial / first-game guidance**
- [ ] **Export setup** so the game can be built as a standalone program to
      share (Windows .exe first)
- [ ] **Later eras**: the design names eras (ancient → modern) but they
      aren't specified; the population engine's era constants (C, k) exist
      only for the ancient era

## Decisions

All answered in the Facsimilia Ledger on 25 Sep 2026 (the full reasoning is
in MECHANICS.md, "Design decisions"). Nothing is waiting on a decision.

- [x] **Goal**: sandbox with optional goals (play forever; a score and
      goals to chase if you want)
- [x] **Other realms**: their own choices, with history as a pull
- [x] **War and diplomacy**: declared wars, peace deals, alliances and
      vassals
- [x] **New realms**: historical arrivals plus revolts and civil wars;
      revolts follow history unless the player is involved or causes them
- [x] **Culture and religion**: both, with conversion and assimilation
- [x] **Pacing**: the player chooses years per turn; games are meant to be
      long (weeks or months per playthrough)
- [x] **Characters**: births, marriages, deaths, succession, new houses
      (built); traits and events later
- [x] **Frontier tribes**: start unorganized, as is historically accurate
      (built)
- [x] **Personal unions**: allowed, guided by history (realms stay
      separate unless history merged them; names change if history did;
      splits only when the player is involved or history split them)
- [x] **Growth drivers**: approved as drafted
- [x] **Resources**: every good simulated, shown by family; other realms'
      resources follow history; seasons as a yearly weather draw; labour
      from the population as history had it (slave trade included,
      abolished as history abolished it); real data with hand fixes, only
      sources that allow selling the game

## Known issues

- [ ] Family records grow with time (about 7 MB in the save by AD 1700):
      fine for now, trim or compress before the save gets slow
- [ ] The HUD names the heir but not why (e.g. a newborn grandson because
      his father already rules elsewhere): wants a family tree view
- [ ] The brush paints a proposal that is never confirmed or saved
      *(fixed by conquest by painting, step 7)*
- [ ] Mouse offset on Windows: fixed by starting maximized; press F3 in
      game to check the crosshair sits under the pointer

## Done

- [x] 300 BC map: real coastline, reconciled political boundaries,
      frontier tribes, terrain, 12 playable realms
- [x] Dynasties and succession laws (male-preference and plain
      primogeniture)
- [x] Living dynasties: the real 300 BC ruling families, births,
      marriages (including between realms), deaths by a real life table,
      succession through grandchildren, brothers and cousins, new houses
      when a line ends or a throne is seized; names from eight cultures
- [x] Save and load
- [x] Save safety: three save slots plus three rotating autosaves (off or every
      5-25 years, set in Settings > Gameplay; default 5),
      a warning before overwriting, Load Game list with delete, crash-safe
      writing (an interrupted save never damages the old one); the old
      single save moves into slot 1 automatically
- [x] Red on-screen warning when the game runs old compiled code
- [x] Automatic tests on GitHub: every push builds the game and runs all
      test suites (the "Tests" workflow; a green check or red X per commit)
- [x] HYDE population data and the population engine (see step 2)
- [x] Whole game ported to C# (population tick ~30 ms, world generation
      ~3 s); all GDScript removed
- [x] UI redesign: Cinzel/Alegreya fonts, bronze-and-gold theme, icons,
      main menu, realm selection, HUD top bar, chronicle, turn button
- [x] Map camera: whole map when zoomed out, zoom at the cursor, keyboard
      pan/zoom, readable realm names
- [x] Pause menu: save and leave, or leave without saving (with
      confirmation)
- [x] Build stamp in the main menu corner showing which version is running
- [x] Session setup (.NET SDK + Godot .NET) and one-click assets release
      workflow
- [x] 15 automated test suites (population balance, land layer and crop model tests added)
- [x] Fixed: the simulated world no longer lags HYDE's total (was 47.7
      million against 48.2 million by 0 AD); regional totals now follow
      HYDE exactly
