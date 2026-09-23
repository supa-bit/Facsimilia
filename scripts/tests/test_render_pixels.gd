extends SceneTree

const MapViewScript := preload("res://scripts/world/map_view.gd")
const OwnershipGrid := preload("res://scripts/world/ownership_grid.gd")
const CharacterRegistry := preload("res://scripts/dynasty/character_registry.gd")

# Color.is_equal_approx() takes no tolerance parameter (fixed internal
# epsilon, too strict for RGBA8 byte-quantization rounding), so this
# compares channels manually against an explicit tolerance instead.
static func colors_close(a: Color, b: Color, tolerance: float) -> bool:
	return abs(a.r - b.r) <= tolerance and abs(a.g - b.g) <= tolerance \
		and abs(a.b - b.b) <= tolerance and abs(a.a - b.a) <= tolerance

func _init() -> void:
	var mv = MapViewScript.new()
	mv.grid = OwnershipGrid.new(MapViewScript.GRID_WIDTH, MapViewScript.GRID_HEIGHT)
	mv.registry = CharacterRegistry.new()
	await mv._seed_real_civs()
	await mv._seed_frontier_zones()
	await mv._seed_sea()
	mv.map_sprite = Sprite2D.new()
	await mv._build_full_map_image()

	# Sample actual built-image pixels at cells whose owner we know from
	# the grid directly, and compare against what that owner's color
	# should be - this catches a byte-layout bug in _build_full_map_image
	# that pure ownership-data tests can't see. Tolerance is intentionally
	# looser than Color.is_equal_approx()'s default: converting a float
	# color to RGBA8 (8 bits/channel, ~0.0039 per step) and back always
	# introduces sub-1% rounding, which isn't a bug.
	const COLOR_TOLERANCE := 0.01

	var checked := 0
	var mismatches := 0
	var rng := RandomNumberGenerator.new()
	rng.seed = 42
	for i in 40:
		var x := rng.randi_range(0, MapViewScript.GRID_WIDTH - 1)
		var y := rng.randi_range(0, MapViewScript.GRID_HEIGHT - 1)
		var owner_id: int = mv.grid.get_owner(x, y)
		var expected: Color = mv._color_for_owner(owner_id)
		var actual: Color = mv.map_image.get_pixel(x, y)
		checked += 1
		if not colors_close(actual, expected, COLOR_TOLERANCE):
			mismatches += 1
			print("MISMATCH at (", x, ",", y, ") owner=", owner_id,
				" expected=", expected, " actual=", actual)

	# Also check one pixel from each known region explicitly (centroid-ish
	# guess: just scan for the first cell with each realm id).
	for civ_key in mv.civ_realm_ids.keys():
		var realm_id: int = mv.civ_realm_ids[civ_key]
		var found := false
		for y in range(0, MapViewScript.GRID_HEIGHT, 37):
			for x in range(0, MapViewScript.GRID_WIDTH, 37):
				if mv.grid.get_owner(x, y) == realm_id:
					var expected: Color = mv._realm(realm_id).color
					var actual: Color = mv.map_image.get_pixel(x, y)
					checked += 1
					if not colors_close(actual, expected, COLOR_TOLERANCE):
						mismatches += 1
						print("MISMATCH (", civ_key, ") at (", x, ",", y, ") expected=", expected, " actual=", actual)
					found = true
					break
			if found:
				break
		if not found:
			print("Could not find a sample cell for ", civ_key, " on the coarse scan (not necessarily a bug).")

	print("Checked ", checked, " pixels, ", mismatches, " mismatches.")
	print("map_image size: ", mv.map_image.get_size(), " format: ", mv.map_image.get_format())
	mv.free()
	quit()
