extends RefCounted

# The Ancient era's visual theme (300 BC start): dark bronze panels with
# gold trim, Cinzel (Roman inscriptional capitals) for headings and
# Alegreya (a book serif) for body text, gold-tinted game-icons.net icons.
# Later eras (Medieval, etc.) get their own build()-style functions
# alongside this one; nothing here assumes it's the only theme.
#
# Type variations, set on a control with `theme_type_variation`:
#   Label:          TitleLabel (huge Cinzel), HeaderLabel (Cinzel heading),
#                   DateLabel (Cinzel, bright gold), SubtleLabel (dim italic),
#                   SmallLabel
#   PanelContainer: CardPanel, CardPanelHover, BarPanel (top bar), GlassPanel
#   Button:         BigButton (Cinzel, taller), IconButton (square, compact)

const GOLD := Color(0.83, 0.67, 0.36)
const GOLD_BRIGHT := Color(0.97, 0.84, 0.52)
const GOLD_DIM := Color(0.55, 0.43, 0.24)
const TEXT := Color(0.93, 0.88, 0.77)
const TEXT_DIM := Color(0.70, 0.64, 0.54)
const PANEL := Color(0.10, 0.08, 0.06, 0.94)
const PANEL_LIGHT := Color(0.17, 0.13, 0.09, 0.96)
const INK := Color(0.07, 0.05, 0.04)

const HEADING_FONT_PATH := "res://assets/fonts/Cinzel.ttf"
const BODY_FONT_PATH := "res://assets/fonts/Alegreya.ttf"
const ITALIC_FONT_PATH := "res://assets/fonts/Alegreya-Italic.ttf"
const TERRAIN_PATH := "res://data/terrain_texture.png"

