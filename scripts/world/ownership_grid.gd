extends RefCounted

# Fine-grained territory ownership, stored as a flat grid of owner IDs
# (0 = unclaimed/wild). This is the whole foundation of the border system:
# borders are never stored as shapes, only ever derived from where owner
# IDs differ between neighboring cells. See shaders/border_outline.gdshader
# for the rendering side, and flood_fill_region()/is_contiguous() below for
# the logic side (sieges, cutoff territory, negotiation regions all reduce
# to grid connectivity, not geometry).

var width: int
var height: int
var cells: PackedInt32Array

func _init(w: int, h: int) -> void:
	width = w
	height = h
	cells = PackedInt32Array()
	cells.resize(w * h)

func in_bounds(x: int, y: int) -> bool:
	return x >= 0 and y >= 0 and x < width and y < height

func get_owner(x: int, y: int) -> int:
	if not in_bounds(x, y):
		return -1
	return cells[y * width + x]

func set_owner(x: int, y: int, owner_id: int) -> void:
	if in_bounds(x, y):
		cells[y * width + x] = owner_id

func fill_rect(x0: int, y0: int, x1: int, y1: int, owner_id: int) -> void:
	for y in range(max(0, y0), min(height, y1)):
		for x in range(max(0, x0), min(width, x1)):
			cells[y * width + x] = owner_id

# Seeds an irregular, organic-looking territory blob instead of a circle,
# so early demo maps don't look like they're made of stamped-out disks.
func fill_blob(cx: int, cy: int, radius: int, owner_id: int, seed: int = 0) -> void:
	var rng := RandomNumberGenerator.new()
	rng.seed = hash(Vector3(cx, cy, seed))
	var bands := 24
	var radii := PackedFloat32Array()
	radii.resize(bands)
	for i in bands:
		radii[i] = radius * rng.randf_range(0.7, 1.15)
	var reach := int(radius * 1.3)
	for y in range(max(0, cy - reach), min(height, cy + reach)):
		for x in range(max(0, cx - reach), min(width, cx + reach)):
			var dx := x - cx
			var dy := y - cy
			var dist := Vector2(dx, dy).length()
			var angle := atan2(dy, dx) + PI
			var band := int(angle / TAU * bands) % bands
			if dist <= radii[band]:
				cells[y * width + x] = owner_id

# Iterative (non-recursive) flood fill of the same-owner region containing
# (start_x, start_y). Used for contiguity checks and for resolving what a
# painted negotiation selection actually contains.
func flood_fill_region(start_x: int, start_y: int) -> Array:
	var start_owner := get_owner(start_x, start_y)
	if start_owner == -1:
		return []
	var visited := {}
	var stack: Array[Vector2i] = [Vector2i(start_x, start_y)]
	var region: Array[Vector2i] = []
	while stack.size() > 0:
		var p: Vector2i = stack.pop_back()
		if visited.has(p):
			continue
		visited[p] = true
		if get_owner(p.x, p.y) != start_owner:
			continue
		region.append(p)
		stack.append(Vector2i(p.x + 1, p.y))
		stack.append(Vector2i(p.x - 1, p.y))
		stack.append(Vector2i(p.x, p.y + 1))
		stack.append(Vector2i(p.x, p.y - 1))
	return region

# True if every cell owned by owner_id is reachable from every other
# (i.e. the realm isn't split into disconnected pockets - relevant once
# war/negotiation can carve arbitrary shapes out of a territory).
func is_contiguous(owner_id: int) -> bool:
	var first := Vector2i(-1, -1)
	for y in height:
		for x in width:
			if cells[y * width + x] == owner_id:
				first = Vector2i(x, y)
				break
		if first.x != -1:
			break
	if first.x == -1:
		return true
	var region := flood_fill_region(first.x, first.y)
	var region_set := {}
	for p in region:
		region_set[p] = true
	for y in height:
		for x in width:
			if cells[y * width + x] == owner_id and not region_set.has(Vector2i(x, y)):
				return false
	return true
