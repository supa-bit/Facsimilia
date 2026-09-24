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
2. **Translate** `H_final` into a target population per node, and move
   each node's actual population a limited step toward it.
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

**Source of `H_hist`: HYDE, not a hand-authored map. (decided)** HYDE is
already the decided source of the population density field (see Might,
below), and it ships gridded population snapshots from 10,000 BCE to
the present (the game uses HYDE 3.2.1, which ends at 2017). That's
exactly a sequence of historical target maps, baked onto this projection
by `build_population_mask.py`. So `H_hist(year)` = the HYDE snapshot for
that year, normalized to 0-1. Between HYDE's snapshots (per-century from
0 AD to 1700, per-decade to 2000, yearly after), interpolate linearly so
the pull moves smoothly instead of jumping at each keyframe. HYDE has no
300 BC snapshot: before 0 AD it steps by 1000 years. The 300 BC keyframe
is derived from 1000 BC and 0 AD per node, assuming steady exponential
growth in between (39.4 million people on the map, against 26.1 million
at 1000 BC and 48.2 million at 0 AD). At the 300 BC start, `H_sim` is seeded from `H_hist` itself, so
turn one is historical by construction and divergence only comes from play.

**The slow pull comes from population feedback into `H_sim`. (decided)**
On its own the blend is instantaneous and memoryless. An ignored but
historically important region would get `α * H_hist` worth of heat once
and then stay there forever, because nothing feeds `H_final` back into
`H_sim`. So `H_sim` is partly a function of the previous tick's
population, since people attract trade, labor, and markets:

```
H_sim(t+1) = normalize( drivers(t+1) + β * (Pop(t) / max Pop(t)) ^ (1/k) )
```

Here `drivers` is the gameplay term (food, water, trade, infrastructure,
war) and `β` is how strongly existing population draws more. Taking the
`1/k` root converts population back to heat scale, so the feedback carries
population's momentum without adding concentration of its own. `k` stays
the only concentration knob. At the 300 BC start the feedback term equals
`H_hist`, so the historical starting state is a fixed point: nothing drifts
until play changes the drivers. The small historical boost from `α` then
compounds into a real drift toward history, and heavy, sustained player
investment in `drivers` can still beat it. That's the "realist alternate
history" behavior. Explicit relaxation of `H_sim` toward `H_hist` was the
alternative. It was rejected because it drags on the simulation directly
instead of acting through population.

**`β = 0.25`, together with population inertia `μ = 0.0115` (stage 2).
Both are calibrated to real founded cities. (decided)** Feedback alone
can't make history slow to bend. The update is a contraction whose
per-tick factor is at most `1 - α`, so any change in drivers settles
within a few years whatever `β` is. A simulation showed a barren node
turning into a major city within 0-1 years. The timescales come from
population being a stock (see stage 2). The pair `(β, μ)` is then fitted
to how long real non-capital cities took to climb the rankings. Capitals
get their own calibrated bonus on top (see Capital growth bonuses, below).

**Historical target.** Years from a city's founding until it entered the
world's top 50 and top 10 cities by population. The figures are rough
estimates from Chandler (*Four Thousand Years of Urban Growth*) and
Modelski. The sample is cities that did make the list; most foundings
never do. "Capital" means the city was a country's capital from founding
or within its first few decades.

| City | Founded | Capital? | → top 50 | → top 10 |
|---|---|---|---|---|
| Alexandria | 331 BC | yes | ~20 | ~50 |
| Seleucia-on-Tigris | 305 BC | yes | ~25 | ~55 |
| Antioch | 300 BC | yes | ~50 | ~100 |
| Pataliputra | 490 BC | yes | — | ~190 |
| Constantinople (refounded) | 330 AD | yes | ~10 | ~60 |
| Baghdad | 762 | yes | ~10 | ~30 |
| Samarra | 836 | yes | ~8 | ~15 |
| Edo | 1603 | yes | ~20 | ~50 |
| St. Petersburg | 1703 | yes | ~50 | ~160 |
| Calcutta | 1690 | no | ~110 | — |
| New York | 1624 | no | ~190 | ~226 |
| Philadelphia | 1682 | no | ~150 | ~210 |
| Chicago | 1833 | no | ~37 | ~57 |
| Melbourne | 1835 | no | ~50 | — |
| Los Angeles | 1781 | no | ~140 | ~170 |
| Shenzhen | 1980 | no | ~20 | — |
| **Average, capitals** | | | **24.1 → 24** | **78.9 → 78** |
| **Average, non-capitals** | | | **99.6 → 100** | **165.8 → 166** |
| *Average, all* | | | *59.3 → 60* | *105.6 → 106* |

