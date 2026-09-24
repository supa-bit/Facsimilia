using System;
using System.Threading.Tasks;
using Facsimilia.UI;
using Godot;

namespace Facsimilia.World;

/// <summary>
/// Main.tscn's root: runs a game session. New game: title card, realm
/// selection, world generation (behind a loading screen), then the HUD.
/// Continue / Load Game (SaveSystem.PendingLoadSlot): load that save instead.
/// Escape opens the pause menu, which saves to a slot; every
/// SaveSystem.AutosaveEveryYears years the game autosaves.
/// </summary>
public partial class GameRoot : Node2D
{
    static readonly PackedScene CivSelectScene = GD.Load<PackedScene>("res://scenes/CivSelect.tscn");
    static readonly PackedScene HudScene = GD.Load<PackedScene>("res://scenes/Hud.tscn");
    static readonly PackedScene PauseMenuScene = GD.Load<PackedScene>("res://scenes/PauseMenu.tscn");

    const double IntroMinSeconds = 2.5;    // title card before realm selection
    const double RegionMinSeconds = 1.5;   // after picking a realm; generation itself takes longer
    const double ContinueMinSeconds = 1.0; // while a save loads (it still rebuilds the map image)
    const double FadeSeconds = 0.6;

    public MapView? Map { get; private set; }
    public Hud? Hud { get; private set; }
    public CivSelect? CivSelect { get; private set; }
    public PauseMenu? PauseMenu { get; private set; }
    /// <summary>The manual slot this game was loaded from or last saved to; null for a new game.</summary>
    public string? CurrentSlot { get; private set; }
    bool _saving;
    CanvasLayer _uiLayer = null!;  // keeps UI in screen space, unaffected by the map camera
    Control? _loadingScreen;
    Label? _loadingLabel;

    public override async void _Ready()
    {
        _uiLayer = new CanvasLayer { Layer = 1 };
        AddChild(_uiLayer);
        if (SaveSystem.PendingLoadSlot is { } slot)
        {
            SaveSystem.PendingLoadSlot = null;
            if (await StartContinuedGame(slot))
                return;
            // No save, or it couldn't be read: start fresh rather than a dead screen.
        }
        await StartNewGame();
    }

    async Task StartNewGame()
    {
        await RunLoadingScreen("FACSIMILIA", "The known world, 300 BC", IntroMinSeconds, null);
        CivSelect = CivSelectScene.Instantiate<CivSelect>();
        CivSelect.Theme = ThemeAncient.Build();
        _uiLayer.AddChild(CivSelect);
        CivSelect.CivChosen += OnCivChosen;
    }

    async Task<bool> StartContinuedGame(string slot)
    {
        Map = NewMap();
        bool loaded = false;
        await RunLoadingScreen("FACSIMILIA", "Loading your realm...", ContinueMinSeconds,
            async () => loaded = await Map.LoadSavedGame(slot));
        if (!loaded)
        {
            Map.QueueFree();
            Map = null;
            return false;
        }
        CurrentSlot = slot == SaveSystem.AutosaveSlot ? null : slot;
        ShowHud();
        return true;
    }

    async void OnCivChosen(string civKey)
    {
        CivSelect!.QueueFree();
        CivSelect = null;
        Map = NewMap();
        Map.SetPlayerCiv(civKey);
        await RunLoadingScreen("300 BC", "Preparing the ancient world...", RegionMinSeconds, Map.GenerateWorld);
        ShowHud();
    }

    MapView NewMap()
    {
        var map = new MapView { Name = "MapView" };
        AddChild(map);
        map.LoadingStatusChanged += text =>
        {
            if (_loadingLabel != null)
                _loadingLabel.Text = text;
        };
        return map;
    }

    void ShowHud()
    {
        Hud = HudScene.Instantiate<Hud>();
        Hud.Theme = ThemeAncient.Build();
        _uiLayer.AddChild(Hud);
        Hud.Setup(Map!);
        Hud.Refresh();
        Hud.AdvanceRequested += OnAdvanceRequested;
    }

