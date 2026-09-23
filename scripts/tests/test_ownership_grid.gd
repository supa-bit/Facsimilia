extends SceneTree

const OwnershipGrid := preload("res://scripts/world/ownership_grid.gd")

func _init() -> void:
	var grid := OwnershipGrid.new(20, 20)
	grid.fill_rect(0, 0, 10, 10, 1)
	grid.fill_rect(10, 0, 20, 10, 2)

	assert(grid.get_owner(5, 5) == 1)
	assert(grid.get_owner(15, 5) == 2)
	assert(grid.get_owner(5, 15) == 0)
	assert(grid.get_owner(-1, 0) == -1)
	assert(grid.is_contiguous(1))
	assert(grid.is_contiguous(2))

	# Detach one cell of faction 1 from the rest and confirm contiguity breaks.
	grid.set_owner(3, 3, 0)
	grid.set_owner(2, 2, 5)
	grid.set_owner(17, 17, 1)
	assert(not grid.is_contiguous(1))

	var region := grid.flood_fill_region(1, 1)
	assert(region.size() > 0)

	print("OwnershipGrid tests passed (flood-filled region size: ", region.size(), ")")
	quit()
