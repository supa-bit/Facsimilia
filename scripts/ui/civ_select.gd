extends Control

signal civ_chosen(civ_key: String)

# Matches map_view.gd's REAL_CIVS (sourced from real 300 BC political
# boundary data) plus FRONTIER_ZONES (tribal regions with no unified state
# at this date - genuinely accurate, not a data gap).
const CIVS := [
	{"key": "rome", "name": "Rome", "region": "Italy",
		"blurb": "A modest city-state on the Tiber, one power among many on the Italian peninsula. History remembers what it became - not what it started as."},
	{"key": "carthage", "name": "Carthage", "region": "North Africa",
		"blurb": "The dominant trading power of the western Mediterranean, backed by a navy few can match."},
	{"key": "egypt", "name": "Ptolemaic Egypt", "region": "Nile Valley",
		"blurb": "Alexander's Macedonian generals still rule the Nile, richer than almost anyone."},
	{"key": "kush", "name": "Kingdom of Kush", "region": "Nubia, south of Egypt",
		"blurb": "An indigenous African kingdom on the upper Nile, with its own pharaohs, iron industry, and pyramids - never conquered by the Ptolemies to its north."},
	{"key": "seleucid", "name": "Seleucid Empire", "region": "Syria / Mesopotamia",
		"blurb": "The largest of Alexander's successor kingdoms, stretching deep into the east."},
	{"key": "greek_world", "name": "Greek World", "region": "Macedon / Greece",
		"blurb": "Kassander's Macedon and the southern Greek city-states, forever rivals to one another."},
	{"key": "lysimachus", "name": "Kingdom of Lysimachus", "region": "Thrace",
		"blurb": "One of Alexander's own bodyguards, now a king in his own right on the European side of the straits."},
	{"key": "antigonus", "name": "Kingdom of Antigonus", "region": "Anatolia / Syria",
		"blurb": "The One-Eyed's sprawling, contested holdings across Asia Minor and the Levant."},
	{"key": "nabatea", "name": "Nabatean Kingdom", "region": "Arabia",
		"blurb": "Desert traders controlling the incense routes, centered on their rock-cut capital."},
	{"key": "iberia", "name": "Iberian & Celtiberian Tribes", "region": "Spain",
		"blurb": "Fierce, fragmented tribal peoples across the peninsula, prized as mercenaries - no single king rules here yet."},
	{"key": "gaul", "name": "Gallic Tribes", "region": "Gaul / the Alps",
		"blurb": "Sprawling, fractious confederations north of the Alps, fiercely independent."},
	{"key": "scythia", "name": "Scythian Peoples", "region": "Pontic Steppe",
		"blurb": "Mounted nomads ranging the grasslands north of the Black Sea."},
]
const DEFAULT_KEY := "rome"
const CARD_SIZE := Vector2(400, 176)
const ThemeAncient := preload("res://scripts/ui/theme_ancient.gd")
const MapViewScript := preload("res://scripts/world/map_view.gd")

var _selection_made := false  # guards against a double-fire from overlapping click handlers

func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_STOP
	set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(ThemeAncient.backdrop(0.72))

	var center := CenterContainer.new()
	center.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(center)

	var column := VBoxContainer.new()
	column.add_theme_constant_override("separation", 12)
	center.add_child(column)

	var title := Label.new()
	title.text = "CHOOSE YOUR REALM"
	title.theme_type_variation = "HeaderLabel"
	title.add_theme_font_size_override("font_size", 46)
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	column.add_child(title)

	var subtitle := Label.new()
	subtitle.text = "The world as it stood in 300 BC. Every realm starts where history had it."
	subtitle.theme_type_variation = "SubtleLabel"
	subtitle.add_theme_font_size_override("font_size", 21)
	subtitle.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	column.add_child(subtitle)
	column.add_child(ThemeAncient.ornament(520))

	var gap := Control.new()
	gap.custom_minimum_size = Vector2(0, 10)
	column.add_child(gap)

	var grid := GridContainer.new()
	grid.columns = 4
	grid.add_theme_constant_override("h_separation", 18)
	grid.add_theme_constant_override("v_separation", 18)
	column.add_child(grid)
	for civ in CIVS:
		grid.add_child(_card(civ))

	var hint := Label.new()
	hint.text = "Click a realm to begin."
	hint.theme_type_variation = "SmallLabel"
	hint.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	column.add_child(hint)

# One realm: its map color as a banner strip, name, region, and blurb.
# The whole card is the click target.
func _card(civ: Dictionary) -> PanelContainer:
	var panel := PanelContainer.new()
	panel.theme_type_variation = "CardPanel"
	panel.custom_minimum_size = CARD_SIZE
	panel.mouse_filter = Control.MOUSE_FILTER_STOP
	panel.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
	panel.gui_input.connect(_on_card_gui_input.bind(civ.key))
	panel.mouse_entered.connect(func(): panel.theme_type_variation = "CardPanelHover")
	panel.mouse_exited.connect(func(): panel.theme_type_variation = "CardPanel")

	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 14)
	row.mouse_filter = Control.MOUSE_FILTER_IGNORE
	panel.add_child(row)

	var banner := ColorRect.new()
	banner.color = MapViewScript.civ_color(civ.key)
	banner.custom_minimum_size = Vector2(8, 0)
	banner.mouse_filter = Control.MOUSE_FILTER_IGNORE
	row.add_child(banner)

	var text := VBoxContainer.new()
	text.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	text.add_theme_constant_override("separation", 2)
	text.mouse_filter = Control.MOUSE_FILTER_IGNORE
	row.add_child(text)

	var name_label := Label.new()
	name_label.text = civ.name
	name_label.theme_type_variation = "HeaderLabel"
	name_label.add_theme_font_size_override("font_size", 22)
	name_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	name_label.custom_minimum_size = Vector2(CARD_SIZE.x - 60, 0)
	name_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	text.add_child(name_label)

	var region := Label.new()
	region.text = civ.region + ("  ·  suggested start" if civ.key == DEFAULT_KEY else "")
	region.theme_type_variation = "SubtleLabel"
	region.add_theme_font_size_override("font_size", 17)
	region.mouse_filter = Control.MOUSE_FILTER_IGNORE
	text.add_child(region)

	var blurb := Label.new()
	blurb.text = civ.blurb
	blurb.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	blurb.add_theme_font_size_override("font_size", 16)
	blurb.add_theme_color_override("font_color", ThemeAncient.TEXT)
	blurb.custom_minimum_size = Vector2(CARD_SIZE.x - 60, 0)
	blurb.mouse_filter = Control.MOUSE_FILTER_IGNORE
	text.add_child(blurb)
	return panel

func _on_card_gui_input(event: InputEvent, civ_key: String) -> void:
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		_select_civ(civ_key)

func _select_civ(civ_key: String) -> void:
	if _selection_made:
		return
	_selection_made = true
	civ_chosen.emit(civ_key)
