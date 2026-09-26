using System;
using System.Linq;
using System.Threading.Tasks;
using Facsimilia.Game;
using Facsimilia.World;

namespace Facsimilia.Tests;

/// <summary>
/// Diplomacy: 300 BC's treaties and rivalries are in force; pretexts
/// depend on the two realms (a border for neighbours, none against the
/// far-off); declaring war raises aggression and calls in the defender's
/// allies; opinion has its reasons; peace terms cost war score and are
/// accepted only when earned; ceded provinces change hands; exhaustion
/// grows and forces peace; beaten enemies sue with offers; vassals pay
/// tribute; a feared conqueror faces a coalition; and it all survives a save.
/// </summary>
public partial class DiplomacyTest : TestRunner
{
    protected override async Task Run()
    {
        SaveSystemTest.UseCleanTestFolder();
        var map = await WorldFixture.Seeded();
        await map.GenerateProvinces();
        map.StartPopulation();
        map.LoadLand();
        map.SetPlayerCiv("rome");
        map.StartGame();
        int rome = map.CivRealmIds["rome"], greeks = map.CivRealmIds["samnites"], egypt = map.CivRealmIds["egypt"],
            carthage = map.CivRealmIds["carthage"], seleucid = map.CivRealmIds["seleucid"], lysimachus = map.CivRealmIds["lysimachus"],
            nabatea = map.CivRealmIds["nabatea"];
        var g = map.Game;
        Check(g.Treaties.Allied(seleucid, lysimachus), "Seleucus and Lysimachus should be allies in 300 BC");
        Check(map.Rivals(rome, carthage) && map.Opinion(carthage, rome).Reasons.Any(r => r.Contains("rivals")), "Rome and Carthage are old rivals");

        var toGreeks = map.PretextsAgainst(rome, greeks);
        Check(toGreeks.Contains(Pretexts.Border), "Rome borders the Samnites: a border dispute");
        Check(!map.PretextsAgainst(rome, nabatea).Contains(Pretexts.Border), "Rome has no border with the Nabataeans");
        Check(map.PretextsAgainst(rome, nabatea).Last() == Pretexts.None, "war without a pretext is always possible");

        // War on Lysimachus: his allies Seleucus and Ptolemy come in against the player.
        var events = map.DeclareWar(lysimachus, "")!;
        Check(g.Wars.AtWar(rome, seleucid) && g.Wars.AtWar(rome, egypt) && events.Any(e => e.Text.Contains("honours its treaty")),
            "Lysimachus' allies, Seleucus and Ptolemy, should join a war on him");
        g.Wars.MakePeace(g.Wars.Between(rome, lysimachus)!, map.DemoYear);
        Check(!g.Wars.AtWar(rome, seleucid) && !g.Wars.AtWar(rome, egypt), "peace should end the allies' wars too");
        map.PlayerState.Aggression = 0;

        // War on the Samnites on a border pretext: aggression rises.
        map.DeclareWar(greeks, Pretexts.Border.Id);
        Check(g.Wars.AtWar(rome, greeks) && g.Wars.Between(rome, greeks)!.CasusBelli == "border", "war on a border pretext");
        Check(map.PlayerState.Aggression >= Pretexts.Border.AggressionOnDeclare, "declaring war should raise aggression");

        // Peace terms by war score.
        var war = g.Wars.Between(rome, greeks)!;
        var samnium = map.Provinces!.Provinces.Values.First(p => p.Name == "Samnium");
        var terms = new PeaceTerms();
        terms.Provinces.Add(samnium.Id);
        double cost = map.TermsCost(war, rome, terms);
        Check(cost >= PeaceTerms.ProvinceMin, $"a province should cost war score ({cost:0})");
        Check(!map.OfferPeace(greeks, terms).Accepted, "a fresh enemy shouldn't give up a province");
        war.Score = cost + 1;
        var (ok, text) = map.OfferPeace(greeks, terms);
        Check(ok && samnium.RealmId == rome, $"with enough war score they should cede Lucania: {text}");
        Check(!g.Wars.AtWar(rome, greeks), "the war should be over");

        // Exhaustion forces peace.
        map.DeclareWar(carthage, "");
        var punic = g.Wars.Between(rome, carthage)!;
        punic.DefenderExhaustion = Diplomacy.ForcedPeace - 1;
        map.AdvanceYear();
        Check(!g.Wars.AtWar(rome, carthage), "an exhausted realm should be forced to make peace");

        // A beaten enemy sues with an offer.
        g.Wars.MakePeace(g.Wars.Between(rome, greeks) ?? new War(0, 0, 0, false), map.DemoYear);
        var w2 = Diplomacy.Declare(g, rome, nabatea, map.DemoYear, false);
        w2.Score = 40;
        map.AdvanceYear();
        var offer = g.Offers.FirstOrDefault(o => o.From == nabatea && o.Kind == "peace");
        Check(offer != null, "a beaten Nabataea should sue for peace");
        if (offer != null)
        {
            double before = map.PlayerState.Treasury;
            map.AnswerOffer(offer, true);
            Check(!g.Wars.AtWar(rome, nabatea) && map.PlayerState.Treasury >= before, "accepting the offer should end the war");
        }

        // Vassals pay tribute.
        g.Treaties.Add(new Treaty(TreatyKind.Vassal, rome, nabatea, map.DemoYear));
        map.AdvanceYear();
        Check(map.PlayerState.LastVassalTribute > 0 && map.Game.Realm(nabatea).LastVassalTribute < 0, "a vassal should pay its overlord");
        Check(Diplomacy.CanDeclare(g, nabatea, egypt, map.DemoYear) != null, "a vassal makes no wars of its own");

        // A feared conqueror faces a coalition.
        map.PlayerState.Aggression = 90;
        map.AdvanceYear();
        Check(g.Treaties.CoalitionAgainst(rome).Count() >= 2, "Rome's neighbours should band together against a feared conqueror");

        Check(await map.SaveCurrentGame("slot1"), "save failed");
        var map2 = new MapView();
        Check(await map2.LoadSavedGame("slot1"), "load failed");
        Check(map2.Game.Treaties.All.Count == g.Treaties.All.Count && map2.Game.Treaties.OverlordOf(nabatea) == rome
            && Math.Abs(map2.PlayerState.Aggression - map.PlayerState.Aggression) < 1e-6
            && map2.Game.Lost.Count == g.Lost.Count, "treaties, aggression or losses didn't survive the save");

        Finish($"Diplomacy tests passed: treaties and rivals of 300 BC; pretexts; allies called in; terms by war score " +
            $"(Samnium cost {cost:0}); forced peace by exhaustion; offers; vassal tribute; a coalition of " +
            $"{g.Treaties.CoalitionAgainst(rome).Count()} against an aggressive Rome; saves.");
        map.Free();
        map2.Free();
    }
}
