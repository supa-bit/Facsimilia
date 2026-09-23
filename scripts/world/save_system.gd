extends RefCounted
class_name SaveSystem

# Single save slot: user://save_state.json + user://save_grid.png. The
# ownership grid is stored as an L8 PNG (one byte per cell, same encoding
# already used for data/land_mask.png and data/political_mask.png - see
# tools/build_land_mask.py) rather than raw PackedInt32Array bytes, since
# owner ids never exceed 255 (see SEA_OWNER_ID in map_view.gd) and PNG's
# own compression shrinks the mostly-contiguous ownership data a great
# deal. Everything else (year, player realm, the character/dynasty/realm
# registry) is small enough to just be JSON.
#
# save_game()/load_game() are chunked and yielded exactly like world
# generation in map_view.gd (see _maybe_yield there), so triggering a save
# mid-game doesn't freeze the engine for the few seconds a full-grid pass
# takes. yield_host just needs to be some Node currently in the tree; pass
# null (as headless tests do) to run synchronously instead.

const OwnershipGrid := preload("res://scripts/world/ownership_grid.gd")
const CharacterRegistry := preload("res://scripts/dynasty/character_registry.gd")

const STATE_PATH := "user://save_state.json"
const GRID_PATH := "user://save_grid.png"
# Population engine state (PopulationEngine.to_dict(): per-node float
# arrays plus capitals), zstd-compressed via Godot's own store_var -
# optional, absent when the game ran without HYDE data.
const POPULATION_PATH := "user://save_population.bin"
const CHUNK_SIZE := 2000000

# Set by MainMenu's "Continue" button just before changing to Main.tscn;
# read and reset by game_root._ready() to pick the continue-flow instead
# of the normal new-game intro/civ-select flow.
static var continue_requested: bool = false

static func has_save() -> bool:
	return FileAccess.file_exists(STATE_PATH) and FileAccess.file_exists(GRID_PATH)

static func save_game(grid: OwnershipGrid, registry: CharacterRegistry, demo_year: int,
		player_realm_id: int, yield_host: Node = null, population: Dictionary = {}) -> bool:
	var bytes := await _cells_to_bytes(grid.cells, yield_host)
	if bytes.is_empty() and grid.cells.size() > 0:
		return false  # an owner id didn't fit in a byte - see _cells_to_bytes
	var image := Image.create_from_data(grid.width, grid.height, false, Image.FORMAT_L8, bytes)
	if image.save_png(GRID_PATH) != OK:
		push_error("SaveSystem: failed to write " + GRID_PATH)
		return false

	var state := {
		"grid_width": grid.width,
		"grid_height": grid.height,
		"demo_year": demo_year,
		"player_realm_id": player_realm_id,
		"registry": registry.to_dict(),
	}
	var file := FileAccess.open(STATE_PATH, FileAccess.WRITE)
	if file == null:
		push_error("SaveSystem: failed to open " + STATE_PATH + " for writing")
		return false
	file.store_string(JSON.stringify(state))
	file.close()

	if population.is_empty():
		if FileAccess.file_exists(POPULATION_PATH):
			DirAccess.remove_absolute(POPULATION_PATH)  # don't pair a stale population with this save
	else:
		var pop_file := FileAccess.open_compressed(POPULATION_PATH, FileAccess.WRITE, FileAccess.COMPRESSION_ZSTD)
		if pop_file == null:
			push_error("SaveSystem: failed to open " + POPULATION_PATH + " for writing")
			return false
		pop_file.store_var(population)
		pop_file.close()
	return true

# Returns {grid, registry, demo_year, player_realm_id, population} on
# success (population is {} if the save has none), or an empty Dictionary
# if there's no save or it couldn't be read.
static func load_game(yield_host: Node = null) -> Dictionary:
	if not has_save():
		return {}

	var file := FileAccess.open(STATE_PATH, FileAccess.READ)
	if file == null:
		return {}
	var parsed = JSON.parse_string(file.get_as_text())
	file.close()
	if typeof(parsed) != TYPE_DICTIONARY:
		push_error("SaveSystem: " + STATE_PATH + " is not valid JSON")
		return {}

	var image := Image.new()
	if image.load(GRID_PATH) != OK:
		push_error("SaveSystem: failed to read " + GRID_PATH)
		return {}
	image.convert(Image.FORMAT_L8)
	var width: int = int(parsed.get("grid_width", image.get_width()))
	var height: int = int(parsed.get("grid_height", image.get_height()))
	if image.get_width() != width or image.get_height() != height:
		push_error("SaveSystem: saved grid image size doesn't match saved state")
		return {}

	var grid := OwnershipGrid.new(width, height)
	grid.cells = await _bytes_to_cells(image.get_data(), yield_host)

	var registry := CharacterRegistry.new()
	registry.load_from_dict(parsed.get("registry", {}))

	var population := {}
	if FileAccess.file_exists(POPULATION_PATH):
		var pop_file := FileAccess.open_compressed(POPULATION_PATH, FileAccess.READ, FileAccess.COMPRESSION_ZSTD)
		if pop_file != null:
			var value = pop_file.get_var()
			pop_file.close()
			if typeof(value) == TYPE_DICTIONARY:
				population = value

	return {
		"grid": grid,
		"registry": registry,
		"demo_year": int(parsed.get("demo_year", 0)),
		"player_realm_id": int(parsed.get("player_realm_id", 0)),
		"population": population,
	}

static func _cells_to_bytes(cells: PackedInt32Array, yield_host: Node) -> PackedByteArray:
	var bytes := PackedByteArray()
	bytes.resize(cells.size())
	var total := cells.size()
	var i := 0
	while i < total:
		var end: int = mini(i + CHUNK_SIZE, total)
		for j in range(i, end):
			var owner_id := cells[j]
			if owner_id > 255:
				push_error("SaveSystem: owner id %d at cell %d exceeds the single-byte save format" % [owner_id, j])
				return PackedByteArray()
			bytes[j] = owner_id
		i = end
		if yield_host != null and yield_host.is_inside_tree():
			await yield_host.get_tree().process_frame
	return bytes

static func _bytes_to_cells(bytes: PackedByteArray, yield_host: Node) -> PackedInt32Array:
	var cells := PackedInt32Array()
	cells.resize(bytes.size())
	var total := bytes.size()
	var i := 0
	while i < total:
		var end: int = mini(i + CHUNK_SIZE, total)
		for j in range(i, end):
			cells[j] = bytes[j]
		i = end
		if yield_host != null and yield_host.is_inside_tree():
			await yield_host.get_tree().process_frame
	return cells
