extends Node2D

const OwnershipGrid := preload("res://scripts/world/ownership_grid.gd")
const CharacterRegistry := preload("res://scripts/dynasty/character_registry.gd")
const Realm := preload("res://scripts/dynasty/realm.gd")

const GRID_WIDTH := 8192
const GRID_HEIGHT := 5476  # ~0.59 km^2/cell over the map's real-world extent
const CELL_PIXELS := 1  # grid IS the texture resolution; camera handles all zoom/scale
const WILD_COLOR := Color(0.12, 0.12, 0.12, 1.0)  # unclaimed land - not a realm, no owner
const SEA_COLOR := Color(0.08, 0.16, 0.24, 1.0)
const SEA_OWNER_ID := 255  # reserved sentinel in the SAME ownership grid - see is_sea()
const PAINT_RADIUS := 12
const START_YEAR := -300  # 300 BC
const DATA_PATH := "res://data/ancient_bc300.json"
const MIN_ZOOM := 0.02

# Real 300 BC political boundaries (Roman Republic, Carthaginian Empire,
# Ptolemaic Kingdom, Meroe, Seleucid Kingdom, Kingdom of Kassander + Greek
# city-states, Kingdom of Lysimachus, Kingdom of Antigonus, Nabatean
# Kingdom), sourced from aourednik/historical-basemaps world_bc300.geojson
# and pre-projected into this grid's coordinate space by
# tools/import_bc300.js. See that script for the exact source mapping,
# projection parameters, and simplification tolerance.
const REAL_CIVS := [
	{"key": "rome", "ruler": "Numerius", "color": Color(0.75, 0.20, 0.20, 1.0), "law": Realm.SuccessionLaw.MALE_PREFERENCE_PRIMOGENITURE},
	{"key": "carthage", "ruler": "Hasdrubal", "color": Color(0.55, 0.30, 0.65, 1.0), "law": Realm.SuccessionLaw.MALE_PREFERENCE_PRIMOGENITURE},
	{"key": "egypt", "ruler": "Ptolemy", "color": Color(0.85, 0.75, 0.15, 1.0), "law": Realm.SuccessionLaw.PRIMOGENITURE},
	{"key": "kush", "ruler": "Arkamani", "color": Color(0.55, 0.25, 0.15, 1.0), "law": Realm.SuccessionLaw.PRIMOGENITURE},
	{"key": "seleucid", "ruler": "Seleukos", "color": Color(0.35, 0.25, 0.65, 1.0), "law": Realm.SuccessionLaw.MALE_PREFERENCE_PRIMOGENITURE},
	{"key": "greek_world", "ruler": "Kassandros", "color": Color(0.20, 0.40, 0.75, 1.0), "law": Realm.SuccessionLaw.PRIMOGENITURE},
	{"key": "lysimachus", "ruler": "Lysimachos", "color": Color(0.75, 0.35, 0.55, 1.0), "law": Realm.SuccessionLaw.PRIMOGENITURE},
	{"key": "antigonus", "ruler": "Antigonos", "color": Color(0.80, 0.45, 0.15, 1.0), "law": Realm.SuccessionLaw.MALE_PREFERENCE_PRIMOGENITURE},
	{"key": "nabatea", "ruler": "Aretas", "color": Color(0.70, 0.55, 0.30, 1.0), "law": Realm.SuccessionLaw.PRIMOGENITURE},
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

# Broad, deliberately loose "this is open water" zone threaded through the
# gaps between the real civs' actual positions. Only fills cells still
# unclaimed after both the real civs AND the frontier zones are placed, so
# it can never overwrite actual territory.
var SEA_ZONE: PackedVector2Array = [
	Vector2(683,1217), Vector2(1707,1115), Vector2(2560,1055), Vector2(3243,1055), Vector2(3925,1318),
	Vector2(4437,1217), Vector2(5120,1115), Vector2(6144,1825), Vector2(6827,2231), Vector2(7168,2839),
	Vector2(6315,2637), Vector2(5632,2393), Vector2(5120,2130), Vector2(4437,2028), Vector2(3755,1927),
	Vector2(3072,1724), Vector2(2389,1521), Vector2(1707,1420), Vector2(853,1379),
]

var grid: OwnershipGrid
var registry: CharacterRegistry
var player_realm_id: int
var demo_year := START_YEAR
var civ_realm_ids: Dictionary = {}  # civ_key -> realm.id

var proposal: PackedInt32Array
var dirty_proposal_cells: Dictionary = {}  # (y*GRID_WIDTH+x) -> true, cells currently painted
var map_image: Image
var map_texture: ImageTexture
var map_sprite: Sprite2D
var camera: Camera2D
var max_zoom: float = 1.0  # "whole map fits the window" - the zoomed-out limit
var label_container: Node2D
var painting := false
var panning := false

func _ready() -> void:
	grid = OwnershipGrid.new(GRID_WIDTH, GRID_HEIGHT)
	registry = CharacterRegistry.new()

	_seed_real_civs()
	_seed_frontier_zones()
	_seed_sea()

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
	map_sprite.material = mat

	_build_full_map_image()
	_build_labels()

	camera = Camera2D.new()
	camera.position = Vector2(GRID_WIDTH * CELL_PIXELS / 2.0, GRID_HEIGHT * CELL_PIXELS / 2.0)
	add_child(camera)
	camera.make_current()
	get_viewport().size_changed.connect(_fit_camera_to_window)
	_fit_camera_to_window()

	print("Facsimilia map view ready: ", GRID_WIDTH, "x", GRID_HEIGHT, " cells, year ", demo_year, ". ",
		"Left-drag to propose annexing/settling land, right-click to clear the proposal, ",
		"scroll to zoom, middle-drag to pan.")

func _fit_camera_to_window() -> void:
	var viewport_size := get_viewport_rect().size
	if viewport_size.x <= 0 or viewport_size.y <= 0:
		return
	var map_size := Vector2(GRID_WIDTH * CELL_PIXELS, GRID_HEIGHT * CELL_PIXELS)
	max_zoom = max(map_size.x / viewport_size.x, map_size.y / viewport_size.y)
	camera.zoom = Vector2(max_zoom, max_zoom)

func _realm(realm_id: int) -> Realm:
	return registry.realms[realm_id]

func get_player_realm() -> Realm:
	return _realm(player_realm_id)

func set_player_civ(civ_key: String) -> void:
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

	for spec in REAL_CIVS:
		var region: Dictionary = regions_by_key[spec.key]
		var realm := _make_ruler_and_realm(region.name, spec.ruler, spec.color, spec.law)
		civ_realm_ids[spec.key] = realm.id
		for poly_points in region.polygons:
			var polygon := PackedVector2Array()
			for point in poly_points:
				polygon.append(Vector2(point[0], point[1]))
			_fill_polygon(polygon, realm.id, false)

	player_realm_id = civ_realm_ids.get("rome", civ_realm_ids.values()[0])

func _seed_frontier_zones() -> void:
	for spec in FRONTIER_ZONES:
		var realm := _make_ruler_and_realm(spec.realm, spec.ruler, spec.color, spec.law)
		civ_realm_ids[spec.key] = realm.id
		_fill_polygon(spec.polygon, realm.id, true)

func _seed_sea() -> void:
	_fill_polygon(SEA_ZONE, SEA_OWNER_ID, true)

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
				if only_if_unclaimed and grid.get_owner(x, y) != 0:
					continue
				grid.set_owner(x, y, owner_id)
			i += 2

func _build_labels() -> void:
	label_container = Node2D.new()
	add_child(label_container)
	var centroids := _compute_centroids()
	for realm_id in registry.realms.keys():
		if not centroids.has(realm_id):
			continue
		var realm: Realm = registry.realms[realm_id]
		var centroid: Vector2 = centroids[realm_id]
		var label := Label.new()
		label.text = realm.name
		label.add_theme_color_override("font_color", Color.WHITE)
		label.add_theme_color_override("font_outline_color", Color.BLACK)
		label.add_theme_constant_override("outline_size", 3)
		label.position = centroid * CELL_PIXELS - Vector2(label.size.x / 2.0, 0)
		label_container.add_child(label)

# Single pass over the whole grid (raw array, not per-cell get_owner()
# calls) computing every realm's centroid at once, instead of one
# full-grid pass per realm.
func _compute_centroids() -> Dictionary:
	var sums := {}
	var counts := {}
	var cells := grid.cells
	for i in cells.size():
		var o: int = cells[i]
		if o <= 0 or o == SEA_OWNER_ID:
			continue
		var x := i % GRID_WIDTH
		var y := i / GRID_WIDTH
		sums[o] = sums.get(o, Vector2.ZERO) + Vector2(x, y)
		counts[o] = counts.get(o, 0) + 1
	var centroids := {}
	for o in sums.keys():
		centroids[o] = sums[o] / counts[o]
	return centroids

# Succession changes only realm.ruler_id, never territory ownership on
# the grid, so no rendering update is needed here.
func advance_year() -> Array:
	demo_year += 1
	return registry.advance_year(demo_year)

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
# single create_from_data() bulk upload is dramatically faster.
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
	for i in cells.size():
		var owner_id: int = cells[i]
		var rgba: PackedByteArray
		if owner_id == 0:
			rgba = wild8
		elif owner_id == SEA_OWNER_ID:
			rgba = sea8
		else:
			rgba = palette_bytes.get(owner_id, wild8)
		var base := i * 4
		bytes[base] = rgba[0]
		bytes[base + 1] = rgba[1]
		bytes[base + 2] = rgba[2]
		bytes[base + 3] = rgba[3]

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
			_zoom_by(0.9)
		elif event.button_index == MOUSE_BUTTON_WHEEL_DOWN and event.pressed:
			_zoom_by(1.1)
	elif event is InputEventMouseMotion:
		if painting:
			_paint_at(get_global_mouse_position())
		elif panning:
			camera.position -= event.relative * camera.zoom

func _zoom_by(factor: float) -> void:
	var new_zoom: float = clampf(camera.zoom.x * factor, MIN_ZOOM, max_zoom)
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
