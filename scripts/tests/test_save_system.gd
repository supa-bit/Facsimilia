extends SceneTree

const SaveSystem := preload("res://scripts/world/save_system.gd")
const OwnershipGrid := preload("res://scripts/world/ownership_grid.gd")
const CharacterRegistry := preload("res://scripts/dynasty/character_registry.gd")
const Realm := preload("res://scripts/dynasty/realm.gd")

func _init() -> void:
	# Clean slate: an earlier run (or a real play session on this machine)
	# may have left a save behind, which would make has_save() below
	# meaningless.
	var dir := DirAccess.open("user://")
	if dir:
		dir.remove("save_state.json")
		dir.remove("save_grid.png")
		dir.remove("save_population.bin")
	assert(not SaveSystem.has_save())

	var grid := OwnershipGrid.new(20, 15)
	grid.fill_rect(0, 0, 10, 10, 1)
	grid.fill_rect(10, 0, 20, 10, 2)
	grid.set_owner(5, 12, 255)  # SEA_OWNER_ID-like sentinel, well within a byte

	var registry := CharacterRegistry.new()
	var founder := registry.create_character("Numerius", "male", -345)
	registry.create_dynasty("House Numerius", founder)
	var spouse := registry.create_character("Numerius's spouse", "female", -343)
	registry.marry(founder, spouse)
	var heir := registry.have_child(spouse, founder, "Numerius's heir", "male", -322)
	var realm := registry.create_realm("Rome", founder,
		Realm.SuccessionLaw.MALE_PREFERENCE_PRIMOGENITURE, Color(0.75, 0.20, 0.20, 1.0))

	var ok: bool = await SaveSystem.save_game(grid, registry, -280, realm.id)
	assert(ok)
	assert(SaveSystem.has_save())

	var loaded: Dictionary = await SaveSystem.load_game()
	assert(not loaded.is_empty())

	var loaded_grid: OwnershipGrid = loaded.grid
	assert(loaded_grid.width == 20 and loaded_grid.height == 15)
	assert(loaded_grid.get_owner(5, 5) == 1)
	assert(loaded_grid.get_owner(15, 5) == 2)
	assert(loaded_grid.get_owner(5, 12) == 255)
	assert(loaded_grid.get_owner(0, 14) == 0)

	assert(loaded.demo_year == -280)
	assert(loaded.player_realm_id == realm.id)

	var loaded_registry: CharacterRegistry = loaded.registry
	assert(loaded_registry.characters.size() == 3)
	assert(loaded_registry.dynasties.size() == 1)
	assert(loaded_registry.realms.size() == 1)

	var loaded_founder = loaded_registry.characters[founder.id]
	assert(loaded_founder.name == "Numerius")
	assert(loaded_founder.is_alive)
	assert(loaded_founder.children_ids.size() == 1)
	assert(loaded_founder.children_ids[0] == heir.id)

	var loaded_realm = loaded_registry.realms[realm.id]
	assert(loaded_realm.name == "Rome")
	assert(loaded_realm.ruler_id == founder.id)
	assert(loaded_realm.succession_law == Realm.SuccessionLaw.MALE_PREFERENCE_PRIMOGENITURE)
	assert(is_equal_approx(loaded_realm.color.r, 0.75))

	# The id counters must continue past what was loaded, not restart from
	# 1 - otherwise a character/realm/dynasty created after a load could
	# collide with an id that already exists.
	var new_character := loaded_registry.create_character("New Arrival", "male", -280)
	assert(new_character.id > loaded_founder.id and new_character.id > heir.id)

	# No population passed: the load reports none.
	assert((loaded.population as Dictionary).is_empty())

	# Population state round-trips; saving again without one clears it,
	# so a stale population is never paired with a newer save.
	var pop := PackedFloat32Array([0.0, 12.5, 30000.0])
	assert(await SaveSystem.save_game(grid, registry, -279, realm.id, null, {"year": -279, "pop": pop}))
	var with_pop: Dictionary = await SaveSystem.load_game()
	assert(with_pop.population.year == -279)
	assert(with_pop.population.pop == pop)
	assert(await SaveSystem.save_game(grid, registry, -278, realm.id))
	assert(((await SaveSystem.load_game()).population as Dictionary).is_empty())

	print("SaveSystem tests passed: grid + registry + population round-trip through save_game/load_game, id counters continue correctly.")
	quit()
