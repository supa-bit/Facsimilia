extends Node2D

signal loading_status_changed(text: String)

const OwnershipGrid := preload("res://scripts/world/ownership_grid.gd")
const CharacterRegistry := preload("res://scripts/dynasty/character_registry.gd")
const Realm := preload("res://scripts/dynasty/realm.gd")
const SaveSystem := preload("res://scripts/world/save_system.gd")
const PopulationEngine := preload("res://scripts/world/population_engine.gd")

const GRID_WIDTH := 8192
const GRID_HEIGHT := 5476  # ~0.59 km^2/cell over the map's real-world extent
const CELL_PIXELS := 1  # grid IS the texture resolution; camera handles all zoom/scale
const WILD_COLOR := Color(0.12, 0.12, 0.12, 1.0)  # unclaimed land - not a realm, no owner
const SEA_COLOR := Color(0.08, 0.16, 0.24, 1.0)
const SEA_OWNER_ID := 255  # reserved sentinel in the SAME ownership grid - see is_sea()
const PAINT_RADIUS := 12
const START_YEAR := -300  # 300 BC
const DATA_PATH := "res://data/ancient_bc300.json"
const LAND_MASK_PATH := "res://data/land_mask.png"
const POLITICAL_MASK_PATH := "res://data/political_mask.png"
const TERRAIN_TEXTURE_PATH := "res://data/terrain_texture.png"
const MAX_ZOOM := 16.0
const LON_MIN := -10.0  # map extent, shared with tools/import_bc300.js and the HYDE import
const LON_MAX := 55.0
const LAT_MIN := 10.0
const LAT_MAX := 48.0

# Real 300 BC political boundaries (Roman Republic, Carthaginian Empire,
# Ptolemaic Kingdom, Meroe, Seleucid Kingdom, Kingdom of Kassander + Greek
# city-states, Kingdom of Lysimachus, Kingdom of Antigonus, Nabatean
# Kingdom), sourced from aourednik/historical-basemaps world_bc300.geojson
# and pre-projected into this grid's coordinate space by
# tools/import_bc300.js. See that script for the exact source mapping,
# projection parameters, and simplification tolerance.
# "capital_lonlat" is each realm's seat in 300 BC (checked to fall inside
# its own territory in the political mask). Population engine only: the
# capital's growth bonus already counts as part of recorded history.
const REAL_CIVS := [
	{"key": "rome", "ruler": "Numerius", "color": Color(0.75, 0.20, 0.20, 1.0), "law": Realm.SuccessionLaw.MALE_PREFERENCE_PRIMOGENITURE, "capital": "Rome", "capital_lonlat": Vector2(12.48, 41.89)},
	{"key": "carthage", "ruler": "Hasdrubal", "color": Color(0.55, 0.30, 0.65, 1.0), "law": Realm.SuccessionLaw.MALE_PREFERENCE_PRIMOGENITURE, "capital": "Carthage", "capital_lonlat": Vector2(10.32, 36.85)},
	{"key": "egypt", "ruler": "Ptolemy", "color": Color(0.85, 0.75, 0.15, 1.0), "law": Realm.SuccessionLaw.PRIMOGENITURE, "capital": "Alexandria", "capital_lonlat": Vector2(29.92, 31.2)},
	{"key": "kush", "ruler": "Arkamani", "color": Color(0.55, 0.25, 0.15, 1.0), "law": Realm.SuccessionLaw.PRIMOGENITURE, "capital": "Meroe", "capital_lonlat": Vector2(33.75, 16.94)},
	{"key": "seleucid", "ruler": "Seleukos", "color": Color(0.35, 0.25, 0.65, 1.0), "law": Realm.SuccessionLaw.MALE_PREFERENCE_PRIMOGENITURE, "capital": "Seleucia-on-Tigris", "capital_lonlat": Vector2(44.52, 33.1)},
	{"key": "greek_world", "ruler": "Kassandros", "color": Color(0.20, 0.40, 0.75, 1.0), "law": Realm.SuccessionLaw.PRIMOGENITURE, "capital": "Pella", "capital_lonlat": Vector2(22.52, 40.76)},
	{"key": "lysimachus", "ruler": "Lysimachos", "color": Color(0.75, 0.35, 0.55, 1.0), "law": Realm.SuccessionLaw.PRIMOGENITURE, "capital": "Lysimachia", "capital_lonlat": Vector2(26.75, 40.52)},
	{"key": "antigonus", "ruler": "Antigonos", "color": Color(0.80, 0.45, 0.15, 1.0), "law": Realm.SuccessionLaw.MALE_PREFERENCE_PRIMOGENITURE, "capital": "Antigonia", "capital_lonlat": Vector2(36.2, 36.23)},
	{"key": "nabatea", "ruler": "Aretas", "color": Color(0.70, 0.55, 0.30, 1.0), "law": Realm.SuccessionLaw.PRIMOGENITURE, "capital": "Petra", "capital_lonlat": Vector2(35.44, 30.33)},
]

