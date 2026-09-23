extends RefCounted

# Owns every Character/Dynasty/Realm and is the only place that creates,
# links, or kills them - the data objects themselves stay plain and never
# reference this registry back.

const Character := preload("res://scripts/dynasty/character.gd")
const Dynasty := preload("res://scripts/dynasty/dynasty.gd")
const Realm := preload("res://scripts/dynasty/realm.gd")

var characters: Dictionary = {}  # id -> Character
var dynasties: Dictionary = {}   # id -> Dynasty
var realms: Dictionary = {}      # id -> Realm

var _next_character_id := 1
var _next_dynasty_id := 1
var _next_realm_id := 1

func create_dynasty(name: String, founder: Character) -> Dynasty:
	var dynasty := Dynasty.new(_next_dynasty_id, name, founder.id)
	_next_dynasty_id += 1
	dynasties[dynasty.id] = dynasty
	founder.dynasty_id = dynasty.id
	return dynasty

func create_character(name: String, sex: String, birth_year: int,
		dynasty_id: int = -1, father_id: int = -1, mother_id: int = -1) -> Character:
	var c := Character.new(_next_character_id, name, sex, birth_year, dynasty_id, father_id, mother_id)
	_next_character_id += 1
	characters[c.id] = c
	if dynasty_id != -1 and dynasties.has(dynasty_id):
		dynasties[dynasty_id].member_ids.append(c.id)
	if father_id != -1 and characters.has(father_id):
		characters[father_id].children_ids.append(c.id)
	if mother_id != -1 and characters.has(mother_id):
		characters[mother_id].children_ids.append(c.id)
	return c

func create_realm(name: String, ruler: Character, succession_law: int, color: Color) -> Realm:
	var r := Realm.new(_next_realm_id, name, ruler.id, succession_law, color)
	_next_realm_id += 1
	realms[r.id] = r
	return r

func marry(a: Character, b: Character) -> void:
	a.spouse_id = b.id
	b.spouse_id = a.id

func have_child(mother: Character, father: Character, name: String, sex: String, birth_year: int) -> Character:
	return create_character(name, sex, birth_year, father.dynasty_id, father.id, mother.id)

func kill(character: Character, death_year: int) -> void:
	character.is_alive = false
	character.death_year = death_year
	if character.spouse_id != -1 and characters.has(character.spouse_id):
		characters[character.spouse_id].spouse_id = -1

func living_children(character: Character) -> Array[Character]:
	var result: Array[Character] = []
	for cid in character.children_ids:
		var c: Character = characters[cid]
		if c.is_alive:
			result.append(c)
	result.sort_custom(func(a, b): return a.birth_year < b.birth_year)
	return result

# Who inherits realm when its ruler is dead, per the realm's succession
# law. Only considers the late ruler's direct living children for now -
# falling back to collateral lines (siblings, cousins) when there are no
# children is a follow-up, not implemented here. Returns null (a
# succession crisis) when no eligible heir exists.
func resolve_heir(realm: Realm) -> Character:
	var ruler: Character = characters.get(realm.ruler_id)
	if ruler == null:
		return null
	var children := living_children(ruler)
	match realm.succession_law:
		Realm.SuccessionLaw.PRIMOGENITURE:
			return children[0] if children.size() > 0 else null
		Realm.SuccessionLaw.MALE_PREFERENCE_PRIMOGENITURE:
			for c in children:
				if c.sex == "male":
					return c
			return children[0] if children.size() > 0 else null
	return null

# The "jump into your heir" moment: kills the current ruler and, if an
# heir can be resolved, hands them the realm. Returns the new ruler, or
# null on a succession crisis (realm.ruler_id is left pointing at the now-
# dead ruler in that case, since there's no one to hand it to).
func handle_ruler_death(realm: Realm, death_year: int) -> Character:
	var ruler: Character = characters.get(realm.ruler_id)
	if ruler == null or not ruler.is_alive:
		return null
	kill(ruler, death_year)
	var heir := resolve_heir(realm)
	if heir != null:
		realm.ruler_id = heir.id
	return heir

# Placeholder mortality curve: flat 0% below 40, rising 1%/year, capped at
# 35%. Deliberately simple and deterministic (no RNG) so it's fully unit-
# testable - a real health/trait system replaces this later. Pure function.
func death_chance_for_age(age: int) -> float:
	return clamp(float(age - 40) * 0.01, 0.0, 0.35)