static func build() -> Theme:
	var theme := Theme.new()
	var body := _font(BODY_FONT_PATH, 450)
	var heading := _font(HEADING_FONT_PATH, 600)
	var heading_bold := _font(HEADING_FONT_PATH, 700)
	var italic := _font(ITALIC_FONT_PATH, 450)
	theme.default_font = body
	theme.default_font_size = 19

	# Panels.
	theme.set_stylebox("panel", "PanelContainer", _panel(PANEL, GOLD_DIM, 1, 18))
	theme.set_type_variation("CardPanel", "PanelContainer")
	theme.set_stylebox("panel", "CardPanel", _panel(Color(0.12, 0.095, 0.07, 0.95), GOLD_DIM, 1, 16))
	theme.set_type_variation("CardPanelHover", "PanelContainer")
	theme.set_stylebox("panel", "CardPanelHover", _panel(Color(0.19, 0.145, 0.095, 0.98), GOLD_BRIGHT, 2, 16))
	theme.set_type_variation("BarPanel", "PanelContainer")
	var bar := _panel(Color(0.08, 0.065, 0.05, 0.95), GOLD_DIM, 0, 10)
	bar.border_width_bottom = 2
	bar.content_margin_left = 20
	bar.content_margin_right = 20
	theme.set_stylebox("panel", "BarPanel", bar)
	theme.set_type_variation("GlassPanel", "PanelContainer")
	theme.set_stylebox("panel", "GlassPanel", _panel(Color(0.08, 0.065, 0.05, 0.82), GOLD_DIM, 1, 14))

	# Labels.
	theme.set_color("font_color", "Label", TEXT)
	theme.set_color("font_shadow_color", "Label", Color(0, 0, 0, 0.45))
	theme.set_constant("shadow_offset_x", "Label", 0)
	theme.set_constant("shadow_offset_y", "Label", 1)
	_label_variation(theme, "TitleLabel", heading_bold, 96, GOLD_BRIGHT)
	theme.set_constant("outline_size", "TitleLabel", 10)
	theme.set_color("font_outline_color", "TitleLabel", Color(0, 0, 0, 0.55))
	_label_variation(theme, "HeaderLabel", heading, 30, GOLD_BRIGHT)
	_label_variation(theme, "DateLabel", heading_bold, 28, GOLD_BRIGHT)
	_label_variation(theme, "SubtleLabel", italic, 18, TEXT_DIM)
	_label_variation(theme, "SmallLabel", body, 16, TEXT_DIM)

	# Buttons.
	var normal := _button(Color(0.20, 0.15, 0.10), GOLD_DIM)
	var hover := _button(Color(0.30, 0.22, 0.13), GOLD_BRIGHT)
	var pressed := _button(Color(0.55, 0.42, 0.20), GOLD_BRIGHT)
	var disabled := _button(Color(0.14, 0.12, 0.10, 0.85), Color(0.30, 0.26, 0.21))
	var focus := StyleBoxFlat.new()
	focus.draw_center = false
	focus.border_color = Color(GOLD_BRIGHT, 0.7)
	focus.set_border_width_all(1)
	focus.set_corner_radius_all(3)
	focus.set_expand_margin_all(3)
	for type in ["Button", "OptionButton"]:
		theme.set_stylebox("normal", type, normal)
		theme.set_stylebox("hover", type, hover)
		theme.set_stylebox("pressed", type, pressed)
		theme.set_stylebox("hover_pressed", type, pressed)
		theme.set_stylebox("disabled", type, disabled)
		theme.set_stylebox("focus", type, focus)
		theme.set_color("font_color", type, TEXT)
		theme.set_color("font_hover_color", type, Color(1, 0.96, 0.86))
		theme.set_color("font_pressed_color", type, INK)
		theme.set_color("font_hover_pressed_color", type, INK)
		theme.set_color("font_disabled_color", type, Color(0.45, 0.41, 0.35))
		theme.set_color("icon_normal_color", type, GOLD)
		theme.set_color("icon_hover_color", type, GOLD_BRIGHT)
		theme.set_color("icon_pressed_color", type, INK)
		theme.set_color("icon_disabled_color", type, Color(0.40, 0.36, 0.30))
		theme.set_constant("h_separation", type, 12)
		theme.set_constant("icon_max_width", type, 26)
	theme.set_font("font", "Button", heading)
	theme.set_font_size("font_size", "Button", 19)

	theme.set_type_variation("BigButton", "Button")
	for state in ["normal", "hover", "pressed", "hover_pressed", "disabled"]:
		var s: StyleBoxFlat = theme.get_stylebox(state, "Button").duplicate()
		s.content_margin_top = 14
		s.content_margin_bottom = 14
		s.content_margin_left = 22
		s.content_margin_right = 22
		theme.set_stylebox(state, "BigButton", s)
	theme.set_font_size("font_size", "BigButton", 22)
	theme.set_constant("icon_max_width", "BigButton", 30)

	theme.set_type_variation("IconButton", "Button")
	for state in ["normal", "hover", "pressed", "hover_pressed", "disabled"]:
		var s: StyleBoxFlat = theme.get_stylebox(state, "Button").duplicate()
		s.set_content_margin_all(8)
		theme.set_stylebox(state, "IconButton", s)
	theme.set_constant("icon_max_width", "IconButton", 24)
	theme.set_font_size("font_size", "IconButton", 24)

	# Tooltips.
	theme.set_stylebox("panel", "TooltipPanel", _panel(Color(0.07, 0.055, 0.04, 0.97), GOLD, 1, 10))
	theme.set_color("font_color", "TooltipLabel", TEXT)
	theme.set_font("font", "TooltipLabel", body)
	theme.set_font_size("font_size", "TooltipLabel", 17)

	# Separators, sliders, check boxes.
	var line := StyleBoxLine.new()
	line.color = Color(GOLD, 0.45)
	line.thickness = 1
	theme.set_stylebox("separator", "HSeparator", line)
	theme.set_constant("separation", "HSeparator", 14)
	var slider := StyleBoxFlat.new()
	slider.bg_color = Color(0.05, 0.04, 0.03)
	slider.border_color = GOLD_DIM
	slider.set_border_width_all(1)
	slider.set_corner_radius_all(3)
	slider.content_margin_top = 3
	slider.content_margin_bottom = 3
	theme.set_stylebox("slider", "HSlider", slider)
	var fill := slider.duplicate()
	fill.bg_color = GOLD
	theme.set_stylebox("grabber_area", "HSlider", fill)
	theme.set_stylebox("grabber_area_highlight", "HSlider", fill)
	theme.set_color("font_color", "CheckBox", TEXT)
	theme.set_font("font", "CheckBox", body)
	return theme

# A game-icons.net icon (white on transparent in assets/icons/), tinted
# by whatever displays it.
static func icon(icon_name: String) -> Texture2D:
	return load("res://assets/icons/%s.svg" % icon_name)

# An icon sized for inline use next to text, tinted gold unless told otherwise.
static func icon_rect(icon_name: String, px: int = 24, tint: Color = GOLD) -> TextureRect:
	var r := TextureRect.new()
	r.texture = icon(icon_name)
	r.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	r.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	r.custom_minimum_size = Vector2(px, px)
	r.modulate = tint
	r.mouse_filter = Control.MOUSE_FILTER_IGNORE
	return r

static func heading_font(weight: int = 600) -> Font:
	return _font(HEADING_FONT_PATH, weight)

