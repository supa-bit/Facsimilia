extends Node2D

const OwnershipGrid := preload("res://scripts/world/ownership_grid.gd")
const CharacterRegistry := preload("res://scripts/dynasty/character_registry.gd")
const Realm := preload("res://scripts/dynasty/realm.gd")

const GRID_WIDTH := 320
const GRID_HEIGHT := 240
const CELL_PIXELS := 4  # on-screen scale per grid cell
const PAINT_RADIUS := 3
const WILD_COLOR := Color(0.12, 0.12, 0.12, 1.0)  # unclaimed land - not a realm, no owner
const SEA_COLOR := Color(0.08, 0.16, 0.24, 1.0)  # water - never claimable, never painted
const START_YEAR := -300  # 300 BC

# A single hand-authored polygon standing in for the Mediterranean Sea,
# separating "Europe" (north) from "Africa" (south), with a notch pulled
# down to give Italy a recognizable boot-shaped peninsula. This is a
# stylized approximation, not traced real coastline data - but it's what
# actually makes the map read as a map instead of a field of blobs.
const SEA_POLYGON: PackedVector2Array = [
	Vector2(0, 150), Vector2(60, 155), Vector2(120, 148), Vector2(150, 150),
	Vector2(168, 168), Vector2(185, 150), Vector2(200, 145), Vector2(220, 155),
	Vector2(250, 160), Vector2(290, 165), Vector2(320, 170),
	Vector2(320, 200), Vector2(280, 195), Vector2(240, 195), Vector2(200, 195),
	Vector2(160, 190), Vector2(110, 195), Vector2(60, 195), Vector2(0, 190),
]

var grid: OwnershipGrid
var registry: CharacterRegistry
var player_realm_id: int
var demo_year := START_YEAR
var sea_mask: PackedByteArray  # 1 = water, 0 = land

# civ_key ("rome", "carthage", ...) -> realm.id, so the civ-select screen
# can point the player at the right realm without the grid/registry layer
# needing to know anything about civ-select UI.
var civ_realm_ids: Dictionary = {}

# Per-cell proposing realm id (0 = no active proposal). Preview layer only -
# never mutates grid's real ownership, just how a cell is displayed, so an
# offer can be seen before it's ever committed.
var proposal: PackedInt32Array
var map_sprite: Sprite2D
var camera: Camera2D
var painting := false

func _ready() -> void:
	grid = OwnershipGrid.new(GRID_WIDTH, GRID_HEIGHT)
	registry = CharacterRegistry.new()
	_build_sea_mask()
	_seed_ancient_world()
	_clip_to_land()

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

	camera = Camera2D.new()
	camera.position = Vector2(GRID_WIDTH * CELL_PIXELS / 2.0, GRID_HEIGHT * CELL_PIXELS / 2.0)
	add_child(camera)
	camera.make_current()
	get_viewport().size_changed.connect(_fit_camera_to_window)
	_fit_camera_to_window()

	print("Facsimilia map view ready: ", GRID_WIDTH, "x", GRID_HEIGHT, " cells, year ", demo_year, ". ",
		"Left-drag to propose annexing/settling land, right-click to clear the proposal.")

# The map (320x240 cells * 4px = 1280x960) is bigger than most default
# windows, so without this only the top-left corner was ever visible -
# that's what made the previous screenshot look like the coastline was
# broken, when really most of the map was just off-screen. Zooms out
# exactly enough that the whole map fits inside whatever window size
# exists right now, and re-fits automatically if the window is resized.
func _fit_camera_to_window() -> void:
	var viewport_size := get_viewport_rect().size
	if viewport_size.x <= 0 or viewport_size.y <= 0:
		return
	var map_size := Vector2(GRID_WIDTH * CELL_PIXELS, GRID_HEIGHT * CELL_PIXELS)
	var zoom_factor: float = max(map_size.x / viewport_size.x, map_size.y / viewport_size.y)
	camera.zoom = Vector2(zoom_factor, zoom_factor)

func _build_sea_mask() -> void:
	sea_mask = PackedByteArray()
	sea_mask.resize(GRID_WIDTH * GRID_HEIGHT)
	for y in GRID_HEIGHT:
		for x in GRID_WIDTH:
			var point := Vector2(x + 0.5, y + 0.5)
			if Geometry2D.is_point_in_polygon(point, SEA_POLYGON):
				sea_mask[y * GRID_WIDTH + x] = 1

func is_sea(x: int, y: int) -> bool:
	return sea_mask[y * GRID_WIDTH + x] == 1

# Blobs are seeded ignoring terrain (organic circular jitter, same as
# before), then this pass erases ownership anywhere that landed in the
# sea - so each realm's final shape is its blob clipped to the coastline,
# not a perfect circle.
func _clip_to_land() -> void:
	for y in GRID_HEIGHT:
		for x in GRID_WIDTH:
			if is_sea(x, y) and grid.get_owner(x, y) != 0:
				grid.set_owner(x, y, 0)

func _realm(realm_id: int) -> Realm:
	return registry.realms[realm_id]

func get_player_realm() -> Realm:
	return _realm(player_realm_id)

func set_player_civ(civ_key: String) -> void:
	if civ_realm_ids.has(civ_key):
		player_realm_id = civ_realm_ids[civ_key]

