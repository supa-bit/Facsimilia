extends Control

# The game's boot scene (see project.godot run/main_scene). "New Game"
# hands off to game_root.gd's normal intro/civ-select flow unchanged;
# "Continue" sets SaveSystem.continue_requested so game_root picks the
# load-a-save path instead - see game_root.gd's _ready().

const ThemeAncient := preload("res://scripts/ui/theme_ancient.gd")
const SettingsPanelScene := preload("res://scenes/SettingsPanel.tscn")
const SaveSystem := preload("res://scripts/world/save_system.gd")

var _continue_button: Button
var _settings_panel: Control

func _ready() -> void:
	theme = ThemeAncient.build()
	set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)

	var backdrop := ThemeAncient.backdrop(0.45)
	add_child(backdrop)
	# Darker on the left, where the menu sits.
	var side := TextureRect.new()
	var grad := Gradient.new()
	grad.set_color(0, Color(ThemeAncient.INK, 0.92))
	grad.set_color(1, Color(ThemeAncient.INK, 0.0))
	var tex := GradientTexture2D.new()
	tex.gradient = grad
	tex.width = 256
	tex.height = 4
	side.texture = tex
	side.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	side.stretch_mode = TextureRect.STRETCH_SCALE
	side.anchor_bottom = 1.0
	side.anchor_right = 0.55
	side.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(side)

	var margin := MarginContainer.new()
	margin.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	margin.add_theme_constant_override("margin_left", 140)
	margin.add_theme_constant_override("margin_bottom", 60)
	add_child(margin)

	var column := VBoxContainer.new()
	column.alignment = BoxContainer.ALIGNMENT_CENTER
	column.add_theme_constant_override("separation", 14)
	column.size_flags_horizontal = Control.SIZE_SHRINK_BEGIN
	margin.add_child(column)

	var title := Label.new()
	title.text = "FACSIMILIA"
	title.theme_type_variation = "TitleLabel"
	column.add_child(title)

	var subtitle := Label.new()
	subtitle.text = "The known world, 300 BC. History is yours to rewrite."
	subtitle.theme_type_variation = "SubtleLabel"
	subtitle.add_theme_font_size_override("font_size", 22)
	column.add_child(subtitle)

	var rule := ThemeAncient.ornament(440)
	rule.alignment = BoxContainer.ALIGNMENT_BEGIN
	column.add_child(rule)

	var spacer := Control.new()
	spacer.custom_minimum_size = Vector2(0, 16)
	column.add_child(spacer)

	var buttons := VBoxContainer.new()
	buttons.add_theme_constant_override("separation", 12)
	buttons.custom_minimum_size = Vector2(380, 0)
	buttons.size_flags_horizontal = Control.SIZE_SHRINK_BEGIN
	column.add_child(buttons)

	var new_game := _menu_button("New Game", "new_game", _on_new_game_pressed)
	buttons.add_child(new_game)
	_continue_button = _menu_button("Continue", "continue", _on_continue_pressed)
	_continue_button.disabled = not SaveSystem.has_save()
	if _continue_button.disabled:
		_continue_button.tooltip_text = "No saved game yet."
	buttons.add_child(_continue_button)
	buttons.add_child(_menu_button("Settings", "settings", _on_settings_pressed))
	buttons.add_child(_menu_button("Quit to Desktop", "quit", func(): get_tree().quit()))

	var footer := Label.new()
	footer.text = "Map data: Natural Earth, historical-basemaps · Population: HYDE 3.2.1 (PBL / Utrecht University)"
	footer.theme_type_variation = "SmallLabel"
	footer.set_anchors_and_offsets_preset(Control.PRESET_BOTTOM_LEFT)
	footer.position = Vector2(140, -44)
	footer.grow_vertical = Control.GROW_DIRECTION_BEGIN
	add_child(footer)

	new_game.grab_focus()

func _menu_button(text: String, icon_name: String, on_pressed: Callable) -> Button:
	var b := Button.new()
	b.text = text
	b.icon = ThemeAncient.icon(icon_name)
	b.theme_type_variation = "BigButton"
	b.alignment = HORIZONTAL_ALIGNMENT_LEFT
	b.expand_icon = false
	b.pressed.connect(on_pressed)
	return b

func _on_new_game_pressed() -> void:
	get_tree().change_scene_to_file("res://scenes/Main.tscn")

func _on_continue_pressed() -> void:
	SaveSystem.continue_requested = true
	get_tree().change_scene_to_file("res://scenes/Main.tscn")

func _on_settings_pressed() -> void:
	if _settings_panel:
		return
	_settings_panel = SettingsPanelScene.instantiate()
	_settings_panel.theme = theme
	add_child(_settings_panel)
	_settings_panel.closed.connect(func():
		_settings_panel.queue_free()
		_settings_panel = null)