    /// <summary>
    /// A loading screen over the terrain backdrop: fades in, awaits work (if
    /// any), holds for at least minSeconds in total, fades out. It blocks clicks
    /// while up; generation yields frames, so the window stays responsive.
    /// </summary>
    async Task RunLoadingScreen(string title, string status, double minSeconds, Func<Task>? work)
    {
        _loadingScreen = ThemeAncient.FullRect(new Control
        {
            MouseFilter = Control.MouseFilterEnum.Stop,
            Theme = ThemeAncient.Build(),
            Modulate = new Color(1, 1, 1, 0),
        });
        _loadingScreen.AddChild(ThemeAncient.Backdrop(0.62f));
        var center = ThemeAncient.FullRect(new CenterContainer());
        _loadingScreen.AddChild(center);
        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 16);
        center.AddChild(vbox);
        vbox.AddChild(ThemeAncient.Label(title, "TitleLabel", align: HorizontalAlignment.Center));
        vbox.AddChild(ThemeAncient.Ornament(420));
        _loadingLabel = ThemeAncient.Label(status, "SubtleLabel", 24, HorizontalAlignment.Center);
        vbox.AddChild(_loadingLabel);
        _uiLayer.AddChild(_loadingScreen);

        await Fade(_loadingScreen, 1f);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        if (work != null)
            await work();
        double remaining = minSeconds - clock.Elapsed.TotalSeconds;
        if (remaining > 0)
            await ToSignal(GetTree().CreateTimer(remaining), SceneTreeTimer.SignalName.Timeout);
        await Fade(_loadingScreen, 0f);
        _loadingScreen.QueueFree();
        _loadingScreen = null;
        _loadingLabel = null;
    }

    async Task Fade(Control control, float alpha)
    {
        var tween = CreateTween();
        tween.TweenProperty(control, "modulate:a", alpha, FadeSeconds);
        await ToSignal(tween, Tween.SignalName.Finished);
    }

    async void OnAdvanceRequested()
    {
        if (PauseMenu != null || _loadingScreen != null || _saving || Map == null || Hud == null)
            return;
        var events = Map.AdvanceYear();
        Hud.Refresh();
        Hud.LogEvents(events);
        if (Math.Abs(Map.DemoYear - MapView.StartYear) % SaveSystem.AutosaveEveryYears == 0)
            await Autosave();
    }

    /// <summary>Every few years, into the autosave slot (never over the player's own slots).</summary>
    async Task Autosave()
    {
        _saving = true;
        Hud!.ShowStatus("Autosaving...");
        bool ok = await Map!.SaveCurrentGame(SaveSystem.AutosaveSlot);
        Hud.ShowStatus(ok ? "Autosaved." : "Autosave failed.", fadeAfter: 2.5);
        _saving = false;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true } key)
            return;
        if (key.Keycode == Key.F11)
            SettingsStore.Instance.SetFullscreen(!SettingsStore.Instance.Fullscreen);
        else if (key.Keycode == Key.Escape)
            TogglePauseMenu();
    }

    public void TogglePauseMenu()
    {
        if (PauseMenu != null || Hud == null || _loadingScreen != null)
            return;  // already open (its Resume closes it), or nothing to pause yet
        PauseMenu = PauseMenuScene.Instantiate<PauseMenu>();
        PauseMenu.Theme = ThemeAncient.Build();
        _uiLayer.AddChild(PauseMenu);
        PauseMenu.ResumeRequested += () =>
        {
            PauseMenu.QueueFree();
            PauseMenu = null;
        };
        PauseMenu.CurrentSlot = CurrentSlot;
        PauseMenu.SaveRequested += async (slot, then) =>
        {
            if (!await SaveFromPauseMenu(slot))
                return;
            if (then == PauseMenu.ThenMainMenu)
                GetTree().ChangeSceneToFile("res://scenes/MainMenu.tscn");
            else if (then == PauseMenu.ThenQuit)
                GetTree().Quit();
            else
            {
                PauseMenu.ShowMessage($"Saved to {SaveSystem.SlotName(slot)}.");
                PauseMenu.SetBusy(false);
            }
        };
        PauseMenu.ReturnToMenuWithoutSavingRequested += () => GetTree().ChangeSceneToFile("res://scenes/MainMenu.tscn");
        PauseMenu.QuitWithoutSavingRequested += () => GetTree().Quit();
    }

    async Task<bool> SaveFromPauseMenu(string slot)
    {
        PauseMenu!.SetBusy(true);
        PauseMenu.ShowMessage("Saving...");
        if (await Map!.SaveCurrentGame(slot))
        {
            CurrentSlot = slot;
            PauseMenu.CurrentSlot = slot;
            return true;
        }
        PauseMenu.ShowMessage("Save failed - try again.");
        PauseMenu.SetBusy(false);
        return false;
    }
}
