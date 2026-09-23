extends Node2D

const OwnershipGrid := preload("res://scripts/world/ownership_grid.gd")
const CharacterRegistry := preload("res://scripts/dynasty/character_registry.gd")
const Realm := preload("res://scripts/dynasty/realm.gd")

const GRID_WIDTH := 200
const GRID_HEIGHT := 150
const CELL_PIXELS := 4  # on-screen scale per grid cell
const PAINT_RADIUS := 3
const WILD_COLOR := Color(0.12, 0.12, 0.12, 1.0)  # unclaimed land - not a realm, no owner

var grid: OwnershipGrid
var registry: CharacterRegistry
var painting_realm_id: int
var demo_year := 1090

# Per-cell proposing realm id (0 = no active proposal). Preview layer only -
# never mutates grid's real ownership, just how a cell is displayed, so an
# offer can be seen before it's ever committed.
var proposal: PackedInt32Array
var map_sprite: Sprite2D
var painting := false

func _ready() -> void:
	grid = OwnershipGrid.new(GRID_WIDTH, GRID_HEIGHT)
	registry = CharacterRegistry.new()
	_seed_demo_realms()

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

	print("Facsimilia map view ready: ", GRID_WIDTH, "x", GRID_HEIGHT, " cells. ",
		"Left-drag to propose annexing/settling land as ", _realm(painting_realm_id).name,
		", right-click to clear the proposal. Press K to kill that realm's current ",
		"ruler and watch succession happen (territory color stays the same - only ",
		"the ruler changes).")

func _realm(realm_id: int) -> Realm:
	return registry.realms[realm_id]

# Each demo realm gets a founding ruler, a dynasty, a spouse, and one heir
# already born - enough for the K-key succession demo to have someone to
# inherit. Territory ownership on the grid is keyed by realm.id, not by an
# arbitrary faction number, so the map's political map IS the realm data.
func _seed_demo_realms() -> void:
	var specs := [
		{"realm": "Aldric", "ruler": "Aldric", "color": Color(0.75, 0.20, 0.20, 1.0),
			"cx": 50, "cy": 45, "radius": 24, "law": Realm.SuccessionLaw.MALE_PREFERENCE_PRIMOGENITURE},
		{"realm": "Marveld", "ruler": "Osric", "color": Color(0.20, 0.40, 0.75, 1.0),
			"cx": 130, "cy": 55, "radius": 20, "law": Realm.SuccessionLaw.PRIMOGENITURE},
		{"realm": "Cassenor", "ruler": "Ivo", "color": Color(0.25, 0.65, 0.30, 1.0),
			"cx": 90, "cy": 105, "radius": 22, "law": Realm.SuccessionLaw.PRIMOGENITURE},
	]
	var first_realm_id := -1
	for i in specs.size():
		var spec: Dictionary = specs[i]
		var ruler := registry.create_character(spec.ruler, "male", 1055)
		registry.create_dynasty("House " + spec.ruler, ruler)
		var spouse := registry.create_character(spec.ruler + "'s spouse", "female", 1057)
		registry.marry(ruler, spouse)
		registry.have_child(spouse, ruler, spec.ruler + "'s heir", "male", 1078)
		var realm := registry.create_realm(spec.realm, ruler, spec.law, spec.color)
		if first_realm_id == -1:
			first_realm_id = realm.id
		grid.fill_blob(spec.cx, spec.cy, spec.radius, realm.id, i + 1)
	painting_realm_id = first_realm_id

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
	elif event is InputEventKey and event.pressed and event.keycode == KEY_K:
		_debug_kill_painting_realm_ruler()

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
				proposal[y * GRID_WIDTH + x] = painting_realm_id
	_refresh_map_texture()

# Debug-only trigger so succession can be watched live in the running game,
# not just in the headless test. Prints to the editor's Output panel since
# there's no on-screen UI for character info yet.
func _debug_kill_painting_realm_ruler() -> void:
	var realm := _realm(painting_realm_id)
	var old_ruler = registry.characters.get(realm.ruler_id)
	if old_ruler == null:
		print(realm.name, ": no living ruler to kill.")
		return
	var old_ruler_name: String = old_ruler.name
	demo_year += 1
	var heir = registry.handle_ruler_death(realm, demo_year)
	if heir == null:
		print(realm.name, ": ", old_ruler_name, " has died with no heir. ",
			"Succession crisis - the realm has no ruler.")
	else:
		print(realm.name, ": ", old_ruler_name, " has died. ", heir.name, " inherits. ",
			"Territory color is unchanged - same realm, new ruler.")