# Tribal/cultural frontier zones - deliberately NOT sourced from real
# political-boundary data, because Gaul, Iberia, and the Pontic steppe
# genuinely were not unified states in 300 BC. Coordinates are the old
# 480x270-grid hand placements rescaled to this grid's 8192x5476 (see
# tools/import_bc300.js's LON/LAT bounds - this is the same linear scale
# that projection implies, applied directly since these were never
# lon/lat-sourced to begin with). Each fills only cells still unclaimed
# after the real civs are placed - see _fill_polygon(..., true).
var FRONTIER_ZONES := [
	{"key": "iberia", "realm": "Iberian & Celtiberian Tribes", "ruler": "Indibilis", "color": Color(0.20, 0.55, 0.55, 1.0),
		"law": Realm.SuccessionLaw.PRIMOGENITURE,
		"polygon": PackedVector2Array([Vector2(0,304), Vector2(1536,203), Vector2(1621,1115), Vector2(1195,1825), Vector2(341,1724), Vector2(0,1217)])},
	{"key": "gaul", "realm": "Gallic Tribes", "ruler": "Brennos", "color": Color(0.25, 0.65, 0.30, 1.0),
		"law": Realm.SuccessionLaw.PRIMOGENITURE,
		"polygon": PackedVector2Array([Vector2(939,0), Vector2(3669,0), Vector2(3840,1014), Vector2(2560,1176), Vector2(1451,1055), Vector2(939,710)])},
	{"key": "scythia", "realm": "Scythian Peoples", "ruler": "Ateas", "color": Color(0.35, 0.65, 0.75, 1.0),
		"law": Realm.SuccessionLaw.MALE_PREFERENCE_PRIMOGENITURE,
		"polygon": PackedVector2Array([Vector2(4949,0), Vector2(8021,0), Vector2(8107,811), Vector2(6827,1115), Vector2(5461,913), Vector2(4949,507)])},
]

var grid: OwnershipGrid
var registry: CharacterRegistry
var player_realm_id: int
var demo_year := START_YEAR
var civ_realm_ids: Dictionary = {}  # civ_key -> realm.id
var pending_player_civ := ""
# Null when the baked HYDE keyframes (data/population/, built by
# tools/build_population_mask.py) aren't present - the game then runs
# without population, exactly as before.
var population: PopulationEngine

var proposal: PackedInt32Array
var dirty_proposal_cells: Dictionary = {}  # (y*GRID_WIDTH+x) -> true, cells currently painted
var map_image: Image
var map_texture: ImageTexture
var map_sprite: Sprite2D
var camera: Camera2D
var fit_zoom: float = 1.0  # "whole map fits the window" - the zoomed-out limit (MINIMUM zoom value)
var label_container: Node2D
var painting := false
var panning := false

func _ready() -> void:
	# Only the fast, instant setup lives in _ready() - grid/registry
	# creation and node setup are microseconds. The expensive world
	# generation (seeding, building the display image, computing label
	# positions - tens of millions of cells) is generate_world(), a
	# separate coroutine game_root calls and awaits AFTER add_child(this),
	# broken into yielded chunks so it never blocks the engine's main
	# loop (rendering/input/OS message pump) for more than a fraction of
	# a second at a stretch.
	grid = OwnershipGrid.new(GRID_WIDTH, GRID_HEIGHT)
	registry = CharacterRegistry.new()

	proposal = PackedInt32Array()
	proposal.resize(GRID_WIDTH * GRID_HEIGHT)

	map_sprite = Sprite2D.new()
	map_sprite.centered = false
	map_sprite.scale = Vector2(CELL_PIXELS, CELL_PIXELS)
	map_sprite.texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
	add_child(map_sprite)

	var mat := ShaderMaterial.new()
	mat.shader = load("res://shaders/border_outline.gdshader")
	mat.set_shader_parameter("texel_size", Vector2(1.0 / GRID_WIDTH, 1.0 / GRID_HEIGHT))
	mat.set_shader_parameter("terrain_texture", load(TERRAIN_TEXTURE_PATH))
	map_sprite.material = mat

	camera = Camera2D.new()
	camera.position = Vector2(GRID_WIDTH * CELL_PIXELS / 2.0, GRID_HEIGHT * CELL_PIXELS / 2.0)
	add_child(camera)
	camera.make_current()
	get_viewport().size_changed.connect(_fit_camera_to_window)
	_fit_camera_to_window()

