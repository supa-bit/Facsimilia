using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Facsimilia.World;

namespace Facsimilia.Game;

public enum GoodSource { Land, ByProduct, Made, Beyond }

/// <summary>One good (data/goods.json): where it comes from, what it is worth, how much people need.</summary>
public sealed class GoodDef
{
    public int Index { get; init; }
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Family { get; init; } = "";
    public GoodSource Source { get; init; }
    /// <summary>Land field giving how well a place yields it, 0-1 ("crop_wheat", "res_iron", "woodland").</summary>
    public string Field { get; init; } = "";
    public string Work { get; init; } = "";
    public double Share { get; init; } = 1;
    public int By { get; init; } = -1;
    public double Per { get; init; }
    public (int Good, double Qty)[] Inputs { get; init; } = Array.Empty<(int, double)>();
    public double Labour { get; init; }
    /// <summary>Any one of the inputs will do (threshed grain from wheat, barley or rye).</summary>
    public bool AnyInput { get; init; }
    public double Weight { get; init; }
    /// <summary>Drachmae per load.</summary>
    public double Price { get; set; }
    /// <summary>Loads each person uses a year.</summary>
    public double Need { get; init; }
    /// <summary>Goods from beyond the map: the regions where they arrive, and loads a year.</summary>
    public string[] EntryRegions { get; init; } = Array.Empty<string>();
    public double Supply { get; init; }
}

/// <summary>The catalogue of goods and families (data/goods.json).</summary>
public sealed class GoodsCatalog
{
    public const string Path = "res://data/goods.json";

    public List<GoodDef> Goods { get; } = new();
    public List<(string Id, string Name)> Families { get; } = new();
    public Dictionary<string, double> WorkShare { get; } = new();
    public double Wage { get; private set; } = 80;
    public double HouseholdCrafts { get; private set; } = 0.1;
    readonly Dictionary<string, int> _index = new();

    public GoodDef this[string id] => Goods[_index[id]];
    public bool Has(string id) => _index.ContainsKey(id);
    public string FamilyName(string id) => Families.FirstOrDefault(f => f.Id == id).Name ?? id;

    public static GoodsCatalog Parse(string json)
    {
        var c = new GoodsCatalog();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        c.Wage = root.GetProperty("wage").GetDouble();
        c.HouseholdCrafts = root.GetProperty("household_crafts").GetDouble();
        foreach (var w in root.GetProperty("work").EnumerateObject())
            c.WorkShare[w.Name] = w.Value.GetDouble();
        foreach (var f in root.GetProperty("families").EnumerateArray())
            c.Families.Add((f.GetProperty("id").GetString()!, f.GetProperty("name").GetString()!));
        foreach (var g in root.GetProperty("goods").EnumerateArray())
        {
            string id = g.GetProperty("id").GetString()!;
            GoodSource kind = g.TryGetProperty("inputs", out _) ? GoodSource.Made
                : g.TryGetProperty("by", out _) ? GoodSource.ByProduct
                : g.TryGetProperty("beyond", out _) ? GoodSource.Beyond : GoodSource.Land;
            string field = "";
            if (kind == GoodSource.Land)
            {
                string src = g.GetProperty("source").GetString()!;
                field = src.StartsWith("crop:") ? "crop_" + src[5..] : src[5..];
            }
            var inputs = new List<(int, double)>();
            if (kind == GoodSource.Made)
                foreach (var p in g.GetProperty("inputs").EnumerateObject())
                    inputs.Add((c._index[p.Name], p.Value.GetDouble()));   // inputs are listed first (checked by the data)
            var def = new GoodDef
            {
                Index = c.Goods.Count, Id = id, Name = g.GetProperty("name").GetString()!,
                Family = g.GetProperty("family").GetString()!, Source = kind, Field = field,
                Work = g.TryGetProperty("work", out var wk) ? wk.GetString()! : "",
                Share = g.TryGetProperty("share", out var sh) ? sh.GetDouble() : 1,
                By = kind == GoodSource.ByProduct ? c._index[g.GetProperty("by").GetString()!] : -1,
                Per = g.TryGetProperty("per", out var per) ? per.GetDouble() : 0,
                Inputs = inputs.ToArray(),
                Labour = g.TryGetProperty("labour", out var lb) ? lb.GetDouble() : 0,
                AnyInput = g.TryGetProperty("any_input", out var ai) && ai.GetBoolean(),
                Weight = g.TryGetProperty("weight", out var wt) ? wt.GetDouble() : 0,
                Price = g.TryGetProperty("price", out var pr) ? pr.GetDouble() : 0,
                Need = g.TryGetProperty("need", out var nd) ? nd.GetDouble() : 0,
                EntryRegions = g.TryGetProperty("beyond", out var by) ? by.EnumerateArray().Select(x => x.GetString()!).ToArray() : Array.Empty<string>(),
                Supply = g.TryGetProperty("supply", out var su) ? su.GetDouble() : 0,
            };
            if (kind == GoodSource.Made)
                def.Price = (def.AnyInput ? def.Inputs.Average(x => c.Goods[x.Good].Price * x.Qty)
                    : def.Inputs.Sum(x => c.Goods[x.Good].Price * x.Qty)) + def.Labour * c.Wage;
            c._index[id] = def.Index;
            c.Goods.Add(def);
        }
        return c;
    }
}