Targets are the averages rounded to the nearest even number. The
all-cities average (60/106) was the first calibration. It blended two
different populations: capitals founded by decree and organically grown
commercial cities. So the base engine is now fitted to **non-capitals
(100/166)**, and capitals reach **24/78** through their own bonus.

**Fit.** A toy simulation: 5,000 nodes, a rural background plus 300
cities on an ancient-style rank-size curve (population ∝ rank^-0.6, i.e.
flatter than modern Zipf), `k = 2`, yearly ticks. It measures a median
rural node given drivers matching the world's best site and held there.
A grid search (`tools/calibrate_population.py`, rerunnable) gives **`β =
0.25`, `μ = 0.0115` → top 50 at year 96, top 10 at year 166.** That's the
closest fit available. The model's growth curve can't quite reproduce
non-capitals' short 1.66x gap between the two milestones, so top 50 lands
four years early. Behavior at these values:

| Scenario | Result |
|---|---|
| Barren median node, drivers maxed and held indefinitely (player only; see Historical ceiling) | Top 50 at **96 years**, top 10 at **166 years**. Levels off at ~64% of the largest city's population, the world's #3, after a few centuries |
| Same city, investment abandoned after it matures | Half its growth (measured on a log scale) is gone in ~92 years, and it drops out of the top 20% and loses its name after ~260 years |
| 6th-largest historical city loses **all** its drivers for 30 years | Loses ~49% of its population but keeps its name. Back to 90% of its former size ~190 years after the war ends. Harsh, but this is total loss of food, trade, and infrastructure for a generation, the Rome-after-the-sack scale of disaster |

History's pull at these values comes from `α`, from the slow `μ`, and,
for everyone except the player, from the hard Historical ceiling below.
These values were fitted on a synthetic toy city distribution.
`python3 tools/calibrate_population.py --hyde` reruns the same fits on
every land node of the real baked 300 BC keyframe. (Ranks there are within
the map's extent, not the whole planet, so the rank-size curve of the map
region is what matters.) Result on the real grid:

| Test node | Current constants (top 50 / top 10) | Best refit | Target |
|---|---|---|---|
| Ordinary | 143y / 165y | 139y / 163y (β 0.75, μ 0.0145) | 100 / 166 |
| Country capital | 42y / 59y | 38y / 62y (transfer 7%, no bonus) | 24 / 78 |
| Province capital | 85y / 103y | 80y / 104y (transfer 2%, no bonus) | 62 / 122 |

No constants fit both times, and the refit barely improves on the current
values, so they stay as they are for now. **(open)** The cause is the
shape of HYDE's curve, not the constants. HYDE's top is flat: at 300 BC
the #10 node holds ~20,000 and the #50 ~15,000, so a growing city that
passes #50 is almost at #10. Every combination tried lands the two about
25 years apart, against the 55-65 years the historical founded cities
took. Options: target only one of the two ranks, measure ranks on the
largest nodes of a settlement cluster (stage 3) rather than single nodes,
or take the historical times as world ranks rather than map ranks.

### 2. Heatmap-to-population translation

```
Target_node   = C * H_final ^ k
Pop_node(t+1) = Pop_node(t) * (Target_node / Pop_node(t)) ^ μ        μ = 0.0115 / year
```

Population is a **stock**, not recomputed from scratch each tick. People
can only be born, die, and migrate so fast. Each year a node closes ~1.2%
of the gap to its target, **measured on a log scale**: growth and decline
are percentage rates, like real demography. A town 100x below its target
grows ~5% a year, a boomtown rate. A city 10% below its target grows
~0.1% a year. The log-scale form is required, not stylistic. A linear
step (`Pop += μ(Target - Pop)`) grows fastest at the very start, which
makes a founded city's climb from top 50 to top 10 take at least ~2.6x as
long as reaching the top 50, versus ~1.8x historically. Compounding growth
matches the real curve. It's what makes bending history a long-term
strategy rather than something one build order does. Each node's own
`Pop` is the value everything else reads (Might, taxes, naming, the
feedback term above). **(decided)**