func generate_world() -> void:
	loading_status_changed.emit("Charting the coastline...")
	await _seed_land_and_sea()
	loading_status_changed.emit("Mapping the ancient world...")
	await _seed_real_civs()
	loading_status_changed.emit("Settling the frontier tribes...")
	await _seed_frontier_zones()
	if civ_realm_ids.has(pending_player_civ):
		player_realm_id = civ_realm_ids[pending_player_civ]
	loading_status_changed.emit("Counting the people...")
	_start_population()
	loading_status_changed.emit("Drawing the map...")
	await _build_full_map_image()
	loading_status_changed.emit("Naming the realms...")
	var centroids: Dictionary = await _compute_centroids()
	_build_labels(centroids)

	print("Facsimilia map view ready: ", GRID_WIDTH, "x", GRID_HEIGHT, " cells, year ", demo_year, ". ",
		"Left-drag to propose annexing/settling land, right-click to clear the proposal, ",
		"scroll to zoom, middle-drag to pan.")

func save_current_game() -> bool:
	return await SaveSystem.save_game(grid, registry, demo_year, player_realm_id, self,
		population.to_dict() if population else {})

# Restores a previously-saved world instead of generating a new one: skips
# the seeding steps (_seed_land_and_sea/_seed_real_civs/_seed_frontier_zones)
# entirely and re-runs only the rendering tail generate_world() also ends
# with, against the loaded grid/registry. Returns false (leaving this
# MapView unmodified) if there's no save or it couldn't be read - callers
# should fall back to generate_world() in that case.
func load_saved_game() -> bool:
	var loaded := await SaveSystem.load_game(self)
	if loaded.is_empty():
		return false
	var loaded_grid: OwnershipGrid = loaded.grid
	if loaded_grid.width != GRID_WIDTH or loaded_grid.height != GRID_HEIGHT:
		# Rendering (proposal array size, shader texel_size, sprite scale)
		# is all set up for this build's GRID_WIDTH/GRID_HEIGHT in _ready() -
		# a save from a different grid size can't be reconciled with that
		# without re-running _ready()'s setup, so refuse it rather than
		# silently misindexing cells.
		push_error("SaveSystem: saved grid is %dx%d, this build expects %dx%d" %
			[loaded_grid.width, loaded_grid.height, GRID_WIDTH, GRID_HEIGHT])
		return false

	grid = loaded.grid
	registry = loaded.registry
	demo_year = loaded.demo_year
	player_realm_id = loaded.player_realm_id
	population = null
	var saved_population: Dictionary = loaded.get("population", {})
	if not saved_population.is_empty():
		var engine := PopulationEngine.new()
		if engine.load_hyde() and engine.load_from_dict(saved_population):
			population = engine
			_sync_population_ownership()
	elif PopulationEngine.hyde_available():
		# Save from before the population engine existed: start it fresh
		# from history at the saved year.
		_start_population(demo_year)

	loading_status_changed.emit("Drawing the map...")
	await _build_full_map_image()
	loading_status_changed.emit("Naming the realms...")
	var centroids: Dictionary = await _compute_centroids()
	_build_labels(centroids)
	return true

