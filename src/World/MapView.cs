using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Facsimilia.Dynasties;
using Facsimilia.UI;
using Godot;

namespace Facsimilia.World;

/// <summary>A playable civilization at the 300 BC start.</summary>
/// <param name="Culture">Naming tradition for children born in play, spouses and new houses.</param>
/// <param name="Family">The ruling family in 300 BC.</param>
/// <param name="RealmName">Display name; for real civs it comes from data/ancient_bc300.json instead.</param>
/// <param name="Polygon">Frontier zones only: their hand-placed area on the grid.</param>
/// <param name="CapitalLonLat">Real civs only: the 300 BC seat, for the population engine.</param>
public sealed record CivSpec(string Key, Culture Culture, StartFamily Family, Color Color,
    SuccessionLaw Law, string? RealmName = null, Vector2[]? Polygon = null, Vector2? CapitalLonLat = null);

/// <summary>
/// A realm's ruling family at the start: the ruler and spouse with birth
/// years (negative = BC), their children, and optionally the children's
/// own spouses and children. House defaults to "House " + the ruler's name.
/// </summary>
public sealed record StartFamily(string Ruler, int RulerBorn, string Spouse, int SpouseBorn, Kin[] Children,
    string? House = null);

/// <summary>A child in a StartFamily, with their own spouse and children if they have any.</summary>
public sealed record Kin(string Name, bool Male, int Born, string? Spouse = null, int SpouseBorn = 0, Kin[]? Children = null);

/// <summary>
/// The world map: an 8192x5476 ownership grid (~0.59 km² per cell) drawn as
/// one texture under a border/terrain shader, with a camera, realm names,
/// proposal painting, and the population engine. World generation seeds the
/// real coastline, the reconciled 300 BC political mask, and the tribal
/// frontier zones; it's split into chunks that yield a frame so the window
/// stays responsive while it runs.
/// </summary>
public partial class MapView : Node2D
{
    [Signal] public delegate void LoadingStatusChangedEventHandler(string text);

    public const int GridWidth = 8192;
    public const int GridHeight = 5476;  // ~0.59 km^2/cell over the map's real-world extent
    public const int CellPixels = 1;     // grid IS the texture resolution; the camera handles zoom
    public const int SeaOwnerId = 255;   // reserved sentinel in the same ownership grid - see IsSea()
    public const int StartYear = -300;   // 300 BC
    public const int PaintRadius = 12;
    public const float MaxZoom = 16f;
    public const float ZoomStep = 1.2f;       // per mouse-wheel notch / zoom button press
    public const float KeyPanSpeed = 900f;    // screen pixels per second, arrow keys / WASD
    public const float HudTopMargin = 64f;    // the HUD's top bar covers this much of the screen
    public const float FitPadding = 14f;      // screen pixels around the whole-map view, so its frame shows
    public const double LonMin = -10, LonMax = 55, LatMin = 10, LatMax = 48;  // shared with tools/ and the HYDE import

    public static readonly Color WildColor = new(0.12f, 0.12f, 0.12f);  // unclaimed land
    public static readonly Color SeaColor = new(0.08f, 0.16f, 0.24f);
    static readonly Color BeyondMapColor = new(0.06f, 0.05f, 0.04f);  // "edge of the known world" around the map
    static readonly Color FrameColor = new(0.83f, 0.67f, 0.36f, 0.8f);

    const string DataPath = "res://data/ancient_bc300.json";
    const string LandMaskPath = "res://data/land_mask.png";
    const string PoliticalMaskPath = "res://data/political_mask.png";
    const string TerrainTexturePath = "res://data/terrain_texture.png";
    const int ChunkCells = 2_000_000;  // cells processed between yields during generation

