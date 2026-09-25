using Godot;

namespace Facsimilia.UI;

/// <summary>
/// F3 toggles a diagnostic overlay: a gold crosshair where the game thinks
/// the mouse is, plus the window, screen and viewport sizes. If the crosshair
/// isn't exactly under the real pointer, clicks are landing offset. Added by
/// the SettingsStore autoload so it works on every screen.
/// </summary>
public partial class MouseDebugOverlay : CanvasLayer
{
    Control _canvas = null!;
    Label _info = null!;

    public override void _Ready()
    {
        Layer = 128;  // above everything
        Visible = false;
        _canvas = ThemeAncient.FullRect(new Control { MouseFilter = Control.MouseFilterEnum.Ignore });
        _canvas.Draw += DrawCrosshair;
        AddChild(_canvas);
        _info = new Label { Position = new Vector2(12, 70), MouseFilter = Control.MouseFilterEnum.Ignore };
        _info.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.4f));
        _info.AddThemeColorOverride("font_outline_color", Colors.Black);
        _info.AddThemeConstantOverride("outline_size", 6);
        _info.AddThemeFontSizeOverride("font_size", 18);
        _canvas.AddChild(_info);
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F3 })
            Visible = !Visible;
        if (Visible && @event is InputEventMouse)
            Refresh();
    }

    void Refresh()
    {
        var mouse = _canvas.GetGlobalMousePosition();
        int screen = DisplayServer.WindowGetCurrentScreen();
        _info.Text =
            $"Mouse (game): {mouse.X:F0}, {mouse.Y:F0}\n" +
            $"Window: {DisplayServer.WindowGetSize()} at {DisplayServer.WindowGetPosition()}, mode {DisplayServer.WindowGetMode()}\n" +
            $"Screen usable area: {DisplayServer.ScreenGetUsableRect(screen)}, scale {DisplayServer.ScreenGetScale(screen)}\n" +
            $"Viewport: {GetViewport().GetVisibleRect().Size}   Embedded in editor: {Engine.IsEmbeddedInEditor()}\n" +
            "The gold crosshair should sit exactly under your mouse pointer. (F3 to hide)";
        _canvas.QueueRedraw();
    }

    void DrawCrosshair()
    {
        var p = _canvas.GetLocalMousePosition();
        var gold = new Color(1f, 0.8f, 0.3f);
        _canvas.DrawLine(p + new Vector2(-18, 0), p + new Vector2(18, 0), gold, 2);
        _canvas.DrawLine(p + new Vector2(0, -18), p + new Vector2(0, 18), gold, 2);
        _canvas.DrawArc(p, 8, 0, Mathf.Tau, 24, gold, 2);
    }
}