func _fit_camera_to_window() -> void:
	var viewport_size := get_viewport_rect().size
	if viewport_size.x <= 0 or viewport_size.y <= 0:
		return
	# Camera2D.zoom is screen pixels shown per map pixel - the visible
	# world area is viewport_size / zoom, NOT viewport_size * zoom. This
	# was inverted before (map_size / viewport_size), which computed a
	# zoom of ~4-5 instead of ~0.2 and made the camera show a tiny,
	# heavily-magnified fragment of the map instead of fitting the whole
	# thing - the earlier "letterboxing" fix (switching max() to min())
	# was correct in spirit but was tuning the wrong-direction ratio, so
	# it couldn't have actually fixed the framing on its own.
	# min(): fills the whole window (cropping whichever axis overflows)
	# instead of letterboxing empty space around the map to preserve its
	# aspect ratio. The map is much bigger than one screen regardless,
	# so a small crop at the default view is the right tradeoff versus
	# dead gray space on the sides. fit_zoom doubles as the MINIMUM zoom
	# (can't zoom out further than "whole map visible") - see _zoom_by().
	var map_size := Vector2(GRID_WIDTH * CELL_PIXELS, GRID_HEIGHT * CELL_PIXELS)
	fit_zoom = min(viewport_size.x / map_size.x, viewport_size.y / map_size.y)
	camera.zoom = Vector2(fit_zoom, fit_zoom)

func _realm(realm_id: int) -> Realm:
	return registry.realms[realm_id]

func get_player_realm() -> Realm:
	return _realm(player_realm_id)

# game_root calls this before generate_world(), when no realms exist yet,
# so the choice is remembered and applied once seeding has created them.
func set_player_civ(civ_key: String) -> void:
	pending_player_civ = civ_key
	if civ_realm_ids.has(civ_key):
		player_realm_id = civ_realm_ids[civ_key]

func _make_ruler_and_realm(realm_name: String, ruler_name: String, color: Color, law: int) -> Realm:
	var ruler := registry.create_character(ruler_name, "male", START_YEAR - 45)
	registry.create_dynasty("House " + ruler_name, ruler)
	var spouse := registry.create_character(ruler_name + "'s spouse", "female", START_YEAR - 43)
	registry.marry(ruler, spouse)
	registry.have_child(spouse, ruler, ruler_name + "'s heir", "male", START_YEAR - 22)
	return registry.create_realm(realm_name, ruler, law, color)

func _seed_real_civs() -> void:
	var file := FileAccess.open(DATA_PATH, FileAccess.READ)
	var parsed = JSON.parse_string(file.get_as_text())
	var regions_by_key := {}
	for region in parsed.regions:
		regions_by_key[region.key] = region

	# Region codes 1..9 in the baked political mask follow
	# ancient_bc300.json's region order (see tools/reconcile_map.py) -
	# REAL_CIVS must stay in that same order for realm_ids[code] to
	# resolve correctly. Still read ancient_bc300.json for the display
	# names; the polygon geometry itself is no longer used here.
	var realm_ids := PackedInt32Array([0])  # code 0 = unclaimed
	for spec in REAL_CIVS:
		var region: Dictionary = regions_by_key[spec.key]
		var realm := _make_ruler_and_realm(region.name, spec.ruler, spec.color, spec.law)
		civ_realm_ids[spec.key] = realm.id
		realm_ids.append(realm.id)

	# The mask is a pre-baked, offline-reconciled raster (tools/
	# reconcile_map.py): the real political polygons clipped to the real
	# coastline, warped as ONE continuous coordinate field so neighboring
	# regions share borders instead of independently wiggling into gaps
	# or overlaps, plus bounded coastal-sliver repair (never crosses
	# water, never overwrites an existing claim, never auto-assigns an
	# unclaimed island - see data/map_reconciliation.json for exactly
	# what it changed). This replaces per-polygon scanline filling for
	# the real civs entirely - just a raster code lookup now.
	var mask_texture := load(POLITICAL_MASK_PATH) as Texture2D
	assert(mask_texture != null, "Political mask texture could not be loaded")
	var mask := mask_texture.get_image()
	assert(mask != null and mask.get_width() == GRID_WIDTH and mask.get_height() == GRID_HEIGHT,
		"Political mask missing or incompatible with the territory projection")
	mask.convert(Image.FORMAT_L8)
	var pixels := mask.get_data()
	var cells := grid.cells
	var total := pixels.size()
	var chunk_size := 1000000
	var i := 0
	while i < total:
		var end: int = mini(i + chunk_size, total)
		for j in range(i, end):
			var code: int = pixels[j]
			# Sea stays a hard constraint even here, in case the mask
			# ever disagreed with the land mask at the very margin.
			if code > 0 and cells[j] != SEA_OWNER_ID:
				cells[j] = realm_ids[code]
		i = end
		await _maybe_yield()
	grid.cells = cells

	player_realm_id = civ_realm_ids.get("rome", civ_realm_ids.values()[0])

