extends RefCounted

# Population & settlement engine, stages 1-2 (see MECHANICS.md,
# "Population & settlement engine"). Runs on a coarse node grid - one
# node per HYDE 5-arcminute cell, 780x456 over the map's extent, each
# covering ~10.5x12 ownership-grid cells - because HYDE is the finest
# population truth there is to compute against.
#
# Each yearly tick:
#   1. Blend simulated heat with historical heat:
#        H_sim   = normalize(drivers + BETA * (Pop / max Pop)^(1/K))
#        H_final = (1 - ALPHA) * H_sim + ALPHA * H_hist
#   2. Turn heat into a target population, world total preserved:
#        Target_i = P_world * H_final_i^K / sum_j H_final_j^K
#      then move each node a limited step toward it, on a log scale:
#        Pop_i <- Pop_i * (Target_i / Pop_i)^MU
#   3. Historical ceiling: every node not owned by the player's realm is
#      clamped to what HYDE says that place held in the current year.
#
# H_hist, the ceiling, and the 300 BC starting population all come from
# the same HYDE keyframes (tools/build_population_mask.py), linearly
# interpolated between snapshots. H_hist = (pop / max pop)^(1/K), so
# Target reproduces HYDE exactly when nothing has diverged: the
# historical start is a fixed point, and nothing drifts until play
# changes the drivers.
#
# Constants are calibrated in tools/calibrate_population.py --hyde, on the
# real 300 BC grid, against how long real founded cities took to reach the
# world top 50 / top 10.

const ALPHA := 0.175   # historical pull on H_final
const BETA := 5.0      # how strongly existing population attracts more
const MU := 0.0625     # share of the log gap to target closed per year
const K := 2.0         # urbanization exponent (ancient/agrarian eras)
# Rate a lost player city's excess fades back to history: ~60 years to
# halve it on a log scale. Kept apart from MU, which the growth fit set.
const LEGACY_MU := 0.0115

enum CapitalKind { COUNTRY, PROVINCE }
# One-time resettlement when a city first becomes a capital, as a share of
# the largest node's population, drawn from the realm's other nodes.
const CAPITAL_TRANSFER := [0.08, 0.04]
# Added to the capital node's drivers for as long as it stays capital.
const CAPITAL_DRIVER_BONUS := [0.05, 0.0]

const RESETTLE_YEARS := 25   # an emptied node resettles on its own after a generation
const TARGET_FLOOR := 0.5    # log-scale math needs a positive target; below 1 person rounds to 0
const NOT_EMPTY := -2147483648
const REDISTRIBUTE_PASSES := 8

const META_PATH := "res://data/population/hyde_meta.json"

var width := 0
var height := 0
var land_nodes := PackedInt32Array()   # indices of every land node; sea is never touched
var year := 0

# Historical data. Keyframes: [{year: int, pop: PackedFloat32Array}], sorted.
var keyframes: Array = []
var hist_pop := PackedFloat32Array()   # HYDE population at the current year = the ceiling
var h_hist := PackedFloat32Array()     # normalized historical heat

# Simulation state.
var pop := PackedFloat32Array()
var legacy := PackedFloat32Array()     # player-built excess that fades after the player loses a node
var empty_since := PackedInt32Array()  # year a node emptied, or NOT_EMPTY
var driver_mods := PackedFloat32Array()  # gameplay driver modifiers, written by future systems
var start_bonus := PackedFloat32Array()  # capital bonus already baked into the 300 BC history
var node_owner := PackedInt32Array()   # realm id per node (0 = unclaimed)
var player_realm_id := 0
var capitals: Dictionary = {}          # node index -> {"kind": CapitalKind, "realm": realm id}
var designated: Dictionary = {}        # "realm:node:kind" -> true; resettlement happens once
var player_world_delta := 0.0          # player-driven change to the world total (growth mechanics, later)
var seed_fallback := 0.0               # median rural node population, for player foundings on empty land

