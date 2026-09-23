extends Node2D

const OwnershipGrid := preload("res://scripts/world/ownership_grid.gd")
const CharacterRegistry := preload("res://scripts/dynasty/character_registry.gd")
const Realm := preload("res://scripts/dynasty/realm.gd")

const GRID_WIDTH := 480
const GRID_HEIGHT := 270
const CELL_PIXELS := 4  # 480*4 x 270*4 = 1920x1080, matches the window 1:1 - crisp, no scaling
const PAINT_RADIUS := 3
const WILD_COLOR := Color(0.12, 0.12, 0.12, 1.0)  # unclaimed land - not a realm, no owner
const SEA_COLOR := Color(0.08, 0.16, 0.24, 1.0)
const START_YEAR := -300  # 300 BC
const DATA_PATH := "res://data/ancient_bc300.json"

# Real 300 BC political boundaries (Roman Republic, Carthaginian Empire,
# Ptolemaic Kingdom, Meroe, Seleucid Kingdom, Kingdom of Kassander + Greek
# city-states, Kingdom of Lysimachus, Kingdom of Antigonus, Nabatean
# Kingdom), sourced from aourednik/historical-basemaps world_bc300.geojson
# and pre-projected into this grid's coordinate space by
# tools/import_bc300.js. See that script for the exact source mapping and
# projection parameters.
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
# genuinely were not unified states in 300 BC (fragmented tribal
# confederations, historically accurate to represent as frontier rather
# than force into a false single-kingdom shape). Each is a rough
# hand-placed polygon that only claims cells still unclaimed after the
# real civs are placed - see _fill_frontier().
var FRONTIER_ZONES := [
	{"key": "iberia", "realm": "Iberian & Celtiberian Tribes", "ruler": "Indibilis", "color": Color(0.20, 0.55, 0.55, 1.0),
		"law": Realm.SuccessionLaw.PRIMOGENITURE,
		"polygon": PackedVector2Array([Vector2(0,15), Vector2(90,10), Vector2(95,55), Vector2(70,90), Vector2(20,85), Vector2(0,60)])},
	{"key": "gaul", "realm": "Gallic Tribes", "ruler": "Brennos", "color": Color(0.25, 0.65, 0.30, 1.0),
		"law": Realm.SuccessionLaw.PRIMOGENITURE,
		"polygon": PackedVector2Array([Vector2(55,0), Vector2(215,0), Vector2(225,50), Vector2(150,58), Vector2(85,52), Vector2(55,35)])},
	{"key": "scythia", "realm": "Scythian Peoples", "ruler": "Ateas", "color": Color(0.35, 0.65, 0.75, 1.0),
		"law": Realm.SuccessionLaw.MALE_PREFERENCE_PRIMOGENITURE,
		"polygon": PackedVector2Array([Vector2(290,0), Vector2(470,0), Vector2(475,40), Vector2(400,55), Vector2(320,45), Vector2(290,25)])},
]

# Broad, deliberately loose "this is open water" zone, threaded through
# the gaps between the real civs' actual positions now that those are
# known from real data. Only fills cells still unclaimed after both the
# real civs AND the frontier zones are placed, so it can never overwrite
# actual territory - it just fills in the genuine gaps as sea instead of
# generic unclaimed land.
const SEA_ZONE: PackedVector2Array = [
	Vector2(40,60), Vector2(100,55), Vector2(150,52), Vector2(190,52), Vector2(230,65),
	Vector2(260,60), Vector2(300,55), Vector2(360,90), Vector2(400,110), Vector2(420,140),
	Vector2(370,130), Vector2(330,118), Vector2(300,105), Vector2(260,100), Vector2(220,95),
	Vector2(180,85), Vector2(140,75), Vector2(100,70), Vector2(50,68),
]

var grid: OwnershipGrid
var registry: CharacterRegistry
var player_realm_id: int
var demo_year := START_YEAR
var sea_mask: PackedByteArray  # 1 = water, 0 = land
var civ_realm_ids: Dictionary = {}  # civ_key -> realm.id

