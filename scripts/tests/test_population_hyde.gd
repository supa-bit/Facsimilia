extends SceneTree

# Population engine calibration on the real map: the baked HYDE 300 BC
# keyframe (data/population/), history held there, as in
# `python3 tools/calibrate_population.py --hyde`. The test site is the
# median populated land node, given best-in-world drivers. Expected years
# to the map's top 50 / top 10 are that script's numbers at the engine's
# constants (102/168 ordinary, 33/78 country capital, 62/128 province
# capital), against the historical 100/166, 24/78 and 62/122.
# Slow: ~0.5 s per tick on 284,626 nodes, a few minutes in all.

const PopulationEngine := preload("res://scripts/world/population_engine.gd")

const PLAYER := 1
const BOT := 2
const START := -300
const DONORS := 2000   # player-held land nodes the capital's settlers come from

var failures := 0

func _check(ok: bool, msg: String) -> void:
	if not ok:
		printerr("FAIL: ", msg)
		failures += 1

func _near(v: int, expected: int, tol: int = 3) -> bool:
	return absi(v - expected) <= tol

# Years until node reaches the top 50 and top 10 (0 if never within `years`).
func _climb(e: PopulationEngine, node: int, years: int) -> Vector2i:
	var r50 := 0
	for t in range(1, years + 1):
		e.tick(START + t)
		var rank := e.rank_of(node)
		if r50 == 0 and rank <= 50:
			r50 = t
		if rank <= 10:
			return Vector2i(r50, t)
	return Vector2i(r50, 0)

func _init() -> void:
	if not PopulationEngine.hyde_available():
		printerr("FAIL: no HYDE keyframes in data/population/")
		quit(1)
		return
	var base := PopulationEngine.new()
	base.load_hyde()
	var hist: PackedFloat32Array = base.keyframes[0].pop
	_check(base.keyframes[0].year == START, "first keyframe is %d, not 300 BC" % base.keyframes[0].year)

	# The median populated land node, as the calibration script picks it.
	var pairs := []
	for i in base.land_nodes:
		if hist[i] >= 1.0:
			pairs.append([hist[i], i])
	pairs.sort_custom(func(a, b): return a[0] < b[0] or (a[0] == b[0] and a[1] < b[1]))
	var site: int = pairs[pairs.size() / 2][1]
	var max_hist := 0.0
	for i in base.land_nodes:
		max_hist = maxf(max_hist, hist[i])
	var h_site := sqrt(hist[site] / max_hist)

	var realm := PackedInt32Array([site])
	var k := base.land_nodes.find(site)
	for j in range(1, DONORS + 1):
		realm.append(base.land_nodes[(k + j) % base.land_nodes.size()])

	var results := {}
	for kind in ["ordinary", "country", "province"]:
		var e := PopulationEngine.new()
		e.set_keyframes(base.width, base.height, PackedInt32Array([START]), [hist])
		var owners := PackedInt32Array()
		owners.resize(base.width * base.height)
		owners.fill(BOT)
		for i in (realm if kind != "ordinary" else PackedInt32Array([site])):
			owners[i] = PLAYER
		e.set_ownership(owners, PLAYER)
		e.start(START)
		e.driver_mods[site] = 1.0 - h_site
		if kind == "country":
			e.designate_capital(site, PopulationEngine.CapitalKind.COUNTRY, PLAYER)
		elif kind == "province":
			e.designate_capital(site, PopulationEngine.CapitalKind.PROVINCE, PLAYER)
		results[kind] = _climb(e, site, 250)

	_check(_near(results.ordinary.x, 102) and _near(results.ordinary.y, 168),
		"ordinary city climb %s, expected (102, 168)" % results.ordinary)
	_check(_near(results.country.x, 33) and _near(results.country.y, 78),
		"country capital climb %s, expected (33, 78)" % results.country)
	_check(_near(results.province.x, 62) and _near(results.province.y, 128),
		"province capital climb %s, expected (62, 128)" % results.province)

	if failures == 0:
		print("HYDE calibration test passed (years to top 50 / top 10 on the real 300 BC grid): ",
			"ordinary %s, country capital %s, province capital %s." % [results.ordinary, results.country, results.province])
	quit(1 if failures else 0)