static func hyde_available() -> bool:
	return FileAccess.file_exists(META_PATH)

# Loads the baked HYDE keyframes. Returns false if they're missing.
func load_hyde() -> bool:
	if not hyde_available():
		return false
	var meta = JSON.parse_string(FileAccess.get_file_as_string(META_PATH))
	if typeof(meta) != TYPE_DICTIONARY:
		push_error("PopulationEngine: " + META_PATH + " is not valid JSON")
		return false
	var years := PackedInt32Array()
	var arrays: Array = []
	for kf in meta.keyframes:
		var bytes := FileAccess.get_file_as_bytes("res://data/population/" + kf.file)
		var raw := bytes.decompress_dynamic(-1, FileAccess.COMPRESSION_GZIP)
		var values := raw.to_float32_array()
		if values.size() != int(meta.width) * int(meta.height):
			push_error("PopulationEngine: keyframe %s has the wrong size" % kf.file)
			return false
		years.append(int(kf.year))
		arrays.append(values)
	set_keyframes(int(meta.width), int(meta.height), years, arrays)
	return true

# Keyframe arrays: people per node, row-major, negative = sea. The first
# keyframe's sea mask applies to all of them.
func set_keyframes(p_width: int, p_height: int, years: PackedInt32Array, arrays: Array) -> void:
	width = p_width
	height = p_height
	keyframes.clear()
	for i in years.size():
		keyframes.append({"year": years[i], "pop": arrays[i]})
	keyframes.sort_custom(func(a, b): return a.year < b.year)

	var first: PackedFloat32Array = keyframes[0].pop
	land_nodes = PackedInt32Array()
	var rural := PackedFloat32Array()
	for i in first.size():
		if first[i] >= 0.0:
			land_nodes.append(i)
			if first[i] >= 1.0:
				rural.append(first[i])
	rural.sort()
	seed_fallback = rural[rural.size() / 2] if rural.size() > 0 else 1.0

	var n := width * height
	pop.resize(n); legacy.resize(n); driver_mods.resize(n); start_bonus.resize(n)
	hist_pop.resize(n); h_hist.resize(n)
	empty_since.resize(n)
	node_owner.resize(n)

# Begins the simulation at start_year with every node at its historical
# population. start_capitals: [{node, kind, realm}] for capitals that
# already exist then - their bonus counts as part of the recorded history
# (backed out of the baseline drivers) and triggers no resettlement.
func start(start_year: int, start_capitals: Array = []) -> void:
	year = start_year
	_update_history()
	pop.fill(0.0)
	legacy.fill(0.0)
	driver_mods.fill(0.0)
	start_bonus.fill(0.0)
	empty_since.fill(NOT_EMPTY)
	capitals.clear()
	designated.clear()
	player_world_delta = 0.0
	for i in land_nodes:
		pop[i] = hist_pop[i] if hist_pop[i] >= 1.0 else 0.0
		if pop[i] == 0.0:
			empty_since[i] = year - RESETTLE_YEARS  # historically empty, not freshly razed
	for c in start_capitals:
		var node: int = c.node
		capitals[node] = {"kind": c.kind, "realm": c.realm}
		designated[_designation_key(c.realm, node, c.kind)] = true
		start_bonus[node] = minf(CAPITAL_DRIVER_BONUS[c.kind], h_hist[node])

# Ownership per node: realm id at each node, and which realm is the
# player's. Nodes owned by anyone else - bots, or 0 for unclaimed - are
# held to the historical ceiling.
func set_ownership(owners: PackedInt32Array, p_player_realm_id: int) -> void:
	node_owner = owners
	player_realm_id = p_player_realm_id

func is_player_node(i: int) -> bool:
	return player_realm_id != 0 and node_owner[i] == player_realm_id

