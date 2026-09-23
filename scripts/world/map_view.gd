extends Node2D

const OwnershipGrid := preload("res://scripts/world/ownership_grid.gd")

const GRID_WIDTH := 200
const GRID_HEIGHT := 150
const CELL_PIXELS := 4  # on-screen scale per grid cell
const PAINT_RADIUS := 3

const FACTION_COLORS := {
	0: Color(0.12, 0.12, 0.12, 1.0),
	1: Color(0.75, 0.20, 0.20, 1.0),
	2: Color(0.20, 0.40, 0.75, 1.0),
	3: Color(0.25, 0.65, 0.30, 1.0),
}
const SELECTION_COLOR := Color(1.0, 0.9, 0.2, 0.45)

var grid: OwnershipGrid
var map_sprite: Sprite2D
var selection_sprite: Sprite2D
var selection: PackedByteArray
var painting := false

func _ready() -> void:
	grid = OwnershipGrid.new(GRID_WIDTH, GRID_HEIGHT)
	_seed_demo_territories()

	map_sprite = Sprite2D.new()
	map_sprite.centered = false
	map_sprite.scale = Vector2(CELL_PIXELS, CELL_PIXELS)
	map_sprite.texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
	add_child(map_sprite)

	var mat := ShaderMaterial.new()
	mat.shader = load("res://shaders/border_outline.gdshader")
	mat.set_shader_parameter("texel_size", Vector2(1.0 / GRID_WIDTH, 1.0 / GRID_HEIGHT))
	map_sprite.material = mat

	selection = PackedByteArray()
	selection.resize(GRID_WIDTH * GRID_HEIGHT)

	selection_sprite = Sprite2D.new()
	selection_sprite.centered = false
	selection_sprite.scale = Vector2(CELL_PIXELS, CELL_PIXELS)
	selection_sprite.texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
	add_child(selection_sprite)

	_refresh_map_texture()
	_refresh_selection_texture()

	print("Facsimilia map view ready: ", GRID_WIDTH, "x", GRID_HEIGHT, " cells. ",
		"Left-drag to paint a proposed border, right-click to clear it.")

func _seed_demo_territories() -> void:
	grid.fill_blob(50, 45, 24, 1, 1)
	grid.fill_blob(130, 55, 20, 2, 2)
	grid.fill_blob(90, 105, 22, 3, 3)

func _refresh_map_texture() -> void:
	var img := Image.create(GRID_WIDTH, GRID_HEIGHT, false, Image.FORMAT_RGBA8)
	for y in GRID_HEIGHT:
		for x in GRID_WIDTH:
			var owner_id := grid.get_owner(x, y)
			img.set_pixel(x, y, FACTION_COLORS.get(owner_id, Color.MAGENTA))
	var tex := ImageTexture.create_from_image(img)
	map_sprite.texture = tex

func _refresh_selection_texture() -> void:
	var img := Image.create(GRID_WIDTH, GRID_HEIGHT, false, Image.FORMAT_RGBA8)
	img.fill(Color(0, 0, 0, 0))
	for y in GRID_HEIGHT:
		for x in GRID_WIDTH:
			if selection[y * GRID_WIDTH + x] == 1:
				img.set_pixel(x, y, SELECTION_COLOR)
	var tex := ImageTexture.create_from_image(img)
	selection_sprite.texture = tex

func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventMouseButton:
		if event.button_index == MOUSE_BUTTON_LEFT:
			painting = event.pressed
			if painting:
				_paint_at(event.position)
		elif event.button_index == MOUSE_BUTTON_RIGHT and event.pressed:
			selection.fill(0)
			_refresh_selection_texture()
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
				selection[y * GRID_WIDTH + x] = 1
	_refresh_selection_texture()