Populations are whole people. Starting and seed populations come from
HYDE; see Starting and seed populations, below. **(decided)**

This is a power law, not exponential decay, but it has the intended
effect: a higher `k` crushes middling heat far harder than peak heat, so
population funnels into the hottest nodes.

- **`C` (carrying capacity, era multiplier)** scales with technology
  unlocks: about **150,000** for ancient megacities at the 300 BC start,
  and **tens of millions** by the industrial and space eras. It's
  implemented in world-total form (see below), so those per-city numbers
  are calibration checks on the hottest node, not a literal constant.
- **`k` (urbanization exponent)** rises with era. **`k = 2`** in ancient and
  agrarian eras spreads population out. **`k = 4-5`** in modern and
  interstellar eras simulates metropolitan concentration.

**(decided)**

Two consequences worth knowing before tuning:

- **`C` is a world total, not a per-node ceiling. (decided)** Because
  `H ≤ 1`, `H^4 ≤ H^2` everywhere. With `C` as a literal per-node ceiling,
  an era transition from `k = 2` to `k = 4` would make every node except
  the peak lose population overnight: a node at `H = 0.5` would drop from
  `0.25·C` to `0.0625·C`. So the implemented form is

  ```
  Target_i = P_world * H_i^k / Σ_j H_j^k
  ```

  `P_world` is the era's world population: HYDE's historical total by
  default. Growth mechanics (food surplus etc.) can push it off history,
  but only the player's realm can push it *above* history. Bots' growth
  mechanics can only lower it (see Historical ceiling). The total is preserved by construction, `k` still funnels
  people into the hottest nodes, and the output can be checked directly
  against HYDE's totals. The ~150,000 ancient-megacity figure becomes a
  sanity check: at 300 BC with `k = 2`, the hottest node should come out
  around there.
- **`k` must change gradually.** A step change in `k` at an era boundary
  redistributes the whole world in one tick. Interpolate `k` (and `C`) over
  the transition, the same way `H_hist` is interpolated between keyframes.

### Historical ceiling: only the player exceeds history

**Bots and unowned land can never hold more people in a place than real
history had there at that date. Only the player can. (decided)** Every node
has a ceiling that tracks history as it happened, rises *and* falls:

```
Ceiling_i(year) = HYDE_pop_i(year)        (interpolated between snapshots, like H_hist)
```

When real Babylon declines after Seleucia is founded, its ceiling declines
with it, and a bot-held Babylon declines too. A bot can't keep it at its
peak. Together with the `α` pull, which draws *under*-populated places up
toward history, bot-held and unclaimed land hugs the historical record:
never above it, and pulled back up when it falls below. (An earlier draft
used the running historical maximum as the ceiling. That was wrong. It let
a bot freeze Babylon at its peak forever, which contradicts the whole
point of the historical pull.) Rules:

- **Applies to:** every node not owned by the player's realm. That covers
  bot realms, unorganized land held by bots, and unclaimed land.
- **Enforced on the population itself:**
  `Pop_i ← min(Pop_i, Allowance_i)` after each tick's update, where
  `Allowance_i = Ceiling_i` for any place the player never lifted above
  history. Capping only the *target* isn't enough. Population closes about
  1.2% of its log gap to target each year, so when history falls it would
  lag far behind. Babylon's roughly 20x fall over three centuries would
  leave a bot-held Babylon about 2.4x above history the whole way down.
  Clamping the population makes bot-held places follow history's own
  decline at history's own pace. Targets are clamped too, so growth
  toward an unreachable target doesn't spill into the redistribution
  below.
- **Player legacy fades instead of snapping:** when a node the player
  held above its ceiling passes out of player ownership (conquest, or
  abandoning the land), `Allowance_i` starts at the node's population at
  that moment and relaxes toward `Ceiling_i` on the normal log-scale `μ`
  curve: about 60 years to halve the excess on a log scale. The city
  shrinks back to history gradually rather than overnight. Once
  `Allowance_i` reaches the ceiling, the node is an ordinary capped node
  again.
- **Capped excess stays with bots:** population trimmed off capped
  targets is redistributed to other *non-player* nodes still below their
  ceilings, in proportion to their targets. This keeps the world total on
  HYDE's historical figure. The player's own targets are never touched by
  it, so the player neither gains nor loses from bots hitting their caps.
  If every non-player node is at its ceiling, the excess is simply never
  born.
