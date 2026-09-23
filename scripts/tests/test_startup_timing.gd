extends SceneTree

const MapViewScript := preload("res://scripts/world/map_view.gd")
const OwnershipGrid := preload("res://scripts/world/ownership_grid.gd")
const CharacterRegistry := preload("res://scripts/dynasty/character_registry.gd")

func _init() -> void:
	# Measures the full one-time startup cost a real game launch pays:
	# seeding + building the display image + computing label centroids.
	# Skips Sprite2D/shader/camera (rendering-only, no timing relevance).
	var mv = MapViewScript.new()
	mv.grid = OwnershipGrid.new(MapViewScript.GRID_WIDTH, MapViewScript.GRID_HEIGHT)
	mv.registry = CharacterRegistry.new()

	var t0 := Time.get_ticks_msec()
	mv._seed_real_civs()
	mv._seed_frontier_zones()
	mv._seed_sea()
	var t1 := Time.get_ticks_msec()

	mv.map_sprite = Sprite2D.new()  # _build_full_map_image needs this to exist
	mv._build_full_map_image()
	var t2 := Time.get_ticks_msec()

	var centroids := mv._compute_centroids()
	var t3 := Time.get_ticks_msec()

	print("Seeding:            ", t1 - t0, " ms")
	print("Build map image:     ", t2 - t1, " ms")
	print("Compute centroids:   ", t3 - t2, " ms")
	print("TOTAL startup cost:  ", t3 - t0, " ms (", "%.1f" % ((t3 - t0) / 1000.0), " s)")
	print("Centroids computed for ", centroids.size(), " realms.")
	assert(centroids.size() == 12)

	mv.free()
	quit()
