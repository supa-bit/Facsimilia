using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace Facsimilia.World;

/// <summary>A crop, orchard, herd or fishery's needs and yield (data/crops.json).</summary>
public sealed record CropProfile(string Id, string Label, string Kind, bool Food, double YieldTHa, double KcalPerKg,
    double SeedShare, double[]? Rain, double[]? TempMean, double? WinterMin, double? SummerMin, double[]? Ph,
    double SaltTol, double Fertility, double Drainage, double Depth, string Water, bool SouthSlopes, bool Rugged,
    bool NeedsWater, bool WetPenalty, Dictionary<string, double>? Forage, int YearsToBear);

/// <summary>
/// Crop yields and carrying capacity (MECHANICS.md, "Crop yields and
/// carrying capacity"): for every node, how well each crop, tree, herd and
/// fishery does there (0-1), what the best of them yields, and how many
/// people the place could feed at most with ancient farming. Computed from
/// the land layer and data/crops.json at load (about a tenth of a second),
/// so later systems - better tools, irrigation works, depleted soils - can
/// recompute it.
/// </summary>
public sealed class CropModel
{
    public const string ProfilesPath = "res://data/crops.json";
    const double KmPerDegree = 111.2;

    public IReadOnlyList<CropProfile> Profiles => _profiles;
    /// <summary>Suitability 0-1 per profile id, per node.</summary>
    public IReadOnlyDictionary<string, float[]> Suitability => _suit;
    /// <summary>People each node could feed at most (0 at sea).</summary>
    public float[] Capacity { get; private set; } = Array.Empty<float>();
    /// <summary>Food per hectare of the best field crop, kcal a year, rotation included.</summary>
    public float[] FieldKcalPerHa { get; private set; } = Array.Empty<float>();
    /// <summary>Index into Profiles of the best food field crop per node (-1 if none).</summary>
    public short[] BestFieldCrop { get; private set; } = Array.Empty<short>();
    public float[] AreaKm2 { get; private set; } = Array.Empty<float>();

    readonly List<CropProfile> _profiles = new();
    readonly Dictionary<string, float[]> _suit = new();
    double _kcalPerPerson = 730000, _losses = 0.15;
    double _pastureKcalPerNpp = 100, _fieldMax = 0.4, _fieldMaxWatered = 0.85, _terraceShare = 0.2, _fallowRainfed = 0.5;
    double _heavyClayPenalty = 0.35;
    double _fishCoast = 9e8, _fishRiver = 2e8, _fishLake = 4e8, _fishMarsh = 3e8;

    public static bool Available() => Godot.FileAccess.FileExists(ProfilesPath);

    /// <summary>Loads the profiles and computes everything for the land layer's nodes. Null if the profiles are missing.</summary>
    public static CropModel? Build(LandLayer land, IReadOnlyList<int> landNodes)
    {
        if (!Available())
            return null;
        var model = new CropModel();
        if (!model.LoadProfiles(Godot.FileAccess.GetFileAsString(ProfilesPath)))
            return null;
        model.Compute(land, landNodes);
        return model;
    }

    bool LoadProfiles(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            GD.PushError($"CropModel: {ProfilesPath} is not valid JSON");
            return false;
        }
        var root = doc.RootElement;
        _kcalPerPerson = root.GetProperty("kcal_per_person_year").GetDouble();
        _losses = root.GetProperty("losses").GetDouble();
        _pastureKcalPerNpp = root.GetProperty("pasture").GetProperty("kcal_per_ha_per_npp").GetDouble();
        var fish = root.GetProperty("fishing");
        _fishCoast = fish.GetProperty("kcal_coast_shallows").GetDouble();
        _fishRiver = fish.GetProperty("kcal_river").GetDouble();
        _fishLake = fish.GetProperty("kcal_lake").GetDouble();
        _fishMarsh = fish.GetProperty("kcal_marsh").GetDouble();
        var use = root.GetProperty("land_use");
        _fieldMax = use.GetProperty("field_max").GetDouble();
        _fieldMaxWatered = use.GetProperty("field_max_irrigated").GetDouble();
        _heavyClayPenalty = use.GetProperty("heavy_clay_penalty").GetDouble();
        _terraceShare = use.GetProperty("terrace_share").GetDouble();
        _fallowRainfed = use.GetProperty("fallow_rainfed").GetDouble();
        foreach (var c in root.GetProperty("crops").EnumerateArray())
        {
            Dictionary<string, double>? forage = null;
            if (c.TryGetProperty("forage", out var f))
            {
                forage = new Dictionary<string, double>();
                foreach (var p in f.EnumerateObject())
                    forage[p.Name] = p.Value.GetDouble();
            }
            _profiles.Add(new CropProfile(
                c.GetProperty("id").GetString()!, c.GetProperty("label").GetString()!, c.GetProperty("kind").GetString()!,
                Bool(c, "food", true), Num(c, "yield_t_ha", 0), Num(c, "kcal_per_kg", 0), Num(c, "seed_share", 0),
                Arr(c, "rain"), Arr(c, "temp_mean"), Opt(c, "winter_min"), Opt(c, "summer_min"), Arr(c, "ph"),
                Num(c, "salt_tol", 1), Num(c, "fertility", 0), Num(c, "drainage", 0), Num(c, "depth", 0),
                c.TryGetProperty("water", out var w) ? w.GetString()! : "rain", Bool(c, "south_slopes", false),
                Bool(c, "rugged", false), Bool(c, "needs_water", false), Bool(c, "wet_penalty", false), forage,
                (int)Num(c, "years_to_bear", 0)));
        }
        return true;

