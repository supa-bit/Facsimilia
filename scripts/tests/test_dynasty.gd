extends SceneTree

const CharacterRegistry := preload("res://scripts/dynasty/character_registry.gd")
const Realm := preload("res://scripts/dynasty/realm.gd")

func _init() -> void:
	var reg := CharacterRegistry.new()

	# Male-preference primogeniture: younger son should beat older daughter.
	var founder := reg.create_character("Aldric", "male", 1050)
	var dynasty := reg.create_dynasty("House Aldric", founder)
	var spouse := reg.create_character("Elira", "female", 1052)
	reg.marry(founder, spouse)
	var daughter := reg.have_child(spouse, founder, "Beatrix", "female", 1072)
	var son := reg.have_child(spouse, founder, "Cedric", "male", 1075)

	assert(founder.dynasty_id == dynasty.id)
	assert(daughter.father_id == founder.id and daughter.mother_id == spouse.id)

	var realm_male_pref := reg.create_realm("Aldric Realm", founder, Realm.SuccessionLaw.MALE_PREFERENCE_PRIMOGENITURE)
	var heir := reg.handle_ruler_death(realm_male_pref, 1090)
	assert(heir != null)
	assert(heir.id == son.id)
	assert(not founder.is_alive)
	assert(realm_male_pref.ruler_id == son.id)

	# Plain primogeniture: eldest child wins regardless of gender.
	var founder2 := reg.create_character("Osric", "male", 1040)
	reg.create_dynasty("House Osric", founder2)
	var spouse2 := reg.create_character("Mira", "female", 1043)
	reg.marry(founder2, spouse2)
	var eldest_daughter := reg.have_child(spouse2, founder2, "Nadia", "female", 1065)
	reg.have_child(spouse2, founder2, "Talon", "male", 1068)

	var realm_primo := reg.create_realm("Osric Realm", founder2, Realm.SuccessionLaw.PRIMOGENITURE)
	var heir2 := reg.handle_ruler_death(realm_primo, 1085)
	assert(heir2.id == eldest_daughter.id)

	# Succession crisis: no children means no resolvable heir.
	var lone_ruler := reg.create_character("Ivo", "male", 1030)
	reg.create_dynasty("House Ivo", lone_ruler)
	var lonely_realm := reg.create_realm("Ivo Realm", lone_ruler, Realm.SuccessionLaw.PRIMOGENITURE)
	var heir3 := reg.handle_ruler_death(lonely_realm, 1080)
	assert(heir3 == null)
	assert(lonely_realm.ruler_id == lone_ruler.id)

	print("Dynasty tests passed: male-preference picked the son over the elder ",
		"daughter, plain primogeniture picked the elder daughter, and a childless ",
		"ruler correctly produced a succession crisis instead of a fake heir.")
	quit()