/// <summary>A realm's goods this year, in loads (index = good).</summary>
public sealed class RealmGoods
{
    public double[] Produced { get; }
    /// <summary>Used up making other goods.</summary>
    public double[] Used { get; }
    /// <summary>What its people need.</summary>
    public double[] Needed { get; }
    /// <summary>Value of everything made, less the goods used up making it: drachmae a year.</summary>
    public double Value { get; set; }
    public double Workers { get; set; }
    /// <summary>Value of townspeople's trade, carrying, building and service, drachmae.</summary>
    public double Services { get; set; }
    public double Craftsmen { get; set; }

    /// <summary>Bought from other realms this year, and sold to them.</summary>
    public double[] Imported { get; }
    public double[] Exported { get; }
    /// <summary>Silver paid for imports and earned from exports, drachmae.</summary>
    public double ImportCost { get; set; }
    public double ExportIncome { get; set; }
    /// <summary>Share of people's needs met, by value (1 = everything they need).</summary>
    public double Satisfaction { get; set; } = 1;
    public HashSet<int> Partners { get; } = new();
    /// <summary>Markets, harbours and roads: a share more on trade's profits and customs.</summary>
    public double TradeBonus { get; set; }

    public RealmGoods(int count)
    {
        Imported = new double[count];
        Exported = new double[count];
        Produced = new double[count];
        Used = new double[count];
        Needed = new double[count];
    }

    /// <summary>What is left after making other goods and meeting people's needs (negative: a shortage).</summary>
    public double Surplus(int good) => Produced[good] + Imported[good] - Exported[good] - Used[good] - Needed[good];

    /// <summary>Loads of a good available to the realm after trade (made plus bought, less sold and used as inputs).</summary>
    public double Available(int good) => Produced[good] + Imported[good] - Exported[good] - Used[good];

    public double FamilyValue(GoodsCatalog cat, string family)
    {
        double v = 0;
        foreach (var g in cat.Goods)
            if (g.Family == family)
                v += (Produced[g.Index] - Used[g.Index]) * g.Price;
        return v;
    }
}

/// <summary>
/// Production (MECHANICS.md, "Goods and recipes"). Country people work the
/// land: a share of them in each kind of work (fields, orchards, herds,
/// gathering, forests, mines), spread over the goods their place yields,
/// favouring what grows best there. Townspeople, and country people in
/// their spare time, turn raw goods into made ones by recipe, as far as the
/// inputs go. By-products come with their source (wool with sheep).
/// </summary>
public static class GoodsEngine
{
    /// <summary>What a townsman not making goods earns, as a share of the wage.</summary>
    public const double ServiceShare = 0.5;

