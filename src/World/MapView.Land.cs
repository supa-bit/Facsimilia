using System;
using System.Collections.Generic;
using Godot;

namespace Facsimilia.World;

/// <summary>How a land view colours its values, low to high.</summary>
public enum LandRamp { Height, Heat, Wet, Good, Bad, Water, Forest, Farm, Scrub, Sand, Gold }

/// <summary>One entry of the Map view menu that shows a land field.</summary>
public sealed record LandView(string Field, string Label, LandRamp Ramp);

/// <summary>
/// Map views: Political (the default), Regions, and one view per land
/// field (MECHANICS.md, "Resources and the land"). A land view tints each
/// place by its value over the terrain, hides realm borders (coasts stay),
/// and the HUD shows a legend with the value under the mouse.
/// </summary>
public partial class MapView
{
    [Signal] public delegate void MapViewChangedEventHandler(string view);

    public const string PoliticalView = "political";
    public const string RegionsView = "regions";

    public LandLayer? Land { get; private set; }
    public string CurrentView { get; private set; } = PoliticalView;

    /// <summary>The land views offered in the menu, grouped by part, in menu order.</summary>
    public static readonly (string Part, LandView[] Views)[] LandViews =
    {
        ("Terrain", new[]
        {
            new LandView("elevation", "Elevation", LandRamp.Height),
            new LandView("slope", "Steepness", LandRamp.Bad),
            new LandView("flat_share", "Ploughable flat land", LandRamp.Good),
            new LandView("shallows", "Coastal shallows", LandRamp.Water),
        }),
        ("Climate", new[]
        {
            new LandView("temp_mean", "Temperature", LandRamp.Heat),
            new LandView("temp_winter", "Winter lows (frost)", LandRamp.Heat),
            new LandView("temp_summer", "Summer highs", LandRamp.Heat),
            new LandView("rain", "Rainfall", LandRamp.Wet),
            new LandView("rain_seasonality", "Rain seasonality", LandRamp.Bad),
            new LandView("growing_days", "Growing season (rain-fed)", LandRamp.Good),
            new LandView("drought_risk", "Drought risk", LandRamp.Bad),
            new LandView("wind", "Wind", LandRamp.Water),
        }),
        ("Soil", new[]
        {
            new LandView("fertility", "Fertility", LandRamp.Good),
            new LandView("soil_depth", "Soil depth", LandRamp.Good),
            new LandView("drainage", "Drainage", LandRamp.Good),
            new LandView("salinity", "Salinity", LandRamp.Bad),
            new LandView("erosion", "Erosion risk", LandRamp.Bad),
        }),
        ("Water", new[]
        {
            new LandView("river", "Rivers", LandRamp.Water),
            new LandView("floodplain", "Floodplains", LandRamp.Water),
            new LandView("irrigation", "Irrigation water", LandRamp.Water),
            new LandView("groundwater", "Groundwater", LandRamp.Water),
        }),
        ("Habitat, 300 BC", new[]
        {
            new LandView("farmed", "Farmed and grazed", LandRamp.Farm),
            new LandView("woodland", "Forest", LandRamp.Forest),
            new LandView("conifer", "Conifer share of forest", LandRamp.Forest),
            new LandView("scrub", "Scrub", LandRamp.Scrub),
            new LandView("grassland", "Grassland", LandRamp.Farm),
            new LandView("marsh", "Marsh", LandRamp.Water),
            new LandView("desert", "Desert", LandRamp.Sand),
        }),
        ("Resources", new[]
        {
            new LandView("res_iron", "Iron", LandRamp.Gold),
            new LandView("res_copper", "Copper", LandRamp.Gold),
            new LandView("res_tin", "Tin", LandRamp.Gold),
            new LandView("res_lead", "Lead", LandRamp.Gold),
            new LandView("res_silver", "Silver", LandRamp.Gold),
            new LandView("res_gold", "Gold", LandRamp.Gold),
            new LandView("res_salt", "Salt", LandRamp.Gold),
            new LandView("res_sulfur", "Sulfur", LandRamp.Gold),
            new LandView("res_bitumen", "Bitumen", LandRamp.Gold),
            new LandView("res_natron", "Natron", LandRamp.Gold),
            new LandView("res_marble", "Marble", LandRamp.Gold),
            new LandView("res_fine_clay", "Fine pottery clay", LandRamp.Gold),
            new LandView("res_glass_sand", "Glass sand", LandRamp.Gold),
            new LandView("res_cedar", "Cedar", LandRamp.Gold),
            new LandView("res_ship_timber", "Ship timber", LandRamp.Gold),
            new LandView("res_papyrus", "Papyrus", LandRamp.Gold),
            new LandView("res_murex", "Purple dye (murex)", LandRamp.Gold),
            new LandView("res_frankincense", "Frankincense", LandRamp.Gold),
            new LandView("res_myrrh", "Myrrh", LandRamp.Gold),
            new LandView("res_balsam", "Balsam", LandRamp.Gold),
            new LandView("res_silphium", "Silphium", LandRamp.Gold),
            new LandView("res_horses", "Horses", LandRamp.Gold),
            new LandView("res_elephants", "Elephants", LandRamp.Gold),
            new LandView("res_ivory", "Ivory", LandRamp.Gold),
        }),
    };

