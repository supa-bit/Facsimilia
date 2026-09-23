extends Node

# Autoload (see project.godot [autoload]). Owns the two settings the game
# currently has - fullscreen and master volume - persists them to
# user://settings.cfg, and applies them to the engine. Anything that reads
# or changes a setting (SettingsPanel, game_root's F11 shortcut) goes
# through here instead of touching DisplayServer/AudioServer directly, so
# every entry point stays in sync with whatever was last saved.

const SAVE_PATH := "user://settings.cfg"

var fullscreen: bool = false
var master_volume: float = 1.0  # linear 0..1, matches HSlider's natural range

func _ready() -> void:
	_load()
	_apply()

func set_fullscreen(value: bool) -> void:
	fullscreen = value
	_apply()
	_save()

func set_master_volume(value: float) -> void:
	master_volume = clampf(value, 0.0, 1.0)
	_apply()
	_save()

func _apply() -> void:
	DisplayServer.window_set_mode(
		DisplayServer.WINDOW_MODE_FULLSCREEN if fullscreen else DisplayServer.WINDOW_MODE_WINDOWED)
	var bus := AudioServer.get_bus_index("Master")
	if bus != -1:
		AudioServer.set_bus_volume_db(bus, linear_to_db(maxf(master_volume, 0.0001)))

func _load() -> void:
	var config := ConfigFile.new()
	if config.load(SAVE_PATH) != OK:
		return  # no settings file yet - defaults above stand
	fullscreen = config.get_value("display", "fullscreen", fullscreen)
	master_volume = config.get_value("audio", "master_volume", master_volume)

func _save() -> void:
	var config := ConfigFile.new()
	config.set_value("display", "fullscreen", fullscreen)
	config.set_value("audio", "master_volume", master_volume)
	config.save(SAVE_PATH)
