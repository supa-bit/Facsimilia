extends SceneTree

const MapViewScript := preload("res://scripts/world/map_view.gd")
const OwnershipGrid := preload("res://scripts/world/ownership_grid.gd")
const CharacterRegistry := preload("res://scripts/dynasty/character_registry.gd")

func _init() -> void:
	# Drive map_view's setup manually, without add_child/_ready, so this
	# stays a pure headless data check - no Sprite2D/shader/window needed.
	var mv = MapViewScript.new()
	mv.grid = OwnershipGrid.new(MapViewScript.GRID_WIDTH, MapViewScript.GRID_HEIGHT)
	mv.registry = CharacterRegistry.new()
	mv._build_sea_mask()
	mv._seed_ancient_world()
	mv._clip_to_land()

	var total_sea := 0
	for v in mv.sea_mask:
		if v == 1:
			total_sea += 1
	var total_cells: int = MapViewScript.GRID_WIDTH * MapViewScript.GRID_HEIGHT
	print("Sea covers ", total_sea, " of ", total_cells, " cells (",
		"%.1f" % (100.0 * total_sea / total_cells), "%).")
	assert(total_sea > 0)
	assert(total_sea < total_cells)

	for civ_key in mv.civ_realm_ids.keys():
		var realm_id: int = mv.civ_realm_ids[civ_key]
		var count := 0
		for cell_owner in mv.grid.cells:
			if cell_owner == realm_id:
				count += 1
		print(civ_key, ": ", count, " land cells after coastline clipping")
		assert(count > 30)  # sanity floor - nobody should be clipped to near-nothing

	print("Terrain sanity check passed: sea carved out correctly, every civ still has real territory.")
	mv.free()
	quit()
