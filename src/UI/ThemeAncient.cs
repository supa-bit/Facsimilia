using Godot;

namespace Facsimilia.UI;

/// <summary>
/// The Ancient era's visual theme (300 BC start): dark bronze panels with
/// gold trim, Cinzel (Roman inscriptional capitals) for headings and
/// Alegreya (a book serif) for body text, gold-tinted game-icons.net icons.
/// Later eras (Medieval, etc.) get their own Build()-style methods alongside
/// this one; nothing here assumes it's the only theme.
///
/// Type variations, set on a control with ThemeTypeVariation:
///   Label:          TitleLabel (huge Cinzel), HeaderLabel (Cinzel heading),
///                   DateLabel (Cinzel, bright gold), SubtleLabel (dim italic), SmallLabel
///   PanelContainer: CardPanel, CardPanelHover, BarPanel (top bar), GlassPanel
///   Button:         BigButton (Cinzel, taller), IconButton (square, compact)
/// </summary>
public static class ThemeAncient
{
    public static readonly Color Gold = new(0.83f, 0.67f, 0.36f);
    public static readonly Color GoldBright = new(0.97f, 0.84f, 0.52f);
    public static readonly Color GoldDim = new(0.55f, 0.43f, 0.24f);
    public static readonly Color Text = new(0.93f, 0.88f, 0.77f);
    public static readonly Color TextDim = new(0.70f, 0.64f, 0.54f);
    public static readonly Color Panel = new(0.10f, 0.08f, 0.06f, 0.94f);
    public static readonly Color Ink = new(0.07f, 0.05f, 0.04f);

    const string HeadingFontPath = "res://assets/fonts/Cinzel.ttf";
    const string BodyFontPath = "res://assets/fonts/Alegreya.ttf";
    const string ItalicFontPath = "res://assets/fonts/Alegreya-Italic.ttf";
    const string TerrainPath = "res://data/terrain_texture.png";

    static Theme? _theme;

    /// <summary>The theme, built once and shared.</summary>
    public static Theme Build() => _theme ??= CreateTheme();

