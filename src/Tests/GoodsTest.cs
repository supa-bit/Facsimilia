using System;
using System.Linq;
using System.Threading.Tasks;
using Facsimilia.Game;
using Facsimilia.World;

namespace Facsimilia.Tests;

/// <summary>
/// Goods and recipes on the real 300 BC world: every good on the
/// designer's list is in the catalogue under its family; every realm makes
/// goods from its own land (Kush ivory, Egypt papyrus, Gaul timber);
/// recipes never use more than was made; by-products follow their source;
/// the value of it all is of the size of the old flat output; and taxes
/// are now a share of it.
/// </summary>
public partial class GoodsTest : TestRunner
{
    static readonly string[] DesignerGoods =
    {
        "emmer", "durum_wheat", "wheat", "barley", "millet", "rye", "oats", "lentils", "chickpeas", "peas", "broad_beans", "flax", "hemp",
        "olives", "grapes", "figs", "dates", "pomegranates", "almonds", "walnuts", "vegetables", "herbs",
        "cattle", "sheep", "goats", "pigs", "horses", "donkeys", "mules", "camels", "poultry",
        "milk", "cheese", "wool", "hides", "horn", "bone", "tallow", "dung",
        "fish", "preserved_fish", "shellfish", "honey", "beeswax", "reeds", "logs", "resin", "pitch", "medicinal_plants",
        "threshed_grain", "flour", "meal", "bread", "beer", "fodder",
        "olive_oil", "olive_waste", "wine", "vinegar", "fruit_preserves",
        "flax_fibre", "hemp_fibre", "linen_yarn", "wool_yarn", "linen", "woollen_cloth", "felt", "rope", "sailcloth", "dyed_cloth",
        "planks", "beams", "firewood", "charcoal", "ash",
        "limestone", "marble", "clay", "sand", "lime", "plaster", "bricks", "roof_tiles", "pottery", "amphorae",
        "iron_ore", "copper_ore", "tin_ore", "lead_ore", "silver_ore", "gold_ore", "salt", "sulfur", "cinnabar",
        "bloom_iron", "wrought_iron", "steel", "copper", "tin", "bronze", "lead", "silver", "gold", "nails", "tools", "weapons", "armour",
        "dressed_stone", "timber_frames", "carts", "wheels", "harnesses", "ship_planks", "hulls", "sails", "rigging", "anchors",
        "incense", "perfume", "purple_dye", "glass", "papyrus", "parchment", "jewellery", "worked_ivory", "fine_ceramics",
    };

    protected override async Task Run()
    {
        var map = await WorldFixture.Seeded();
        await map.GenerateProvinces();
        map.StartPopulation();
        map.LoadLand();
        map.StartGame();
        var cat = map.GoodsCatalog()!;
        Check(cat != null, "no goods catalogue");
        var missing = DesignerGoods.Where(g => !cat!.Has(g)).ToList();
        Check(missing.Count == 0, "goods missing from the catalogue: " + string.Join(", ", missing));
        Check(cat!.Families.Count == 13 && cat.Goods.All(g => cat.Families.Any(f => f.Id == g.Family)),
            "every good should belong to one of the 13 families");
        Check(cat.Goods.All(g => g.Price > 0), "every good should have a price");

        double Made(string realm, string good) => map.CensusOf(map.CivRealmIds[realm]).Goods!.Produced[cat[good].Index];
        foreach (var (key, id) in map.CivRealmIds)
        {
            var c = map.CensusOf(id);
            if (!Check(c.Goods != null, $"{key} has no goods"))
                continue;
            var g = c.Goods!;
            Check(cat.Goods.All(x => g.Used[x.Index] <= g.Produced[x.Index] + 1e-6), $"{key} uses more of a good than it made");
            double perPerson = g.Value / c.People;
            Check(perPerson > 20 && perPerson < 200, $"{key} makes {perPerson:0} drachmae a person a year; a working year was worth about 100 (poor oases and forest peoples less)");
        }
        Check(Made("kush", "ivory") > Made("gaul", "ivory") * 10, "Kush, not Gaul, should have the ivory");
        Check(Made("egypt", "papyrus_reeds") > Made("greek_world", "papyrus_reeds") * 10, "papyrus should come from Egypt");
        Check(Made("gaul", "logs") > Made("egypt", "logs") * 10, "Gaul's forests, not Egypt, should give timber");
        Check(Made("rome", "wool") > 0 && Math.Abs(Made("rome", "wool") - Made("rome", "sheep") * cat["wool"].Per) < 1e-6,
            "wool should come with sheep");
        Check(Made("rome", "bread") > 0 && Made("rome", "wine") > 0 && Made("carthage", "pottery") > 0,
            "grain should become bread, grapes wine, and clay pottery");

        // Taxes are a share of what people make.
        var eg = map.CensusOf(map.CivRealmIds["egypt"]);
        Check(Math.Abs(Economy.Output(eg) - eg.Goods!.Value / 6000) < 1e-6, "output should be the goods' value in talents");
        var (tax, tribute) = Economy.Revenue(eg, TaxRate.Normal);
        Check(tax + tribute > 2000 && tax + tribute < 20000, $"Egypt's income {tax + tribute:0} talents is off");

        Finish($"Goods tests passed: {cat.Goods.Count} goods in {cat.Families.Count} families; Egypt's goods are worth " +
            $"{eg.Goods.Value / 6000:N0} talents a year, Rome's {map.CensusOf(map.CivRealmIds["rome"]).Goods!.Value / 6000:N0}.");
        map.Free();
    }
}