    /// <summary>
    /// Real 300 BC political boundaries (Roman Republic, Carthage, the Ptolemaic
    /// Kingdom, Meroe, the Seleucid Kingdom, Kassander's Macedon and the Greek
    /// city-states, Lysimachus, Antigonus, Nabataea), from aourednik/historical-
    /// basemaps world_bc300.geojson, pre-projected by tools/import_bc300.js and
    /// baked into data/political_mask.png by tools/reconcile_map.py. Region
    /// codes 1..9 in the mask follow this order.
    /// </summary>
    public static readonly CivSpec[] RealCivs =
    {
        // The Republic had no king; the "ruling family" stands for its leading
        // house, the Valerii - Marcus Valerius Corvus, six times consul, who by
        // tradition lived to 100.
        new("rome", Culture.Latin, new("Marcus", -371, "Claudia", -352, new Kin[]
            { new("Marcus", true, -332), new("Gaius", true, -326), new("Valeria", false, -322) }, "House Valerius"),
            new Color(0.75f, 0.20f, 0.20f), SuccessionLaw.MalePreferencePrimogeniture, CapitalLonLat: new Vector2(12.48f, 41.89f)),
        new("carthage", Culture.Punic, new("Hasdrubal", -348, "Arishat", -340, new Kin[]
            { new("Hamilcar", true, -318), new("Hanno", true, -315), new("Elissa", false, -312) }),
            new Color(0.55f, 0.30f, 0.65f), SuccessionLaw.MalePreferencePrimogeniture, CapitalLonLat: new Vector2(10.32f, 36.85f)),
        new("egypt", Culture.Greek, new("Ptolemaios", -367, "Berenike", -340, new Kin[]
            { new("Arsinoe", false, -316), new("Philotera", false, -315), new("Ptolemaios", true, -308) }),
            new Color(0.85f, 0.75f, 0.15f), SuccessionLaw.MalePreferencePrimogeniture, CapitalLonLat: new Vector2(29.92f, 31.2f)),
        // The Kushite royal line passed through queens as well as kings.
        new("kush", Culture.Meroitic, new("Arkamani", -340, "Nahirqo", -335, new Kin[]
            { new("Shanakdakhete", false, -314), new("Amanislo", true, -312) }),
            new Color(0.55f, 0.25f, 0.15f), SuccessionLaw.Primogeniture, CapitalLonLat: new Vector2(33.75f, 16.94f)),
        new("seleucid", Culture.Greek, new("Seleukos", -358, "Apama", -345, new Kin[]
            { new("Antiochos", true, -324), new("Apama", false, -320), new("Achaios", true, -318) }),
            new Color(0.35f, 0.25f, 0.65f), SuccessionLaw.MalePreferencePrimogeniture, CapitalLonLat: new Vector2(44.52f, 33.1f)),
        new("greek_world", Culture.Greek, new("Kassandros", -355, "Thessalonike", -345, new Kin[]
            { new("Philippos", true, -315), new("Antipatros", true, -314), new("Alexandros", true, -313) }),
            new Color(0.20f, 0.40f, 0.75f), SuccessionLaw.MalePreferencePrimogeniture, CapitalLonLat: new Vector2(22.52f, 40.76f)),
        new("lysimachus", Culture.Greek, new("Lysimachos", -360, "Nikaia", -340, new Kin[]
            { new("Agathokles", true, -320), new("Eurydike", false, -318) }),
            new Color(0.75f, 0.35f, 0.55f), SuccessionLaw.MalePreferencePrimogeniture, CapitalLonLat: new Vector2(26.75f, 40.52f)),
        // Antigonos the One-Eyed, 82: his son Demetrios and grandson
        // Antigonos Gonatas are already grown.
        new("antigonus", Culture.Greek, new("Antigonos", -382, "Stratonike", -370, new Kin[]
            {
                new("Demetrios", true, -337, "Phila", -350, new Kin[]
                    { new("Antigonos", true, -319), new("Stratonike", false, -317) }),
            }),
            new Color(0.80f, 0.45f, 0.15f), SuccessionLaw.MalePreferencePrimogeniture, CapitalLonLat: new Vector2(36.2f, 36.23f)),
        // Nabataean queens ruled beside their husbands and appear on the coins.
        new("nabatea", Culture.Nabataean, new("Aretas", -345, "Huldu", -340, new Kin[]
            { new("Obodas", true, -320), new("Shaqilat", false, -317), new("Rabbel", true, -313) }),
            new Color(0.70f, 0.55f, 0.30f), SuccessionLaw.Primogeniture, CapitalLonLat: new Vector2(35.44f, 30.33f)),
    };

    /// <summary>
    /// Tribal/cultural frontier zones - deliberately NOT from political-boundary
    /// data, because Gaul, Iberia and the Pontic steppe really weren't unified
    /// states in 300 BC. Hand-placed grid polygons; each fills only cells still
    /// unclaimed after the real civs are placed.
    /// </summary>
    public static readonly CivSpec[] FrontierZones =
    {
        new("iberia", Culture.Iberian, new("Indibilis", -340, "Ilduria", -335, new Kin[]
            { new("Mandonios", true, -318), new("Imilce", false, -315) }),
            new Color(0.20f, 0.55f, 0.55f), SuccessionLaw.Primogeniture,
            "Iberian & Celtiberian Tribes", new Vector2[] { new(0, 304), new(1536, 203), new(1621, 1115), new(1195, 1825), new(341, 1724), new(0, 1217) }),
        new("gaul", Culture.Celtic, new("Brennos", -338, "Onomaris", -335, new Kin[]
            { new("Bolgios", true, -316), new("Chiomara", false, -312) }),
            new Color(0.25f, 0.65f, 0.30f), SuccessionLaw.Primogeniture,
            "Gallic Tribes", new Vector2[] { new(939, 0), new(3669, 0), new(3840, 1014), new(2560, 1176), new(1451, 1055), new(939, 710) }),
        // Agaros, a Scythian king named in Diodorus for 309 BC.
        new("scythia", Culture.Scythian, new("Agaros", -350, "Opia", -340, new Kin[]
            { new("Kanitos", true, -322), new("Saulios", true, -318), new("Amage", false, -315) }),
            new Color(0.35f, 0.65f, 0.75f), SuccessionLaw.MalePreferencePrimogeniture,
            "Scythian Peoples", new Vector2[] { new(4949, 0), new(8021, 0), new(8107, 811), new(6827, 1115), new(5461, 913), new(4949, 507) }),
    };

    /// <summary>Map color of any playable civ, by key.</summary>
    public static Color CivColor(string key) =>
        RealCivs.Concat(FrontierZones).FirstOrDefault(c => c.Key == key)?.Color ?? WildColor;

    public OwnershipGrid Grid { get; set; } = new(GridWidth, GridHeight);
    public CharacterRegistry Registry { get; set; } = new();
    public int PlayerRealmId { get; set; }
    public int DemoYear { get; set; } = StartYear;
    public Dictionary<string, int> CivRealmIds { get; } = new();  // civ key -> realm id
    /// <summary>The province of every cell; null only until world generation or loading has run.</summary>
    public ProvinceMap? Provinces { get; private set; }
    /// <summary>Null when the baked HYDE keyframes aren't present; the game then runs without population.</summary>
    public PopulationEngine? Population { get; private set; }