        static double Num(JsonElement e, string k, double d) => e.TryGetProperty(k, out var v) ? v.GetDouble() : d;
        static double? Opt(JsonElement e, string k) => e.TryGetProperty(k, out var v) ? v.GetDouble() : null;
        static bool Bool(JsonElement e, string k, bool d) => e.TryGetProperty(k, out var v) ? v.GetBoolean() : d;
        static double[]? Arr(JsonElement e, string k)
        {
            if (!e.TryGetProperty(k, out var v))
                return null;
            var a = new double[v.GetArrayLength()];
            int i = 0;
            foreach (var x in v.EnumerateArray())
                a[i++] = x.GetDouble();
            return a;
        }
    }

    // --- Response curves -----------------------------------------------------------

    /// <summary>0 at or below a, 1 from b to c, 0 at or above d, straight ramps between.</summary>
    public static double Trapezoid(double x, double[] r)
    {
        if (x <= r[0] || x >= r[3])
            return 0;
        if (x < r[1])
            return (x - r[0]) / (r[1] - r[0]);
        if (x > r[2])
            return (r[3] - x) / (r[3] - r[2]);
        return 1;
    }

    static double Smooth(double x, double a, double b)
    {
        double t = Math.Clamp((x - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }

    // --- The computation ---------------------------------------------------------------

    void Compute(LandLayer land, IReadOnlyList<int> landNodes)
    {
        int n = land.Width * land.Height;
        Capacity = new float[n];
        FieldKcalPerHa = new float[n];
        BestFieldCrop = new short[n];
        AreaKm2 = new float[n];
        Array.Fill(BestFieldCrop, (short)-1);
        foreach (var p in _profiles)
            _suit[p.Id] = new float[n];

        // Every land value used, read once as a function node -> value.
        float[] F(string name)
        {
            var field = land.Field(name);
            byte[] b = land.Bytes(name);
            double lo = field.Min, span = field.Max - field.Min;
            var values = new float[b.Length];
            for (int i = 0; i < b.Length; i++)
                values[i] = (float)(lo + b[i] / 255.0 * span);
            return values;
        }
        var rain = F("rain"); var tMean = F("temp_mean"); var tWinter = F("temp_winter"); var tSummer = F("temp_summer");
        var ph = F("ph"); var salinity = F("salinity"); var fertility = F("fertility"); var drainage = F("drainage");
        var depth = F("soil_depth"); var irrigation = F("irrigation"); var groundwater = F("groundwater");
        var floodplain = F("floodplain"); var seasonality = F("rain_seasonality"); var flat = F("flat_share");
        var desert = F("desert"); var marsh = F("marsh"); var lake = F("lake"); var grass = F("grassland");
        var scrub = F("scrub"); var wood = F("woodland"); var farmed = F("farmed"); var npp = F("plant_growth");
        var clay = F("clay");
        var south = F("southness"); var rugged = F("ruggedness"); var river = F("river"); var shallows = F("shallows");
        var habitat = new Dictionary<string, float[]>
        {
            ["grassland"] = grass, ["scrub"] = scrub, ["woodland"] = wood, ["farmed"] = farmed, ["desert"] = desert,
            ["marsh"] = marsh,
        };

        int w = land.Width;
        double latMax = MapView.LatMax, step = (MapView.LatMax - MapView.LatMin) / land.Height;
        double nodeKm = KmPerDegree * step;
        var fishSuit = _suit.GetValueOrDefault("fishing");
        var fishKcalAt = new float[n];
        var phRanges = new double[_profiles.Count][];
        for (int k = 0; k < _profiles.Count; k++)
            if (_profiles[k].Ph is { } r)
                phRanges[k] = new[] { r[0] - 0.6, r[0], r[1], r[1] + 0.6 };
        var grazeWeights = new (float[] Share, double Weight)[]
        {
            (grass, 1.0), (scrub, 0.6), (wood, 0.3), (farmed, 0.4), (marsh, 0.4), (desert, 0.05),
        };

        var herdForage = new (float[] Share, double Weight)[_profiles.Count][];
        for (int k = 0; k < _profiles.Count; k++)
        {
            var list = new List<(float[], double)>();
            foreach (var (name, weight) in _profiles[k].Forage ?? new Dictionary<string, double>())
                if (habitat.TryGetValue(name, out var share))
                    list.Add((share, weight));
            herdForage[k] = list.ToArray();
        }

        // Every node is independent: split them across the processor's cores.
        System.Threading.Tasks.Parallel.For(0, landNodes.Count, j =>
        {
            int i = landNodes[j];
            double lat = latMax - (i / w + 0.5) * step;
            double areaKm2 = nodeKm * nodeKm * Math.Cos(lat * Math.PI / 180);
            AreaKm2[i] = (float)areaKm2;
            double areaHa = areaKm2 * 100;
            double water = Math.Max(irrigation[i], floodplain[i]);
            // Winter-sown crops grow through the cool season: judge them on
            // its temperature, not the year's (Egypt's winter wheat).
            double coolSeason = (tMean[i] + tWinter[i]) / 2 + 3;
            // Summer crops need summer water: the Mediterranean's summers are
            // dry (high seasonality north of the Sahara), the Sahel's are wet.
            double summerRain = lat < 22 ? 1 : 1 - Smooth(seasonality[i], 50, 90);

            double bestField = 0, bestOrchard = 0;
            short bestFieldIndex = -1;
            for (short k = 0; k < _profiles.Count; k++)
            {
                var p = _profiles[k];
                if (p.Kind == "fish")
                    continue;
                double s;
                if (p.Kind == "herd")
                {
                    s = HerdSuitability(p, i, rain, tMean, tWinter, herdForage[k], rugged, river);
                }
                else
                {
                    double waterSuit = p.Water switch
                    {
                        "groundwater" => Math.Max(groundwater[i], water),
                        "summer" => Math.Max(Trapezoid(rain[i], p.Rain!) * summerRain, water),
                        _ => Math.Max(Trapezoid(rain[i], p.Rain!), water),
                    };
                    double temp = p.Kind == "field" && p.Water == "rain" ? coolSeason : tMean[i];
                    s = waterSuit * Trapezoid(temp, p.TempMean!);
                    if (s > 0 && p.WinterMin is double wmin)
                        s *= Smooth(tWinter[i], wmin - 3, wmin + 2);
                    if (s > 0 && p.SummerMin is double smin)
                        s *= Smooth(tSummer[i], smin - 3, smin + 2);
                    if (s > 0 && phRanges[k] != null)
                        s *= Trapezoid(ph[i], phRanges[k]);
                    if (s > 0)
                    {
                        double sal = salinity[i] / Math.Max(p.SaltTol, 0.01);
                        s *= 1 / (1 + sal * sal);
                        s *= Smooth(drainage[i], p.Drainage - 0.2, p.Drainage + 0.05) * 0.55 + 0.45;
                        s *= Smooth(depth[i], p.Depth - 0.15, p.Depth + 0.1) * 0.85 + 0.15;
                        s *= (1 - p.Fertility) + p.Fertility * (0.3 + 0.7 * fertility[i]);
                        if (p.SouthSlopes)
                            s *= 1 + 0.15 * Math.Clamp(south[i], -1, 1);
                    }
                    s = Math.Clamp(s, 0, 1);
                    if (p.Food)
                    {
                        double cropKcal = s * p.YieldTHa * 1000 * p.KcalPerKg * (1 - p.SeedShare);
                        if (p.Kind == "field" && cropKcal > bestField)
                        {
                            bestField = cropKcal;
                            bestFieldIndex = k;
                        }
                        else if (p.Kind == "orchard" && cropKcal > bestOrchard)
                        {
                            bestOrchard = cropKcal;
                        }
                    }
                }
                _suit[p.Id][i] = (float)s;
            }
            // Rain-fed fields lie fallow every other year; floods and canals
            // let the land be cropped yearly.
            double rotation = _fallowRainfed + (1 - _fallowRainfed) * Math.Clamp(water, 0, 1);
            bestField *= rotation;
            // The ancient ard scratches light soils; heavy clay waits for the
            // medieval mouldboard plough (a technology to unlock later).
            bestField *= 1 - _heavyClayPenalty * Smooth(clay[i], 35, 55);
            FieldKcalPerHa[i] = (float)bestField;
            BestFieldCrop[i] = bestFieldIndex;

            double usable = Math.Max(0, 1 - desert[i] - marsh[i] - lake[i]);
            // Rain-fed farming needs woodland for fuel and pasture for the
            // plough oxen, so only part of the flat land is ever sown;
            // irrigated valleys are sown almost wall to wall.
            double fieldMax = _fieldMax + (_fieldMaxWatered - _fieldMax) * Math.Clamp(water, 0, 1);
            double fieldShare = usable * flat[i] * fieldMax;
            double terraceShare = usable * (1 - flat[i]) * _terraceShare;
            double grazeShare = Math.Max(0, 1 - fieldShare - terraceShare - lake[i]);
            double grazing = 0;
            foreach (var (share, weight) in grazeWeights)
                grazing += share[i] * weight;
            double pastureKcal = areaHa * grazeShare * grazing * npp[i] * _pastureKcalPerNpp;
            double fishKcal = _fishCoast * shallows[i] + _fishRiver * river[i] + _fishLake * lake[i] + _fishMarsh * marsh[i];
            fishKcalAt[i] = (float)fishKcal;

            double kcal = areaHa * (fieldShare * bestField + terraceShare * bestOrchard) + pastureKcal + fishKcal;
            Capacity[i] = (float)(kcal * (1 - _losses) / _kcalPerPerson);
        });
        double fishMax = 0;
        foreach (int i in landNodes)
            fishMax = Math.Max(fishMax, fishKcalAt[i]);
        if (fishSuit != null && fishMax > 0)
            foreach (int i in landNodes)
                fishSuit[i] = (float)(fishKcalAt[i] / fishMax);
    }

    static double HerdSuitability(CropProfile p, int i, float[] rain, float[] tMean, float[] tWinter,
        (float[] Share, double Weight)[] forageOf, float[] rugged, float[] river)
    {
        double s = Trapezoid(rain[i], p.Rain!) * Trapezoid(tMean[i], p.TempMean!);
        if (p.WinterMin is double wmin)
            s *= Smooth(tWinter[i], wmin - 3, wmin + 2);
        double forage = 0;
        foreach (var (share, weight) in forageOf)
            forage += share[i] * weight;
        s *= Math.Clamp(forage * 1.3, 0, 1);
        if (p.Rugged)
            s *= 0.75 + 0.25 * Smooth(rugged[i], 30, 250);
        if (p.NeedsWater)
            s *= Math.Max(Smooth(rain[i], 350, 550), Math.Min(1, river[i] * 1.5));
        if (p.WetPenalty)
            s *= 1 - 0.5 * Smooth(rain[i], 1000, 1600);
        return Math.Clamp(s, 0, 1);
    }

    /// <summary>People each region could feed at most (index = region id), summed from its nodes.</summary>
    public double[] RegionCapacity(PopulationEngine population)
    {
        var caps = new double[population.RegionCount + 1];
        foreach (int i in population.LandNodes)
            caps[population.RegionOf(i)] += Capacity[i];
        return caps;
    }

    /// <summary>
    /// Adds the model's results to the land layer as views: "food_capacity"
    /// (people per km²) and "crop_&lt;id&gt;" (suitability 0-1) per profile.
    /// </summary>
    public void RegisterViews(LandLayer land)
    {
        var density = new float[Capacity.Length];
        for (int i = 0; i < density.Length; i++)
            density[i] = AreaKm2[i] > 0 ? Capacity[i] / AreaKm2[i] : 0;
        land.AddComputed(new LandField("food_capacity", "Crops and food", "people/km²", 0, 400,
            "How many people the land here could feed at most with ancient farming: the best field crop, orchards " +
            "on the slopes, herds on the pasture and fish", true, new[] { "Facsimilia crop model (data/crops.json) (project's own)" }),
            density);
        foreach (var p in _profiles)
        {
            string what = p.Kind switch
            {
                "herd" => "How well the land suits them: climate and forage",
                "fish" => "Fish from the coastal shallows, rivers, lakes and marshes",
                _ => "How well it grows here, 1 is ideal: climate, water (rain or irrigation), frost, soil and salt",
            };
            land.AddComputed(new LandField("crop_" + p.Id, "Crops and food", "index", 0, 1, what, true,
                new[] { "Facsimilia crop model (data/crops.json) (project's own)" }), _suit[p.Id]);
        }
    }
}