- **World total:** see `P_world` above. Only the player's realm can lift
  it above history.
- **Freedom below the ceiling:** bots still diverge from history
  *downward* and in how population shifts between places under their
  ceilings (wars, lost trade, moved capitals). They just can't create a
  metropolis where history never had one. Alternate-history *growth* is
  the player's alone.

A consequence worth knowing: a bot can't found a large new city in a
place that was historically empty, since the ceiling there is near zero.
Bots found cities where history did. The player is the only realm that
can put a great city somewhere new.

### Capital growth bonuses

Applies to **country capitals** (a realm's seat) and **province
capitals** (each organized province's administrative seat), for both the
player and bots. The ancient geographic regions (see Geographic regions)
are purely geographic and have no capitals. For bots, the bonus only
speeds growth *up to* the historical ceiling; it never lifts them past
it. **(decided)**

Two parts, both historically grounded, fitted to the targets above:

| | One-time resettlement when designated | Ongoing driver bonus | → top 50 | → top 10 | Historical target |
|---|---|---|---|---|---|
| Country capital | **5%** of the largest city's population | **+0.20** | 22y | 77y | 24 / 78 |
| Province capital | **1%** of the largest city's population | **+0.15** | 62y | 119y | 62 / 122 |
| Ordinary city | — | — | 96y | 166y | 100 / 166 |

- **Resettlement** reproduces the founding decree. Seleucia was populated
  from Babylon, Samarra from Baghdad, and Antioch started with ~5,300
  Athenian settlers from Antigonia plus their families. At 300 BC, 5% of
  the largest city is ~15,000 people for a country capital and 1% is
  ~3,000 for a province capital, the size of a Roman veteran colony. The
  settlers are **moved, not created**: they're drawn from the realm's other
  nodes in proportion to their population. It happens only the first time a
  given city becomes that realm's capital of that kind, so toggling
  capitals can't farm settlers. For bots it's clamped to the capital's
  remaining headroom under the ceiling.
- **Driver bonus** stands for the court, bureaucracy, garrison, and
  tribute spent at the seat. It's added to the node's `drivers` in the
  `H_sim` feedback formula for as long as the city stays capital. When it
  stops being capital, the bonus goes away and the city declines along
  the normal `μ` curve, which is the Samarra and Pataliputra arc.
- A faster growth rate (a `μ` multiplier) was tested as the bonus
  instead. It can't reproduce capitals' fast start: its best fit was 45
  years to the top 50 against a target of 24. Resettlement is what
  produces the historical jump.
- **Province-capital targets are interpolated**, midway between country
  capitals and ordinary cities. A founding-to-rank dataset for provincial
  seats doesn't exist the way it does for world top 50 and top 10.
  Rough anchors support it: Basra and Kufa, garrison capitals founded
  636/638, reached the top tier within ~30 years; Roman Carthage, a
  provincial capital refounded in 44 BC, took about 50 years to the top
  50 and ~130 to the top 10; Fustat and Lugdunum took longer.
  **(retune when better provincial data turns up)**
- **Capitals at the 300 BC start** already carry their bonus, and no
  resettlement is triggered for them. Starting drivers are backed out from
  HYDE so the historical start is still a fixed point with every existing
  capital's bonus included. Only capitals moved or founded after the start
  change anything.

### Starting and seed populations

**Everything starts from the historical population heatmaps (HYDE).
(decided)**

- **Game start:** every node's population is its HYDE population for
  300 BC. The whole world begins as historical record, not procedural.
- **Seeding an empty node** (a founding on empty land, or the
  resettlement of a ruin) uses, in order: the node's HYDE population for
  the current year; else, for the player only, the median rural node
  density of its ancient geographic region. For bots and unclaimed land
  the seed is clamped to the ceiling. Where history had nobody that year,
  nobody but the player can settle. A new
  settlement starts at what history says that land held. Capital
  resettlement, above, adds to that seed.
- **Ruins need an event.** Log-scale growth never reaches zero on its own
  while a node has any target. So a settlement only becomes a ruin
  through an explicit depopulating event: razing, plague, or forced
  evacuation. A ruin stays empty until a realm deliberately re-founds it
  (reseeded immediately), or until a generation, **25 years**, passes and
  it's naturally resettled at its seed size. Resettlement doesn't restore
  the name. The ruin revives under its old name only when its cluster
  re-enters the naming percentile. Carthage was razed in 146 BC and
  refounded by decree in 44 BC. **(25 years is a starting value)**

### 3. Settlement naming and spawning (percentile-based)

- **Spawn:** a cluster becomes a named settlement when its population
  enters the **top 2-5%** of all active population clusters (population
  `> 0`; empty clusters are excluded so a large empty map doesn't shift the
  bar). This keeps the label count proportional to map size in every era.
  **(decided — 3% to start; retune within 2-5% in playtesting)**
- **Hysteresis:** once named, a settlement keeps its name, even when
  conquered or when its land becomes unclaimed, until it drops below the
  **top 20%**. The wide gap between entry (2-5%) and exit (20%) stops
  labels flickering on and off when a city hovers near the line.
  **(decided)**
- **Ruins:** a named settlement whose population reaches exactly zero
  loses its active status but becomes a **ruin**: name and position are
  kept as a map marker, not deleted. It can be re-founded. Only
  depopulating events cause this; see Starting and seed populations.
  **(decided)**
- **Scale shift:** at the planetary-to-interstellar transition the rule
  doesn't change, only what a "cluster" is. The top 2% of hexes becomes
  the top 2% of hemispheres or core worlds. **(decided)**

Useful property: **the ordering of target populations depends only on the
ordering of `H_final`, not on `C` or `k`.** A percentile rank is unchanged
by any increasing transform, and `C * H^k` is increasing in `H`. So
retuning `C` and `k` changes displayed populations but never the
settlement ranking the world is heading toward. Only the heatmap changes
that. (Actual populations lag their targets by the inertia above, so the
names on the map settle into that ranking over decades, not instantly.)
Calibration and map readability can be tuned independently.

**Cluster vs. node vs. cell. (decided)**
The percentile has to rank *clusters*, not grid cells. The ownership grid
is 8192x5476 at ~0.59 km² per cell, so there are millions of land cells,
and the top 2% of *cells* would be hundreds of thousands of labels. A
single city also covers many adjacent cells. So:

- **Node:** the unit `H` and `Pop` are computed on. Coarser than a cell,
  roughly HYDE's native resolution (5 arcminutes, ~9 km), since there's no
  finer truth to compute against. Per-cell density values are sampled
  from their node.
- **Cluster:** a local maximum of `H_final` plus the contiguous nodes that
  drain to it (a watershed on the heatmap). Its population is the sum over
  those nodes. Nearby peaks closer than a minimum separation merge, so twin
  peaks don't produce two labels for one city.

**Global ranking with a regional floor. (decided)** A purely global
percentile would put almost every 300 BC label in the Aegean, the Nile,
Syria, and Mesopotamia. That's historically honest, but it leaves regions
like Libya Interior or Scythia with no labels at all. So the ranking is
global, plus a regional floor: any region with no named settlement gets
its single largest cluster named. Hysteresis and ruin rules apply to
floor-named settlements too. Regions are a fixed geographic partition, not
political borders, so conquest never changes which floor applies.

### Geographic regions — the ancient geographers' map

The partition uses the regions the Greco-Roman geographers themselves
drew (Hecataeus, Herodotus, Eratosthenes, Strabo, Ptolemy), grouped under
their three continents: **Europa**, **Libya** (their name for Africa), and
**Asia**. The map's extent (longitude -10 to 55, latitude 10 to 48) is
almost exactly the world those geographers described, so their divisions
cover it without inventing names. Continent borders follow the ancient
convention: the Tanais (Don) between Europa and Asia, the Nile/Isthmus
line between Libya and Asia. **(decided)**

| Continent | Regions |
|---|---|
| **Europa** | Hispania, Gallia, Raetia, Italia (with Sardinia and Corsica), Sicilia, Pannonia, Illyricum, Dacia, Thracia, Macedonia, Hellas (with Crete and the Aegean islands), Scythia |
| **Libya** | Mauretania, Numidia, Africa (the Carthaginian heartland, the name the continent itself later took), Syrtica, Cyrenaica, Marmarica, Aegyptus, Libya Interior (the Sahara), Aethiopia (everything south of Egypt, Kush included) |
| **Asia** | Lydia, Phrygia, Cilicia (with Cyprus), Cappadocia (with Pontus), Colchis (the Caucasus), Armenia, Syria (with Phoenicia and Judaea), Mesopotamia (with Assyria), Babylonia, Arabia Petraea, Arabia Deserta, Arabia Felix, Media, Susiana, Persis, Hyrcania, Sarmatia |

That makes 38 regions. Smaller neighbours are folded into the larger unit
named above rather than given their own entry. As with Europa, several of
these names are the ancient roots of modern ones (Africa, Arabia,
Armenia, Syria, Libya). Boundaries follow the ancient descriptions snapped
to physical features (rivers, ranges, coasts). They get baked as a
`region_id` per node by a `tools/build_region_mask.py` step, following the
same pattern as the land and political masks. **(exact boundary polygons
open, drawn when this is built; regions beyond the current extent, if the
map grows, get names the same way, from the geographers who described
those lands)**

**Display: only on the "Regions" map overlay. (decided)** Region names
never appear on the default map view. They show only when the player
switches the map overlay (see Map overlays, under New concepts) to
**Regions**, which draws each region's name and a faint boundary, grouped
by continent. The regional-floor rule runs all the time regardless of
which overlay is showing.

## New concepts needed

- **`Settlement`** — named populated place: id, name, position, population,
  tier, founded year, plus a status (`active` / `ruin`) for the hysteresis
  and ruin rules in Population & settlement engine. Tracked separately from
  provinces: a province can contain several, and unclaimed or unorganized
  land carries whichever clusters clear the percentile threshold. Named
  status survives conquest and loss of ownership.
- **Capitals** — each realm has one country capital, and each organized
  province has one province capital (its administrative seat). Both are
  a settlement reference plus a "first designated" record per realm, so
  the one-time resettlement can't repeat. They drive the growth bonuses
  in Capital growth bonuses.
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
- **Map overlays** (UI) — a control to switch what the map draws on top
  of the terrain: Political (today's ownership view, the default),
  **Regions** (the ancient geographic regions and their names, the only
  place those names appear), and later the population heatmap and
  resource overlays. One overlay active at a time.
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
the present (HYDE 3.2.1, CC BY 3.0). Free, public, no licensing cost — same footing as the Natural
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
   Then the historical ceiling, with a player/non-player split, and
   capital bonuses; rerun `tools/calibrate_population.py` on the real grid.
3. Unorganized-territory capability gating (the matrix above) + settlements
   (clustering, percentile naming, hysteresis, ruins), plus the region
   mask, regional floor, and the Regions map overlay.
4. Economy (realm treasury from province sums).
5. Military + tech (army/power score, minimal tech multipliers).
6. Integration/sentiment (decay-toward-assimilated stat).
7. Paint-gated annexation (the resolution function tying 2-6 together).

**Status: population engine started.** Built so far:
`scripts/world/population_engine.gd` (stages 1-2, the historical
ceiling with player legacy fade, capital bonuses and resettlement,
HYDE-based seeding and 25-year natural resettlement, save/load),
`tools/build_population_mask.py` (the HYDE import), the engine wired
into world generation, the yearly tick, saves, and a HUD population
line, and each real civ's 300 BC capital registered as an existing
capital. Covered by `scripts/tests/test_population_engine.gd`, which
reproduces the calibration timings. Not built yet: stage 3 (clusters,
percentile naming, ruins' labels), the region mask and Regions overlay,
real gameplay drivers (the engine exposes `driver_mods` for them; until
they exist the historical heat stands in as the baseline), player growth
of the world total, and province capitals, which wait on the province
layer. The HYDE 3.2.1 keyframes are baked into `data/population/`
(300 BC to 2017 AD, the 300 BC one derived; see MAP_DATA.md), so the
engine now runs on the real grid: 284,626 land nodes, loaded in ~0.5 s.
A yearly tick takes ~0.5 s headless, fine for now since the game is
turn-based (one tick per player-requested turn). Known issues on the real
grid: by 0 AD the non-player world holds 45.6 million
against HYDE's 48.2 million, because inertia lags the rising ceiling.
The calibration doesn't fit (see above).

Each phase should be independently verified against the real headless Godot
engine (a Godot 4.3 headless build has been used for this in dev sessions;
see the test scripts under `scripts/tests/` for the existing pattern) before
moving to the next.
