extends Control

# In-game HUD: a top bar (realm, ruler, heir, population, date), the
# chronicle of recent events bottom-left, and the turn button with map
# zoom controls bottom-right.

signal advance_requested

const ThemeAncient := preload("res://scripts/ui/theme_ancient.gd")
const CHRONICLE_LINES := 8

var map_view  # set via setup(); untyped to avoid a circular preload with map_view.gd

var realm_label: Label
var realm_banner: ColorRect
var ruler_label: Label
var heir_label: Label
var population_label: Label
var population_item: Control
var date_label: Label
var advance_button: Button
var chronicle: VBoxContainer
var _chronicle_entries: Array[String] = []

func _ready() -> void:
	set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	_build_top_bar()
	_build_chronicle()
	_build_turn_controls()

func _build_top_bar() -> void:
	var bar := PanelContainer.new()
	bar.theme_type_variation = "BarPanel"
	bar.set_anchors_and_offsets_preset(Control.PRESET_TOP_WIDE)
	bar.mouse_filter = Control.MOUSE_FILTER_STOP
	add_child(bar)

	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 34)
	bar.add_child(row)

	var realm_box := HBoxContainer.new()
	realm_box.add_theme_constant_override("separation", 12)
	realm_box.tooltip_text = "Your realm"
	realm_box.mouse_filter = Control.MOUSE_FILTER_PASS
	row.add_child(realm_box)
	realm_banner = ColorRect.new()
	realm_banner.custom_minimum_size = Vector2(10, 34)
	realm_banner.mouse_filter = Control.MOUSE_FILTER_IGNORE
	realm_box.add_child(realm_banner)
	realm_label = Label.new()
	realm_label.theme_type_variation = "HeaderLabel"
	realm_label.add_theme_font_size_override("font_size", 26)
	realm_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	realm_box.add_child(realm_label)

	ruler_label = _stat(row, "ruler", "Ruler")
	heir_label = _stat(row, "heir", "Heir apparent")
	population_label = _stat(row, "population", "People living in your realm's territory (HYDE historical estimate, then simulated)")
	population_item = population_label.get_parent()

	var spacer := Control.new()
	spacer.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	spacer.mouse_filter = Control.MOUSE_FILTER_IGNORE
	row.add_child(spacer)

	date_label = Label.new()
	date_label.theme_type_variation = "DateLabel"
	date_label.tooltip_text = "Current year"
	date_label.mouse_filter = Control.MOUSE_FILTER_PASS
	row.add_child(date_label)

# An icon + value pair in the top bar; returns the value label.
func _stat(row: HBoxContainer, icon_name: String, tip: String) -> Label:
	var box := HBoxContainer.new()
	box.add_theme_constant_override("separation", 8)
	box.tooltip_text = tip
	box.mouse_filter = Control.MOUSE_FILTER_PASS
	row.add_child(box)
	var icon := ThemeAncient.icon_rect(icon_name, 26)
	icon.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	box.add_child(icon)
	var value := Label.new()
	value.add_theme_font_size_override("font_size", 20)
	value.mouse_filter = Control.MOUSE_FILTER_IGNORE
	box.add_child(value)
	return value

func _build_chronicle() -> void:
	var panel := PanelContainer.new()
	panel.theme_type_variation = "GlassPanel"
	panel.set_anchors_and_offsets_preset(Control.PRESET_BOTTOM_LEFT)
	panel.grow_vertical = Control.GROW_DIRECTION_BEGIN
	panel.position = Vector2(20, -20)
	panel.custom_minimum_size = Vector2(430, 0)
	panel.mouse_filter = Control.MOUSE_FILTER_STOP
	add_child(panel)

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 6)
	panel.add_child(vbox)

	var header := HBoxContainer.new()
	header.add_theme_constant_override("separation", 10)
	vbox.add_child(header)
	header.add_child(ThemeAncient.icon_rect("log", 24))
	var title := Label.new()
	title.text = "Chronicle"
	title.theme_type_variation = "HeaderLabel"
	title.add_theme_font_size_override("font_size", 20)
	header.add_child(title)

	var rule := HSeparator.new()
	rule.add_theme_constant_override("separation", 4)
	vbox.add_child(rule)

	chronicle = VBoxContainer.new()
	chronicle.add_theme_constant_override("separation", 4)
	vbox.add_child(chronicle)

