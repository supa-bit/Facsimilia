using System.Collections.Generic;
using Godot;

namespace Facsimilia.UI;

/// <summary>
/// Floating panels: every panel can be dragged by any empty spot (its title,
/// its text) to anywhere on the screen, comes to the front when clicked, and
/// keeps its place between games (saved in user://panels.cfg).
/// </summary>
public partial class Hud
{
    const string PanelPlacesPath = "user://panels.cfg";
    ConfigFile? _panelPlaces;
    PanelContainer? _dragging;
    Vector2 _dragGrab;

    /// <summary>Makes a panel float: draggable, raised when clicked, remembered.</summary>
    void Float(PanelContainer panel, string key)
    {
        if (_panelPlaces == null)
        {
            _panelPlaces = new ConfigFile();
            _panelPlaces.Load(PanelPlacesPath);   // missing on first run: every panel starts where it was built
        }
        panel.MouseDefaultCursorShape = CursorShape.Move;
        if (_panelPlaces.GetValue("panels", key, Variant.From(Vector2.Inf)).AsVector2() is { } saved && saved.IsFinite())
            CallDeferred(nameof(PlacePanel), panel, saved);
        panel.GuiInput += e =>
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
            {
                if (mb.Pressed)
                {
                    panel.MoveToFront();
                    ToTopLeft(panel);
                    _dragging = panel;
                    _dragGrab = mb.GlobalPosition - panel.GlobalPosition;
                }
                else if (_dragging == panel)
                {
                    _dragging = null;
                    _panelPlaces.SetValue("panels", key, panel.Position);
                    _panelPlaces.Save(PanelPlacesPath);
                }
                panel.AcceptEvent();
            }
            else if (e is InputEventMouseMotion motion && _dragging == panel)
            {
                panel.Position = Clamped(panel, motion.GlobalPosition - _dragGrab);
                panel.AcceptEvent();
            }
        };
    }

    void PlacePanel(PanelContainer panel, Vector2 at)
    {
        ToTopLeft(panel);
        panel.Position = Clamped(panel, at);
    }

    /// <summary>Anchors a panel by its top-left corner, keeping it where it is on screen.</summary>
    static void ToTopLeft(Control panel)
    {
        var at = panel.Position;
        panel.SetAnchorsPreset(LayoutPreset.TopLeft, keepOffsets: true);
        panel.GrowHorizontal = GrowDirection.End;
        panel.GrowVertical = GrowDirection.End;
        panel.Position = at;
    }

    /// <summary>Keeps at least a grip of the panel on screen, below the top bar.</summary>
    Vector2 Clamped(Control panel, Vector2 at)
    {
        var screen = GetViewportRect().Size;
        const float grip = 80;
        return new Vector2(
            Mathf.Clamp(at.X, grip - panel.Size.X, screen.X - grip),
            Mathf.Clamp(at.Y, 64, screen.Y - 40));
    }
}
