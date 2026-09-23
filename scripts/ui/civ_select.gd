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
const CARD_MIN_SIZE := Vector2(260, 90)

var _debug_buttons: Array[Button] = []
var _selection_made := false  # guards against a double-fire if both the card and the button register the same click

func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_STOP
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
		# The WHOLE card is the click target, not just the button text -
		# this is deliberately more forgiving than a precise button hit,
		# as a robust fix regardless of the exact cause of the reported
		# click/visual offset (two prior diagnosis attempts - a DPI/
		# stretch project setting, and confirming via diff that an
		# external "fix" never touched this code - didn't resolve it, so
		# this widens the target instead of trying a third unconfirmed
		# theory). custom_minimum_size gives every card a large, stable
		# hit area independent of how Container auto-sizing settles.
		var panel := PanelContainer.new()
		panel.custom_minimum_size = CARD_MIN_SIZE
		panel.mouse_filter = Control.MOUSE_FILTER_STOP
		panel.gui_input.connect(_on_card_gui_input.bind(civ.key))
		panel.mouse_entered.connect(func(): panel.modulate = Color(1.15, 1.15, 1.15))
		panel.mouse_exited.connect(func(): panel.modulate = Color(1, 1, 1))

		var vbox := VBoxContainer.new()
		vbox.mouse_filter = Control.MOUSE_FILTER_IGNORE
		panel.add_child(vbox)

		var button := Button.new()
		button.text = civ.name + ("  (default)" if civ.key == DEFAULT_KEY else "")
		button.tooltip_text = civ.blurb
		button.mouse_filter = Control.MOUSE_FILTER_IGNORE  # card handles the click; button is visual only
		button.focus_mode = Control.FOCUS_NONE
		vbox.add_child(button)

		var region_label := Label.new()
		region_label.text = civ.region
		region_label.modulate.a = 0.7
		region_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
		vbox.add_child(region_label)

		grid.add_child(panel)
		_debug_buttons.append(button)

	# Layout (container sizing/positioning) isn't finalized the instant
	# these nodes are added - call_deferred runs this after the engine
	# has processed at least one layout pass, so the printed rects
	# reflect where things actually end up, not a stale pre-layout guess.
	call_deferred("_debug_print_layout")

func _debug_print_layout() -> void:
	print("=== CivSelect click-offset diagnostics ===")
	print("viewport size: ", get_viewport_rect().size, "  window size: ", DisplayServer.window_get_size())
	print("CivSelect global rect: ", get_global_rect())
	for b in _debug_buttons:
		print(b.text, " -> global_rect=", b.get_global_rect())

# Fires for any click that lands on this Control and isn't consumed by
# a card first (e.g. a click that misses every card, landing on empty
# background/margin) - if this is STILL happening with the much bigger
# card targets, that's strong evidence the offset is large or the whole
# screen is mispositioned, not just imprecise button hit-testing.
func _gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.pressed:
		print("CLICK (missed every card) at position=", event.position,
			" global_position=", event.global_position,
			" get_global_mouse_position()=", get_global_mouse_position())

func _on_card_gui_input(event: InputEvent, civ_key: String) -> void:
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		_select_civ(civ_key)

func _select_civ(civ_key: String) -> void:
	if _selection_made:
		return
	_selection_made = true
	print("CARD HIT: ", civ_key, " at get_global_mouse_position()=", get_global_mouse_position())
	civ_chosen.emit(civ_key)