# A regional slice of the Mediterranean/western Eurasia at 300 BC, not the
# whole world - positions are stylized, not traced coastlines, but placed
# at roughly the right relative geography per the reference map the user
# confirmed as accurate for this period. Twelve broad regional powers
# (grouping many smaller historical factions together, not one blob per
# named tribe) rather than the whole map's worth of labels. Rome is
# deliberately the smallest - a minor Italian power in 300 BC, not yet the
# Mediterranean superpower it became; that gap is the point of the game.
func _seed_ancient_world() -> void:
	var specs := [
		{"key": "rome", "realm": "Rome", "ruler": "Numerius", "color": Color(0.75, 0.20, 0.20, 1.0),
			"cx": 170, "cy": 120, "radius": 13, "law": Realm.SuccessionLaw.MALE_PREFERENCE_PRIMOGENITURE},
		{"key": "carthage", "realm": "Carthage", "ruler": "Hasdrubal", "color": Color(0.55, 0.30, 0.65, 1.0),
			"cx": 140, "cy": 180, "radius": 26, "law": Realm.SuccessionLaw.MALE_PREFERENCE_PRIMOGENITURE},
		{"key": "numidia", "realm": "Numidian & Berber Peoples", "ruler": "Gaia", "color": Color(0.75, 0.45, 0.20, 1.0),
			"cx": 75, "cy": 195, "radius": 30, "law": Realm.SuccessionLaw.MALE_PREFERENCE_PRIMOGENITURE},
		{"key": "egypt", "realm": "Ptolemaic Egypt", "ruler": "Ptolemy", "color": Color(0.85, 0.75, 0.15, 1.0),
			"cx": 225, "cy": 180, "radius": 20, "law": Realm.SuccessionLaw.PRIMOGENITURE},
		{"key": "kush", "realm": "Kingdom of Kush", "ruler": "Arkamani", "color": Color(0.55, 0.25, 0.15, 1.0),
			"cx": 245, "cy": 220, "radius": 18, "law": Realm.SuccessionLaw.PRIMOGENITURE},
		{"key": "iberia", "realm": "Iberian & Celtiberian Tribes", "ruler": "Indibilis", "color": Color(0.20, 0.55, 0.55, 1.0),
			"cx": 35, "cy": 145, "radius": 26, "law": Realm.SuccessionLaw.PRIMOGENITURE},
		{"key": "gaul", "realm": "Gallic Tribes", "ruler": "Brennos", "color": Color(0.25, 0.65, 0.30, 1.0),
			"cx": 110, "cy": 55, "radius": 32, "law": Realm.SuccessionLaw.PRIMOGENITURE},
		{"key": "greek_world", "realm": "Greek World", "ruler": "Alcetas", "color": Color(0.20, 0.40, 0.75, 1.0),
			"cx": 235, "cy": 140, "radius": 20, "law": Realm.SuccessionLaw.PRIMOGENITURE},
		{"key": "dacia", "realm": "Dacian & Getae Tribes", "ruler": "Oroles", "color": Color(0.45, 0.30, 0.15, 1.0),
			"cx": 225, "cy": 55, "radius": 22, "law": Realm.SuccessionLaw.MALE_PREFERENCE_PRIMOGENITURE},
		{"key": "thrace_anatolia", "realm": "Thrace & Anatolia", "ruler": "Lysimachos", "color": Color(0.75, 0.35, 0.55, 1.0),
			"cx": 280, "cy": 115, "radius": 24, "law": Realm.SuccessionLaw.PRIMOGENITURE},
		{"key": "scythia", "realm": "Scythian Peoples", "ruler": "Ateas", "color": Color(0.35, 0.65, 0.75, 1.0),
			"cx": 270, "cy": 45, "radius": 28, "law": Realm.SuccessionLaw.MALE_PREFERENCE_PRIMOGENITURE},
		{"key": "seleucid", "realm": "Seleucid Empire", "ruler": "Seleukos", "color": Color(0.35, 0.25, 0.65, 1.0),
			"cx": 295, "cy": 165, "radius": 16, "law": Realm.SuccessionLaw.MALE_PREFERENCE_PRIMOGENITURE},
	]
	for i in specs.size():
		var spec: Dictionary = specs[i]
		var ruler := registry.create_character(spec.ruler, "male", START_YEAR - 45)
		registry.create_dynasty("House " + spec.ruler, ruler)
		var spouse := registry.create_character(spec.ruler + "'s spouse", "female", START_YEAR - 43)
		registry.marry(ruler, spouse)
		registry.have_child(spouse, ruler, spec.ruler + "'s heir", "male", START_YEAR - 22)
		var realm := registry.create_realm(spec.realm, ruler, spec.law, spec.color)
		civ_realm_ids[spec.key] = realm.id
		grid.fill_blob(spec.cx, spec.cy, spec.radius, realm.id, i + 1)
	player_realm_id = civ_realm_ids.get("rome", civ_realm_ids.values()[0])

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

# Contested land (already owned by another realm) shows as a true blend of
# both realms' colors - it reads as "disputed between X and Y," not a
# generic highlight. Unclaimed land has nothing to blend with, so a
# proposal there just paints it solid - that's settlement, not negotiation.
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

# Takes a WORLD-space position (get_global_mouse_position(), not raw event
# screen coordinates) - with the camera now zoomed out to fit the whole
# map, those two stop being the same thing, and using screen coordinates
# directly would misalign painting from what's actually under the cursor.
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