var proposal: PackedInt32Array
var map_sprite: Sprite2D
var camera: Camera2D
var label_container: Node2D
var painting := false

func _ready() -> void:
	grid = OwnershipGrid.new(GRID_WIDTH, GRID_HEIGHT)
	registry = CharacterRegistry.new()
	sea_mask = PackedByteArray()
	sea_mask.resize(GRID_WIDTH * GRID_HEIGHT)

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

	_refresh_map_texture()
	_build_labels()

	camera = Camera2D.new()
	camera.position = Vector2(GRID_WIDTH * CELL_PIXELS / 2.0, GRID_HEIGHT * CELL_PIXELS / 2.0)
	add_child(camera)
	camera.make_current()
	get_viewport().size_changed.connect(_fit_camera_to_window)
	_fit_camera_to_window()

	print("Facsimilia map view ready: ", GRID_WIDTH, "x", GRID_HEIGHT, " cells, year ", demo_year, ". ",
		"Left-drag to propose annexing/settling land, right-click to clear the proposal.")

func _fit_camera_to_window() -> void:
	var viewport_size := get_viewport_rect().size
	if viewport_size.x <= 0 or viewport_size.y <= 0:
		return
	var map_size := Vector2(GRID_WIDTH * CELL_PIXELS, GRID_HEIGHT * CELL_PIXELS)
	var zoom_factor: float = max(map_size.x / viewport_size.x, map_size.y / viewport_size.y)
	camera.zoom = Vector2(zoom_factor, zoom_factor)

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
			_fill_polygon(polygon, realm.id)

	player_realm_id = civ_realm_ids.get("rome", civ_realm_ids.values()[0])

func _seed_frontier_zones() -> void:
	for spec in FRONTIER_ZONES:
		var realm := _make_ruler_and_realm(spec.realm, spec.ruler, spec.color, spec.law)
		civ_realm_ids[spec.key] = realm.id
		_fill_frontier(spec.polygon, realm.id)

func _seed_sea() -> void:
	var min_x := INF
	var max_x := -INF
	var min_y := INF
	var max_y := -INF
	for p in SEA_ZONE:
		min_x = min(min_x, p.x)
		max_x = max(max_x, p.x)
		min_y = min(min_y, p.y)
		max_y = max(max_y, p.y)
	var x0 := clampi(int(min_x), 0, GRID_WIDTH - 1)
	var x1 := clampi(int(max_x), 0, GRID_WIDTH - 1)
	var y0 := clampi(int(min_y), 0, GRID_HEIGHT - 1)
	var y1 := clampi(int(max_y), 0, GRID_HEIGHT - 1)
	for y in range(y0, y1 + 1):
		for x in range(x0, x1 + 1):
			if grid.get_owner(x, y) != 0:
				continue
			if Geometry2D.is_point_in_polygon(Vector2(x + 0.5, y + 0.5), SEA_ZONE):
				sea_mask[y * GRID_WIDTH + x] = 1

func is_sea(x: int, y: int) -> bool:
	return sea_mask[y * GRID_WIDTH + x] == 1

# Unconditional within the polygon - used only for the real, accurate
# civ data, which should always win any overlap.
func _fill_polygon(polygon: PackedVector2Array, owner_id: int) -> void:
	if polygon.size() < 3:
		return
	var min_x := INF
	var max_x := -INF
	var min_y := INF
	var max_y := -INF
	for p in polygon:
		min_x = min(min_x, p.x)
		max_x = max(max_x, p.x)
		min_y = min(min_y, p.y)
		max_y = max(max_y, p.y)
	var x0 := clampi(int(min_x), 0, GRID_WIDTH - 1)
	var x1 := clampi(int(max_x), 0, GRID_WIDTH - 1)
	var y0 := clampi(int(min_y), 0, GRID_HEIGHT - 1)
	var y1 := clampi(int(max_y), 0, GRID_HEIGHT - 1)
	for y in range(y0, y1 + 1):
		for x in range(x0, x1 + 1):
			if Geometry2D.is_point_in_polygon(Vector2(x + 0.5, y + 0.5), polygon):
				grid.set_owner(x, y, owner_id)

