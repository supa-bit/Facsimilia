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
3. **Density fields** (new) — population and raw-resource presence.
   Population is **live, not baked**: it's `C * H_final^k`, recomputed
   each population tick from the blended simulated and historical heatmaps
   (see Population & settlement engine, below). HYDE (see Might, below)
   provides the historical side of that blend and seeds the 300 BC starting
   state. Raw-resource presence is still baked at world-gen from terrain
   data already loaded for the terrain shader. Not normally player-painted
   (geography/history-driven), though the same brush code could
   hand-adjust them in a dev/authoring context.

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

## Unclaimed vs. unorganized land — two different things

Easy to conflate, so pinning it down: **unclaimed** land (`owner_id == 0` in
the existing ownership grid — today's "wild" cells) has no owning realm at
all. **Unorganized** land has a realm owner, just no province yet
(`province_id == 0`). They share one mechanic and diverge on everything that
depends on having an owner to credit output to.

Population is a base geographic fact, independent of ownership — the
density field (see Layers, above) exists on *every* land cell, unclaimed or
not. Unclaimed land has its own (typically sparse) native population, same
as unorganized or organized land does. Local militia strength derives from
that population everywhere it applies: unclaimed land resists incursion the
same way unorganized land resists conquest, scaled by however populated it
actually is. What differs is output — unclaimed land has no owning realm to
credit raw resources to, so **it produces nothing for no one**, even though
the same coal/ore/water/flora deposits are physically present in its
density field. The moment it's claimed and becomes unorganized territory of
some realm, that realm starts drawing the base-rate raw output described
below. This also refines the earlier assumption that settling unclaimed
land is an unconditional freebie: it's cheap in practice (native
population, and therefore militia, is usually sparse) but not literally
free — it still resolves against whatever local defense exists.
**(decided)**

Newly gained land (settled or conquered) defaults to `province_id = 0`.
It's real territory, but capped well below an organized province:

| Capability | Unclaimed (no owner) | Unorganized (owned, no province) | Organized (in a province) |
|---|---|---|---|
| Population | Yes — sparse, native | Yes | Yes |
| Raw resource extraction (coal, ores, water, flora/fauna) | None — no owner to credit it to | Yes, base rate, credited to owner | Yes, improvable with infrastructure |
| Resource processing | No | No | Yes |
| Roads | None built; at most a natural/trade trail passing through | Dirt & gravel only | Full tiers as tech allows |
| Electricity routes | Cannot pass through | Cannot pass through | Allowed once unlocked |
| Military / defense | Population-scaled local militia, resists incursion | Population-scaled local militia, non-deployable | Full army stationing + logistics |
| Taxation | None (no owner) | None | Yes |
| Named settlements | Suppressed, same highly-populous exception applies | Suppressed, same highly-populous exception applies | Full, plus founding new ones |

Deliberately weak defense on both unclaimed and unorganized land is the
incentive to actually organize conquered/settled territory rather than
leave it as paperwork. **(decided)**

### Settlement naming threshold — percentile rank, with hysteresis

Supersedes the earlier "10% of the world's largest settlement" rule (and the
flat 50,000 placeholder before that). Both of those measured *size*; what
the rule actually needs to protect is *map readability*, and that's a
question of how many labels are on screen, which a percentile controls
directly. Full rules, including where the populations being ranked come
from, are in **Population & settlement engine**, below. Short version: a
population cluster gets named when it enters the top **2-5%** of active
clusters, and keeps its name until it falls out of the top **20%** or hits
zero. This is the "highly populous exception" the capability matrix above
refers to. It applies the same way on unclaimed, unorganized, and organized
land. **(decided)**

## Population & settlement engine

Three stages, run in this order every population tick:

1. **Blend** the simulated and historical heatmaps into one `H_final`.
2. **Translate** `H_final` into raw population per node.
3. **Rank** clusters by population, and name or un-name settlements.

### 1. Historical gravity: the attractor field

Two heatmaps are kept, both normalized to 0.0-1.0 per node:

- **`H_sim`** (simulated heat) is driven by gameplay: food, water, trade
  routes, infrastructure the player or AI builds, war damage.
- **`H_hist`** (historical target heat) is a static, invisible reference,
  never shown to the player. It carries e.g. a heavy spike over Rome or the
  Nile Delta in antiquity.

```
H_final = (1 - α) * H_sim + α * H_hist        α ∈ [0.15, 0.20]
```

