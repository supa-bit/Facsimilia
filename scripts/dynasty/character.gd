extends RefCounted

# A single person: alive or dead, with the family links succession needs to
# walk (parents, spouse, children). This is a plain data holder - all the
# logic that creates, marries, and kills characters lives in
# character_registry.gd, so a Character never needs a back-reference to
# the registry that owns it.

var id: int
var name: String
var sex: String  # "male" or "female"
var birth_year: int
var death_year: int = -1
var is_alive: bool = true
var dynasty_id: int = -1
var father_id: int = -1
var mother_id: int = -1
var spouse_id: int = -1
var children_ids: Array[int] = []
var traits: Array[String] = []

func _init(p_id: int, p_name: String, p_sex: String, p_birth_year: int,
		p_dynasty_id: int = -1, p_father_id: int = -1, p_mother_id: int = -1) -> void:
	id = p_id
	name = p_name
	sex = p_sex
	birth_year = p_birth_year
	dynasty_id = p_dynasty_id
	father_id = p_father_id
	mother_id = p_mother_id

func age_in(year: int) -> int:
	return year - birth_year
