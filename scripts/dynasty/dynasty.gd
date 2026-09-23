extends RefCounted

# A house/bloodline - just an id, a name, and the set of characters who
# belong to it. Membership is patrilineal by default (a child joins its
# father's dynasty - see CharacterRegistry.have_child); that's a deliberate
# early default, not a hard rule, and can grow options later.

var id: int
var name: String
var founder_id: int
var member_ids: Array[int] = []

func _init(p_id: int, p_name: String, p_founder_id: int) -> void:
	id = p_id
	name = p_name
	founder_id = p_founder_id
	member_ids = [p_founder_id]