    public static LandView? FindLandView(string field)
    {
        foreach (var (_, views) in LandViews)
            foreach (var v in views)
                if (v.Field == field)
                    return v;
        return null;
    }

    public bool LandAvailable => Land != null && Population != null;

    /// <summary>Switches the map view: PoliticalView, RegionsView, or a land field's name.</summary>
    public void SetMapView(string view)
    {
        if (view == CurrentView)
            return;
        bool isLand = view != PoliticalView && view != RegionsView;
        if (isLand && (!LandAvailable || FindLandView(view) == null || !Land!.Has(view)))
            return;
        if (view == RegionsView && !RegionsAvailable)
            return;
        CurrentView = view;
        SetRegionsOverlay(view == RegionsView);
        if (MapSprite?.Material is ShaderMaterial material)
        {
            if (isLand)
            {
                var land = Land!;
                var image = Image.CreateFromData(land.Width, land.Height, false, Image.Format.R8, land.Bytes(view));
                material.SetShaderParameter("land_texture", ImageTexture.CreateFromImage(image));
                material.SetShaderParameter("land_ramp", RampTexture(FindLandView(view)!.Ramp));
            }
            material.SetShaderParameter("land_strength", isLand ? 1f : 0f);
        }
        UpdateLabels();
        EmitSignal(SignalName.MapViewChanged, view);
    }

    /// <summary>The shown land field's value at a map position, or null at sea or outside a land view.</summary>
    public (LandField Field, double Value)? LandValueAtWorld(Vector2 world)
    {
        if (!LandAvailable || !Land!.Has(CurrentView))
            return null;
        int node = Population!.NodeAtCell((int)(world.X / CellPixels), (int)(world.Y / CellPixels), GridWidth, GridHeight);
        if (node < 0 || Population.NodeRegion.Length == 0 || Population.NodeRegion[node] == 0)
            return null;  // sea
        return (Land.Field(CurrentView), Land.Value(CurrentView, node));
    }

    internal void LoadLand()
    {
        Land = LandLayer.Load();
        if (Land != null && Population != null && (Land.Width != Population.Width || Land.Height != Population.Height))
        {
            GD.PushError("MapView: the land layer doesn't match the population grid; land views are off");
            Land = null;
        }
    }

