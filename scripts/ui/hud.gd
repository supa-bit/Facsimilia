extends Control

signal advance_requested

var map_view  # set via setup(); untyped to avoid a circular preload with map_view.gd

var realm_label: Label
var ruler_label: Label
var heir_label: Label
var population_label: Label
var log_label: Label
var advance_button: Button

func _ready() -> void:
	set_anchors_preset(Control.PRESET_FULL_RECT)
	mouse_filter = Control.MOUSE_FILTER_IGNORE

	var panel := PanelContainer.new()
	panel.position = Vector2(16, 16)
	panel.mouse_filter = Control.MOUSE_FILTER_STOP
	add_child(panel)

	var vbox := VBoxContainer.new()
	vbox.custom_minimum_size = Vector2(280, 0)
	vbox.add_theme_constant_override("separation", 6)
	panel.add_child(vbox)

	realm_label = Label.new()
	ruler_label = Label.new()
	heir_label = Label.new()
	population_label = Label.new()

	advance_button = Button.new()
	advance_button.text = "Advance 1 Year"
	advance_button.pressed.connect(func(): advance_requested.emit())

	log_label = Label.new()
	log_label.autowrap_mode = TextServer.AUTOWRAP_WORD
	log_label.custom_minimum_size = Vector2(280, 0)

	vbox.add_child(realm_label)
	vbox.add_child(ruler_label)
	vbox.add_child(heir_label)
	vbox.add_child(population_label)
	vbox.add_child(advance_button)
	vbox.add_child(log_label)

func setup(p_map_view) -> void:
	map_view = p_map_view

func refresh() -> void:
	var realm = map_view.get_player_realm()
	realm_label.text = "Realm: %s" % realm.name
	var ruler = map_view.registry.characters.get(realm.ruler_id)
	if ruler:
		ruler_label.text = "Ruler: %s (age %d)" % [ruler.name, ruler.age_in(map_view.demo_year)]
		var heirs: Array = map_view.registry.living_children(ruler)
		heir_label.text = "Heir: %s" % (heirs[0].name if heirs.size() > 0 else "none")
	else:
		ruler_label.text = "Ruler: none - interregnum"
		heir_label.text = "Heir: -"
	population_label.visible = map_view.population != null
	if map_view.population:
		population_label.text = "Population: %s" % _group_thousands(int(map_view.player_population()))

static func _group_thousands(n: int) -> String:
	var digits := str(absi(n))
	var out := ""
	while digits.length() > 3:
		out = "," + digits.substr(digits.length() - 3) + out
		digits = digits.substr(0, digits.length() - 3)
	return ("-" if n < 0 else "") + digits + out

func log_events(events: Array) -> void:
	if events.is_empty():
		log_label.text = "Year %d: nothing notable happened." % map_view.demo_year
	else:
		log_label.text = "\n".join(events)