# Advances the world by one year: every living ruler ages, and can die per
# death_chance_for_age, triggering handle_ruler_death (and therefore real
# succession) automatically. Pass a seeded rng for deterministic testing;
# omit it for real randomized play. Returns human-readable event strings
# for the HUD to display.
# Plain-Dictionary serialization for SaveSystem. JSON has no int/Color/
# typed-array types of its own, so load_from_dict() casts every numeric
# field back explicitly (JSON.parse_string returns floats for all numbers)
# instead of trusting the parsed types.
func to_dict() -> Dictionary:
	var characters_out := {}
	for id in characters:
		var c: Character = characters[id]
		characters_out[str(id)] = {
			"id": c.id, "name": c.name, "sex": c.sex, "birth_year": c.birth_year,
			"death_year": c.death_year, "is_alive": c.is_alive, "dynasty_id": c.dynasty_id,
			"father_id": c.father_id, "mother_id": c.mother_id, "spouse_id": c.spouse_id,
			"children_ids": Array(c.children_ids), "traits": Array(c.traits),
		}
	var dynasties_out := {}
	for id in dynasties:
		var d: Dynasty = dynasties[id]
		dynasties_out[str(id)] = {
			"id": d.id, "name": d.name, "founder_id": d.founder_id,
			"member_ids": Array(d.member_ids),
		}
	var realms_out := {}
	for id in realms:
		var r: Realm = realms[id]
		realms_out[str(id)] = {
			"id": r.id, "name": r.name, "ruler_id": r.ruler_id,
			"succession_law": r.succession_law,
			"color": [r.color.r, r.color.g, r.color.b, r.color.a],
		}
	return {
		"characters": characters_out, "dynasties": dynasties_out, "realms": realms_out,
		"next_character_id": _next_character_id, "next_dynasty_id": _next_dynasty_id,
		"next_realm_id": _next_realm_id,
	}

func load_from_dict(data: Dictionary) -> void:
	characters.clear()
	dynasties.clear()
	realms.clear()
	for key in data.get("characters", {}):
		var cd: Dictionary = data.characters[key]
		var c := Character.new(int(cd.id), cd.name, cd.sex, int(cd.birth_year),
			int(cd.dynasty_id), int(cd.father_id), int(cd.mother_id))
		c.death_year = int(cd.death_year)
		c.is_alive = bool(cd.is_alive)
		c.spouse_id = int(cd.spouse_id)
		var kids: Array[int] = []
		for k in cd.children_ids:
			kids.append(int(k))
		c.children_ids = kids
		var traits: Array[String] = []
		for t in cd.traits:
			traits.append(String(t))
		c.traits = traits
		characters[c.id] = c
	for key in data.get("dynasties", {}):
		var dd: Dictionary = data.dynasties[key]
		var d := Dynasty.new(int(dd.id), dd.name, int(dd.founder_id))
		var members: Array[int] = []
		for m in dd.member_ids:
			members.append(int(m))
		d.member_ids = members
		dynasties[d.id] = d
	for key in data.get("realms", {}):
		var rd: Dictionary = data.realms[key]
		var color_arr: Array = rd.color
		var color := Color(color_arr[0], color_arr[1], color_arr[2], color_arr[3])
		var r := Realm.new(int(rd.id), rd.name, int(rd.ruler_id), int(rd.succession_law), color)
		realms[r.id] = r
	_next_character_id = int(data.get("next_character_id", _next_character_id))
	_next_dynasty_id = int(data.get("next_dynasty_id", _next_dynasty_id))
	_next_realm_id = int(data.get("next_realm_id", _next_realm_id))

func advance_year(new_year: int, rng: RandomNumberGenerator = null) -> Array[String]:
	if rng == null:
		rng = RandomNumberGenerator.new()
		rng.randomize()
	var events: Array[String] = []
	for realm_id in realms.keys():
		var realm: Realm = realms[realm_id]
		var ruler: Character = characters.get(realm.ruler_id)
		if ruler == null or not ruler.is_alive:
			continue
		var age := ruler.age_in(new_year)
		if age < 0:
			continue
		if rng.randf() < death_chance_for_age(age):
			var old_name := ruler.name
			var heir := handle_ruler_death(realm, new_year)
			if heir == null:
				events.append("%s: %s has died at %d with no heir - succession crisis." % [realm.name, old_name, age])
			else:
				events.append("%s: %s has died at %d. %s inherits." % [realm.name, old_name, age, heir.name])
	return events
