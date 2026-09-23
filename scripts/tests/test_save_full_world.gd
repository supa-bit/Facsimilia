extends SceneTree

const MapViewScript := preload("res://scripts/world/map_view.gd")
const OwnershipGrid := preload("res://scripts/world/ownership_grid.gd")
const CharacterRegistry := preload("res://scripts/dynasty/character_registry.gd")
const SaveSystem := preload("res://scripts/world/save_system.gd")

func _init() -> void:
	var dir := DirAccess.open("user://")
	if dir:
		dir.remove("save_state.json")
		dir.remove("save_grid.png")

	var mv = MapViewScript.new()
	mv.grid = OwnershipGrid.new(MapViewScript.GRID_WIDTH, MapViewScript.GRID_HEIGHT)
	mv.registry = CharacterRegistry.new()
	await mv._seed_land_and_sea()
	await mv._seed_real_civs()
	await mv._seed_frontier_zones()
	await mv.advance_year()  # exercise a year of dynasty state before saving, not just a fresh registry
	mv.demo_year = -290

	var rome_cells_before := 0
	for owner in mv.grid.cells:
		if owner == mv.civ_realm_ids["rome"]:
			rome_cells_before += 1
	assert(rome_cells_before > 0)

	var ok: bool = await mv.save_current_game()
	assert(ok)
	assert(SaveSystem.has_save())

	# Load into a second, independent instance - proves this isn't just
	# reading back the same in-memory objects.
	var mv2 = MapViewScript.new()
	mv2.grid = OwnershipGrid.new(MapViewScript.GRID_WIDTH, MapViewScript.GRID_HEIGHT)
	mv2.registry = CharacterRegistry.new()
	mv2.map_sprite = Sprite2D.new()  # _build_full_map_image needs this to exist, same as other headless tests
	var loaded: bool = await mv2.load_saved_game()
	assert(loaded)

	assert(mv2.demo_year == -290)
	assert(mv2.player_realm_id == mv.player_realm_id)
	assert(mv2.registry.characters.size() == mv.registry.characters.size())
	assert(mv2.registry.realms.size() == mv.registry.realms.size())

	var rome_realm_id: int = mv.civ_realm_ids["rome"]
	var rome_cells_after := 0
	for owner in mv2.grid.cells:
		if owner == rome_realm_id:
			rome_cells_after += 1
	assert(rome_cells_after == rome_cells_before)

	print("Full-world save/load round-trip passed: ", rome_cells_before,
		" Rome cells preserved, ", mv2.registry.characters.size(),
		" characters and ", mv2.registry.realms.size(), " realms restored.")

	mv.free()
	mv2.free()
	quit()
