extends Node2D

const OwnershipGrid := preload("res://scripts/world/ownership_grid.gd")

const GRID_WIDTH := 200
const GRID_HEIGHT := 150
const CELL_PIXELS := 4  # on-screen scale per grid cell
const PAINT_RADIUS := 3
const PAINTING_FACTION := 1  # placeholder for "the player"/initiating faction until selection exists

const FACTION_COLORS := {
	0: Color(0.12, 0.12, 0.12, 1.0),
	1: Color(0.75, 0.20, 0.20, 1.0),
	2: Color(0.20, 0.40, 0.75, 1.0),
	3: Color(0.25, 0.65, 0.30, 1.0),
}

var grid: OwnershipGrid
# Per-cell proposing faction id (0 = no active proposal). This is a preview
# layer only - it never mutates grid's real ownership, it just changes how
# a cell is displayed, so a war/negotiation offer can be seen before it's
# ever committed.
var proposal: PackedInt32Array
var map_sprite: Sprite2D
var painting := false

func _ready() -> void:
	grid = OwnershipGrid.new(GRID_WIDTH, GRID_HEIGHT)
	_seed_demo_territories()

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
		"Left-drag to propose annexing/settling land as faction ", PAINTING_FACTION,
		", right-click to clear the proposal.")

func _seed_demo_territories() -> void:
	grid.fill_blob(50, 45, 24, 1, 1)
	grid.fill_blob(130, 55, 20, 2, 2)
	grid.fill_blob(90, 105, 22, 3, 3)

# Contested land (already owned by someone else) shows as a true blend of
# both factions' real colors - it reads as "this land is disputed between
# X and Y," not as a generic highlight color. Unclaimed land has nothing to
# blend with, so a proposal there just paints it solid - that's settlement,
# not negotiation.
func _display_color(x: int, y: int) -> Color:
	var proposer := proposal[y * GRID_WIDTH + x]
	var current_owner := grid.get_owner(x, y)
	if proposer == 0:
		return FACTION_COLORS.get(current_owner, Color.MAGENTA)
	if current_owner == 0:
		return FACTION_COLORS.get(proposer, Color.MAGENTA)
	var owner_color: Color = FACTION_COLORS.get(current_owner, Color.MAGENTA)
	var proposer_color: Color = FACTION_COLORS.get(proposer, Color.MAGENTA)
	return owner_color.lerp(proposer_color, 0.5)

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
				proposal[y * GRID_WIDTH + x] = PAINTING_FACTION
	_refresh_map_texture()
