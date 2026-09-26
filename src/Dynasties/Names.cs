using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Facsimilia.Dynasties;

/// <summary>The naming tradition a character is born into.</summary>
public enum Culture { Latin, Punic, Greek, Meroitic, Nabataean, Iberian, Celtic, Scythian, Italic, Etruscan, Thracian, Illyrian, Berber, Persian, Caucasian, Arabian }

/// <summary>
/// Personal names per culture, for children born in play, generated spouses
/// and new ruling houses. Mostly attested names of the period; where records
/// are thin (Iberian, Meroitic, Scythian women) some are plausible forms built
/// from attested name elements rather than recorded individuals.
/// </summary>
public static class Names
{
    static readonly Dictionary<Culture, (string[] Male, string[] Female)> Pools = new()
    {
        [Culture.Latin] = (
            new[] { "Marcus", "Gaius", "Lucius", "Publius", "Quintus", "Titus", "Gnaeus", "Aulus", "Spurius", "Tiberius", "Servius", "Decimus", "Numerius", "Manius", "Sextus" },
            new[] { "Cornelia", "Claudia", "Fabia", "Valeria", "Aemilia", "Julia", "Sempronia", "Livia", "Tullia", "Caecilia", "Fulvia", "Octavia", "Papiria" }),
        [Culture.Punic] = (
            new[] { "Hannibal", "Hasdrubal", "Hamilcar", "Hanno", "Himilco", "Mago", "Bomilcar", "Adherbal", "Gisco", "Bodashtart", "Carthalo", "Maharbal" },
            new[] { "Sophoniba", "Elissa", "Arishat", "Batbaal", "Ammatmilk", "Hanatbaal", "Himilce", "Tanitbaal" }),
        [Culture.Greek] = (
            new[] { "Alexandros", "Philippos", "Ptolemaios", "Antiochos", "Seleukos", "Demetrios", "Antigonos", "Lysimachos", "Kassandros", "Perdikkas", "Pyrrhos", "Magas", "Attalos", "Nikanor", "Menelaos", "Leonnatos", "Agathokles", "Antipatros" },
            new[] { "Berenike", "Arsinoe", "Kleopatra", "Stratonike", "Laodike", "Apama", "Phila", "Eurydike", "Thessalonike", "Nikaia", "Philotera", "Olympias", "Kratesipolis" }),
        [Culture.Meroitic] = (
            new[] { "Arkamani", "Amanislo", "Arnekhamani", "Arqamani", "Adikhalamani", "Tabirqo", "Tanyidamani", "Naqyrinsan", "Aryamani", "Taritekas" },
            new[] { "Nahirqo", "Shanakdakhete", "Amanirenas", "Amanishakheto", "Nawidemak", "Amanitore", "Amanikhatashan" }),
        [Culture.Nabataean] = (
            new[] { "Aretas", "Obodas", "Malichus", "Rabbel", "Syllaeus", "Zaydu", "Taymu", "Aslah", "Wahballahi" },
            new[] { "Huldu", "Shaqilat", "Gamilat", "Hagru", "Saadat", "Kamkam", "Unaishu" }),
        [Culture.Iberian] = (
            new[] { "Indibilis", "Mandonios", "Culchas", "Allucius", "Attenes", "Edeco", "Istolatius", "Orissus", "Bilistages", "Abilux" },
            new[] { "Imilce", "Ilduria", "Neitinbeles", "Iltirbikis", "Baisetas", "Selkibeles" }),
        [Culture.Celtic] = (
            new[] { "Brennos", "Bolgios", "Akichorios", "Luernios", "Bituitos", "Orgetorix", "Dumnorix", "Diviciacos", "Cingetorix", "Ambiorix", "Comontorios", "Kerethrios" },
            new[] { "Onomaris", "Chiomara", "Camma", "Epponina", "Nantosvelta", "Belisama", "Litavicca" }),
        [Culture.Scythian] = (
            new[] { "Ateas", "Agaros", "Skilurus", "Palakos", "Idanthyrsos", "Skyles", "Octamasadas", "Ariapeithes", "Saulios", "Kanitos" },
            new[] { "Opia", "Tomyris", "Zarina", "Amage", "Tirgatao", "Kamasarye" }),
        // Oscan and Umbrian praenomina and names from inscriptions.
        [Culture.Italic] = (
            new[] { "Gellius", "Statius", "Pacius", "Ovius", "Numerius", "Herius", "Marahis", "Vibius", "Trebius", "Minatus", "Stenius", "Papius" },
            new[] { "Paquia", "Vibia", "Ovia", "Minia", "Statia", "Trebia", "Pompedia", "Herennia" }),
        [Culture.Etruscan] = (
            new[] { "Larth", "Arnth", "Vel", "Aule", "Laris", "Sethre", "Thefarie", "Avle", "Marce", "Tite", "Venel" },
            new[] { "Ramtha", "Velia", "Larthia", "Thanchvil", "Fasti", "Hasti", "Seianti", "Ravnthu", "Tanaquil" }),
        // Thracian, Paeonian, Bithynian and Getic kings and nobles.
        [Culture.Thracian] = (
            new[] { "Seuthes", "Teres", "Kotys", "Rhoimetalkes", "Sitalkes", "Sadalas", "Hebryzelmis", "Zipoites", "Nikomedes", "Dromichaites", "Audoleon", "Ariston", "Satokos" },
            new[] { "Berenike", "Tatia", "Zia", "Dizazelmis", "Mezenai", "Beithis", "Mokazeis" }),
        [Culture.Illyrian] = (
            new[] { "Glaukias", "Bardylis", "Kleitos", "Agron", "Pleuratos", "Genthios", "Monunios", "Longaros", "Bato", "Skerdilaidas" },
            new[] { "Teuta", "Beroia", "Triteuta", "Etleva", "Birkenna", "Grabaia", "Tatta" }),
        // Numidian, Moorish, Libyan and Garamantian names from Punic and Latin records.
        [Culture.Berber] = (
            new[] { "Zelalsan", "Ilasan", "Gaia", "Masinissa", "Oezalces", "Syphax", "Ailymas", "Laqumazes", "Baga", "Bocchar", "Micipsa", "Tinmir", "Aghram" },
            new[] { "Tadra", "Tanfust", "Tamezgida", "Tagrawla", "Tiziri", "Tanit-Ammat" }),
        // Old Persian and Median names borne by the Iranian dynasts of Anatolia, Armenia and Media.
        [Culture.Persian] = (
            new[] { "Mithridates", "Ariobarzanes", "Ariarathes", "Ariaramnes", "Orontes", "Sames", "Arsames", "Atropates", "Artabazanes", "Pharnakes", "Datames", "Artavasdes" },
            new[] { "Laodike", "Stateira", "Rhodogune", "Parysatis", "Amastris", "Atossa", "Apama", "Artakama" }),
        // Georgian and Caucasian names from the chronicles and Greek writers.
        [Culture.Caucasian] = (
            new[] { "Parnavaz", "Saurmag", "Mirvan", "Kuji", "Aietes", "Azon", "Oroises", "Kosis", "Arshak", "Pharasmanes" },
            new[] { "Durdzukeli", "Medea", "Zarine", "Tamar", "Nana", "Rusudan" }),
        // South and north Arabian royal names from the inscriptions.
        [Culture.Arabian] = (
            new[] { "Yada'il", "Karib'il", "Sumhu'alay", "Yada'ab", "Shahr", "Waqah'il", "Abkarib", "Ilriyam", "Talmi", "Dhu'aslan", "Yatha'amar", "Dhamar'ali" },
            new[] { "Malikat", "Hawfat", "Barirum", "Ghanamat", "Amat-Wadd", "Bilqis", "Lahay'at" }),
    };

