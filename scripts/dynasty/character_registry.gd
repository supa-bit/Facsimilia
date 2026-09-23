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
