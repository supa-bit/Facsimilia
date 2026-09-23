extends Node2D

const CivSelectScene := preload("res://scenes/CivSelect.tscn")
const HudScene := preload("res://scenes/Hud.tscn")
const MapViewScript := preload("res://scripts/world/map_view.gd")
const ThemeAncient := preload("res://scripts/ui/theme_ancient.gd")

var map_view: Node2D
var hud: Control
var civ_select: Control
var ui_layer: CanvasLayer  # keeps UI in screen space, unaffected by map_view's camera zoom/pan

func _ready() -> void:
	ui_layer = CanvasLayer.new()
	add_child(ui_layer)

	civ_select = CivSelectScene.instantiate()
	civ_select.theme = ThemeAncient.build()
	ui_layer.add_child(civ_select)
	civ_select.civ_chosen.connect(_on_civ_chosen)

func _on_civ_chosen(civ_key: String) -> void:
	civ_select.queue_free()

	map_view = MapViewScript.new()
	map_view.name = "MapView"
	add_child(map_view)
	map_view.set_player_civ(civ_key)

	hud = HudScene.instantiate()
	hud.theme = ThemeAncient.build()
	ui_layer.add_child(hud)
	hud.setup(map_view)
	hud.refresh()
	hud.advance_requested.connect(_on_advance_requested)

func _on_advance_requested() -> void:
	var events: Array = map_view.advance_year()
	hud.refresh()
	hud.log_events(events)

func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and event.keycode == KEY_F11:
		_toggle_fullscreen()

func _toggle_fullscreen() -> void:
	if DisplayServer.window_get_mode() == DisplayServer.WINDOW_MODE_FULLSCREEN:
		DisplayServer.window_set_mode(DisplayServer.WINDOW_MODE_WINDOWED)
	else:
		DisplayServer.window_set_mode(DisplayServer.WINDOW_MODE_FULLSCREEN)
