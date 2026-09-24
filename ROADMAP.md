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

- [ ] **Merge the work branch into `master`** (pull request
      supa-bit/Facsimilia#1 on GitHub). Also enables the one-click
      "Assets release" button in the Actions tab.

## Build order (from MECHANICS.md)

Each step is verified on the real game before the next begins.

1. [ ] **Provinces**: the `province_id` layer, brush-editable boundaries,
       drawn on the map. No stats yet.
2. [~] **Density fields and population**
   - [x] HYDE 3.2.1 population data imported (300 BC derived, 0 AD–2017 AD)
   - [x] Population engine: history blend, historical ceiling, player legacy
         fade, capital bonuses, resettlement, save/load
   - [x] Calibrated to real founded-city growth on the real grid
   - [ ] Growth drivers (natural increase + migration channels, per region):
         **needs your sign-off** on the draft in MECHANICS.md
   - [ ] Region mask (the 38 ancient geographic regions), needed by the
         growth drivers
   - [ ] Balance-test harness (the contract in MECHANICS.md)
   - [ ] Resource presence per cell (coal, ores, water, flora/fauna)
3. [ ] **Settlements and territory rules**: population clusters, city names
       by percentile (with hysteresis and ruins), unorganized-land limits,
       the Regions map overlay
4. [ ] **Economy**: realm treasury from province sums, taxes, upkeep
5. [ ] **Military and technology**: armies, Might score, basic tech
6. [ ] **Integration**: how conquered people come to accept a new ruler
7. [ ] **Conquest by painting**: the floating Might panel while painting,
       confirm, win or lose per province. This is where it becomes a game.

## Also needed before it's a full game

- [ ] **Other realms act** (AI): expand, go to war, react to the player.
      Blocked on the "how do other realms behave" decision below.
- [ ] **Map views**: population heatmap (the data already exists), regions,
      resources, political (today's view)
- [ ] **Click a realm or place** to see its details
- [ ] **Family tree view**: the player's ruler, heir and court
- [ ] **A goal**: whatever is decided below
- [ ] **Sound and music**
- [ ] **Tutorial / first-game guidance**
- [ ] **Export setup** so the game can be built as a standalone program to
      share (Windows .exe first)
- [ ] **Later eras**: the design names eras (ancient → modern) but they
      aren't specified; the population engine's era constants (C, k) exist
      only for the ancient era

## Needs your decision

These aren't in the design yet. Each one blocks the work listed after it.

- [ ] **What's the goal?** Sandbox with no end, a score, victory
      conditions, or reaching a certain year? *(blocks: the goal, end-game)*
- [ ] **How do other realms behave?** Follow history on rails (Rome
      expands roughly as it really did), make their own decisions, or a mix?
      *(blocks: AI)*
- [ ] **War and diplomacy**: can the player paint into a neighbour at any
      time, or is war declared first? Treaties, alliances, vassals?
      *(blocks: conquest by painting, AI)*
- [ ] **New realms**: how do new states appear (Parthia in 247 BC,
      rebellions, breakaways)? Can realms split in civil wars?
- [ ] **Culture and religion**: are they in? They'd drive integration.
      *(blocks: integration)*
- [ ] **Pacing**: one year per turn all the way (~2,300 turns to the
      present), or longer/shorter turns in some eras?
- [x] ~~**Characters**: how deep?~~ Answered "as deep as makes sense for
      now": births, marriages, deaths, succession and new houses are built;
      traits, events and intrigue can come later (see MECHANICS.md,
      Dynasties and characters)
- [ ] **Personal unions**: should one ruler be able to inherit two realms
      (and what happens to them then)? Today they can't: a monarch is passed
      over for a second throne. *(affects: new realms, diplomacy)*
- [x] ~~Growth drivers~~ drafted in MECHANICS.md, **needs sign-off**, see
      build step 2

## Known issues

- [ ] Family records grow with time (about 7 MB in the save by AD 1700):
      fine for now, trim or compress before the save gets slow
- [ ] The HUD names the heir but not why (e.g. a newborn grandson because
      his father already rules elsewhere): wants a family tree view
- [ ] The brush paints a proposal that is never confirmed or saved
      *(fixed by conquest by painting, step 7)*
- [ ] By 0 AD the simulated world holds 47.7 million against HYDE's 48.2
      million (population inertia lags the rising historical ceiling)
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
- [x] Save safety: three save slots plus an autosave every 10 years,
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
- [x] 11 automated test suites
