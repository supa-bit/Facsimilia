extends SceneTree

const MapViewScript := preload("res://scripts/world/map_view.gd")
const OwnershipGrid := preload("res://scripts/world/ownership_grid.gd")
const CharacterRegistry := preload("res://scripts/dynasty/character_registry.gd")

func _init() -> void:
	# Drive map_view's setup manually, without add_child/_ready, so this
	# stays a pure headless data check - no Sprite2D/shader/window/camera/
	# labels needed.
	var mv = MapViewScript.new()
	mv.grid = OwnershipGrid.new(MapViewScript.GRID_WIDTH, MapViewScript.GRID_HEIGHT)
	mv.registry = CharacterRegistry.new()
	mv.sea_mask = PackedByteArray()
	mv.sea_mask.resize(MapViewScript.GRID_WIDTH * MapViewScript.GRID_HEIGHT)

	mv._seed_real_civs()
	mv._seed_frontier_zones()
	mv._seed_sea()

	var total_sea := 0
	for v in mv.sea_mask:
		if v == 1:
			total_sea += 1
	var total_cells: int = MapViewScript.GRID_WIDTH * MapViewScript.GRID_HEIGHT
	print("Sea covers ", total_sea, " of ", total_cells, " cells (",
		"%.1f" % (100.0 * total_sea / total_cells), "%).")
	assert(total_sea > 0)
	assert(total_sea < total_cells)

	var expected_keys := ["rome", "carthage", "egypt", "kush", "seleucid", "greek_world",
		"lysimachus", "antigonus", "nabatea", "iberia", "gaul", "scythia"]
	assert(mv.civ_realm_ids.size() == expected_keys.size())
	for civ_key in expected_keys:
		assert(mv.civ_realm_ids.has(civ_key), "missing civ: " + civ_key)
		var realm_id: int = mv.civ_realm_ids[civ_key]
		var count := 0
		for cell_owner in mv.grid.cells:
			if cell_owner == realm_id:
				count += 1
		print(civ_key, ": ", count, " land cells")
		assert(count > 15, civ_key + " has suspiciously little territory (" + str(count) + " cells)")

	print("Terrain sanity check passed: real 300 BC boundaries loaded, frontier zones filled the gaps, sea carved out the rest, every one of the 12 regions has real territory.")
	mv.free()
	quit()
