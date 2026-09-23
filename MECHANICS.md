# Facsimilia game mechanics — design plan

This is a living design reference for systems beyond what's implemented today
(grid ownership painting, dynasty/succession, the 12-civ 300 BC start). It's
updated as design decisions get made, before or alongside implementation, the
same way MAP_DATA.md documents the map generation pipeline. Sections marked
**(decided)** are settled; sections marked **(open)** still need a call.

## Core interaction principle: the brush stays authoritative

The existing left-drag-to-paint / right-click-to-clear ownership brush
(`map_view.gd`, `PAINT_RADIUS`, the `proposal` overlay) is the one mechanic
everything else builds on top of, not around. Every new layer described below
reuses that same brush code path (radius stamp, drag-paint, dirty-cell
tracking) rather than introducing a different interaction paradigm (no
click-to-select-a-region UI, no modal province editor). New layers just give
the brush a different target to paint onto.

## Layers

Three per-cell layers, all the same 8192x5476 shape as the existing ownership
grid:

1. **Ownership** (`OwnershipGrid.cells`, existing) — which realm holds this
   cell. Painted directly today (immediately, no commit step yet).
2. **`province_id`** (new) — which province this cell administratively
   belongs to. `0` = unorganized. Brush-editable in a "province mode" that
   targets this layer instead of ownership, with a selected target
   (an existing province of yours, to expand it, or "new province", to found
   one).
3. **Density fields** (new) — population and raw-resource presence, baked at
   world-gen from terrain data already loaded for the terrain shader. Not
   normally player-painted (geography/history-driven), though the same brush
   code could hand-adjust them in a dev/authoring context.

Provinces never store population/resources as an opaque blob — a province's
stats are always the sum of the density fields over whatever cells currently
carry its `province_id`. This is what makes provinces safely brush-editable:
resizing a province's boundary just changes what it sums over, nothing needs
manual splitting/merging logic. **(decided)**

### Invariant: ownership on organized land lives on the province, not the cell

For a province (`province_id != 0`), the *province* is the authoritative
owner (`realm_of_province[province_id]`) — every member cell is owned by
whoever owns the province, always wholly, never partially. `OwnershipGrid.
cells` still holds a per-cell realm id (unchanged, so the renderer and
existing tests keep reading it exactly as cheaply as today), but for
organized cells it's a write-through cache: whenever a province's owning
realm changes, every one of its member cells gets bulk-written to match in
one pass — the same bulk-write pattern `_seed_real_civs` already uses for
the political mask — never decided cell-by-cell.

For unorganized land (`province_id == 0`), there's no province to defer to:
`OwnershipGrid.cells` is the sole, direct source of truth, exactly as it
works today. Ownership there still changes cell-by-cell via the brush,
independent of any province. **(decided)**

## Unorganized territory

Newly gained land (settled or conquered) defaults to `province_id = 0`.
It's real territory, but capped well below an organized province. Full
capability matrix: see the message this was drafted from, or below:

| Capability | Unorganized | Organized (in a province) |
|---|---|---|
| Raw resource extraction (coal, ores, water, flora/fauna) | Yes, base rate only | Yes, improvable with infrastructure |
| Resource processing (refining, manufacturing) | No | Yes |
| Roads | Dirt & gravel only | Full tiers as tech allows |
| Electricity routes | Cannot pass through | Allowed once unlocked |
| Military | Small local militia only, non-deployable | Full army stationing + logistics |
| Taxation | None | Yes |
| Named settlements | Suppressed | Full, plus founding new ones |
| Exception | A pre-existing settlement >= the "highly populous" threshold keeps its name/marker regardless | -- |

This gives unorganized land a real but weak defensive profile (population-
scaled militia only) — deliberately easy to take, which is the incentive to
actually organize conquered land rather than leave it as paperwork.
**(decided)**

### Settlement population threshold

**50,000** population, at the game's 300 BC start. Historical grounding:
only a handful of cities worldwide (Carthage, Alexandria, Rome, Syracuse)
crossed 100,000 at this date; the next tier (Athens proper, Antioch, regional
capitals) sits around 20,000-50,000; the large majority of ancient
settlements never passed a few thousand. 50,000 keeps this a genuinely rare
exception rather than catching every regional capital. **(decided for the
300 BC start; open how it scales)**

