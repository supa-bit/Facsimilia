extends Control

signal civ_chosen(civ_key: String)

const CIVS := [
	{"key": "rome", "name": "Rome",
		"blurb": "A modest city-state on the Tiber, one power among many on the Italian peninsula. History remembers what it became - not what it started as."},
	{"key": "carthage", "name": "Carthage",
		"blurb": "The dominant trading power of the western Mediterranean, backed by a navy few can match."},
	{"key": "epirus", "name": "Epirus",
		"blurb": "A Greek kingdom on the rise in the east, ambitious and militarily bold."},
	{"key": "gaul", "name": "Gallic Tribes",
		"blurb": "Sprawling, fractious confederations north of the Alps, fiercely independent."},
]
const DEFAULT_KEY := "rome"

func _ready() -> void:
	set_anchors_preset(Control.PRESET_FULL_RECT)

	var bg := ColorRect.new()
	bg.color = Color(0.09, 0.07, 0.05, 1.0)
	bg.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(bg)

	var center := CenterContainer.new()
	center.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(center)

	var panel := PanelContainer.new()
	center.add_child(panel)

	var vbox := VBoxContainer.new()
	vbox.custom_minimum_size = Vector2(440, 0)
	vbox.add_theme_constant_override("separation", 10)
	panel.add_child(vbox)

	var title := Label.new()
	title.text = "300 BC - Choose Your Origin"
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	vbox.add_child(title)

	for civ in CIVS:
		var button := Button.new()
		button.text = civ.name + ("  (default)" if civ.key == DEFAULT_KEY else "")
		button.tooltip_text = civ.blurb
		button.pressed.connect(_on_civ_button_pressed.bind(civ.key))
		vbox.add_child(button)

		var blurb_label := Label.new()
		blurb_label.text = civ.blurb
		blurb_label.autowrap_mode = TextServer.AUTOWRAP_WORD
		blurb_label.modulate.a = 0.8
		vbox.add_child(blurb_label)

func _on_civ_button_pressed(civ_key: String) -> void:
	civ_chosen.emit(civ_key)
