using System.Collections.Generic;
using System.Linq;
using Facsimilia.Dynasties;
using Godot;

namespace Facsimilia.UI;

/// <summary>
/// A person's panel (the Ledger's "Character panel") and hover card: their
/// skills, traits, family, what they think of your ruler and your ruler of
/// them, and their life. Opened by clicking the ruler or heir at the top,
/// or anyone named in another person's panel.
/// </summary>
public partial class Hud
{
    PanelContainer? _personPanel;
    VBoxContainer _personBox = null!;
    int _personId = -1;

    /// <summary>Makes a top-bar stat open a person's panel when clicked.</summary>
    void MakePersonLink(Label stat, System.Func<int> personId)
    {
        var box = stat.GetParent<Control>();
        box.MouseFilter = MouseFilterEnum.Stop;
        box.MouseDefaultCursorShape = CursorShape.PointingHand;
        box.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } && personId() is int id and >= 0)
                ShowPerson(id);
        };
    }

    int RulerId() => _map.Registry.Realms.TryGetValue(_map.PlayerRealmId, out var r) ? r.RulerId : -1;

    int HeirId() => _map.Registry.Realms.TryGetValue(_map.PlayerRealmId, out var r) ? _map.Registry.ResolveHeir(r)?.Id ?? -1 : -1;

    /// <summary>The hover card: who they are, their best skills and their traits.</summary>
    string HoverCard(Character c)
    {
        var reg = _map.Registry;
        int year = _map.DemoYear;
        var points = Enumerable.Range(0, CharSkills.Count).Select(s => reg.SkillPoints(c, s, year)).ToArray();
        var best = Enumerable.Range(0, CharSkills.Count).OrderByDescending(s => points[s]).Take(3)
            .Select(s => $"{CharSkills.Names[s]} {points[s]} ({CharSkills.Word(s, points[s])})");
        var cat = TraitCatalog.Instance;
        var traits = c.Traits.Select(t => cat[t]?.Name ?? t);
        return $"{c.Name}, {Life(c)}\nBest at: {string.Join(", ", best)}\n{string.Join(", ", traits)}\n(Click to open.)";
    }

    string Life(Character c)
    {
        int year = _map.DemoYear;
        return c.IsAlive ? $"aged {c.AgeIn(year)}"
            : $"died {ThemeAncient.YearText(c.DeathYear)} {CharacterRegistry.HowDied(c)} at {c.DeathYear - c.BirthYear}".Replace("  ", " ");
    }

    void ShowPerson(int id)
    {
        var reg = _map.Registry;
        if (!reg.Characters.TryGetValue(id, out var c))
            return;
        if (_personPanel == null)
        {
            _personPanel = SidePanel("A person", () => _personPanel!.Visible = false, 640);
            _personBox = new VBoxContainer();
            _personBox.AddThemeConstantOverride("separation", 6);
            var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(620, 640), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            scroll.AddChild(_personBox);
            _personPanel.GetNode<VBoxContainer>("Box").AddChild(scroll);
        }
        _personId = id;
        foreach (Node child in _personBox.GetChildren())
            child.QueueFree();
        int year = _map.DemoYear;
        reg.Develop(c);
        var cat = TraitCatalog.Instance;

        // Who they are.
        var titles = reg.Realms.Values.Where(r => r.RulerId == c.Id && c.IsAlive).Select(r => r.Name).ToList();
        string dynasty = reg.Dynasties.TryGetValue(c.DynastyId, out var d) ? d.Name : "of no great house";
        AddLine(c.Name, "HeaderLabel", 26);
        AddLine((titles.Count > 0 ? $"Ruler of {string.Join(" and ", titles)}. " : "") +
                $"{dynasty}, {c.Culture}. Born {ThemeAncient.YearText(c.BirthYear)}; {Life(c)}.", "SubtleLabel", 16);

        // Skills.
        AddLine("Skills", "HeaderLabel", 19);
        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 18);
        _personBox.AddChild(grid);
        grid.Columns = 2;
        for (int s = 0; s < CharSkills.Count; s++)
        {
            int points = reg.SkillPoints(c, s, year);
            var l = ThemeAncient.Label($"{CharSkills.Names[s]} {points}: {CharSkills.Word(s, points)}", fontSize: 16);
            l.TooltipText = $"{CharSkills.Names[s]}: {CharSkills.Help[s]}. 50 is ordinary, 90 outstanding.";
            l.MouseFilter = MouseFilterEnum.Pass;
            l.Modulate = points >= 75 ? new Color(1f, 0.85f, 0.45f) : points <= 30 ? new Color(0.8f, 0.6f, 0.55f) : Colors.White;
            grid.AddChild(l);
        }

        // Traits, by kind.
        AddLine("Traits", "HeaderLabel", 19);
        var kinds = new[] { (TraitDef.Reputation, "Reputation"), (TraitDef.Personality, "Nature"), (TraitDef.Faith, "Faith"),
            (TraitDef.Bloodline, "Blood"), (TraitDef.Education, "Schooling"), (TraitDef.Lifestyle, "Pastimes"),
            (TraitDef.Body, "Body"), (TraitDef.HealthKind, "Health"), (TraitDef.Earned, "Deeds") };
        foreach (var (kind, label) in kinds)
        {
            var held = c.Traits.Select(t => cat[t]).OfType<TraitDef>().Where(t => t.Kind == kind).ToList();
            if (held.Count == 0)
                continue;
            var row = new HFlowContainer();
            row.AddThemeConstantOverride("h_separation", 8);
            row.AddChild(ThemeAncient.Label(label + ":", "SubtleLabel", 15));
            foreach (var t in held)
            {
                var chip = ThemeAncient.Label(t.Name, fontSize: 16);
                chip.MouseFilter = MouseFilterEnum.Pass;
                chip.TooltipText = $"{t.Name}: {t.Description}" + TraitEffects(t);
                row.AddChild(chip);
            }
            _personBox.AddChild(row);
        }

        // Family.
        AddLine("Family", "HeaderLabel", 19);
        PersonRow("Father", reg.Father(c));
        PersonRow("Mother", reg.Mother(c));
        PersonRow(c.IsMale ? "Wife" : "Husband", c.SpouseId >= 0 ? reg.Characters.GetValueOrDefault(c.SpouseId) : null);
        foreach (var child in c.ChildrenIds.Select(i => reg.Characters[i]).OrderBy(x => x.BirthYear))
            PersonRow(child.IsMale ? "Son" : "Daughter", child);

        // Bonds with named people.
        if (c.Bonds.Count > 0)
        {
            AddLine("Bonds", "HeaderLabel", 19);
            foreach (var bond in c.Bonds)
                PersonRow(cat.Bond(bond.Kind)?.Name ?? bond.Kind, reg.Characters.GetValueOrDefault(bond.With));
        }

        // What they think of each other.
        if (RulerId() is int rulerId and >= 0 && rulerId != c.Id && c.IsAlive && reg.Characters.TryGetValue(rulerId, out var ruler))
        {
            AddLine("Opinions", "HeaderLabel", 19);
            Opinion($"{c.Name} of {ruler.Name}", reg.OpinionReasons(c, ruler, year));
            Opinion($"{ruler.Name} of {c.Name}", reg.OpinionReasons(ruler, c, year));
        }
        _personPanel.Visible = true;
    }

    static string TraitEffects(TraitDef t)
    {
        var parts = t.Skills.Select(kv => $"{(kv.Value > 0 ? "+" : "")}{kv.Value} {kv.Key}").ToList();
        parts.AddRange(t.Effects.Select(kv => $"{kv.Key} {(kv.Value > 0 ? "+" : "")}{kv.Value * 100:0.#}% as ruler"));
        if (t.Health != 1) parts.Add(t.Health > 1 ? "shortens life" : "lengthens life");
        if (t.Fertility != 1) parts.Add(t.Fertility == 0 ? "no children" : t.Fertility > 1 ? "more children" : "fewer children");
        if (t.Inherit > 0) parts.Add("can pass to children");
        return parts.Count > 0 ? "\n" + string.Join(", ", parts) : "";
    }

    void AddLine(string text, string variation, int size)
    {
        var l = ThemeAncient.Label(text, variation, size);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.CustomMinimumSize = new Vector2(600, 0);
        _personBox.AddChild(l);
    }

    void PersonRow(string relation, Character? who)
    {
        if (who == null)
            return;
        var row = new HBoxContainer();
        row.AddChild(ThemeAncient.Label(relation + ":", "SubtleLabel", 15));
        var link = new LinkButton { Text = $"{who.Name} ({Life(who)})", TooltipText = HoverCard(who), FocusMode = FocusModeEnum.None };
        int id = who.Id;
        link.Pressed += () => ShowPerson(id);
        row.AddChild(link);
        _personBox.AddChild(row);
    }

    void Opinion(string who, List<(string Reason, int Value)> reasons)
    {
        int sum = System.Math.Clamp(reasons.Sum(r => r.Value), -100, 100);
        var l = ThemeAncient.Label($"{who}: {(sum > 0 ? "+" : "")}{sum}", fontSize: 16);
        l.Modulate = sum >= 20 ? new Color(0.7f, 1f, 0.7f) : sum <= -20 ? new Color(1f, 0.65f, 0.6f) : Colors.White;
        l.MouseFilter = MouseFilterEnum.Pass;
        l.TooltipText = string.Join("\n", reasons.Select(r => $"{(r.Value > 0 ? "+" : "")}{r.Value}  {r.Reason}"));
        _personBox.AddChild(l);
        var why = ThemeAncient.Label(string.Join("; ", reasons.Select(r => $"{r.Reason} {(r.Value > 0 ? "+" : "")}{r.Value}")), "SubtleLabel", 14);
        why.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        why.CustomMinimumSize = new Vector2(600, 0);
        _personBox.AddChild(why);
    }
}