**(open)** This needs to scale once the timeline moves past antiquity —
50,000 stops being exceptional in later eras (the design already implies the
game runs into electricity/rail eras). Candidate approach: a relative measure
(top-percentile-of-world, or era-indexed tiers) instead of one fixed number
for the whole run. Revisit before building any era past Ancient.

## New concepts needed

- **`Settlement`** — named populated place: id, name, position, population,
  tier, founded year. Tracked separately from provinces (a province can
  contain several; an unorganized region can contain exactly one, if it
  clears the highly-populous threshold).
- **Resource categories** — coal, ores (iron/copper/tin/gold/silver/lead),
  water, flora/fauna (timber/game/fish/wild plants), plus categories that tie
  into the existing civ flavor text rather than inventing a separate trade-
  goods system later: Kush's iron industry, Nabatea's incense routes.
  **(open)** full list not finalized.
- **Infrastructure tier per cell** — none/dirt/gravel/paved/rail/electrified,
  hard-capped at gravel while unorganized.
- **Local militia** — defensive-only strength derived from population
  density, distinct from a realm's real armies (which require organized,
  supplied provinces to station).

## The five simulation systems (realm/province level)

- **Population** (per province, summed from the density field) — growth
  driven by food surplus; carries integration/sentiment (freshly conquered
  = low, decays toward assimilated over years unless suppressed by unrest).
- **Resources** (per province, summed from the density field) — yield type
  from terrain; gates military unit types and economic output.
- **Economy** (realm-level, aggregated from provinces) — treasury, income
  (tax from population x output) minus expenses (upkeep, admin cost).
- **Technology** (realm-level) — military / administrative / agriculture
  tracks; administrative tech is what should eventually cap how much
  territory a realm can hold before unrest/corruption penalties bite.
- **Military** (realm-level armies drawn from province manpower + resources)
  — recruited units gated by resources and tech; a leader/general slot
  extends the existing `Character.traits`, not a new character concept.

## Annexation resolution

Stays a paint action, not a menu. But the *unit of capture is the province*,
not the cell, once land is organized: painting/confirming inside an enemy
province checks that province's defense (garrison/fortification, derived
from the owning realm's military + tech + that province's integration)
against the attacker's strength. Beating it flips the *entire* province —
every member cell, including ones the brush stroke never touched, keeps its
existing `province_id`, population, infrastructure, and settlements, just
under a new owning realm (mechanically: the province's owner pointer flips,
then a bulk write propagates that to its cells, per the invariant above).
Same brush, same immediate feedback — the simulation decides whether paint
sticks, at province granularity, not a separate siege UI. **(decided)**

Unorganized land (enemy-held or wild) has no "whole shape" to inherit, so it
still resolves locally — per painted cell or contiguous painted blob,
against the weak militia already established for unorganized territory —
matching how much easier it's meant to be to take.

This also settles the earlier open question about post-conquest
organization: captured organized provinces do **not** reset to unorganized
and don't need a separate "decide how to organize this" step. They arrive
already organized, under new management, exactly as they were run before.

**(open)** Is a province's defense anchored specifically at its seat/main
settlement (so painting elsewhere in the province does nothing until you
reach the city), or can beating defense be triggered by occupying enough of
the province generally? "Conquered from one city" points at the former — a
defended seat that, once beaten, delivers the whole province — but this
decides where fortification/garrison data actually lives (on the
Settlement, not spread across the province), so worth confirming before
Phase 1.

**(open)** Instant flip the moment defense is beaten, or an occupation/siege
that plays out over multiple yearly ticks (accumulating painted presence)?
Affects how swingy conquest feels, especially for large provinces.

Since a province is now captured as an atomic whole, **province size is a
real balancing lever**: small/numerous provinces make conquest gradual, a
few huge provinces make it all-or-nothing and swingy. Worth weighing during
Phase 1 (province generation/seeding), not just an implementation detail.

## Suggested build order

1. Province layer (`province_id` grid + brush-editable boundaries), no stats
   yet — verify rendering/paint behavior only.
2. Density fields (population + resource-type per cell), provinces sum on
   demand.
3. Unorganized-territory capability gating (the matrix above) + settlements.
4. Economy (realm treasury from province sums).
5. Military + tech (army/power score, minimal tech multipliers).
6. Integration/sentiment (decay-toward-assimilated stat).
7. Paint-gated annexation (the resolution function tying 2-6 together).

Each phase should be independently verified against the real headless Godot
engine (a Godot 4.3 headless build has been used for this in dev sessions;
see the test scripts under `scripts/tests/` for the existing pattern) before
moving to the next.