    public static IReadOnlyList<string> Pool(Culture culture, bool male) =>
        male ? Pools[culture].Male : Pools[culture].Female;

    /// <summary>A random name of the culture and sex, avoiding the given names when possible.</summary>
    public static string Pick(Culture culture, bool male, RandomNumberGenerator rng, ICollection<string>? avoid = null)
    {
        var pool = Pool(culture, male);
        var fresh = avoid == null ? pool : pool.Where(n => !avoid.Contains(n)).ToList();
        var from = fresh.Count > 0 ? fresh : pool;
        return from[rng.RandiRange(0, from.Count - 1)];
    }

    /// <summary>
    /// The culture of a realm from its display name, for saves made before
    /// cultures were recorded. Greek is the fallback: most of the map's
    /// realms were Macedonian successor states.
    /// </summary>
    public static Culture ForRealmName(string realmName)
    {
        string n = realmName.ToLowerInvariant();
        if (n.Contains("rom")) return Culture.Latin;
        if (n.Contains("carthag")) return Culture.Punic;
        if (n.Contains("kush") || n.Contains("meroe") || n.Contains("meroë")) return Culture.Meroitic;
        if (n.Contains("nabat")) return Culture.Nabataean;
        if (n.Contains("iberia")) return Culture.Iberian;
        if (n.Contains("gal") || n.Contains("gaul") || n.Contains("celt")) return Culture.Celtic;
        if (n.Contains("scyth") || n.Contains("sarmat")) return Culture.Scythian;
        if (n.Contains("samnit") || n.Contains("lucan") || n.Contains("messap") || n.Contains("veneti")) return Culture.Italic;
        if (n.Contains("etrusc")) return Culture.Etruscan;
        if (n.Contains("thrac") || n.Contains("odrys") || n.Contains("bithyn") || n.Contains("getae") || n.Contains("paeon")) return Culture.Thracian;
        if (n.Contains("illyr") || n.Contains("dardan")) return Culture.Illyrian;
        if (n.Contains("numid") || n.Contains("mauri") || n.Contains("garamant")) return Culture.Berber;
        if (n.Contains("pontus") || n.Contains("cappadoc") || n.Contains("armen") || n.Contains("atropat")) return Culture.Persian;
        if (n.Contains("kartli") || n.Contains("colchis") || n.Contains("albania")) return Culture.Caucasian;
        if (n.Contains("saba") || n.Contains("ma'in") || n.Contains("qataban") || n.Contains("hadram") || n.Contains("lihyan")) return Culture.Arabian;
        return Culture.Greek;
    }

    public static Culture Parse(string value, Culture fallback) =>
        Enum.TryParse(value, out Culture c) ? c : fallback;
}
