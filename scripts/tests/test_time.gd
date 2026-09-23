extends SceneTree

const CharacterRegistry := preload("res://scripts/dynasty/character_registry.gd")
const Realm := preload("res://scripts/dynasty/realm.gd")

func _init() -> void:
	var reg := CharacterRegistry.new()

	# Mortality curve is a pure function - fully deterministic, no RNG needed.
	assert(reg.death_chance_for_age(30) == 0.0)
	assert(is_equal_approx(reg.death_chance_for_age(50), 0.10))
	assert(is_equal_approx(reg.death_chance_for_age(90), 0.35))
	assert(is_equal_approx(reg.death_chance_for_age(200), 0.35))  # capped

	# A ruler under 40 has exactly 0% death chance, so advance_year must
	# never kill them regardless of RNG - deterministic even with real
	# randomness, since rng.randf() is in [0, 1) and can never be < 0.0.
	var ruler := reg.create_character("Young Ruler", "male", 1000)
	reg.create_dynasty("House Young", ruler)
	var realm := reg.create_realm("Young Realm", ruler, Realm.SuccessionLaw.PRIMOGENITURE, Color.GRAY)
	for year in range(1000, 1035):
		var events := reg.advance_year(year)
		assert(events.is_empty())
	assert(ruler.is_alive)

	# An old ruler with no heir, advanced with an RNG forced to always
	# "succeed" the mortality roll (seed chosen so the first randf() call
	# lands well under any positive death chance), must die and produce a
	# succession-crisis event since they have no children.
	var old_ruler := reg.create_character("Old Ruler", "male", 900)
	reg.create_dynasty("House Old", old_ruler)
	var old_realm := reg.create_realm("Old Realm", old_ruler, Realm.SuccessionLaw.PRIMOGENITURE, Color.GRAY)
	var forced_rng := RandomNumberGenerator.new()
	forced_rng.seed = 1
	var first_roll := forced_rng.randf()
	assert(first_roll < reg.death_chance_for_age(999 - 900))  # confirm the seed actually forces a death this run
	forced_rng.seed = 1
	var events2 := reg.advance_year(999, forced_rng)
	assert(events2.size() == 1)
	assert(events2[0].findn("succession crisis") != -1)
	assert(not old_ruler.is_alive)
	assert(old_realm.ruler_id == old_ruler.id)

	print("Time/mortality tests passed: age-40 threshold holds, curve caps at 35%, ",
		"and a forced death correctly produced a succession crisis.")
	quit()