# Makes `node` a capital of `realm`. The first time a given city becomes
# that realm's capital of that kind, settlers move in from the realm's
# other nodes (moved, not created). Bots can't be resettled past the
# historical ceiling.
func designate_capital(node: int, kind: int, realm: int) -> void:
	capitals[node] = {"kind": kind, "realm": realm}
	var key := _designation_key(realm, node, kind)
	if designated.has(key):
		return
	designated[key] = true

	var amount: float = CAPITAL_TRANSFER[kind] * _max_pop()
	if not is_player_node(node):
		amount = minf(amount, maxf(0.0, hist_pop[node] - pop[node]))
	var donors_total := 0.0
	for i in land_nodes:
		if i != node and node_owner[i] == realm:
			donors_total += pop[i]
	# Never strip a realm of more than half of everyone else it has.
	amount = minf(amount, donors_total * 0.5)
	if amount <= 0.0:
		return
	var share := amount / donors_total
	for i in land_nodes:
		if i != node and node_owner[i] == realm:
			pop[i] -= pop[i] * share
	pop[node] += amount
	empty_since[node] = NOT_EMPTY

func clear_capital(node: int) -> void:
	capitals.erase(node)

func _designation_key(realm: int, node: int, kind: int) -> String:
	return "%d:%d:%d" % [realm, node, kind]

# Advances the simulation to new_year (normally year + 1). The hot loops
# work on local copies of the packed arrays and inline the per-node
# checks: GDScript member and function-call overhead dominates otherwise
# (~264k land nodes on the real map).
func tick(new_year: int) -> void:
	year = new_year
	_update_history()
	var nodes := land_nodes
	var hh := h_hist
	var hp := hist_pop
	var p_arr := pop
	var owners := node_owner
	var pid := player_realm_id if player_realm_id != 0 else -1
	var n := width * height

	# Stage 1: blend simulated and historical heat.
	var max_pop := _max_pop()
	var inv_max := 1.0 / max_pop if max_pop > 0.0 else 0.0
	var sb := start_bonus
	var mods := driver_mods
	var bonus := _capital_bonus_array()
	var hs := PackedFloat32Array()
	hs.resize(n)
	var hs_max := 0.0
	for i in nodes:
		var v := maxf(0.0, hh[i] - sb[i]) + mods[i] + bonus[i] + BETA * _heat_of_share(p_arr[i] * inv_max)
		hs[i] = v
		hs_max = maxf(hs_max, v)

	# Stage 2: heat -> target population, world total preserved.
	var inv_hs := 1.0 / hs_max if hs_max > 0.0 else 0.0
	var weight_sum := 0.0
	var hist_total := 0.0
	for i in nodes:
		var hf := (1.0 - ALPHA) * hs[i] * inv_hs + ALPHA * hh[i]
		var w := hf * hf if K == 2.0 else pow(hf, K)
		hs[i] = w  # reuse: now holds the target weight
		weight_sum += w
		hist_total += hp[i]
	var p_world := maxf(0.0, hist_total + player_world_delta)
	var scale := p_world / weight_sum if weight_sum > 0.0 else 0.0
	var target := PackedFloat32Array()
	target.resize(n)
	for i in nodes:
		target[i] = hs[i] * scale

	# Historical ceiling on targets, excess redistributed among other
	# non-player nodes with headroom (the player's own targets untouched).
	_update_legacy()
	var lg := legacy
	_apply_ceiling_to_targets(target)

	# Log-scale step toward target; empty nodes resettle after a generation.
	var es := empty_since
	var y := year
	for i in nodes:
		var p := p_arr[i]
		var t := target[i]
		var mine := owners[i] == pid
		if p > 0.0:
			p = p * pow(maxf(t, TARGET_FLOOR) / p, MU)
			if p < 1.0:
				p = 0.0
				es[i] = y
		elif t >= 1.0 and y - es[i] >= RESETTLE_YEARS:
			p = seed_population(i)
			if p >= 1.0:
				es[i] = NOT_EMPTY
			else:
				p = 0.0
		# The ceiling applies to the population itself, not just the
		# target - otherwise bot-held places would lag history's declines.
		if mine:
			lg[i] = p  # starts fading from here if the player loses it
		else:
			p = minf(p, maxf(hp[i], lg[i]))
		p_arr[i] = p
	pop = p_arr
	legacy = lg
	empty_since = es