A convex blend of two 0-1 fields stays in 0-1, so no renormalization is
needed. Since `H_final ≥ (1 - α) * H_sim`, a player who builds heavily in a
historically barren region always keeps at least 80-85% of their own heat
there. `H_sim` overpowers the historical pull, which is what makes
alternate history possible. **(decided)**

**Source of `H_hist`: HYDE, not a hand-authored map. (proposed)** HYDE is
already the decided source of the population density field (see Might,
below), and it ships gridded population snapshots from 10,000 BCE to
2023 CE. That's exactly a sequence of historical target maps, and it's
already on this projection once `build_population_mask.py` exists. So
`H_hist(year)` = the HYDE snapshot for that year, normalized to 0-1.
Between HYDE's snapshots (per-century before 1700, per-decade after),
interpolate linearly so the pull moves smoothly instead of jumping at each
keyframe. At the 300 BC start, `H_sim` is seeded from `H_hist` itself, so
turn one is historical by construction and divergence only comes from play.

**The blend alone doesn't produce a slow pull. (open — needs a call before
implementation)** As written, the blend is instantaneous and memoryless.
An ignored but historically important region immediately gets `α * H_hist`
worth of heat and then stays there forever. It never drifts further toward
history, because nothing feeds `H_final` back into `H_sim`. To get the
intended "populations wander there organically over time" behavior, one of
these is required:

- **Feedback (recommended):** `H_sim` is partly a function of the previous
  tick's population, since people attract trade, labor, and markets. The
  small historical boost then compounds tick over tick into a real drift,
  and it stays counterable by player investment. It's also how `H_sim`
  most likely wants to work anyway.
- **Explicit relaxation:** `H_sim += r * (H_hist - H_sim) * dt` with a slow
  rate `r`. This is simpler to reason about, but it's a second tuning knob
  alongside `α`.

### 2. Heatmap-to-population translation

```
Pop_node = C * H_final ^ k
```

This is a power law, not exponential decay, but it has the intended
effect: a higher `k` crushes middling heat far harder than peak heat, so
population funnels into the hottest nodes.

- **`C` (carrying capacity, era multiplier)** is the population of a node at
  `H = 1.0`, i.e. the ceiling for the single hottest node in the world. It
  scales with technology unlocks: about **150,000** for ancient megacities
  at the 300 BC start, and **tens of millions** by the industrial and space
  eras.
- **`k` (urbanization exponent)** rises with era. **`k = 2`** in ancient and
  agrarian eras spreads population out. **`k = 4-5`** in modern and
  interstellar eras simulates metropolitan concentration.

**(decided)**

Two consequences worth knowing before tuning:

- **Raising `k` shrinks total world population unless `C` rises with it.**
  Because `H ≤ 1`, `H^4 ≤ H^2` everywhere, so an era transition that bumps
  `k` from 2 to 4 with `C` held flat would make every node except the peak
  lose population overnight. A node at `H = 0.5` goes from `0.25·C` to
  `0.0625·C`. `C` and `k` steps must be tuned together, or `C` gets defined
  as a *world total* instead: `Pop_i = P_world * H_i^k / Σ_j H_j^k`. That
  form preserves the total by construction and keeps the funneling effect.
  It also lines up directly against HYDE's world totals. **(open — which
  form of `C`)**
- **`k` must change gradually.** A step change in `k` at an era boundary
  redistributes the whole world in one tick. Interpolate `k` (and `C`) over
  the transition, the same way `H_hist` is interpolated between keyframes.

### 3. Settlement naming and spawning (percentile-based)

- **Spawn:** a cluster becomes a named settlement when its population
  enters the **top 2-5%** of all active population clusters (population
  `> 0`; empty clusters are excluded so a large empty map doesn't shift the
  bar). This keeps the label count proportional to map size in every era.
  **(decided — exact value within 2-5% to be set in playtesting; 3% as the
  starting value)**
- **Hysteresis:** once named, a settlement keeps its name, even when
  conquered or when its land becomes unclaimed, until it drops below the
  **top 20%**. The wide gap between entry (2-5%) and exit (20%) stops
  labels flickering on and off when a city hovers near the line.
  **(decided)**
- **Ruins:** a named settlement whose population reaches exactly zero
  loses its active status but becomes a **ruin**: name and position are
  kept as a map marker, not deleted. It can be re-founded. **(decided)**
- **Scale shift:** at the planetary-to-interstellar transition the rule
  doesn't change, only what a "cluster" is. The top 2% of hexes becomes
  the top 2% of hemispheres or core worlds. **(decided)**

