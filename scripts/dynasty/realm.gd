extends RefCounted

# A title/realm currently held by whichever character is ruler_id. Not
# wired to the map's territory grid yet - that link (a realm owning a set
# of provinces) is the next layer to build on top of this one.

enum SuccessionLaw {
	PRIMOGENITURE,                  # eldest living child, any gender
	MALE_PREFERENCE_PRIMOGENITURE,  # eldest living son; else eldest living daughter
}

var id: int
var name: String
var ruler_id: int
var succession_law: int
var color: Color  # the realm's map color - persists across succession, unlike the ruler

func _init(p_id: int, p_name: String, p_ruler_id: int, p_succession_law: int, p_color: Color) -> void:
	id = p_id
	name = p_name
	ruler_id = p_ruler_id
	succession_law = p_succession_law
	color = p_color