func _seed_frontier_zones() -> void:
	for spec in FRONTIER_ZONES:
		var realm := _make_ruler_and_realm(spec.realm, spec.ruler, spec.color, spec.law)
		civ_realm_ids[spec.key] = realm.id
		await _fill_polygon(_soften_frontier(spec.polygon), realm.id, true)

# Frontier zones are explicitly approximate cultural/tribal sketches,
# not sourced boundary data (unlike the real civs, which now come from
# the reconciled political mask above) - this densifies each straight
# hand-placed edge and displaces every point with a fixed sinusoidal
# coordinate field, purely to avoid a dead-straight ruler-drawn look.
# Deterministic (position-based, not random), so it's reproducible.
func _soften_frontier(polygon: PackedVector2Array) -> PackedVector2Array:
	var result := PackedVector2Array()
	for i in polygon.size():
		var a: Vector2 = polygon[i]
		var b: Vector2 = polygon[(i + 1) % polygon.size()]
		var steps := maxi(1, ceili(a.distance_to(b) / 20.0))
		for step in steps:
			var p := a.lerp(b, float(step) / steps)
			var dx := 8.0 * sin(p.y / 61.0 + p.x / 147.0) + 3.0 * sin(p.y / 19.0 - p.x / 47.0)
			var dy := 8.0 * sin(p.x / 73.0 - p.y / 163.0) + 3.0 * sin(p.x / 23.0 + p.y / 53.0)
			result.append(p + Vector2(dx, dy))
	return result

# Physical geography is independent of political borders. The mask is a
# real rasterized coastline (Natural Earth's public domain 1:10m land
# dataset, see tools/build_land_mask.py and MAP_DATA.md) in the SAME
# lon/lat projection as the political importer: white = land, black =
# sea. This replaces the earlier hand-drawn approximate sea polygon.
# Establishing this FIRST, before any political territory is painted,
# is what lets _fill_polygon (below) treat sea as a hard constraint
# even real/"trusted" civ polygons can't override - a political
# boundary that's imprecise right at the coast (e.g. Carthage's known
# rough western edge) still can't paint over what the real coastline
# says is ocean.
func _seed_land_and_sea() -> void:
	var mask_texture := load(LAND_MASK_PATH) as Texture2D
	assert(mask_texture != null, "Land mask texture could not be loaded")
	var mask := mask_texture.get_image()
	assert(mask != null and mask.get_width() == GRID_WIDTH and mask.get_height() == GRID_HEIGHT,
		"Land mask missing or incompatible with the territory projection")
	mask.convert(Image.FORMAT_L8)
	var pixels := mask.get_data()
	var cells := grid.cells
	cells.fill(SEA_OWNER_ID)
	var total := pixels.size()
	var chunk_size := 1000000
	var i := 0
	while i < total:
		var end: int = mini(i + chunk_size, total)
		for j in range(i, end):
			if pixels[j] != 0:
				cells[j] = 0
		i = end
		await _maybe_yield()
	grid.cells = cells

# Yields to the engine's main loop if (and only if) this node is
# actually inside a running SceneTree. Headless test scripts construct
# a MapViewScript instance without ever add_child-ing it anywhere, so
# get_tree() is null there - in that case this is a no-op and every
# await site below falls through synchronously, keeping those tests
# fast and simple. In the real game (added to the tree by game_root
# before generate_world() runs), this is what keeps rendering/input/the
# OS message pump alive during the ~15-25s of world generation instead
# of freezing the whole window.
func _maybe_yield() -> void:
	if is_inside_tree():
		await get_tree().process_frame

func is_sea(x: int, y: int) -> bool:
	return grid.get_owner(x, y) == SEA_OWNER_ID