Useful property: **naming depends only on the ordering of `H_final`, not
on `C` or `k`.** A percentile rank is unchanged by any increasing
transform, and `C * H^k` is increasing in `H`. So retuning `C` and `k`
changes displayed populations but never which settlements are named. Only
the heatmap changes that. Calibration and map readability can be tuned
independently.

**Cluster vs. node vs. cell. (open — needs a call before implementation)**
The percentile has to rank *clusters*, not grid cells. The ownership grid
is 8192x5476 at ~0.59 km² per cell, so there are millions of land cells,
and the top 2% of *cells* would be hundreds of thousands of labels. A
single city also covers many adjacent cells. The proposed definition:

- **Node:** the unit `H` and `Pop` are computed on. Coarser than a cell,
  roughly HYDE's native resolution (5 arcminutes, ~9 km), since there's no
  finer truth to compute against. Per-cell density values are sampled
  from their node.
- **Cluster:** a local maximum of `H_final` plus the contiguous nodes that
  drain to it (a watershed on the heatmap). Its population is the sum over
  those nodes. Nearby peaks closer than a minimum separation merge, so twin
  peaks don't produce two labels for one city.

**Global vs. regional ranking. (open)** A purely global percentile will
cluster almost every 300 BC label into the Mediterranean, Mesopotamia, the
Ganges, and the Yellow River. That's historically honest, but it leaves
regions like Sub-Saharan Africa or Northern Europe label-free. Suggested
compromise: rank globally, plus a regional floor that names each region's
single largest cluster if it has none yet. Hysteresis applies to those too.

## New concepts needed

- **`Settlement`** — named populated place: id, name, position, population,
  tier, founded year, plus a status (`active` / `ruin`) for the hysteresis
  and ruin rules in Population & settlement engine. Tracked separately from
  provinces: a province can contain several, and unclaimed or unorganized
  land carries whichever clusters clear the percentile threshold. Named
  status survives conquest and loss of ownership.
- **Resource categories** — coal, ores (iron/copper/tin/gold/silver/lead),
  water, flora/fauna (timber/game/fish/wild plants), plus categories that tie
  into the existing civ flavor text rather than inventing a separate trade-
  goods system later: Kush's iron industry, Nabatea's incense routes.
  **(open)** full list not finalized.
- **Infrastructure tier per cell** — none/dirt/gravel/paved/rail/electrified,
  hard-capped at gravel while unorganized.
- **Might** — see the dedicated section below. Supersedes the standalone
  "local militia" idea by generalizing it into the domain-split strength
  score used everywhere annexation resolves, for both realms and raw land.
- **Floating confirmation panel** (UI) — live Might + resource-icon readout
  while painting; see Annexation UX, below.

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

## Might: the unified strength score

The number actually compared whenever annexation resolves. Split into
domains that unlock by era: **Land** and **Naval** (both available from the
game's 300 BC start — ancient/medieval navies were real, triremes and
quinqueremes included, and several starting civs are explicitly maritime
powers: Carthage, Rome, the Greek world, Ptolemaic Egypt), **Aerial**
(~1900 AD), and eventually **Space**. A domain that hasn't unlocked yet
shows as 0/greyed out for every realm equally. The data model carries all
four slots from day one (cheap now, expensive to retrofit later) even
though two sit inert for a long time. **(decided)**

(The ~1500 AD instinct isn't wrong, just describes a different thing — true
ocean-crossing "Age of Sail" capability, as opposed to Naval Might existing
at all. That's a plausible later refinement, e.g. a coastal/riverine vs.
blue-water distinction, not something blocking Naval Might being active
from turn one.)

Where a Might number comes from depends on what's being measured:
- **A realm's own Might** (the attacker, or an organized province's
  defender) — derived from that realm's Military system (armies, tech,
  fortification), per domain.
- **Unclaimed or unorganized land's Might** (defender only — raw land has
  no attacker side) — derived directly from local population density: the
  same per-cell density field already planned under Layers, read live, not
  a separately baked asset. "Heat map" here describes the *display* (a
  colored intensity overlay), not a second data source that could drift out
  of sync with the density field it's reading.

This resolves both open questions from before:
- Defense is **not** seat-anchored — it's the whole province's aggregate
  Might, so beating it (in whichever domain(s) apply — Land and Naval, for
  a very long time) flips the entire province regardless of exactly which
  cells got painted, matching the earlier "conquered a state from one city"
  decision.
- Resolution reads as **instant at confirm-time** — one decisive Might
  comparison when you commit, not a multi-tick siege bar. (Inferred from
  "confirm... if you can handle it" rather than stated outright — flagging
  in case a multi-year siege was actually intended.)

