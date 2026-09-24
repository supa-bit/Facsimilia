extends SceneTree

# Population engine tests on a deterministic 100x50 toy world: rural
# background ~1,000 people per node plus 300 cities on an ancient-style
# rank-size curve (300,000 * rank^-0.6). The climb timings pin the engine
# to tools/calibrate_population.py's numpy model of the same rules, which
# gives the same numbers on its toy world (--check-only): 59 years to the
# top 50 for an ordinary city, 6 for a country capital, 28 for a province
# capital, and none reaches the top 10. The constants are fitted on the
# real HYDE grid instead, where that fit is checked:
# test_population_hyde.gd.

const PopulationEngine := preload("res://scripts/world/population_engine.gd")

const W := 100
const H := 50
const N := W * H
const PLAYER := 1
const BOT := 2
const START := -300

func _frac(x: float) -> float:
	return x - floor(x)

func _u(i: int) -> float:
	return _frac(sin(i * 12.9898) * 43758.5453)

func _fixture() -> PackedFloat32Array:
	var pop := PackedFloat32Array()
	pop.resize(N)
	for i in N:
		var g := (_u(3 * i + 1) + _u(3 * i + 2) + _u(3 * i + 3) - 1.5) * 2.0
		pop[i] = 1000.0 * exp(0.6 * g)
	for r in range(1, 301):
		pop[(r * 997 + 13) % N] = 300000.0 * pow(r, -0.6)
	return pop

# Median-heat node: an ordinary rural spot, the "found a city here" site.
func _barren(pop: PackedFloat32Array) -> int:
	var pairs := []
	for i in N:
		pairs.append([pop[i], i])
	pairs.sort_custom(func(a, b): return a[0] < b[0] or (a[0] == b[0] and a[1] < b[1]))
	return pairs[N / 2][1]

func _engine(keyframe_years: Array, keyframe_pops: Array, owners: PackedInt32Array) -> PopulationEngine:
	var e := PopulationEngine.new()
	e.set_keyframes(W, H, PackedInt32Array(keyframe_years), keyframe_pops)
	e.set_ownership(owners, PLAYER)
	e.start(keyframe_years[0])
	return e

func _owners(player_nodes: Array) -> PackedInt32Array:
	var owners := PackedInt32Array()
	owners.resize(N)
	owners.fill(BOT)
	for i in player_nodes:
		owners[i] = PLAYER
	return owners

# Years until node reaches world top 50 and top 10 (0 if never within `years`).
func _climb(e: PopulationEngine, node: int, years: int) -> Vector2i:
	var r50 := 0
	var r10 := 0
	for t in range(1, years + 1):
		e.tick(START + t)
		var rank := e.rank_of(node)
		if r50 == 0 and rank <= 50:
			r50 = t
		if r10 == 0 and rank <= 10:
			r10 = t
			break
	return Vector2i(r50, r10)

func _near(v: int, expected: int, tol: int = 2) -> bool:
	return absi(v - expected) <= tol

