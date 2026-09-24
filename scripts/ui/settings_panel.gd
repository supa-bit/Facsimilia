extends Control

# Reusable settings overlay - instantiated as a child wherever it's needed
# (MainMenu, or on top of PauseMenu in-game) rather than swapped in as its
# own scene, matching how civ_select/hud/loading screens are already added
# as overlay children of a CanvasLayer instead of full scene changes.
# Emits `closed` instead of freeing itself, so the owner decides what
# happens next (MainMenu just frees it; PauseMenu re-shows its own buttons).

signal closed

func _ready() -> void:
	set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	mouse_filter = Control.MOUSE_FILTER_STOP

	var bg := ColorRect.new()
	bg.color = Color(0.03, 0.02, 0.01, 0.7)
	bg.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(bg)

	var center := CenterContainer.new()
	center.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(center)

	var panel := PanelContainer.new()
	center.add_child(panel)

	var vbox := VBoxContainer.new()
	vbox.custom_minimum_size = Vector2(420, 0)
	vbox.add_theme_constant_override("separation", 14)
	panel.add_child(vbox)

	var title := Label.new()
	title.text = "Settings"
	title.theme_type_variation = "HeaderLabel"
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	vbox.add_child(title)
	vbox.add_child(preload("res://scripts/ui/theme_ancient.gd").ornament(300))

	var fullscreen_row := HBoxContainer.new()
	vbox.add_child(fullscreen_row)
	var fullscreen_label := Label.new()
	fullscreen_label.text = "Fullscreen"
	fullscreen_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	fullscreen_row.add_child(fullscreen_label)
	var fullscreen_check := CheckBox.new()
	fullscreen_check.button_pressed = SettingsStore.fullscreen
	fullscreen_check.toggled.connect(func(pressed: bool): SettingsStore.set_fullscreen(pressed))
	fullscreen_row.add_child(fullscreen_check)

	var volume_row := HBoxContainer.new()
	vbox.add_child(volume_row)
	var volume_label := Label.new()
	volume_label.text = "Master Volume"
	volume_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	volume_row.add_child(volume_label)
	var volume_slider := HSlider.new()
	volume_slider.custom_minimum_size = Vector2(200, 0)
	volume_slider.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	volume_slider.min_value = 0.0
	volume_slider.max_value = 1.0
	volume_slider.step = 0.01
	volume_slider.value = SettingsStore.master_volume
	volume_slider.value_changed.connect(func(value: float): SettingsStore.set_master_volume(value))
	volume_row.add_child(volume_slider)

	var back_button := Button.new()
	back_button.text = "Back"
	back_button.pressed.connect(func(): closed.emit())
	vbox.add_child(back_button)