One refinement folded back into Layers: since Might for unclaimed land is
explicitly meant to reflect *historical* population, the density field
itself is sourced from **HYDE** (History Database of the Global
Environment — PBL Netherlands Environmental Assessment Agency / Utrecht
University), which gives gridded population estimates from 10,000 BCE to
2023 CE. Free, public, no licensing cost — same footing as the Natural
Earth coastline and historical-basemaps borders already in the project.
Implementation follows the same pattern as `tools/build_land_mask.py`: a
new `tools/build_population_mask.py`-style import step bakes HYDE's grid
into textures on this project's own projection. One snapshot is baked per
HYDE keyframe the game's timeline covers, starting at 300 BC, since the
snapshots double as the `H_hist` keyframes in Population & settlement
engine. Terrain-driven estimation is dropped as the source of
record; it remains only as a plausible gap-filler if HYDE's resolution
turns out too coarse for a specific region. **(decided)**

## Annexation UX: fog of war and the floating confirmation panel

A target's Might is hidden until you start painting over it — no defense
numbers just from looking at the map. Starting a paint stroke on unclaimed
or enemy territory triggers the reveal, and it updates live as the stroke
grows or shrinks, reusing the same dirty-cell tracking the ownership
proposal paint already has.

While painting, a floating confirmation panel shows, live:
- Might per domain for whatever's currently painted (Land and Naval as real
  numbers; Aerial/Space greyed out at 0 until their era unlocks)
- Icons for whichever resource types are present in the currently-painted
  cells

Confirm stays available regardless of whether you're favored — showing the
numbers is about an informed choice, not a blocked action. Losing (per the
existing rule below) means the paint snaps back with attrition to the
attacker's own Might, not that confirm gets disabled. **(decided)**

When one stroke spans multiple distinct provinces (or crosses from
unclaimed land into an organized enemy province), the panel shows a
**breakdown per province** — a separate Might/resource readout for each
one touched, not a blended total. Matches how resolution itself happens:
each affected province is checked against its own Might independently, so
the panel should show exactly what's about to be decided, not an average
that hides which individual provinces you can and can't take. **(decided)**

## Annexation resolution

Stays a paint action, not a menu. But the *unit of capture is the province*,
not the cell, once land is organized: painting/confirming inside an enemy
province checks that province's Might (see above — garrison/fortification,
derived from the owning realm's military + tech + that province's
integration) against the attacker's Might. Beating it flips the *entire*
province — every member cell, including ones the brush stroke never touched, keeps its
existing `province_id`, population, infrastructure, and settlements, just
under a new owning realm (mechanically: the province's owner pointer flips,
then a bulk write propagates that to its cells, per the invariant above).
Same brush, same immediate feedback — the simulation decides whether paint
sticks, at province granularity, not a separate siege UI. **(decided)**

Unorganized land (enemy-held, but never administered into a province) has
no "whole shape" to inherit, so it still resolves locally — per painted
cell or contiguous painted blob — against its population-scaled militia.
Unclaimed land (no owner at all) resolves the same way, against its own
native militia — usually weak given how sparse unclaimed populations tend
to be, but not an unconditional freebie either.

This also settles the earlier open question about post-conquest
organization: captured organized provinces do **not** reset to unorganized
and don't need a separate "decide how to organize this" step. They arrive
already organized, under new management, exactly as they were run before.

Since a province is now captured as an atomic whole, **province size is a
real balancing lever**: small/numerous provinces make conquest gradual, a
few huge provinces make it all-or-nothing and swingy. Worth weighing during
Phase 1 (province generation/seeding), not just an implementation detail.

## Suggested build order

1. Province layer (`province_id` grid + brush-editable boundaries), no stats
   yet — verify rendering/paint behavior only.
2. Density fields (population + resource-type per cell), provinces sum on
   demand. Population via the engine: HYDE-sourced `H_hist`, `H_sim`
   seeded from it, blend, then `C * H^k`. Start with `H_sim` static and
   verify the 300 BC output against HYDE before adding any dynamics.
3. Unorganized-territory capability gating (the matrix above) + settlements
   (clustering, percentile naming, hysteresis, ruins).
4. Economy (realm treasury from province sums).
5. Military + tech (army/power score, minimal tech multipliers).
6. Integration/sentiment (decay-toward-assimilated stat).
7. Paint-gated annexation (the resolution function tying 2-6 together).

Each phase should be independently verified against the real headless Godot
engine (a Godot 4.3 headless build has been used for this in dev sessions;
see the test scripts under `scripts/tests/` for the existing pattern) before
moving to the next.
