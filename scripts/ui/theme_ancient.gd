extends RefCounted

# The Ancient era's visual theme (300 BC start). Deliberately just one era
# for now - later eras (Medieval, etc.) get their own build()-style
# functions alongside this one; nothing here assumes it's the only theme
# that will ever exist.

static func build() -> Theme:
	var theme := Theme.new()

	var panel_color := Color(0.20, 0.16, 0.11, 0.92)   # aged leather/parchment-dark
	var accent_color := Color(0.78, 0.62, 0.30, 1.0)   # antique gold
	var text_color := Color(0.93, 0.86, 0.72, 1.0)     # parchment cream
	var bg_color := Color(0.13, 0.10, 0.07, 1.0)       # dark umber

	var panel_style := StyleBoxFlat.new()
	panel_style.bg_color = panel_color
	panel_style.border_color = accent_color
	panel_style.set_border_width_all(2)
	panel_style.set_corner_radius_all(4)
	panel_style.set_content_margin_all(12)
	theme.set_stylebox("panel", "PanelContainer", panel_style)

	var button_normal := StyleBoxFlat.new()
	button_normal.bg_color = Color(0.28, 0.20, 0.12, 1.0)
	button_normal.border_color = accent_color
	button_normal.set_border_width_all(1)
	button_normal.set_corner_radius_all(3)
	button_normal.set_content_margin_all(8)

	var button_hover: StyleBoxFlat = button_normal.duplicate()
	button_hover.bg_color = Color(0.38, 0.28, 0.16, 1.0)

	var button_pressed: StyleBoxFlat = button_normal.duplicate()
	button_pressed.bg_color = accent_color

	theme.set_stylebox("normal", "Button", button_normal)
	theme.set_stylebox("hover", "Button", button_hover)
	theme.set_stylebox("pressed", "Button", button_pressed)
	theme.set_color("font_color", "Button", text_color)
	theme.set_color("font_hover_color", "Button", text_color)
	theme.set_color("font_pressed_color", "Button", bg_color)

	theme.set_color("font_color", "Label", text_color)

	return theme