# "300 BC", "AD 14". There's no year 0 (1 BC is followed by AD 1);
# map_view.advance_year() skips it.
static func year_text(year: int) -> String:
	return "%d BC" % -year if year < 0 else "AD %d" % year

static func group_thousands(n: int) -> String:
	var digits := str(absi(n))
	var out := ""
	while digits.length() > 3:
		out = "," + digits.substr(digits.length() - 3) + out
		digits = digits.substr(0, digits.length() - 3)
	return ("-" if n < 0 else "") + digits + out

# The real terrain map, darkened, as a full-screen backdrop for menus and
# loading screens. `shade` is how dark the veil over it is (0-1).
static func backdrop(shade: float = 0.6) -> Control:
	var root := Control.new()
	root.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	root.mouse_filter = Control.MOUSE_FILTER_IGNORE
	root.clip_contents = true
	var base := ColorRect.new()
	base.color = INK
	base.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	base.mouse_filter = Control.MOUSE_FILTER_IGNORE
	root.add_child(base)
	if ResourceLoader.exists(TERRAIN_PATH):
		var terrain := TextureRect.new()
		terrain.texture = load(TERRAIN_PATH)
		terrain.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		terrain.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_COVERED
		terrain.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
		terrain.mouse_filter = Control.MOUSE_FILTER_IGNORE
		terrain.modulate = Color(0.85, 0.78, 0.68)
		root.add_child(terrain)
	var veil := ColorRect.new()
	veil.color = Color(INK, shade)
	veil.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	veil.mouse_filter = Control.MOUSE_FILTER_IGNORE
	root.add_child(veil)
	# Vignette: darker toward the edges, so the centre content reads.
	var vignette := TextureRect.new()
	var grad := Gradient.new()
	grad.set_color(0, Color(INK, 0.0))
	grad.set_color(1, Color(INK, 0.75))
	var tex := GradientTexture2D.new()
	tex.gradient = grad
	tex.fill = GradientTexture2D.FILL_RADIAL
	tex.fill_from = Vector2(0.5, 0.5)
	tex.fill_to = Vector2(1.1, 1.1)
	tex.width = 256
	tex.height = 256
	vignette.texture = tex
	vignette.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	vignette.stretch_mode = TextureRect.STRETCH_SCALE
	vignette.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	vignette.mouse_filter = Control.MOUSE_FILTER_IGNORE
	root.add_child(vignette)
	return root

# A thin gold rule with a small diamond in the middle, for under titles.
static func ornament(width: int = 360) -> Control:
	var box := HBoxContainer.new()
	box.alignment = BoxContainer.ALIGNMENT_CENTER
	box.add_theme_constant_override("separation", 10)
	box.mouse_filter = Control.MOUSE_FILTER_IGNORE
	for side in 2:
		var rule := ColorRect.new()
		rule.color = Color(GOLD, 0.6)
		rule.custom_minimum_size = Vector2(width / 2.0 - 16, 1)
		rule.size_flags_vertical = Control.SIZE_SHRINK_CENTER
		rule.mouse_filter = Control.MOUSE_FILTER_IGNORE
		box.add_child(rule)
		if side == 0:
			var gem := Label.new()
			gem.text = "◆"
			gem.add_theme_color_override("font_color", GOLD)
			gem.add_theme_font_size_override("font_size", 14)
			box.add_child(gem)
	return box

static func _font(path: String, weight: int) -> Font:
	var v := FontVariation.new()
	v.base_font = load(path)
	v.variation_opentype = {"wght": weight}
	return v

static func _panel(bg: Color, border: Color, border_px: int, margin: int) -> StyleBoxFlat:
	var s := StyleBoxFlat.new()
	s.bg_color = bg
	s.border_color = border
	s.set_border_width_all(border_px)
	s.set_corner_radius_all(3)
	s.set_content_margin_all(margin)
	s.shadow_color = Color(0, 0, 0, 0.45)
	s.shadow_size = 10
	s.shadow_offset = Vector2(0, 3)
	s.anti_aliasing = true
	return s

static func _button(bg: Color, border: Color) -> StyleBoxFlat:
	var s := StyleBoxFlat.new()
	s.bg_color = bg
	s.border_color = border
	s.set_border_width_all(1)
	s.border_width_bottom = 2
	s.set_corner_radius_all(3)
	s.content_margin_left = 18
	s.content_margin_right = 18
	s.content_margin_top = 9
	s.content_margin_bottom = 9
	return s

static func _label_variation(theme: Theme, type_name: String, font: Font, px: int, color: Color) -> void:
	theme.set_type_variation(type_name, "Label")
	theme.set_font("font", type_name, font)
	theme.set_font_size("font_size", type_name, px)
	theme.set_color("font_color", type_name, color)