    /// <summary>A ramp's colour at a position 0..1, alpha included (transparent lets the terrain show).</summary>
    public static Color RampColor(LandRamp ramp, float t)
    {
        (float At, Color C)[] stops = ramp switch
        {
            LandRamp.Height => new[] { (0f, C(0.20f, 0.42f, 0.28f, 0.75f)), (0.12f, C(0.45f, 0.58f, 0.30f, 0.75f)),
                (0.3f, C(0.78f, 0.68f, 0.42f, 0.8f)), (0.55f, C(0.55f, 0.36f, 0.22f, 0.85f)), (1f, C(0.97f, 0.97f, 0.97f, 0.9f)) },
            LandRamp.Heat => new[] { (0f, C(0.22f, 0.32f, 0.78f, 0.8f)), (0.45f, C(0.85f, 0.88f, 0.9f, 0.7f)),
                (0.7f, C(0.98f, 0.75f, 0.30f, 0.8f)), (1f, C(0.75f, 0.12f, 0.10f, 0.85f)) },
            LandRamp.Wet => new[] { (0f, C(0.82f, 0.66f, 0.40f, 0.75f)), (0.12f, C(0.85f, 0.80f, 0.45f, 0.7f)),
                (0.3f, C(0.40f, 0.68f, 0.35f, 0.75f)), (0.6f, C(0.15f, 0.50f, 0.65f, 0.8f)), (1f, C(0.10f, 0.20f, 0.60f, 0.9f)) },
            LandRamp.Good => new[] { (0f, C(0.70f, 0.20f, 0.15f, 0.75f)), (0.5f, C(0.92f, 0.80f, 0.35f, 0.7f)),
                (1f, C(0.18f, 0.58f, 0.25f, 0.8f)) },
            LandRamp.Bad => new[] { (0f, C(1f, 1f, 1f, 0f)), (0.15f, C(0.98f, 0.85f, 0.45f, 0.45f)),
                (0.5f, C(0.90f, 0.45f, 0.15f, 0.75f)), (1f, C(0.50f, 0.08f, 0.08f, 0.9f)) },
            LandRamp.Water => new[] { (0f, C(1f, 1f, 1f, 0f)), (0.1f, C(0.55f, 0.80f, 0.95f, 0.4f)),
                (0.5f, C(0.20f, 0.55f, 0.90f, 0.75f)), (1f, C(0.05f, 0.20f, 0.65f, 0.9f)) },
            LandRamp.Forest => new[] { (0f, C(1f, 1f, 1f, 0f)), (0.1f, C(0.65f, 0.80f, 0.45f, 0.4f)),
                (0.5f, C(0.20f, 0.52f, 0.20f, 0.75f)), (1f, C(0.05f, 0.28f, 0.10f, 0.9f)) },
            LandRamp.Farm => new[] { (0f, C(1f, 1f, 1f, 0f)), (0.1f, C(0.95f, 0.90f, 0.55f, 0.4f)),
                (0.5f, C(0.85f, 0.70f, 0.25f, 0.75f)), (1f, C(0.60f, 0.42f, 0.10f, 0.9f)) },
            LandRamp.Scrub => new[] { (0f, C(1f, 1f, 1f, 0f)), (0.1f, C(0.80f, 0.80f, 0.55f, 0.4f)),
                (0.5f, C(0.55f, 0.58f, 0.25f, 0.75f)), (1f, C(0.35f, 0.40f, 0.12f, 0.9f)) },
            LandRamp.Sand => new[] { (0f, C(1f, 1f, 1f, 0f)), (0.3f, C(0.95f, 0.85f, 0.60f, 0.45f)),
                (1f, C(0.90f, 0.70f, 0.35f, 0.85f)) },
            _ => new[] { (0f, C(1f, 1f, 1f, 0f)), (0.08f, C(1f, 0.92f, 0.55f, 0.35f)),
                (0.4f, C(1f, 0.75f, 0.15f, 0.8f)), (1f, C(0.80f, 0.35f, 0.02f, 0.95f)) },
        };
        t = Math.Clamp(t, 0f, 1f);
        for (int i = 1; i < stops.Length; i++)
        {
            if (t <= stops[i].At)
            {
                float f = (t - stops[i - 1].At) / Math.Max(stops[i].At - stops[i - 1].At, 1e-6f);
                return stops[i - 1].C.Lerp(stops[i].C, f);
            }
        }
        return stops[^1].C;

        static Color C(float r, float g, float b, float a) => new(r, g, b, a);
    }

    static ImageTexture RampTexture(LandRamp ramp)
    {
        var image = Image.CreateEmpty(256, 1, false, Image.Format.Rgba8);
        for (int i = 0; i < 256; i++)
            image.SetPixel(i, 0, RampColor(ramp, i / 255f));
        return ImageTexture.CreateFromImage(image);
    }
}
