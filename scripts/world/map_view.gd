extends Node2D

const OwnershipGrid := preload("res://scripts/world/ownership_grid.gd")
const CharacterRegistry := preload("res://scripts/dynasty/character_registry.gd")
const Realm := preload("res://scripts/dynasty/realm.gd")

const GRID_WIDTH := 200
const GRID_HEIGHT := 150
const CELL_PIXELS := 4  # on-screen scale per grid cell
const PAINT_RADIUS := 3
const WILD_COLOR := Color(0.12, 0.12, 0.12, 1.0)  # unclaimed land - not a realm, no owner
const START_YEAR := -300  # 300 BC

var grid: OwnershipGrid
var registry: CharacterRegistry
var player_realm_id: int
var demo_year := START_YEAR

# civ_key ("rome", "carthage", ...) -> realm.id, so the civ-select screen
# can point the player at the right realm without the grid/registry layer
# needing to know anything about civ-select UI.
var civ_realm_ids: Dictionary = {}

# Per-cell proposing realm id (0 = no active proposal). Preview layer only -
# never mutates grid's real ownership, just how a cell is displayed, so an
# offer can be seen before it's ever committed.
var proposal: PackedInt32Array
var map_sprite: Sprite2D
var painting := false

func _ready() -> void:
	grid = OwnershipGrid.new(GRID_WIDTH, GRID_HEIGHT)
	registry = CharacterRegistry.new()
	_seed_ancient_world()

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

	print("Facsimilia map view ready: ", GRID_WIDTH, "x", GRID_HEIGHT, " cells, year ", demo_year, ". ",
		"Left-drag to propose annexing/settling land, right-click to clear the proposal.")

func _realm(realm_id: int) -> Realm:
	return registry.realms[realm_id]

func get_player_realm() -> Realm:
	return _realm(player_realm_id)

func set_player_civ(civ_key: String) -> void:
	if civ_realm_ids.has(civ_key):
		player_realm_id = civ_realm_ids[civ_key]

# A small regional slice of the Mediterranean/western Europe at 300 BC,
# not the whole world - positions are stylized, not traced coastlines, but
# placed at roughly the right relative geography: Rome small and central
# in Italy, Carthage larger to the southwest across the sea, the Greek
# world (Epirus) to the east, Gallic tribes sprawling to the north. Rome
# is deliberately the smallest here - it was a minor regional power in
# 300 BC, not yet the Mediterranean superpower it became.
func _seed_ancient_world() -> void:
	var specs := [
		{"key": "rome", "realm": "Rome", "ruler": "Numerius", "color": Color(0.75, 0.20, 0.20, 1.0),
			"cx": 100, "cy": 90, "radius": 13, "law": Realm.SuccessionLaw.MALE_PREFERENCE_PRIMOGENITURE},
		{"key": "carthage", "realm": "Carthage", "ruler": "Hasdrubal", "color": Color(0.55, 0.30, 0.65, 1.0),
			"cx": 60, "cy": 128, "radius": 22, "law": Realm.SuccessionLaw.MALE_PREFERENCE_PRIMOGENITURE},
		{"key": "epirus", "realm": "Epirus", "ruler": "Alcetas", "color": Color(0.20, 0.40, 0.75, 1.0),
			"cx": 150, "cy": 100, "radius": 18, "law": Realm.SuccessionLaw.PRIMOGENITURE},
		{"key": "gaul", "realm": "Gallic Tribes", "ruler": "Brennos", "color": Color(0.25, 0.65, 0.30, 1.0),
			"cx": 90, "cy": 30, "radius": 27, "law": Realm.SuccessionLaw.PRIMOGENITURE},
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
				_paint_at(event.position)
		elif event.button_index == MOUSE_BUTTON_RIGHT and event.pressed:
			proposal.fill(0)
			_refresh_map_texture()
	elif event is InputEventMouseMotion and painting:
		_paint_at(event.position)

func _paint_at(screen_pos: Vector2) -> void:
	var cell := Vector2i(screen_pos / CELL_PIXELS)
	if not grid.in_bounds(cell.x, cell.y):
		return
	for dy in range(-PAINT_RADIUS, PAINT_RADIUS + 1):
		for dx in range(-PAINT_RADIUS, PAINT_RADIUS + 1):
			var x := cell.x + dx
			var y := cell.y + dy
			if not grid.in_bounds(x, y):
				continue
			if Vector2(dx, dy).length() <= PAINT_RADIUS:
				proposal[y * GRID_WIDTH + x] = player_realm_id
	_refresh_map_texture()
