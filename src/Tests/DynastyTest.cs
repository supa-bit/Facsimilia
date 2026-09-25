using System.Threading.Tasks;
using Facsimilia.Dynasties;
using Godot;

namespace Facsimilia.Tests;

/// <summary>Succession: male-preference and plain primogeniture, and a childless ruler's crisis.</summary>
public partial class DynastyTest : TestRunner
{
    protected override Task Run()
    {
        var reg = new CharacterRegistry();

        // Male-preference primogeniture: the younger son beats the older daughter.
        var founder = reg.CreateCharacter("Aldric", "male", 1050);
        var dynasty = reg.CreateDynasty("House Aldric", founder);
        var spouse = reg.CreateCharacter("Elira", "female", 1052);
        CharacterRegistry.Marry(founder, spouse);
        var daughter = reg.HaveChild(spouse, founder, "Beatrix", "female", 1072);
        var son = reg.HaveChild(spouse, founder, "Cedric", "male", 1075);
        Check(founder.DynastyId == dynasty.Id, "founder isn't in his dynasty");
        Check(daughter.FatherId == founder.Id && daughter.MotherId == spouse.Id, "child's parents are wrong");

        var malePref = reg.CreateRealm("Aldric Realm", founder, SuccessionLaw.MalePreferencePrimogeniture, Colors.Gray);
        var heir = reg.HandleRulerDeath(malePref, 1090);
        Check(heir?.Id == son.Id, "male preference should pick the son");
        Check(!founder.IsAlive, "the ruler should be dead");
        Check(malePref.RulerId == son.Id, "the son should rule");

        // Plain primogeniture: the eldest child wins regardless of gender.
        var founder2 = reg.CreateCharacter("Osric", "male", 1040);
        reg.CreateDynasty("House Osric", founder2);
        var spouse2 = reg.CreateCharacter("Mira", "female", 1043);
        CharacterRegistry.Marry(founder2, spouse2);
        var eldestDaughter = reg.HaveChild(spouse2, founder2, "Nadia", "female", 1065);
        reg.HaveChild(spouse2, founder2, "Talon", "male", 1068);
        var primo = reg.CreateRealm("Osric Realm", founder2, SuccessionLaw.Primogeniture, Colors.Gray);
        Check(reg.HandleRulerDeath(primo, 1085)?.Id == eldestDaughter.Id, "primogeniture should pick the eldest daughter");

        // Succession crisis: no children means no heir.
        var lone = reg.CreateCharacter("Ivo", "male", 1030);
        reg.CreateDynasty("House Ivo", lone);
        var lonely = reg.CreateRealm("Ivo Realm", lone, SuccessionLaw.Primogeniture, Colors.Gray);
        Check(reg.HandleRulerDeath(lonely, 1080) == null, "a childless ruler has no heir");
        Check(lonely.RulerId == lone.Id, "the realm stays with the dead ruler in a crisis");

        Finish("Dynasty tests passed: male-preference picked the son over the elder daughter, plain primogeniture " +
            "picked the elder daughter, and a childless ruler produced a succession crisis instead of a fake heir.");
        return Task.CompletedTask;
    }
}