    string _pendingPlayerCiv = "";
    int[] _proposal = new int[GridWidth * GridHeight];
    readonly HashSet<int> _dirtyProposalCells = new();
    internal Image? MapImage { get; private set; }
    ImageTexture? _mapTexture;
    internal Sprite2D? MapSprite { get; set; }
    Camera2D? _camera;
    float _fitZoom;  // "whole map fills the window": the most zoomed-out view; 0 until first fitted
    CanvasLayer? _labelLayer;
    readonly List<(Label Label, Vector2 World, int Cells)> _mapLabels = new();  // biggest realm first
    Dictionary<int, int> _realmCells = new();  // realm id -> land cells, sizes the realm names
    bool _panning;

    public override void _Ready()
    {
        // Only fast setup lives here. The expensive world generation (seeding,
        // the display image, label positions - tens of millions of cells) is
        // GenerateWorld(), which GameRoot awaits after adding this node.
        MapSprite = new Sprite2D
        {
            Centered = false,
            Scale = new Vector2(CellPixels, CellPixels),
            TextureFilter = TextureFilterEnum.Nearest,
            Material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/border_outline.gdshader") },
        };
        var material = (ShaderMaterial)MapSprite.Material;
        material.SetShaderParameter("texel_size", new Vector2(1f / GridWidth, 1f / GridHeight));
        material.SetShaderParameter("terrain_texture", GD.Load<Texture2D>(TerrainTexturePath));
        AddChild(MapSprite);

        _camera = new Camera2D { Position = MapCenter };
        AddChild(_camera);
        _camera.MakeCurrent();
        GetViewport().SizeChanged += FitCameraToWindow;
        FitCameraToWindow();
    }

    public async Task GenerateWorld()
    {
        EmitSignal(SignalName.LoadingStatusChanged, "Charting the coastline...");
        await SeedLandAndSea();
        EmitSignal(SignalName.LoadingStatusChanged, "Mapping the ancient world...");
        await SeedRealCivs();
        EmitSignal(SignalName.LoadingStatusChanged, "Settling the frontier tribes...");
        await SeedFrontierZones();
        if (CivRealmIds.TryGetValue(_pendingPlayerCiv, out int chosen))
            PlayerRealmId = chosen;
        EmitSignal(SignalName.LoadingStatusChanged, "Drawing the provinces...");
        await GenerateProvinces();
        EmitSignal(SignalName.LoadingStatusChanged, "Counting the people...");
        StartPopulation();
        EmitSignal(SignalName.LoadingStatusChanged, "Drawing the map...");
        await BuildFullMapImage();
        EmitSignal(SignalName.LoadingStatusChanged, "Naming the realms...");
        BuildLabels(await ComputeCentroids());
        GD.Print($"Facsimilia map view ready: {GridWidth}x{GridHeight} cells, year {DemoYear}.");
    }

    public Task<bool> SaveCurrentGame(string slot) =>
        SaveSystem.SaveGame(slot, Grid, Registry, DemoYear, PlayerRealmId, this, Population?.ToDict(), Provinces);

    /// <summary>
    /// Restores a saved world instead of generating one: skips seeding and
    /// re-runs only the rendering tail GenerateWorld() ends with. Returns false
    /// (leaving this MapView unchanged) if there's no usable save.
    /// </summary>
    public async Task<bool> LoadSavedGame(string slot)
    {
        var loaded = SaveSystem.LoadGame(slot);
        if (loaded == null)
            return false;
        if (loaded.Grid.Width != GridWidth || loaded.Grid.Height != GridHeight)
        {
            // Rendering is set up for this build's grid size in _Ready(); a save
            // from a different size can't be reconciled with that.
            GD.PushError($"SaveSystem: saved grid is {loaded.Grid.Width}x{loaded.Grid.Height}, this build expects {GridWidth}x{GridHeight}");
            return false;
        }
        Grid = loaded.Grid;
        Registry = loaded.Registry;
        Provinces = loaded.Provinces;
        if (Provinces == null)
        {
            EmitSignal(SignalName.LoadingStatusChanged, "Drawing the provinces...");
            await GenerateProvinces();  // a save from before provinces
        }
        DemoYear = loaded.DemoYear;
        PlayerRealmId = loaded.PlayerRealmId;
        Population = null;
        if (loaded.Population.Count > 0)
        {
            var engine = new PopulationEngine();
            if (engine.LoadHyde() && engine.LoadFromDict(loaded.Population))
            {
                Population = engine;
                SyncPopulationOwnership();
            }
        }
        else if (PopulationEngine.HydeAvailable())
        {
            StartPopulation(DemoYear);  // save from before the population engine existed
        }

        EmitSignal(SignalName.LoadingStatusChanged, "Drawing the map...");
        await BuildFullMapImage();
        EmitSignal(SignalName.LoadingStatusChanged, "Naming the realms...");
        BuildLabels(await ComputeCentroids());
        return true;
    }

    // --- Camera -----------------------------------------------------------------

    static Vector2 MapSize => new(GridWidth * CellPixels, GridHeight * CellPixels);
    static Vector2 MapCenter => MapSize / 2f;

    void FitCameraToWindow()
    {
        if (_camera == null)
            return;
        var viewport = GetViewportRect().Size;
        if (viewport.X <= 0 || viewport.Y <= 0)
            return;
        // Camera2D.Zoom is screen pixels per map pixel. The most-zoomed-out view
        // shows the WHOLE map (the smaller of the two ratios) in the space below
        // the HUD's top bar; spare room on the other axis shows the dark
        // surround, never a crop of the map.
        bool firstFit = _fitZoom == 0f;
        _fitZoom = Math.Min((viewport.X - 2 * FitPadding) / MapSize.X,
            (viewport.Y - HudTopMargin - 2 * FitPadding) / MapSize.Y);
        // First time: show the whole map. On later resizes, keep the player's zoom.
        float z = firstFit ? _fitZoom : Math.Clamp(_camera.Zoom.X, _fitZoom, MaxZoom);
        _camera.Zoom = new Vector2(z, z);
        ClampCamera();
    }