    static Theme CreateTheme()
    {
        var theme = new Theme();
        var body = Font(BodyFontPath, 450);
        var heading = Font(HeadingFontPath, 600);
        var headingBold = Font(HeadingFontPath, 700);
        var italic = Font(ItalicFontPath, 450);
        theme.DefaultFont = body;
        theme.DefaultFontSize = 19;

        // Panels.
        theme.SetStylebox("panel", "PanelContainer", PanelStyle(Panel, GoldDim, 1, 18));
        theme.SetTypeVariation("CardPanel", "PanelContainer");
        theme.SetStylebox("panel", "CardPanel", PanelStyle(new Color(0.12f, 0.095f, 0.07f, 0.95f), GoldDim, 1, 16));
        theme.SetTypeVariation("CardPanelHover", "PanelContainer");
        theme.SetStylebox("panel", "CardPanelHover", PanelStyle(new Color(0.19f, 0.145f, 0.095f, 0.98f), GoldBright, 2, 16));
        theme.SetTypeVariation("BarPanel", "PanelContainer");
        var bar = PanelStyle(new Color(0.08f, 0.065f, 0.05f, 0.95f), GoldDim, 0, 10);
        bar.BorderWidthBottom = 2;
        bar.ContentMarginLeft = 20;
        bar.ContentMarginRight = 20;
        theme.SetStylebox("panel", "BarPanel", bar);
        theme.SetTypeVariation("GlassPanel", "PanelContainer");
        theme.SetStylebox("panel", "GlassPanel", PanelStyle(new Color(0.08f, 0.065f, 0.05f, 0.82f), GoldDim, 1, 14));

        // Labels.
        theme.SetColor("font_color", "Label", Text);
        theme.SetColor("font_shadow_color", "Label", new Color(0, 0, 0, 0.45f));
        theme.SetConstant("shadow_offset_x", "Label", 0);
        theme.SetConstant("shadow_offset_y", "Label", 1);
        LabelVariation(theme, "TitleLabel", headingBold, 96, GoldBright);
        theme.SetConstant("outline_size", "TitleLabel", 10);
        theme.SetColor("font_outline_color", "TitleLabel", new Color(0, 0, 0, 0.55f));
        LabelVariation(theme, "HeaderLabel", heading, 30, GoldBright);
        LabelVariation(theme, "DateLabel", headingBold, 28, GoldBright);
        LabelVariation(theme, "SubtleLabel", italic, 18, TextDim);
        LabelVariation(theme, "SmallLabel", body, 16, TextDim);

        // Buttons.
        var normal = ButtonStyle(new Color(0.20f, 0.15f, 0.10f), GoldDim);
        var hover = ButtonStyle(new Color(0.30f, 0.22f, 0.13f), GoldBright);
        var pressed = ButtonStyle(new Color(0.55f, 0.42f, 0.20f), GoldBright);
        var disabled = ButtonStyle(new Color(0.14f, 0.12f, 0.10f, 0.85f), new Color(0.30f, 0.26f, 0.21f));
        var focus = new StyleBoxFlat
        {
            DrawCenter = false,
            BorderColor = new Color(GoldBright, 0.7f),
        };
        focus.SetBorderWidthAll(1);
        focus.SetCornerRadiusAll(3);
        focus.SetExpandMarginAll(3);
        foreach (string type in new[] { "Button", "OptionButton" })
        {
            theme.SetStylebox("normal", type, normal);
            theme.SetStylebox("hover", type, hover);
            theme.SetStylebox("pressed", type, pressed);
            theme.SetStylebox("hover_pressed", type, pressed);
            theme.SetStylebox("disabled", type, disabled);
            theme.SetStylebox("focus", type, focus);
            theme.SetColor("font_color", type, Text);
            theme.SetColor("font_hover_color", type, new Color(1f, 0.96f, 0.86f));
            theme.SetColor("font_pressed_color", type, Ink);
            theme.SetColor("font_hover_pressed_color", type, Ink);
            theme.SetColor("font_disabled_color", type, new Color(0.45f, 0.41f, 0.35f));
            theme.SetColor("icon_normal_color", type, Gold);
            theme.SetColor("icon_hover_color", type, GoldBright);
            theme.SetColor("icon_pressed_color", type, Ink);
            theme.SetColor("icon_disabled_color", type, new Color(0.40f, 0.36f, 0.30f));
            theme.SetConstant("h_separation", type, 12);
            theme.SetConstant("icon_max_width", type, 26);
        }
        theme.SetFont("font", "Button", heading);
        theme.SetFontSize("font_size", "Button", 19);

        theme.SetTypeVariation("BigButton", "Button");
        foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled" })
        {
            var s = (StyleBoxFlat)theme.GetStylebox(state, "Button").Duplicate();
            s.ContentMarginTop = 14;
            s.ContentMarginBottom = 14;
            s.ContentMarginLeft = 22;
            s.ContentMarginRight = 22;
            theme.SetStylebox(state, "BigButton", s);
        }
        theme.SetFontSize("font_size", "BigButton", 22);
        theme.SetConstant("icon_max_width", "BigButton", 30);

        theme.SetTypeVariation("IconButton", "Button");
        foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled" })
        {
            var s = (StyleBoxFlat)theme.GetStylebox(state, "Button").Duplicate();
            s.SetContentMarginAll(8);
            theme.SetStylebox(state, "IconButton", s);
        }
        theme.SetConstant("icon_max_width", "IconButton", 24);
        theme.SetFontSize("font_size", "IconButton", 24);

        // Tooltips.
        theme.SetStylebox("panel", "TooltipPanel", PanelStyle(new Color(0.07f, 0.055f, 0.04f, 0.97f), Gold, 1, 10));
        theme.SetColor("font_color", "TooltipLabel", Text);
        theme.SetFont("font", "TooltipLabel", body);
        theme.SetFontSize("font_size", "TooltipLabel", 17);

        // Separators, sliders, check boxes.
        var line = new StyleBoxLine { Color = new Color(Gold, 0.45f), Thickness = 1 };
        theme.SetStylebox("separator", "HSeparator", line);
        theme.SetConstant("separation", "HSeparator", 14);
        var slider = new StyleBoxFlat { BgColor = new Color(0.05f, 0.04f, 0.03f), BorderColor = GoldDim };
        slider.SetBorderWidthAll(1);
        slider.SetCornerRadiusAll(3);
        slider.ContentMarginTop = 3;
        slider.ContentMarginBottom = 3;
        theme.SetStylebox("slider", "HSlider", slider);
        var fill = (StyleBoxFlat)slider.Duplicate();
        fill.BgColor = Gold;
        theme.SetStylebox("grabber_area", "HSlider", fill);
        theme.SetStylebox("grabber_area_highlight", "HSlider", fill);
        theme.SetColor("font_color", "CheckBox", Text);
        theme.SetFont("font", "CheckBox", body);
        return theme;
    }

    /// <summary>A game-icons.net icon (white on transparent in assets/icons/), tinted by whatever displays it.</summary>
    public static Texture2D Icon(string name) => GD.Load<Texture2D>($"res://assets/icons/{name}.svg");