# Scanline polygon fill (even-odd rule): for each row, computes the x
# positions where the polygon's edges cross that row ONCE (O(vertices)),
# then fills the spans between pairs of crossings directly (O(1) per
# cell). This is what makes seeding a multi-million-cell grid from a
# few-hundred-vertex polygon fast - the old approach (Geometry2D.
# is_point_in_polygon per cell) was O(bbox area * vertices), which is
# fine at a few hundred thousand cells but becomes minutes-to-hours slow
# at tens of millions. only_if_unclaimed=true is used for frontier zones
# and sea, which must never overwrite already-claimed real territory.
func _fill_polygon(polygon: PackedVector2Array, owner_id: int, only_if_unclaimed: bool) -> void:
	if polygon.size() < 3:
		return
	var min_y := INF
	var max_y := -INF
	for p in polygon:
		min_y = min(min_y, p.y)
		max_y = max(max_y, p.y)
	var y0 := clampi(int(min_y), 0, GRID_HEIGHT - 1)
	var y1 := clampi(int(max_y), 0, GRID_HEIGHT - 1)
	var n := polygon.size()
	var rows_since_yield := 0
	for y in range(y0, y1 + 1):
		var scan_y := y + 0.5
		var xs: Array = []
		for i in n:
			var a: Vector2 = polygon[i]
			var b: Vector2 = polygon[(i + 1) % n]
			if a.y == b.y:
				continue
			var edge_y_min: float = min(a.y, b.y)
			var edge_y_max: float = max(a.y, b.y)
			if scan_y < edge_y_min or scan_y >= edge_y_max:
				continue
			var t: float = (scan_y - a.y) / (b.y - a.y)
			xs.append(a.x + t * (b.x - a.x))
		xs.sort()
		var i := 0
		while i + 1 < xs.size():
			var x_start := clampi(int(ceil(xs[i] - 0.5)), 0, GRID_WIDTH - 1)
			var x_end := clampi(int(ceil(xs[i + 1] - 0.5)) - 1, 0, GRID_WIDTH - 1)
			for x in range(x_start, x_end + 1):
				var existing := grid.get_owner(x, y)
				# Sea is a hard constraint from the real coastline mask -
				# not even a "trusted" real-civ polygon (only_if_unclaimed
				# = false) may paint over it, since coastal accuracy from
				# real geography outranks a historical political
				# boundary's precision right at the water's edge.
				if existing == SEA_OWNER_ID or (only_if_unclaimed and existing != 0):
					continue
				grid.set_owner(x, y, owner_id)
			i += 2
		rows_since_yield += 1
		if rows_since_yield >= 40:
			rows_since_yield = 0
			await _maybe_yield()

func _build_labels(centroids: Dictionary) -> void:
	label_container = Node2D.new()
	add_child(label_container)
	for realm_id in registry.realms.keys():
		if not centroids.has(realm_id):
			continue
		var realm: Realm = registry.realms[realm_id]
		var centroid: Vector2 = _label_anchor(realm_id, centroids[realm_id])
		var label := Label.new()
		label.text = realm.name
		label.add_theme_color_override("font_color", Color.WHITE)
		label.add_theme_color_override("font_outline_color", Color.BLACK)
		label.add_theme_constant_override("outline_size", 3)
		label.position = centroid * CELL_PIXELS - label.get_minimum_size() / 2.0
		label_container.add_child(label)

# A raw centroid can land at sea, on another realm's territory, or in a
# gap between a realm's disconnected parts (e.g. Carthage's islands).
# Scans a coarse grid for the point closest to the centroid that's
# actually confirmed on this realm's own territory (checked with a
# small margin on each side, not just the single sample point).
func _label_anchor(realm_id: int, centroid: Vector2) -> Vector2:
	var best := centroid
	var best_distance := INF
	for y in range(8, GRID_HEIGHT - 8, 16):
		for x in range(8, GRID_WIDTH - 8, 16):
			if grid.get_owner(x, y) != realm_id:
				continue
			var point := Vector2(x, y)
			var distance := point.distance_squared_to(centroid)
			if distance >= best_distance:
				continue
			if grid.get_owner(x - 8, y) != realm_id or grid.get_owner(x + 8, y) != realm_id:
				continue
			if grid.get_owner(x, y - 8) != realm_id or grid.get_owner(x, y + 8) != realm_id:
				continue
			best = point
			best_distance = distance
	return best

