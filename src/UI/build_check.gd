extends CanvasLayer
## Warns when the game is running old compiled C# code.
##
## Godot only recompiles the C# code when the game is started from the
## editor (the Play button / F5, or the Build hammer). Starting it any other
## way - e.g. the "Run" button in the Project Manager - runs whatever was
## compiled last time, so pulled changes silently don't appear. This check is
## GDScript on purpose: GDScript is never compiled, so it can't go stale
## itself. It compares the compiled code's timestamp with the newest C#
## source file, and shows a banner if any source is newer.
##
## Only for runs from the project folder; exported games skip it.

const DLL_PATH := "res://.godot/mono/temp/bin/Debug/Facsimilia.dll"
const TOLERANCE_SEC := 2


func _ready() -> void:
	layer = 100
	if OS.has_feature("template") or DisplayServer.get_name() == "headless":
		queue_free()
		return
	var newest := _newest_source("res://src")
	newest = maxi(newest, _mtime("res://Facsimilia.csproj"))
	var built := _mtime(DLL_PATH)
	if built > 0 and built + TOLERANCE_SEC >= newest:
		queue_free()
		return
	var files_commit := _git_commit()
	var why := "The code was compiled before your latest files arrived."
	if built == 0:
		why = "The code hasn't been compiled yet."
	_show_banner("OLD CODE RUNNING - your latest changes are NOT in this game",
		why + " (Files on disk: build " + files_commit + ".)\n" +
		"To fix: close the game, open the project in the Godot editor, click the hammer " +
		"(Build) button at the top right, then press F5. If red errors appear at the " +
		"bottom of the editor after Build, send a screenshot of them.")


func _newest_source(dir_path: String) -> int:
	var newest := 0
	var dir := DirAccess.open(dir_path)
	if dir == null:
		return 0
	for file in dir.get_files():
		if file.ends_with(".cs"):
			newest = maxi(newest, _mtime(dir_path.path_join(file)))
	for sub in dir.get_directories():
		newest = maxi(newest, _newest_source(dir_path.path_join(sub)))
	return newest


func _mtime(path: String) -> int:
	return FileAccess.get_modified_time(path) if FileAccess.file_exists(path) else 0


## The commit the project folder is on, read from .git without needing git.
func _git_commit() -> String:
	var head := _read("res://.git/HEAD")
	if head.begins_with("ref: "):
		var ref := head.substr(5)
		var sha := _read("res://.git/" + ref)
		if sha == "":
			for line in _read("res://.git/packed-refs").split("\n"):
				if line.ends_with(" " + ref):
					sha = line.get_slice(" ", 0)
		head = sha
	return head.substr(0, 7) if head.length() >= 7 else "unknown"


func _read(path: String) -> String:
	var f := FileAccess.open(path, FileAccess.READ)
	return f.get_as_text().strip_edges() if f else ""


func _show_banner(title: String, body: String) -> void:
	var panel := PanelContainer.new()
	var style := StyleBoxFlat.new()
	style.bg_color = Color(0.45, 0.05, 0.05, 0.95)
	style.border_color = Color(1, 0.8, 0.3)
	style.set_border_width_all(3)
	style.set_content_margin_all(18)
	panel.add_theme_stylebox_override("panel", style)
	panel.set_anchors_and_offsets_preset(Control.PRESET_CENTER_TOP)
	panel.grow_horizontal = Control.GROW_DIRECTION_BOTH
	panel.offset_top = 90
	panel.custom_minimum_size = Vector2(900, 0)
	panel.mouse_filter = Control.MOUSE_FILTER_IGNORE
	var box := VBoxContainer.new()
	panel.add_child(box)
	var heading := Label.new()
	heading.text = title
	heading.add_theme_font_size_override("font_size", 26)
	heading.add_theme_color_override("font_color", Color(1, 0.9, 0.5))
	heading.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	box.add_child(heading)
	var text := Label.new()
	text.text = body
	text.add_theme_font_size_override("font_size", 19)
	text.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	text.custom_minimum_size = Vector2(860, 0)
	box.add_child(text)
	add_child(panel)
	push_warning(title + " " + body)