    /// <summary>
    /// Keeps the map in view below the top bar: on an axis where the map is
    /// smaller than the screen it's centred; otherwise its edges can't be
    /// panned past. Then redraws the frame and re-places the realm names.
    /// </summary>
    void ClampCamera()
    {
        var screen = GetViewportRect().Size;
        float z = _camera!.Zoom.X;
        _camera.Position = new Vector2(
            ClampAxis(_camera.Position.X, 0f, screen.X, screen.X / 2f, MapSize.X, z),
            ClampAxis(_camera.Position.Y, HudTopMargin, screen.Y, screen.Y / 2f, MapSize.Y, z));
        QueueRedraw();
        UpdateLabels();
        UpdateProvinceStrength();
    }

    /// <summary>
    /// Camera position on one axis. [from, to] is the usable screen span,
    /// center the screen's midpoint, map the map's length, zoom screen px per map px.
    /// </summary>
    static float ClampAxis(float position, float from, float to, float center, float map, float zoom)
    {
        float lowest = -(from - center) / zoom;       // usable edge at map 0
        float highest = map - (to - center) / zoom;   // other usable edge at the map's end
        if (lowest >= highest)                        // the whole map fits: centre it
            return map / 2f - ((from + to) / 2f - center) / zoom;
        return Math.Clamp(position, lowest, highest);
    }

    /// <summary>The dark surround beyond the map's edges and a thin gold frame (drawn under the map).</summary>
    public override void _Draw()
    {
        var map = new Rect2(Vector2.Zero, MapSize);
        DrawRect(map.Grow(Math.Max(MapSize.X, MapSize.Y) * 4f), BeyondMapColor);
        float px = 1f / (_camera?.Zoom.X ?? 1f);  // one screen pixel, in map units
        DrawRect(map.Grow(3f * px), FrameColor, filled: false, width: 2f * px);
    }

    Vector2 ScreenToWorld(Vector2 screen) =>
        _camera!.Position + (screen - GetViewportRect().Size / 2f) / _camera.Zoom.X;

    /// <summary>
    /// Zooms by factor keeping the map point under screenAnchor fixed on screen
    /// (the mouse cursor for the wheel, the screen centre otherwise).
    /// </summary>
    internal void ZoomBy(float factor, Vector2? screenAnchor = null)
    {
        if (_camera == null)
            return;
        var anchor = screenAnchor ?? GetViewportRect().Size / 2f;
        var before = ScreenToWorld(anchor);
        float z = Math.Clamp(_camera.Zoom.X * factor, _fitZoom, MaxZoom);
        _camera.Zoom = new Vector2(z, z);
        _camera.Position += before - ScreenToWorld(anchor);
        ClampCamera();
    }

    public void ZoomIn() => ZoomBy(ZoomStep);
    public void ZoomOut() => ZoomBy(1f / ZoomStep);

    /// <summary>Back to the whole-map view, centred.</summary>
    public void ZoomToFit()
    {
        if (_camera == null)
            return;
        _camera.Position = MapCenter;
        _camera.Zoom = new Vector2(_fitZoom, _fitZoom);
        ClampCamera();
    }