# Population share of the largest node -> heat, the inverse of Target's H^K.
static func _heat_of_share(share: float) -> float:
	return sqrt(share) if K == 2.0 else pow(share, 1.0 / K)

func _capital_bonus_array() -> PackedFloat32Array:
	var bonus := PackedFloat32Array()
	bonus.resize(width * height)
	for node in capitals:
		bonus[node] = CAPITAL_DRIVER_BONUS[capitals[node].kind]
	return bonus

# What a founding or natural resettlement on empty node i starts with:
# HYDE's population there this year; for the player only, the median
# rural node if history had nobody there. Others are clamped to the ceiling.
func seed_population(i: int) -> float:
	var s := hist_pop[i]
	if s < 1.0 and is_player_node(i):
		s = seed_fallback
	if not is_player_node(i):
		s = minf(s, _allowance(i))
	return s

# Most people a non-player node may hold: the historical ceiling, or the
# fading remainder of what the player built there, whichever is larger.
func _allowance(i: int) -> float:
	return maxf(hist_pop[i], legacy[i])

# Player-built excess on nodes the player no longer holds relaxes toward
# the ceiling on a log-scale LEGACY_MU curve (~60 years to halve).
func _update_legacy() -> void:
	var lg := legacy
	var owners := node_owner
	var pid := player_realm_id if player_realm_id != 0 else -1
	for i in land_nodes:
		var l := lg[i]
		if l <= 0.0 or owners[i] == pid:
			continue
		var ceiling := maxf(hist_pop[i], TARGET_FLOOR)
		lg[i] = 0.0 if l <= ceiling else l * pow(ceiling / l, LEGACY_MU)
	legacy = lg

func _apply_ceiling_to_targets(target: PackedFloat32Array) -> void:
	var nodes := land_nodes
	var hp := hist_pop
	var lg := legacy
	var owners := node_owner
	var pid := player_realm_id if player_realm_id != 0 else -1
	var excess := 0.0
	for i in nodes:
		if owners[i] == pid:
			continue
		var cap := maxf(hp[i], lg[i])
		if target[i] > cap:
			excess += target[i] - cap
			target[i] = cap
	for _pass in REDISTRIBUTE_PASSES:
		if excess < 1.0:
			return
		var room_weight := 0.0
		for i in nodes:
			if owners[i] != pid and target[i] < maxf(hp[i], lg[i]):
				room_weight += target[i]
		if room_weight <= 0.0:
			return  # every non-player node is at its ceiling: the excess is never born
		var leftover := 0.0
		var k := excess / room_weight
		for i in nodes:
			if owners[i] == pid:
				continue
			var cap := maxf(hp[i], lg[i])
			if target[i] < cap:
				var t := target[i] * (1.0 + k)
				if t > cap:
					leftover += t - cap
					t = cap
				target[i] = t
		excess = leftover

# Interpolates HYDE to the current year: the ceiling and H_hist.
func _update_history() -> void:
	var a: Dictionary = keyframes[0]
	var b: Dictionary = keyframes[0]
	for kf in keyframes:
		if kf.year <= year:
			a = kf
		if kf.year >= year:
			b = kf
			break
	if b.year < year:
		b = a  # past the last keyframe: hold it
	var f := 0.0 if b.year == a.year else float(year - a.year) / float(b.year - a.year)
	var pa: PackedFloat32Array = a.pop
	var pb: PackedFloat32Array = b.pop
	var hp := hist_pop
	var hh := h_hist
	var max_hist := 0.0
	for i in land_nodes:
		var v := lerpf(maxf(pa[i], 0.0), maxf(pb[i], 0.0), f)
		hp[i] = v
		max_hist = maxf(max_hist, v)
	var inv_max := 1.0 / max_hist if max_hist > 0.0 else 0.0
	for i in land_nodes:
		hh[i] = _heat_of_share(hp[i] * inv_max)
	hist_pop = hp
	h_hist = hh