# Single pass over the whole grid (raw array, not per-cell get_owner()
# calls) computing every realm's centroid at once, instead of one
# full-grid pass per realm. Chunked with yields - see _maybe_yield().
func _compute_centroids() -> Dictionary:
	var sums := {}
	var counts := {}
	var cells := grid.cells
	var total := cells.size()
	var chunk_size := 1000000
	var i := 0
	while i < total:
		var end: int = mini(i + chunk_size, total)
		for j in range(i, end):
			var o: int = cells[j]
			if o <= 0 or o == SEA_OWNER_ID:
				continue
			var x := j % GRID_WIDTH
			var y := j / GRID_WIDTH
			sums[o] = sums.get(o, Vector2.ZERO) + Vector2(x, y)
			counts[o] = counts.get(o, 0) + 1
		i = end
		await _maybe_yield()
	var centroids := {}
	for o in sums.keys():
		centroids[o] = sums[o] / counts[o]
	return centroids

# Succession changes only realm.ruler_id, never territory ownership on
# the grid, so no rendering update is needed here.
func advance_year() -> Array:
	demo_year += 1
	if population:
		_sync_population_ownership()
		population.tick(demo_year)
	return registry.advance_year(demo_year)

# Starts the population engine from HYDE's historical population at
# start_year, with every real civ's 300 BC capital registered as an
# existing capital. No-op if the HYDE keyframes aren't baked.
func _start_population(start_year: int = START_YEAR) -> void:
	if not PopulationEngine.hyde_available():
		print("Population engine off: no HYDE keyframes in data/population/ ",
			"(run tools/build_population_mask.py - see MAP_DATA.md).")
		return
	var engine := PopulationEngine.new()
	if not engine.load_hyde():
		return
	population = engine
	_sync_population_ownership()
	var capitals := []
	for spec in REAL_CIVS:
		if civ_realm_ids.has(spec.key) and spec.has("capital_lonlat"):
			var lonlat: Vector2 = spec.capital_lonlat
			capitals.append({
				"node": population.node_at_lonlat(lonlat.x, lonlat.y, LON_MIN, LON_MAX, LAT_MIN, LAT_MAX),
				"kind": PopulationEngine.CapitalKind.COUNTRY,
				"realm": civ_realm_ids[spec.key],
			})
	population.start(start_year, capitals)

func _sync_population_ownership() -> void:
	population.set_ownership(
		population.ownership_from_grid(grid.cells, GRID_WIDTH, GRID_HEIGHT, SEA_OWNER_ID),
		player_realm_id)

func player_population() -> float:
	return population.realm_population(player_realm_id) if population else 0.0

func _color_for_owner(owner_id: int) -> Color:
	if owner_id == 0:
		return WILD_COLOR
	if owner_id == SEA_OWNER_ID:
		return SEA_COLOR
	if registry.realms.has(owner_id):
		return _realm(owner_id).color
	return Color.MAGENTA

# Contested land (already owned by another realm) shows as a true blend
# of both realms' colors. Unclaimed land has nothing to blend with, so a
# proposal there just paints it solid.
func _display_color(x: int, y: int) -> Color:
	var current_owner := grid.get_owner(x, y)
	if current_owner == SEA_OWNER_ID:
		return SEA_COLOR
	var proposer := proposal[y * GRID_WIDTH + x]
	if proposer == 0:
		return _color_for_owner(current_owner)
	if current_owner == 0:
		return _color_for_owner(proposer)
	return _color_for_owner(current_owner).lerp(_color_for_owner(proposer), 0.5)

# Builds the whole-map image from a raw RGBA byte buffer instead of
# millions of individual Image.set_pixel() calls - set_pixel has real
# per-call overhead (bounds checks, an engine call each time) that adds
# up fast at tens of millions of cells. A palette lookup per cell plus a
# single create_from_data() bulk upload is dramatically faster. Chunked
# with yields - see _maybe_yield().
func _build_full_map_image() -> void:
	var palette := {}
	for realm_id in registry.realms.keys():
		palette[realm_id] = _color_for_owner(realm_id)
	var wild8 := _color_bytes(WILD_COLOR)
	var sea8 := _color_bytes(SEA_COLOR)
	var palette_bytes := {}
	for realm_id in palette.keys():
		palette_bytes[realm_id] = _color_bytes(palette[realm_id])

	var bytes := PackedByteArray()
	bytes.resize(GRID_WIDTH * GRID_HEIGHT * 4)
	var cells := grid.cells
	var total := cells.size()
	var chunk_size := 500000
	var i := 0
	while i < total:
		var end: int = mini(i + chunk_size, total)
		for j in range(i, end):
			var owner_id: int = cells[j]
			var rgba: PackedByteArray
			if owner_id == 0:
				rgba = wild8
			elif owner_id == SEA_OWNER_ID:
				rgba = sea8
			else:
				rgba = palette_bytes.get(owner_id, wild8)
			var base := j * 4
			bytes[base] = rgba[0]
			bytes[base + 1] = rgba[1]
			bytes[base + 2] = rgba[2]
			bytes[base + 3] = rgba[3]
		i = end
		await _maybe_yield()

	map_image = Image.create_from_data(GRID_WIDTH, GRID_HEIGHT, false, Image.FORMAT_RGBA8, bytes)
	map_texture = ImageTexture.create_from_image(map_image)
	map_sprite.texture = map_texture

