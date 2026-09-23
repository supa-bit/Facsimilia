extends Node2D

const CivSelectScene := preload("res://scenes/CivSelect.tscn")
const HudScene := preload("res://scenes/Hud.tscn")
const MapViewScript := preload("res://scripts/world/map_view.gd")
const ThemeAncient := preload("res://scripts/ui/theme_ancient.gd")

const MIN_LOADING_SECONDS := 45.0
const FADE_SECONDS := 0.6

var map_view: Node2D
var hud: Control
var civ_select: Control
var ui_layer: CanvasLayer  # keeps UI in screen space, unaffected by map_view's camera zoom/pan
var loading_screen: Control
var loading_label: Label

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
	map_view.loading_status_changed.connect(_on_loading_status_changed)

	await _show_loading_screen()

	var start_ms := Time.get_ticks_msec()
	await map_view.generate_world()
	var elapsed_sec := (Time.get_ticks_msec() - start_ms) / 1000.0
	if elapsed_sec < MIN_LOADING_SECONDS:
		await get_tree().create_timer(MIN_LOADING_SECONDS - elapsed_sec).timeout

	await _hide_loading_screen()

	hud = HudScene.instantiate()
	hud.theme = ThemeAncient.build()
	ui_layer.add_child(hud)
	hud.setup(map_view)
	hud.refresh()
	hud.advance_requested.connect(_on_advance_requested)

func _on_loading_status_changed(text: String) -> void:
	if loading_label:
		loading_label.text = text

# Held for a minimum of MIN_LOADING_SECONDS regardless of actual load
# time (world generation is usually well under that, but the fixed
# floor avoids a jarring flash-then-gone screen and gives the fade
# in/out room to actually read as a transition). mouse_filter = STOP
# on the root blocks all clicks to whatever's behind it while up -
# world generation is chunked/yielded so the app stays responsive
# during this time, it's just intentionally not interactive yet.
func _show_loading_screen() -> void:
	loading_screen = Control.new()
	loading_screen.set_anchors_preset(Control.PRESET_FULL_RECT)
	loading_screen.mouse_filter = Control.MOUSE_FILTER_STOP
	loading_screen.theme = ThemeAncient.build()
	loading_screen.modulate.a = 0.0

	var bg := ColorRect.new()
	bg.color = Color(0.09, 0.07, 0.05, 1.0)
	bg.set_anchors_preset(Control.PRESET_FULL_RECT)
	loading_screen.add_child(bg)

	var center := CenterContainer.new()
	center.set_anchors_preset(Control.PRESET_FULL_RECT)
	loading_screen.add_child(center)

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 10)
	center.add_child(vbox)

	var title := Label.new()
	title.text = "300 BC"
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	vbox.add_child(title)

	loading_label = Label.new()
	loading_label.text = "Preparing the ancient world..."
	loading_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	vbox.add_child(loading_label)

	ui_layer.add_child(loading_screen)

	var tween := create_tween()
	tween.tween_property(loading_screen, "modulate:a", 1.0, FADE_SECONDS)
	await tween.finished

func _hide_loading_screen() -> void:
	var tween := create_tween()
	tween.tween_property(loading_screen, "modulate:a", 0.0, FADE_SECONDS)
	await tween.finished
	loading_screen.queue_free()
	loading_screen = null
	loading_label = null

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