    /// <summary>Arrow keys / WASD pan.</summary>
    public override void _Process(double delta)
    {
        UpdateBrushCursor();
        if (_camera == null || GetViewport().GuiGetFocusOwner() is LineEdit)
            return;
        var dir = Vector2.Zero;
        if (Input.IsKeyPressed(Key.Left) || Input.IsKeyPressed(Key.A)) dir.X -= 1;
        if (Input.IsKeyPressed(Key.Right) || Input.IsKeyPressed(Key.D)) dir.X += 1;
        if (Input.IsKeyPressed(Key.Up) || Input.IsKeyPressed(Key.W)) dir.Y -= 1;
        if (Input.IsKeyPressed(Key.Down) || Input.IsKeyPressed(Key.S)) dir.Y += 1;
        if (dir == Vector2.Zero)
            return;
        _camera.Position += dir.Normalized() * KeyPanSpeed * (float)delta / _camera.Zoom.X;
        ClampCamera();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton mb:
                if (mb.ButtonIndex == MouseButton.Left)
                    HandleLeftButton(mb);
                else if (mb.ButtonIndex == MouseButton.Right)
                    HandleRightButton(mb);
                else if (mb.ButtonIndex == MouseButton.Middle)
                    _panning = mb.Pressed;
                else if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed)
                    ZoomBy(ZoomStep, mb.Position);
                else if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed)
                    ZoomBy(1f / ZoomStep, mb.Position);
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                if (key.Keycode is Key.Equal or Key.Plus or Key.KpAdd)
                    ZoomIn();
                else if (key.Keycode is Key.Minus or Key.KpSubtract)
                    ZoomOut();
                else if (key.Keycode == Key.Home)
                    ZoomToFit();
                break;
            case InputEventMouseMotion motion:
                if (_leftDown)
                    HandleLeftDrag(motion);
                else if (_rightDown && Mode == MapMode.EditProvinces)
                    ProvinceBrushAt(GetGlobalMousePosition(), erase: true);
                else if (_panning && _camera != null)
                {
                    // A screen-pixel drag moves the camera less far the more zoomed in it is.
                    _camera.Position -= motion.Relative / _camera.Zoom.X;
                    ClampCamera();
                }
                break;
        }
    }

    // --- Realms and players -------------------------------------------------------

    internal Realm GetRealm(int realmId) => Registry.Realms[realmId];

    public Realm GetPlayerRealm() => GetRealm(PlayerRealmId);

    /// <summary>
    /// GameRoot calls this before GenerateWorld(), when no realms exist yet, so
    /// the choice is remembered and applied once seeding has created them.
    /// </summary>
    public void SetPlayerCiv(string civKey)
    {
        _pendingPlayerCiv = civKey;
        if (CivRealmIds.TryGetValue(civKey, out int id))
            PlayerRealmId = id;
    }

    Realm MakeRulerAndRealm(string realmName, CivSpec spec) => CreateStartingRealm(Registry, realmName, spec);

    /// <summary>Creates a civ's realm with its 300 BC ruling family (tests use it without a map).</summary>
    public static Realm CreateStartingRealm(CharacterRegistry registry, string realmName, CivSpec spec)
    {
        var family = spec.Family;
        var ruler = registry.CreateCharacter(family.Ruler, "male", family.RulerBorn, culture: spec.Culture);
        registry.CreateDynasty(family.House ?? "House " + family.Ruler, ruler);
        var spouse = registry.CreateCharacter(family.Spouse, "female", family.SpouseBorn, culture: spec.Culture);
        CharacterRegistry.Marry(ruler, spouse);
        AddChildren(registry, spouse, ruler, family.Children, spec.Culture);
        return registry.CreateRealm(realmName, ruler, spec.Law, spec.Color, spec.Culture);
    }

    static void AddChildren(CharacterRegistry registry, Character mother, Character father, Kin[] children, Culture culture)
    {
        foreach (var kin in children)
        {
            var child = registry.HaveChild(mother, father, kin.Name, kin.Male ? "male" : "female", kin.Born);
            if (kin.Spouse == null)
                continue;
            var spouse = registry.CreateCharacter(kin.Spouse, kin.Male ? "female" : "male", kin.SpouseBorn, culture: culture);
            CharacterRegistry.Marry(child, spouse);
            var (wife, husband) = kin.Male ? (spouse, child) : (child, spouse);
            AddChildren(registry, wife, husband, kin.Children ?? Array.Empty<Kin>(), culture);
        }
    }

    /// <summary>
    /// Succession changes only a realm's ruler, never territory, so there's no
    /// rendering update. Returns the year's events for the chronicle.
    /// </summary>
    public List<ChronicleEvent> AdvanceYear()
    {
        DemoYear++;
        if (DemoYear == 0)
            DemoYear = 1;  // 1 BC is followed by AD 1
        if (Population != null)
        {
            SyncPopulationOwnership();
            Population.Tick(DemoYear);
        }
        return Registry.AdvanceYear(DemoYear);
    }

    // --- World generation -----------------------------------------------------------

    /// <summary>The 300 BC provinces (ProvinceGenerator), computed off the main thread so the window stays responsive.</summary>
    internal async Task GenerateProvinces()
    {
        var seeds = ProvinceGenerator.LoadSeeds();
        var grid = Grid;
        var tribal = TribalRealmIds();
        Provinces = await Task.Run(() => ProvinceGenerator.Generate(grid, seeds, tribal));
    }

    /// <summary>
    /// The frontier peoples (Iberian, Gallic, Scythian) were confederations of
    /// tribes, not administered states, so they start unorganized: no
    /// provinces (decided in the Ledger, Sep 2026).
    /// </summary>
    internal List<int> TribalRealmIds()
    {
        var ids = FrontierZones.Where(z => CivRealmIds.ContainsKey(z.Key)).Select(z => CivRealmIds[z.Key]).ToList();
        if (ids.Count == 0)  // a loaded save: CivRealmIds isn't rebuilt, so match the realm names instead
            ids = Registry.Realms.Values.Where(r => FrontierZones.Any(z => z.RealmName == r.Name)).Select(r => r.Id).ToList();
        return ids;
    }

    /// <summary>
    /// Yields a frame if (and only if) this node is inside a running tree. Tests
    /// build a MapView without adding it anywhere; there this is a no-op and
    /// generation runs straight through. In the game it keeps rendering, input
    /// and the OS message pump alive during generation.
    /// </summary>
    async Task MaybeYield()
    {
        if (IsInsideTree())
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    static byte[] LoadMask(string path)
    {
        var texture = GD.Load<Texture2D>(path) ?? throw new InvalidOperationException($"{path} could not be loaded");
        var mask = texture.GetImage();
        if (mask == null || mask.GetWidth() != GridWidth || mask.GetHeight() != GridHeight)
            throw new InvalidOperationException($"{path} is missing or doesn't match the {GridWidth}x{GridHeight} grid");
        mask.Convert(Image.Format.L8);
        return mask.GetData();
    }

    /// <summary>
    /// Physical geography comes first: the real Natural Earth 1:10m coastline,
    /// rasterized by tools/build_land_mask.py (white = land). Sea is then a hard
    /// constraint no political boundary can paint over.
    /// </summary>
    internal async Task SeedLandAndSea()
    {
        byte[] pixels = LoadMask(LandMaskPath);
        int[] cells = Grid.Cells;
        for (int start = 0; start < pixels.Length; start += ChunkCells)
        {
            int end = Math.Min(start + ChunkCells, pixels.Length);
            for (int j = start; j < end; j++)
                cells[j] = pixels[j] != 0 ? 0 : SeaOwnerId;
            await MaybeYield();
        }
    }

    /// <summary>
    /// The real civs come from a pre-baked, offline-reconciled raster
    /// (tools/reconcile_map.py): the real political polygons clipped to the real
    /// coastline, with bounded coastal-sliver repair - see
    /// data/map_reconciliation.json. Display names come from ancient_bc300.json.
    /// </summary>
    internal async Task SeedRealCivs()
    {
        var parsed = Json.ParseString(Godot.FileAccess.GetFileAsString(DataPath)).AsGodotDictionary();
        var names = new Dictionary<string, string>();
        foreach (Variant region in parsed["regions"].AsGodotArray())
        {
            var r = region.AsGodotDictionary();
            names[r["key"].AsString()] = r["name"].AsString();
        }
        var realmIds = new List<int> { 0 };  // mask code 0 = unclaimed
        foreach (var spec in RealCivs)
        {
            var realm = MakeRulerAndRealm(names[spec.Key], spec);
            CivRealmIds[spec.Key] = realm.Id;
            realmIds.Add(realm.Id);
        }

        byte[] pixels = LoadMask(PoliticalMaskPath);
        int[] cells = Grid.Cells;
        for (int start = 0; start < pixels.Length; start += ChunkCells)
        {
            int end = Math.Min(start + ChunkCells, pixels.Length);
            for (int j = start; j < end; j++)
            {
                int code = pixels[j];
                // Sea stays a hard constraint, in case the masks disagree at the margin.
                if (code > 0 && cells[j] != SeaOwnerId)
                    cells[j] = realmIds[code];
            }
            await MaybeYield();
        }
        PlayerRealmId = CivRealmIds.GetValueOrDefault("rome", realmIds[1]);
    }

    internal async Task SeedFrontierZones()
    {
        foreach (var spec in FrontierZones)
        {
            var realm = MakeRulerAndRealm(spec.RealmName!, spec);
            CivRealmIds[spec.Key] = realm.Id;
            FillPolygon(SoftenFrontier(spec.Polygon!), realm.Id, onlyIfUnclaimed: true);
            await MaybeYield();
        }
    }

    /// <summary>
    /// Frontier zones are approximate sketches, not sourced boundaries: this
    /// densifies each straight hand-placed edge and displaces every point with a
    /// fixed sinusoidal field, purely to avoid a ruler-drawn look. Deterministic.
    /// </summary>
    static List<Vector2> SoftenFrontier(Vector2[] polygon)
    {
        var result = new List<Vector2>();
        for (int i = 0; i < polygon.Length; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Length];
            int steps = Math.Max(1, (int)Math.Ceiling(a.DistanceTo(b) / 20.0));
            for (int step = 0; step < steps; step++)
            {
                var p = a.Lerp(b, (float)step / steps);
                double dx = 8.0 * Math.Sin(p.Y / 61.0 + p.X / 147.0) + 3.0 * Math.Sin(p.Y / 19.0 - p.X / 47.0);
                double dy = 8.0 * Math.Sin(p.X / 73.0 - p.Y / 163.0) + 3.0 * Math.Sin(p.X / 23.0 + p.Y / 53.0);
                result.Add(p + new Vector2((float)dx, (float)dy));
            }
        }
        return result;
    }

    /// <summary>
    /// Scanline polygon fill (even-odd rule): per row, find where the edges
    /// cross it, then fill the spans between pairs of crossings. Sea is never
    /// painted; with onlyIfUnclaimed, neither is anyone's existing territory.
    /// </summary>
    void FillPolygon(List<Vector2> polygon, int ownerId, bool onlyIfUnclaimed)
    {
        if (polygon.Count < 3)
            return;
        int y0 = Math.Clamp((int)polygon.Min(p => p.Y), 0, GridHeight - 1);
        int y1 = Math.Clamp((int)polygon.Max(p => p.Y), 0, GridHeight - 1);
        int n = polygon.Count;
        var xs = new List<double>();
        int[] cells = Grid.Cells;
        for (int y = y0; y <= y1; y++)
        {
            double scanY = y + 0.5;
            xs.Clear();
            for (int i = 0; i < n; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % n];
                if (a.Y == b.Y)
                    continue;
                if (scanY < Math.Min(a.Y, b.Y) || scanY >= Math.Max(a.Y, b.Y))
                    continue;
                double t = (scanY - a.Y) / (b.Y - a.Y);
                xs.Add(a.X + t * (b.X - a.X));
            }
            xs.Sort();
            for (int i = 0; i + 1 < xs.Count; i += 2)
            {
                int xStart = Math.Clamp((int)Math.Ceiling(xs[i] - 0.5), 0, GridWidth - 1);
                int xEnd = Math.Clamp((int)Math.Ceiling(xs[i + 1] - 0.5) - 1, 0, GridWidth - 1);
                for (int x = xStart; x <= xEnd; x++)
                {
                    int idx = y * GridWidth + x;
                    int existing = cells[idx];
                    if (existing == SeaOwnerId || (onlyIfUnclaimed && existing != 0))
                        continue;
                    cells[idx] = ownerId;
                }
            }
        }
    }

    public bool IsSea(int x, int y) => Grid.GetOwner(x, y) == SeaOwnerId;

    /// <summary>
    /// One pass over the whole grid computing every realm's centroid (and land
    /// cell count, which sizes its name).
    /// </summary>
    internal async Task<Dictionary<int, Vector2>> ComputeCentroids()
    {
        var sumX = new Dictionary<int, double>();
        var sumY = new Dictionary<int, double>();
        var counts = new Dictionary<int, int>();
        int[] cells = Grid.Cells;
        for (int start = 0; start < cells.Length; start += ChunkCells)
        {
            int end = Math.Min(start + ChunkCells, cells.Length);
            for (int j = start; j < end; j++)
            {
                int o = cells[j];
                if (o <= 0 || o == SeaOwnerId)
                    continue;
                sumX[o] = sumX.GetValueOrDefault(o) + j % GridWidth;
                sumY[o] = sumY.GetValueOrDefault(o) + j / GridWidth;
                counts[o] = counts.GetValueOrDefault(o) + 1;
            }
            await MaybeYield();
        }
        _realmCells = counts;
        return counts.ToDictionary(kv => kv.Key,
            kv => new Vector2((float)(sumX[kv.Key] / kv.Value), (float)(sumY[kv.Key] / kv.Value)));
    }

    // --- Realm names ------------------------------------------------------------------

    /// <summary>
    /// Realm names in Cinzel, on a screen-space layer at their true pixel size
    /// (crisp at any zoom), repositioned whenever the camera moves. Bigger
    /// realms get bigger names.
    /// </summary>
    void BuildLabels(Dictionary<int, Vector2> centroids)
    {
        _labelLayer = new CanvasLayer { Layer = 0 };  // above the map, below the HUD
        AddChild(_labelLayer);
        var font = ThemeAncient.HeadingFont(700);
        _mapLabels.Clear();
        foreach (var (realmId, realm) in Registry.Realms)
        {
            if (!centroids.TryGetValue(realmId, out var centroid))
                continue;
            int cells = _realmCells.GetValueOrDefault(realmId);
            var label = new Label { Text = realm.Name.ToUpperInvariant(), MouseFilter = Control.MouseFilterEnum.Ignore };
            label.AddThemeFontOverride("font", font);
            label.AddThemeFontSizeOverride("font_size", (int)Math.Clamp(15.0 + Math.Sqrt(cells) / 70.0, 16.0, 34.0));
            label.AddThemeColorOverride("font_color", new Color(1f, 0.97f, 0.9f));
            label.AddThemeColorOverride("font_outline_color", new Color(0.05f, 0.035f, 0.02f, 0.85f));
            label.AddThemeConstantOverride("outline_size", 6);
            _labelLayer.AddChild(label);
            _mapLabels.Add((label, LabelAnchor(realmId, centroid) * CellPixels, cells));
        }
        _mapLabels.Sort((a, b) => b.Cells.CompareTo(a.Cells));
        BuildProvinceLabels();
        UpdateLabels();
    }

    /// <summary>
    /// Places every realm name over its anchor for the current camera, kept on
    /// the visible part of the map (below the top bar): a name that would run
    /// past an edge slides inward. Greedy by realm size: a name that would
    /// overlap a bigger realm's name is hidden until zooming makes room.
    /// </summary>
    void UpdateLabels()
    {
        if (_mapLabels.Count == 0 || _camera == null || !IsInsideTree())
            return;  // offscreen MapViews (tests) have no camera or viewport
        var screen = GetViewportRect().Size;
        float z = _camera.Zoom.X;
        var mapOnScreen = new Rect2((Vector2.Zero - _camera.Position) * z + screen / 2f, MapSize * z);
        var bounds = new Rect2(0, HudTopMargin, screen.X, screen.Y - HudTopMargin).Intersection(mapOnScreen);
        var placed = new List<Rect2>();
        foreach (var (label, world, _) in _mapLabels)
        {
            var size = label.GetMinimumSize();
            var center = (world - _camera.Position) * z + screen / 2f;
            var rect = new Rect2(center - size / 2f, size);
            bool fits = size.X <= bounds.Size.X && size.Y <= bounds.Size.Y && bounds.HasPoint(center);
            if (fits)
            {
                rect.Position = new Vector2(
                    Math.Clamp(rect.Position.X, bounds.Position.X, bounds.End.X - size.X),
                    Math.Clamp(rect.Position.Y, bounds.Position.Y, bounds.End.Y - size.Y));
                fits = !placed.Any(other => rect.Grow(6).Intersects(other));
            }
            label.Visible = fits;
            if (fits)
            {
                label.Position = rect.Position;
                placed.Add(rect);
            }
        }
        PlaceProvinceLabels(bounds, placed, screen, z);
    }

    /// <summary>
    /// A raw centroid can land at sea, on another realm, or between a realm's
    /// disconnected parts. Finds the closest coarse-grid point confirmed on the
    /// realm's own territory, with a margin on each side.
    /// </summary>
    Vector2 LabelAnchor(int realmId, Vector2 centroid)
    {
        var best = centroid;
        float bestDistance = float.PositiveInfinity;
        for (int y = 8; y < GridHeight - 8; y += 16)
        {
            for (int x = 8; x < GridWidth - 8; x += 16)
            {
                if (Grid.GetOwner(x, y) != realmId)
                    continue;
                var point = new Vector2(x, y);
                float distance = point.DistanceSquaredTo(centroid);
                if (distance >= bestDistance)
                    continue;
                if (Grid.GetOwner(x - 8, y) != realmId || Grid.GetOwner(x + 8, y) != realmId
                    || Grid.GetOwner(x, y - 8) != realmId || Grid.GetOwner(x, y + 8) != realmId)
                    continue;
                best = point;
                bestDistance = distance;
            }
        }
        return best;
    }

    // --- Population -----------------------------------------------------------------

    /// <summary>
    /// Starts the population engine from HYDE's historical population, with
    /// every real civ's 300 BC capital registered as an existing capital.
    /// No-op if the HYDE keyframes aren't baked.
    /// </summary>
    internal void StartPopulation(int startYear = StartYear)
    {
        if (!PopulationEngine.HydeAvailable())
        {
            GD.Print("Population engine off: no HYDE keyframes in data/population/ (run tools/build_population_mask.py - see MAP_DATA.md).");
            return;
        }
        var engine = new PopulationEngine();
        if (!engine.LoadHyde())
            return;
        Population = engine;
        SyncPopulationOwnership();
        var capitals = RealCivs
            .Where(spec => spec.CapitalLonLat.HasValue && CivRealmIds.ContainsKey(spec.Key))
            .Select(spec => new StartCapital(
                engine.NodeAtLonLat(spec.CapitalLonLat!.Value.X, spec.CapitalLonLat.Value.Y, LonMin, LonMax, LatMin, LatMax),
                CapitalKind.Country, CivRealmIds[spec.Key]));
        engine.Start(startYear, capitals);
    }

    void SyncPopulationOwnership() =>
        Population!.SetOwnership(Population.OwnershipFromGrid(Grid.Cells, GridWidth, GridHeight, SeaOwnerId), PlayerRealmId);

    public double PlayerPopulation() => Population?.RealmPopulation(PlayerRealmId) ?? 0.0;

    // --- Drawing ---------------------------------------------------------------------

    internal Color ColorForOwner(int ownerId) => ownerId switch
    {
        0 => WildColor,
        SeaOwnerId => SeaColor,
        _ => Registry.Realms.TryGetValue(ownerId, out var realm) ? realm.Color : Colors.Magenta,
    };

    /// <summary>
    /// Contested land (owned by another realm) shows as a blend of both realms'
    /// colors; a proposal on unclaimed land just paints it solid.
    /// </summary>
    Color DisplayColor(int x, int y)
    {
        int owner = Grid.GetOwner(x, y);
        if (owner == SeaOwnerId)
            return SeaColor;
        int proposer = _proposal[y * GridWidth + x];
        if (proposer == 0)
            return ColorForOwner(owner);
        if (owner == 0)
            return ColorForOwner(proposer);
        return ColorForOwner(owner).Lerp(ColorForOwner(proposer), 0.5f);
    }

    /// <summary>
    /// Builds the whole-map image from a raw RGBA buffer with a palette lookup per
    /// cell and one bulk upload, rather than millions of SetPixel calls.
    /// </summary>
    internal async Task BuildFullMapImage()
    {
        var palette = new Dictionary<int, uint>();
        foreach (int id in Registry.Realms.Keys)
            palette[id] = Rgba(ColorForOwner(id));
        uint wild = Rgba(WildColor), sea = Rgba(SeaColor);

        var bytes = new byte[GridWidth * GridHeight * 4];
        int[] cells = Grid.Cells;
        for (int start = 0; start < cells.Length; start += ChunkCells)
        {
            int end = Math.Min(start + ChunkCells, cells.Length);
            for (int j = start; j < end; j++)
            {
                int owner = cells[j];
                uint rgba = owner == 0 ? wild : owner == SeaOwnerId ? sea : palette.GetValueOrDefault(owner, wild);
                int b = j * 4;
                bytes[b] = (byte)(rgba >> 24);
                bytes[b + 1] = (byte)(rgba >> 16);
                bytes[b + 2] = (byte)(rgba >> 8);
                bytes[b + 3] = (byte)rgba;
            }
            await MaybeYield();
        }
        MapImage = Image.CreateFromData(GridWidth, GridHeight, false, Image.Format.Rgba8, bytes);
        _mapTexture = ImageTexture.CreateFromImage(MapImage);
        if (MapSprite != null)
            MapSprite.Texture = _mapTexture;
        BuildProvinceTexture();
    }

    static uint Rgba(Color c) =>
        ((uint)Math.Round(c.R * 255) << 24) | ((uint)Math.Round(c.G * 255) << 16)
        | ((uint)Math.Round(c.B * 255) << 8) | (uint)Math.Round(c.A * 255);

    void RepaintCell(int x, int y) => MapImage!.SetPixel(x, y, DisplayColor(x, y));

    /// <summary>
    /// Paints a proposal brush at worldPos. Touches only the brush area; the one
    /// per-stroke full cost is re-uploading the texture, a fast bulk copy.
    /// </summary>
    void PaintAt(Vector2 worldPos)
    {
        if (MapImage == null)
            return;
        int cx = (int)(worldPos.X / CellPixels), cy = (int)(worldPos.Y / CellPixels);
        if (!Grid.InBounds(cx, cy))
            return;
        for (int dy = -PaintRadius; dy <= PaintRadius; dy++)
        {
            for (int dx = -PaintRadius; dx <= PaintRadius; dx++)
            {
                int x = cx + dx, y = cy + dy;
                if (!Grid.InBounds(x, y) || IsSea(x, y) || dx * dx + dy * dy > PaintRadius * PaintRadius)
                    continue;
                int idx = y * GridWidth + x;
                if (_proposal[idx] == PlayerRealmId)
                    continue;
                _proposal[idx] = PlayerRealmId;
                _dirtyProposalCells.Add(idx);
                RepaintCell(x, y);
            }
        }
        _mapTexture!.Update(MapImage);
    }

    void ClearProposal()
    {
        if (_dirtyProposalCells.Count == 0 || MapImage == null)
            return;
        foreach (int idx in _dirtyProposalCells)
        {
            _proposal[idx] = 0;
            RepaintCell(idx % GridWidth, idx / GridWidth);
        }
        _dirtyProposalCells.Clear();
        _mapTexture!.Update(MapImage);
    }
}