func _max_pop() -> float:
	var p := pop
	var m := 0.0
	for i in land_nodes:
		m = maxf(m, p[i])
	return m

# --- Queries --------------------------------------------------------------

func total_population() -> float:
	var s := 0.0
	for i in land_nodes:
		s += pop[i]
	return s

func realm_population(realm: int) -> float:
	var s := 0.0
	for i in land_nodes:
		if node_owner[i] == realm:
			s += pop[i]
	return s

# World rank of node i by population (1 = largest).
func rank_of(i: int) -> int:
	var r := 1
	for j in land_nodes:
		if pop[j] > pop[i]:
			r += 1
	return r

# --- Grid mapping ---------------------------------------------------------
# Nodes and ownership cells share the map's linear lon/lat projection, so
# the mapping is a plain rescale.

func node_at_cell(x: int, y: int, grid_width: int, grid_height: int) -> int:
	var nx := clampi(x * width / grid_width, 0, width - 1)
	var ny := clampi(y * height / grid_height, 0, height - 1)
	return ny * width + nx

func node_at_lonlat(lon: float, lat: float, lon_min: float, lon_max: float,
		lat_min: float, lat_max: float) -> int:
	var nx := clampi(int((lon - lon_min) / (lon_max - lon_min) * width), 0, width - 1)
	var ny := clampi(int((lat_max - lat) / (lat_max - lat_min) * height), 0, height - 1)
	return ny * width + nx

# Samples each node's owner from the ownership grid at the node's centre
# cell. Sea cells (sea_owner) count as unclaimed.
func ownership_from_grid(cells: PackedInt32Array, grid_width: int, grid_height: int,
		sea_owner: int) -> PackedInt32Array:
	var owners := PackedInt32Array()
	owners.resize(width * height)
	for i in land_nodes:
		var nx := i % width
		var ny := i / width
		var cx := int((nx + 0.5) * grid_width / width)
		var cy := int((ny + 0.5) * grid_height / height)
		var o := cells[cy * grid_width + cx]
		owners[i] = 0 if o == sea_owner else o
	return owners

# --- Save / load ----------------------------------------------------------

func to_dict() -> Dictionary:
	var caps := []
	for node in capitals:
		caps.append({"node": node, "kind": capitals[node].kind, "realm": capitals[node].realm})
	return {
		"year": year,
		"pop": pop,
		"legacy": legacy,
		"empty_since": empty_since,
		"driver_mods": driver_mods,
		"start_bonus": start_bonus,
		"capitals": caps,
		"designated": designated.keys(),
		"player_world_delta": player_world_delta,
	}

# Requires set_keyframes()/load_hyde() first, with the same grid size.
func load_from_dict(data: Dictionary) -> bool:
	var n := width * height
	for key in ["pop", "legacy", "driver_mods", "start_bonus"]:
		if (data.get(key, PackedFloat32Array()) as PackedFloat32Array).size() != n:
			push_error("PopulationEngine: saved '%s' doesn't match this node grid" % key)
			return false
	year = int(data.year)
	pop = data.pop
	legacy = data.legacy
	driver_mods = data.driver_mods
	start_bonus = data.start_bonus
	empty_since = data.empty_since
	capitals.clear()
	for c in data.capitals:
		capitals[int(c.node)] = {"kind": int(c.kind), "realm": int(c.realm)}
	designated.clear()
	for k in data.designated:
		designated[k] = true
	player_world_delta = float(data.get("player_world_delta", 0.0))
	_update_history()
	return true
