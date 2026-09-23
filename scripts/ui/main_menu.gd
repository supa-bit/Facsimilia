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
	set_anchors_preset(Control.PRESET_FULL_RECT)

	var bg := ColorRect.new()
	bg.color = Color(0.09, 0.07, 0.05, 1.0)
	bg.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(bg)

	var center := CenterContainer.new()
	center.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(center)

	var vbox := VBoxContainer.new()
	vbox.custom_minimum_size = Vector2(280, 0)
	vbox.add_theme_constant_override("separation", 10)
	center.add_child(vbox)

	var title := Label.new()
	title.text = "Facsimilia"
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	vbox.add_child(title)

	var subtitle := Label.new()
	subtitle.text = "300 BC"
	subtitle.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	subtitle.modulate.a = 0.7
	vbox.add_child(subtitle)

	var new_game_button := Button.new()
	new_game_button.text = "New Game"
	new_game_button.pressed.connect(_on_new_game_pressed)
	vbox.add_child(new_game_button)

	_continue_button = Button.new()
	_continue_button.text = "Continue"
	_continue_button.disabled = not SaveSystem.has_save()
	_continue_button.pressed.connect(_on_continue_pressed)
	vbox.add_child(_continue_button)

	var settings_button := Button.new()
	settings_button.text = "Settings"
	settings_button.pressed.connect(_on_settings_pressed)
	vbox.add_child(settings_button)

	var quit_button := Button.new()
	quit_button.text = "Quit to Desktop"
	quit_button.pressed.connect(func(): get_tree().quit())
	vbox.add_child(quit_button)

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