func _color_bytes(c: Color) -> PackedByteArray:
	return PackedByteArray([roundi(c.r * 255), roundi(c.g * 255), roundi(c.b * 255), roundi(c.a * 255)])

func _repaint_cell(x: int, y: int) -> void:
	map_image.set_pixel(x, y, _display_color(x, y))

func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventMouseButton:
		if event.button_index == MOUSE_BUTTON_LEFT:
			painting = event.pressed
			if painting:
				_paint_at(get_global_mouse_position())
		elif event.button_index == MOUSE_BUTTON_RIGHT and event.pressed:
			_clear_proposal()
		elif event.button_index == MOUSE_BUTTON_MIDDLE:
			panning = event.pressed
		elif event.button_index == MOUSE_BUTTON_WHEEL_UP and event.pressed:
			_zoom_by(1.1)
		elif event.button_index == MOUSE_BUTTON_WHEEL_DOWN and event.pressed:
			_zoom_by(0.9)
	elif event is InputEventMouseMotion:
		if painting:
			_paint_at(get_global_mouse_position())
		elif panning:
			# world_delta = screen_delta / zoom: at higher zoom (more
			# zoomed in), the same screen-pixel drag should move the
			# camera a SMALLER distance through world space. This was
			# `* camera.zoom` before, which is backwards for the same
			# reason the zoom formula above was inverted.
			camera.position -= event.relative / camera.zoom

func _zoom_by(factor: float) -> void:
	# fit_zoom is the "whole map visible" state = the most you can zoom
	# OUT (lower bound); MAX_ZOOM is the most you can zoom IN (upper
	# bound). This was backwards before (MIN_ZOOM as the lower bound let
	# you zoom out to an absurd 0.02, while fit_zoom as the upper bound
	# meant you couldn't zoom in at all past the initial fitted view).
	var new_zoom: float = clampf(camera.zoom.x * factor, fit_zoom, MAX_ZOOM)
	camera.zoom = Vector2(new_zoom, new_zoom)

# Only touches the brush-sized area actually painted (never the whole
# grid) - CPU cost per stroke is bounded by brush size, not map size, so
# this stays responsive regardless of total cell count. The one
# unavoidable per-stroke cost is uploading the full image to the GPU via
# texture.update(), which is a fast bulk copy (not per-pixel), not a
# per-cell operation.
func _paint_at(world_pos: Vector2) -> void:
	var cell := Vector2i(world_pos / CELL_PIXELS)
	if not grid.in_bounds(cell.x, cell.y):
		return
	for dy in range(-PAINT_RADIUS, PAINT_RADIUS + 1):
		for dx in range(-PAINT_RADIUS, PAINT_RADIUS + 1):
			var x := cell.x + dx
			var y := cell.y + dy
			if not grid.in_bounds(x, y) or is_sea(x, y):
				continue
			if Vector2(dx, dy).length() > PAINT_RADIUS:
				continue
			var idx := y * GRID_WIDTH + x
			if proposal[idx] != player_realm_id:
				proposal[idx] = player_realm_id
				dirty_proposal_cells[idx] = true
				_repaint_cell(x, y)
	map_texture.update(map_image)

func _clear_proposal() -> void:
	if dirty_proposal_cells.is_empty():
		return
	for idx in dirty_proposal_cells.keys():
		var x: int = idx % GRID_WIDTH
		var y: int = idx / GRID_WIDTH
		proposal[idx] = 0
		_repaint_cell(x, y)
	dirty_proposal_cells.clear()
	map_texture.update(map_image)