# Only claims cells still unclaimed - used for the frontier tribal zones,
# which must never overwrite a real civ's actual territory.
func _fill_frontier(polygon: PackedVector2Array, owner_id: int) -> void:
	var min_x := INF
	var max_x := -INF
	var min_y := INF
	var max_y := -INF
	for p in polygon:
		min_x = min(min_x, p.x)
		max_x = max(max_x, p.x)
		min_y = min(min_y, p.y)
		max_y = max(max_y, p.y)
	var x0 := clampi(int(min_x), 0, GRID_WIDTH - 1)
	var x1 := clampi(int(max_x), 0, GRID_WIDTH - 1)
	var y0 := clampi(int(min_y), 0, GRID_HEIGHT - 1)
	var y1 := clampi(int(max_y), 0, GRID_HEIGHT - 1)
	for y in range(y0, y1 + 1):
		for x in range(x0, x1 + 1):
			if grid.get_owner(x, y) != 0:
				continue
			if Geometry2D.is_point_in_polygon(Vector2(x + 0.5, y + 0.5), polygon):
				grid.set_owner(x, y, owner_id)

func _build_labels() -> void:
	label_container = Node2D.new()
	add_child(label_container)
	for realm_id in registry.realms.keys():
		var realm: Realm = registry.realms[realm_id]
		var centroid = _centroid_of_owner(realm_id)
		if centroid == null:
			continue
		var label := Label.new()
		label.text = realm.name
		label.add_theme_color_override("font_color", Color.WHITE)
		label.add_theme_color_override("font_outline_color", Color.BLACK)
		label.add_theme_constant_override("outline_size", 3)
		label.position = centroid * CELL_PIXELS - Vector2(label.size.x / 2.0, 0)
		label_container.add_child(label)

func _centroid_of_owner(owner_id: int):
	var sum := Vector2.ZERO
	var count := 0
	for y in GRID_HEIGHT:
		for x in GRID_WIDTH:
			if grid.get_owner(x, y) == owner_id:
				sum += Vector2(x, y)
				count += 1
	if count == 0:
		return null
	return sum / count

func advance_year() -> Array:
	demo_year += 1
	var events := registry.advance_year(demo_year)
	_refresh_map_texture()
	return events

func _color_for_owner(owner_id: int) -> Color:
	if owner_id == 0:
		return WILD_COLOR
	if registry.realms.has(owner_id):
		return _realm(owner_id).color
	return Color.MAGENTA

func _display_color(x: int, y: int) -> Color:
	if is_sea(x, y):
		return SEA_COLOR
	var proposer := proposal[y * GRID_WIDTH + x]
	var current_owner := grid.get_owner(x, y)
	if proposer == 0:
		return _color_for_owner(current_owner)
	if current_owner == 0:
		return _color_for_owner(proposer)
	return _color_for_owner(current_owner).lerp(_color_for_owner(proposer), 0.5)

func _refresh_map_texture() -> void:
	var img := Image.create(GRID_WIDTH, GRID_HEIGHT, false, Image.FORMAT_RGBA8)
	for y in GRID_HEIGHT:
		for x in GRID_WIDTH:
			img.set_pixel(x, y, _display_color(x, y))
	var tex := ImageTexture.create_from_image(img)
	map_sprite.texture = tex

func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventMouseButton:
		if event.button_index == MOUSE_BUTTON_LEFT:
			painting = event.pressed
			if painting:
				_paint_at(get_global_mouse_position())
		elif event.button_index == MOUSE_BUTTON_RIGHT and event.pressed:
			proposal.fill(0)
			_refresh_map_texture()
	elif event is InputEventMouseMotion and painting:
		_paint_at(get_global_mouse_position())

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
			if Vector2(dx, dy).length() <= PAINT_RADIUS:
				proposal[y * GRID_WIDTH + x] = player_realm_id
	_refresh_map_texture()
