extends Control

signal civ_chosen(civ_key: String)

const CIVS := [
	{"key": "rome", "name": "Rome", "region": "Italy",
		"blurb": "A modest city-state on the Tiber, one power among many on the Italian peninsula. History remembers what it became - not what it started as."},
	{"key": "carthage", "name": "Carthage", "region": "North Africa - coast",
		"blurb": "The dominant trading power of the western Mediterranean, backed by a navy few can match."},
	{"key": "numidia", "name": "Numidian & Berber Peoples", "region": "North Africa - interior",
		"blurb": "Horse-lords of the Maghreb's interior, from the Atlas mountains to the desert's edge."},
	{"key": "egypt", "name": "Ptolemaic Egypt", "region": "Nile Valley",
		"blurb": "Alexander's Macedonian generals still rule the Nile, richer than almost anyone."},
	{"key": "iberia", "name": "Iberian & Celtiberian Tribes", "region": "Spain",
		"blurb": "Fierce, fragmented tribal peoples across the peninsula, prized as mercenaries."},
	{"key": "gaul", "name": "Gallic Tribes", "region": "Gaul / the Alps",
		"blurb": "Sprawling, fractious confederations north of the Alps, fiercely independent."},
	{"key": "kush", "name": "Kingdom of Kush", "region": "Nubia, south of Egypt",
		"blurb": "An indigenous African kingdom on the upper Nile, with its own pharaohs, iron industry, and pyramids - never conquered by the Ptolemies to its north."},
	{"key": "greek_world", "name": "Greek World", "region": "Greece / Aegean",
		"blurb": "City-states, leagues, and Epirus's ambitious kings, forever rivals to one another."},
	{"key": "dacia", "name": "Dacian & Getae Tribes", "region": "Carpathians",
		"blurb": "Mountain and river peoples north of the Danube, skilled in metalwork and war."},
	{"key": "thrace_anatolia", "name": "Thrace & Anatolia", "region": "Asia Minor",
		"blurb": "The fractured remains of Lysimachus's kingdom, contested by every neighbor."},
	{"key": "scythia", "name": "Scythian Peoples", "region": "Pontic Steppe",
		"blurb": "Mounted nomads ranging the grasslands north of the Black Sea."},
	{"key": "seleucid", "name": "Seleucid Empire", "region": "Levant / Mesopotamia",
		"blurb": "The largest of Alexander's successor kingdoms, stretching deep into the east."},
]
const DEFAULT_KEY := "rome"

func _ready() -> void:
	set_anchors_preset(Control.PRESET_FULL_RECT)

	var bg := ColorRect.new()
	bg.color = Color(0.09, 0.07, 0.05, 1.0)
	bg.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(bg)

	var margin := MarginContainer.new()
	margin.set_anchors_preset(Control.PRESET_FULL_RECT)
	for side in ["margin_left", "margin_right", "margin_top", "margin_bottom"]:
		margin.add_theme_constant_override(side, 36)
	add_child(margin)

	var outer_vbox := VBoxContainer.new()
	outer_vbox.add_theme_constant_override("separation", 16)
	margin.add_child(outer_vbox)

	var title := Label.new()
	title.text = "300 BC - Choose Your Origin"
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	outer_vbox.add_child(title)

	var grid := GridContainer.new()
	grid.columns = 3
	grid.add_theme_constant_override("h_separation", 14)
	grid.add_theme_constant_override("v_separation", 14)
	outer_vbox.add_child(grid)

	for civ in CIVS:
		var panel := PanelContainer.new()
		var vbox := VBoxContainer.new()
		panel.add_child(vbox)

		var button := Button.new()
		button.text = civ.name + ("  (default)" if civ.key == DEFAULT_KEY else "")
		button.tooltip_text = civ.blurb
		button.pressed.connect(_on_civ_button_pressed.bind(civ.key))
		vbox.add_child(button)

		var region_label := Label.new()
		region_label.text = civ.region
		region_label.modulate.a = 0.7
		vbox.add_child(region_label)

		grid.add_child(panel)

func _on_civ_button_pressed(civ_key: String) -> void:
	civ_chosen.emit(civ_key)