func _build_turn_controls() -> void:
	var box := VBoxContainer.new()
	box.set_anchors_and_offsets_preset(Control.PRESET_BOTTOM_RIGHT)
	box.grow_horizontal = Control.GROW_DIRECTION_BEGIN
	box.grow_vertical = Control.GROW_DIRECTION_BEGIN
	box.position = Vector2(-24, -24)
	box.alignment = BoxContainer.ALIGNMENT_END
	box.add_theme_constant_override("separation", 12)
	add_child(box)

	var zoom_row := HBoxContainer.new()
	zoom_row.alignment = BoxContainer.ALIGNMENT_END
	zoom_row.add_theme_constant_override("separation", 6)
	box.add_child(zoom_row)
	zoom_row.add_child(_icon_button("", "+", "Zoom in  (mouse wheel, +)", func(): map_view.zoom_in()))
	zoom_row.add_child(_icon_button("", "−", "Zoom out  (mouse wheel, -)", func(): map_view.zoom_out()))
	zoom_row.add_child(_icon_button("world", "", "Whole map  (Home)", func(): map_view.zoom_to_fit()))

	advance_button = Button.new()
	advance_button.text = "Advance Year"
	advance_button.icon = ThemeAncient.icon("end_turn")
	advance_button.theme_type_variation = "BigButton"
	advance_button.custom_minimum_size = Vector2(300, 0)
	advance_button.tooltip_text = "End this year's turn  (Enter)"
	advance_button.pressed.connect(func(): advance_requested.emit())
	box.add_child(advance_button)

	var hint := Label.new()
	hint.text = "Scroll to zoom · Middle-drag or WASD to pan · Esc for menu"
	hint.theme_type_variation = "SmallLabel"
	hint.add_theme_font_size_override("font_size", 14)
	hint.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	box.add_child(hint)

func _icon_button(icon_name: String, glyph: String, tip: String, on_pressed: Callable) -> Button:
	var b := Button.new()
	b.theme_type_variation = "IconButton"
	b.custom_minimum_size = Vector2(46, 46)
	b.tooltip_text = tip
	b.focus_mode = Control.FOCUS_NONE
	if icon_name != "":
		b.icon = ThemeAncient.icon(icon_name)
		b.icon_alignment = HORIZONTAL_ALIGNMENT_CENTER
	else:
		b.text = glyph
	b.pressed.connect(on_pressed)
	return b

func _unhandled_key_input(event: InputEvent) -> void:
	if event.pressed and not event.echo and event.keycode in [KEY_ENTER, KEY_KP_ENTER]:
		advance_requested.emit()
		get_viewport().set_input_as_handled()

func setup(p_map_view) -> void:
	map_view = p_map_view
	var realm = map_view.get_player_realm()
	_chronicle_entries = ["%s — The reign of %s begins." % [ThemeAncient.year_text(map_view.demo_year), realm.name]]
	_render_chronicle()

func refresh() -> void:
	var realm = map_view.get_player_realm()
	realm_label.text = realm.name
	realm_banner.color = realm.color
	date_label.text = ThemeAncient.year_text(map_view.demo_year)
	var ruler = map_view.registry.characters.get(realm.ruler_id)
	if ruler:
		ruler_label.text = "%s, %d" % [ruler.name, ruler.age_in(map_view.demo_year)]
		var heirs: Array = map_view.registry.living_children(ruler)
		heir_label.text = heirs[0].name if heirs.size() > 0 else "None"
	else:
		ruler_label.text = "Interregnum"
		heir_label.text = "—"
	population_item.visible = map_view.population != null
	if map_view.population:
		population_label.text = ThemeAncient.group_thousands(int(map_view.player_population()))

func log_events(events: Array) -> void:
	var when := ThemeAncient.year_text(map_view.demo_year)
	if events.is_empty():
		_chronicle_entries.push_front("%s — A quiet year." % when)
	else:
		for e in events:
			_chronicle_entries.push_front("%s — %s" % [when, e])
	_chronicle_entries.resize(mini(_chronicle_entries.size(), CHRONICLE_LINES))
	_render_chronicle()

func _render_chronicle() -> void:
	for c in chronicle.get_children():
		c.queue_free()
	for i in _chronicle_entries.size():
		var line := Label.new()
		line.text = _chronicle_entries[i]
		line.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		line.custom_minimum_size = Vector2(398, 0)
		line.add_theme_font_size_override("font_size", 17)
		# Newest entry brightest, older ones fade.
		line.modulate.a = 1.0 - 0.09 * i
		chronicle.add_child(line)