    /// <summary>An icon sized for inline use next to text, tinted gold unless told otherwise.</summary>
    public static TextureRect IconRect(string name, int px = 24, Color? tint = null) => new()
    {
        Texture = Icon(name),
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        CustomMinimumSize = new Vector2(px, px),
        Modulate = tint ?? Gold,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    public static Font HeadingFont(int weight = 600) => Font(HeadingFontPath, weight);
    public static Font BodyFont(int weight = 450) => Font(BodyFontPath, weight);

    /// <summary>"300 BC", "AD 14". There's no year 0 (1 BC is followed by AD 1); MapView.AdvanceYear() skips it.</summary>
    public static string YearText(int year) => year < 0 ? $"{-year} BC" : $"AD {year}";

    public static string GroupThousands(long n) => n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// The real terrain map, darkened, as a full-screen backdrop for menus and
    /// loading screens. shade is how dark the veil over it is (0-1).
    /// </summary>
    public static Control Backdrop(float shade = 0.6f)
    {
        var root = Layer(new Control { ClipContents = true });
        root.AddChild(Layer(new ColorRect { Color = Ink }));
        if (ResourceLoader.Exists(TerrainPath))
        {
            root.AddChild(Layer(new TextureRect
            {
                Texture = GD.Load<Texture2D>(TerrainPath),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                Modulate = new Color(0.85f, 0.78f, 0.68f),
            }));
        }
        root.AddChild(Layer(new ColorRect { Color = new Color(Ink, shade) }));
        // Vignette: darker toward the edges, so the centre content reads.
        var gradient = new Gradient();
        gradient.SetColor(0, new Color(Ink, 0f));
        gradient.SetColor(1, new Color(Ink, 0.75f));
        root.AddChild(Layer(new TextureRect
        {
            Texture = new GradientTexture2D
            {
                Gradient = gradient,
                Fill = GradientTexture2D.FillEnum.Radial,
                FillFrom = new Vector2(0.5f, 0.5f),
                FillTo = new Vector2(1.1f, 1.1f),
                Width = 256,
                Height = 256,
            },
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
        }));
        return root;
    }

    /// <summary>A thin gold rule with a small diamond in the middle, for under titles.</summary>
    public static HBoxContainer Ornament(int width = 360)
    {
        var box = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 10);
        for (int side = 0; side < 2; side++)
        {
            box.AddChild(new ColorRect
            {
                Color = new Color(Gold, 0.6f),
                CustomMinimumSize = new Vector2(width / 2f - 16, 2),  // 2px: 1px vanishes when the window is smaller than 1920x1080
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
            if (side == 0)
            {
                var gem = new Label { Text = "◆" };
                gem.AddThemeColorOverride("font_color", Gold);
                gem.AddThemeFontSizeOverride("font_size", 14);
                box.AddChild(gem);
            }
        }
        return box;
    }

    /// <summary>Anchors and sizes a control to fill its parent; returns it for chaining.</summary>
    public static T FullRect<T>(T control) where T : Control
    {
        control.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return control;
    }

    /// <summary>A full-parent decorative layer that lets clicks through.</summary>
    static T Layer<T>(T control) where T : Control
    {
        control.MouseFilter = Control.MouseFilterEnum.Ignore;
        return FullRect(control);
    }

    /// <summary>A label with the given text and type variation (and optional size override).</summary>
    public static Label Label(string text, string variation = "", int fontSize = 0,
        HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var label = new Label { Text = text, ThemeTypeVariation = variation, HorizontalAlignment = align };
        if (fontSize > 0)
            label.AddThemeFontSizeOverride("font_size", fontSize);
        return label;
    }

    static Font Font(string path, int weight) => new FontVariation
    {
        BaseFont = GD.Load<FontFile>(path),
        VariationOpentype = new Godot.Collections.Dictionary { ["wght"] = weight },
    };

    static StyleBoxFlat PanelStyle(Color bg, Color border, int borderPx, int margin)
    {
        var s = new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border,
            ShadowColor = new Color(0, 0, 0, 0.45f),
            ShadowSize = 10,
            ShadowOffset = new Vector2(0, 3),
            AntiAliasing = true,
        };
        s.SetBorderWidthAll(borderPx);
        s.SetCornerRadiusAll(3);
        s.SetContentMarginAll(margin);
        return s;
    }

    static StyleBoxFlat ButtonStyle(Color bg, Color border)
    {
        var s = new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border,
            ContentMarginLeft = 18,
            ContentMarginRight = 18,
            ContentMarginTop = 9,
            ContentMarginBottom = 9,
        };
        s.SetBorderWidthAll(1);
        s.BorderWidthBottom = 2;
        s.SetCornerRadiusAll(3);
        return s;
    }

    static void LabelVariation(Theme theme, string name, Font font, int px, Color color)
    {
        theme.SetTypeVariation(name, "Label");
        theme.SetFont("font", name, font);
        theme.SetFontSize("font_size", name, px);
        theme.SetColor("font_color", name, color);
    }
}