func _init() -> void:
	var hist := _fixture()
	var barren := _barren(hist)
	var h_barren := sqrt(hist[barren] / 300000.0)

	# 1. The historical start is a fixed point: nothing drifts, the world
	#    total holds, and nobody exceeds the ceiling.
	var e := _engine([START], [hist], _owners([]))
	var total0 := e.total_population()
	for t in range(1, 51):
		e.tick(START + t)
	var worst := 0.0
	for i in N:
		worst = maxf(worst, absf(e.pop[i] - hist[i]) / hist[i])
	assert(worst < 1e-3, "historical start drifted by %f" % worst)
	assert(absf(e.total_population() - total0) / total0 < 1e-4)

	# 2. Climb: an ordinary player city with best-in-world drivers.
	e = _engine([START], [hist], _owners([barren]))
	e.driver_mods[barren] = 1.0 - h_barren
	var ordinary := _climb(e, barren, 400)
	assert(_near(ordinary.x, 59) and ordinary.y == 0, "ordinary city climb %s, expected (59, never)" % ordinary)

	# 3. Climb: country and province capitals, settlers drawn from a
	#    small player realm (nodes 0-399, 19 cities).
	var realm := range(0, 400)
	realm.append(barren)
	var caps := {}
	for kind in [PopulationEngine.CapitalKind.COUNTRY, PopulationEngine.CapitalKind.PROVINCE]:
		e = _engine([START], [hist], _owners(realm))
		e.driver_mods[barren] = 1.0 - h_barren
		var before := e.total_population()
		e.designate_capital(barren, kind, PLAYER)
		assert(absf(e.total_population() - before) < 1.0, "resettlement must move people, not create them")
		caps[kind] = _climb(e, barren, 400)
	var country: Vector2i = caps[PopulationEngine.CapitalKind.COUNTRY]
	var province: Vector2i = caps[PopulationEngine.CapitalKind.PROVINCE]
	assert(_near(country.x, 6) and country.y == 0, "country capital climb %s, expected (6, never)" % country)
	assert(_near(province.x, 28) and province.y == 0, "province capital climb %s, expected (28, never)" % province)

	# 4. Resettlement happens only the first time: re-designating after
	#    losing capital status moves nobody.
	e = _engine([START], [hist], _owners(realm))
	e.designate_capital(barren, PopulationEngine.CapitalKind.COUNTRY, PLAYER)
	var after_first := e.pop[barren]
	assert(after_first > hist[barren] * 5.0)
	e.clear_capital(barren)
	e.designate_capital(barren, PopulationEngine.CapitalKind.COUNTRY, PLAYER)
	assert(e.pop[barren] == after_first, "second designation must not resettle again")

	# 5. Bots can't exceed history, however hard they push.
	e = _engine([START], [hist], _owners([]))
	e.driver_mods[barren] = 5.0
	e.designate_capital(barren, PopulationEngine.CapitalKind.COUNTRY, BOT)
	for t in range(1, 201):
		e.tick(START + t)
		assert(e.pop[barren] <= e.hist_pop[barren] + 0.01, "bot node exceeded its historical ceiling")

	# 6. When history declines, bot-held places decline with it: the
	#    largest city falls to a tenth of its size over a century.
	var big := (1 * 997 + 13) % N
	var later := hist.duplicate()
	later[big] = hist[big] * 0.1
	e = _engine([START, START + 100], [hist, later], _owners([]))
	for t in range(1, 101):
		e.tick(START + t)
		assert(e.pop[big] <= e.hist_pop[big] + 0.01, "bot city lagged above a declining history")
	assert(e.pop[big] <= hist[big] * 0.1 + 1.0)

	# 7. The player can exceed history; after the player loses the city,
	#    it fades back gradually (no snap), about 60 years per halving of
	#    the log excess.
	e = _engine([START], [hist], _owners([barren]))
	e.driver_mods[barren] = 1.0 - h_barren
	for t in range(1, 301):
		e.tick(START + t)
	var peak := e.pop[barren]
	assert(peak > hist[barren] * 5.0, "player city failed to exceed history")
	e.set_ownership(_owners([]), PLAYER)
	e.tick(START + 301)
	# A ~170x-above-history city sheds ~6% in its first year (a percentage
	# rate); a snap would drop it straight to history (~0.6% of peak).
	assert(e.pop[barren] >= peak * 0.9,
		"lost player city snapped down instead of fading: %f of peak" % (e.pop[barren] / peak))
	for t in range(302, 362):
		e.tick(START + t)
	var log_excess_60 := log(e.pop[barren] / hist[barren])
	var log_excess_0 := log(peak / hist[barren])
	assert(log_excess_60 < log_excess_0 * 0.6 and log_excess_60 > log_excess_0 * 0.4,
		"legacy fade: log excess %f -> %f after 60 years" % [log_excess_0, log_excess_60])

	# 8. A razed node stays empty for a generation, then resettles at its
	#    historical population.
	e = _engine([START], [hist], _owners([]))
	e.pop[barren] = 0.0
	e.empty_since[barren] = START
	for t in range(1, 25):
		e.tick(START + t)
		assert(e.pop[barren] == 0.0, "razed node resettled early")
	e.tick(START + 25)
	assert(e.pop[barren] >= 1.0, "razed node never resettled")
	assert(e.pop[barren] <= hist[barren] + 0.01)

	# 9. Save/load round-trip continues identically.
	e = _engine([START], [hist], _owners(realm))
	e.driver_mods[barren] = 1.0 - h_barren
	e.designate_capital(barren, PopulationEngine.CapitalKind.PROVINCE, PLAYER)
	for t in range(1, 11):
		e.tick(START + t)
	var saved := var_to_bytes(e.to_dict())
	var e2 := PopulationEngine.new()
	e2.set_keyframes(W, H, PackedInt32Array([START]), [hist])
	e2.set_ownership(_owners(realm), PLAYER)
	assert(e2.load_from_dict(bytes_to_var(saved)))
	e.tick(START + 11)
	e2.tick(START + 11)
	for i in N:
		assert(e.pop[i] == e2.pop[i], "save/load diverged at node %d" % i)

	print("PopulationEngine tests passed: fixed-point start, ordinary %s, country capital %s, " % [ordinary, country],
		"province capital %s (years to top 50 / top 10), ceilings, history decline, legacy fade, " % province,
		"resettlement, save/load.")
	quit()