    public static Dictionary<int, RealmGoods> Compute(GoodsCatalog cat, PopulationEngine pop, Func<string, byte[]?> field,
        double urbanThreshold = Census.UrbanThreshold, IReadOnlyDictionary<string, double[]>? workFactor = null,
        float[]? fieldYield = null, IReadOnlyDictionary<string, float[]>? nodeFactor = null,
        IReadOnlyDictionary<int, double>? craftBoost = null, Func<int, string, double>? realmWorkBoost = null)
    {
        int n = cat.Goods.Count;
        var byWork = cat.Goods.Where(g => g.Source == GoodSource.Land)
            .GroupBy(g => g.Work)
            .Select(grp => (Work: grp.Key, Share: cat.WorkShare.GetValueOrDefault(grp.Key), Factor: workFactor?.GetValueOrDefault(grp.Key),
                OnLand: grp.Key is "field", Node: nodeFactor?.GetValueOrDefault(grp.Key), Goods: grp
                .Select(g => (g.Index, Bytes: field(g.Field), g.Share)).Where(x => x.Bytes != null).ToArray()))
            .ToArray();
        // The yield per hectare of the fields where the typical person lives.
        double typicalYield = 0;
        if (fieldYield != null)
        {
            var sample = pop.LandNodes.Where(i => pop.Pop[i] > 0 && fieldYield[i] > 0)
                .Select(i => (Yield: (double)fieldYield[i], Count: (double)pop.Pop[i])).OrderBy(x => x.Yield).ToList();
            double half = sample.Sum(x => x.Count) / 2, run = 0;
            foreach (var (y, count) in sample)
                if ((run += count) >= half)
                {
                    typicalYield = y;
                    break;
                }
        }
        var boosts = new Dictionary<int, double[]>();
        double[] BoostsOf(int owner)
        {
            if (!boosts.TryGetValue(owner, out var b))
                boosts[owner] = b = byWork.Select(w => 1 + (realmWorkBoost?.Invoke(owner, w.Work) ?? 0)).ToArray();
            return b;
        }
        var result = new Dictionary<int, RealmGoods>();
        var urban = new Dictionary<int, double>();
        var rural = new Dictionary<int, double>();
        float[] people = pop.Pop;
        int[] owners = pop.NodeOwner;
        var s = new double[n];
        foreach (int i in pop.LandNodes)
        {
            int owner = owners[i];
            if (owner <= 0)
                continue;
            if (!result.TryGetValue(owner, out var rg))
            {
                result[owner] = rg = new RealmGoods(n);
                urban[owner] = rural[owner] = 0;
            }
            double p = people[i];
            if (p <= 0)
                continue;
            double town = Math.Max(0, p - urbanThreshold);
            double country = p - town;
            urban[owner] += town;
            rural[owner] += country;
            int region = pop.RegionOf(i);
            // Where the fields yield more (the watered Nile valley), each farmer grows more.
            double richness = fieldYield != null && typicalYield > 0 ? Math.Clamp(fieldYield[i] / typicalYield, 0.5, 2.5) : 1;
            var realmBoost = BoostsOf(owner);
            for (int w = 0; w < byWork.Length; w++)
            {
                var (_, share, factor, onLand, nodeF, goods) = byWork[w];
                double sum = 0;
                foreach (var (g, bytes, gs) in goods)
                {
                    double v = bytes![i] / 255.0 * gs;
                    s[g] = v;
                    sum += v;
                }
                if (sum <= 0)
                    continue;
                double labour = country * share * (factor != null ? factor[region] : 1) * (onLand ? richness : 1)
                    * (nodeF != null ? nodeF[i] : 1) * realmBoost[w] / sum;
                foreach (var (g, _, _) in goods)
                    rg.Produced[g] += labour * s[g] * s[g];
            }
        }
        foreach (var (owner, rg) in result)
        {
            double craftsmen = (urban[owner] + rural[owner] * cat.HouseholdCrafts) * (1 + (craftBoost?.GetValueOrDefault(owner) ?? 0));
            rg.Craftsmen = craftsmen;
            rg.Workers = rural[owner];
            double totalWeight = cat.Goods.Sum(g => g.Weight);
            double total = urban[owner] + rural[owner];
            // What is wanted of each good: people's needs, what craftsmen make
            // for the market, and the inputs of both, passed back up each chain.
            var demand = new double[n];
            foreach (var g in cat.Goods)
            {
                rg.Needed[g.Index] = g.Need * total;
                demand[g.Index] = rg.Needed[g.Index];
                if (g.Source == GoodSource.Made)
                    demand[g.Index] += craftsmen * g.Weight / totalWeight / g.Labour;
            }
            double labourUsed = 0;
            var downstream = new double[n];   // wanted of each good as an input to others
            for (int k = n - 1; k >= 0; k--)
                foreach (var (input, qty) in cat.Goods[k].Inputs)
                {
                    demand[input] += demand[k] * qty;
                    downstream[input] += demand[k] * qty;
                }
            // A good is finished before anything that uses it is made, so each
            // user gets the same share of what is left after people's needs.
            var ration = new double[n];
            Array.Fill(ration, -1);
            double Ration(int g)
            {
                if (ration[g] < 0)
                    ration[g] = downstream[g] > 0
                        ? Math.Clamp((rg.Produced[g] - rg.Needed[g]) / downstream[g], 0, 1) : 1;
                return ration[g];
            }
            foreach (var g in cat.Goods)
            {
                if (g.Source == GoodSource.ByProduct)
                    rg.Produced[g.Index] += rg.Produced[g.By] * g.Per;
                else if (g.Source == GoodSource.Made && g.AnyInput)
                {
                    // Each input can give its fair share of what is left.
                    var give = g.Inputs.Select(x => Math.Min(Math.Max(0, rg.Produced[x.Good] - rg.Used[x.Good] - rg.Needed[x.Good]),
                        demand[g.Index] * Ration(x.Good))).ToArray();
                    double avail = give.Sum();
                    double made = Math.Min(demand[g.Index], avail);
                    labourUsed += made * g.Labour;
                    rg.Produced[g.Index] += made;
                    if (avail > 0)
                        for (int k = 0; k < g.Inputs.Length; k++)
                            rg.Used[g.Inputs[k].Good] += made * give[k] / avail;
                }
                else if (g.Source == GoodSource.Made)
                {
                    double want = demand[g.Index];
                    double can = 1;
                    foreach (var (input, _) in g.Inputs)
                        can = Math.Min(can, Ration(input));   // people's needs first, then shared fairly
                    double made = want * can;
                    labourUsed += made * g.Labour;
                    rg.Produced[g.Index] += made;
                    foreach (var (input, qty) in g.Inputs)
                        rg.Used[input] += made * qty;
                }
            }
            double value = 0;
            foreach (var g in cat.Goods)
                value += (rg.Produced[g.Index] - rg.Used[g.Index]) * g.Price;
            // Townspeople not busy making goods trade, carry, build and serve.
            rg.Services = Math.Max(0, urban[owner] - labourUsed) * cat.Wage * ServiceShare;
            rg.Value = value + rg.Services;
        }
        return result;
    }
}
