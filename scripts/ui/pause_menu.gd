extends Control

# In-game overlay opened by game_root on Escape. Purely emits action
# signals - it holds no map_view reference and does no saving itself
# (matches hud.gd's split: the UI raises an event, game_root decides what
# to do about it), since the actual save is async and game_root is what
# owns map_view.

signal resume_requested
signal save_and_return_to_menu_requested
signal save_and_quit_requested

const SettingsPanelScene := preload("res://scenes/SettingsPanel.tscn")

var _buttons: Array[Button] = []
var _status_label: Label
var _settings_panel: Control

func _ready() -> void:
	set_anchors_preset(Control.PRESET_FULL_RECT)
	mouse_filter = Control.MOUSE_FILTER_STOP

	var bg := ColorRect.new()
	bg.color = Color(0.0, 0.0, 0.0, 0.55)
	bg.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(bg)

	var center := CenterContainer.new()
	center.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(center)

	var panel := PanelContainer.new()
	center.add_child(panel)

	var vbox := VBoxContainer.new()
	vbox.custom_minimum_size = Vector2(300, 0)
	vbox.add_theme_constant_override("separation", 10)
	panel.add_child(vbox)

	var title := Label.new()
	title.text = "Paused"
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	vbox.add_child(title)

	var resume_button := Button.new()
	resume_button.text = "Resume"
	resume_button.pressed.connect(func(): resume_requested.emit())
	vbox.add_child(resume_button)
	_buttons.append(resume_button)

	var settings_button := Button.new()
	settings_button.text = "Settings"
	settings_button.pressed.connect(_on_settings_pressed)
	vbox.add_child(settings_button)
	_buttons.append(settings_button)

	var save_menu_button := Button.new()
	save_menu_button.text = "Save and Return to Main Menu"
	save_menu_button.pressed.connect(func(): save_and_return_to_menu_requested.emit())
	vbox.add_child(save_menu_button)
	_buttons.append(save_menu_button)

	var save_quit_button := Button.new()
	save_quit_button.text = "Save and Quit to Desktop"
	save_quit_button.pressed.connect(func(): save_and_quit_requested.emit())
	vbox.add_child(save_quit_button)
	_buttons.append(save_quit_button)

	_status_label = Label.new()
	_status_label.autowrap_mode = TextServer.AUTOWRAP_WORD
	_status_label.custom_minimum_size = Vector2(300, 0)
	vbox.add_child(_status_label)

func _on_settings_pressed() -> void:
	if _settings_panel:
		return
	_settings_panel = SettingsPanelScene.instantiate()
	_settings_panel.theme = theme
	add_child(_settings_panel)
	_settings_panel.closed.connect(func():
		_settings_panel.queue_free()
		_settings_panel = null)

# Called by game_root while a save is in flight, so a slow chunked save
# (see SaveSystem) can't be triggered twice or interrupted by Resume.
func set_busy(busy: bool) -> void:
	for b in _buttons:
		b.disabled = busy

func show_message(text: String) -> void:
	_status_label.text = text
