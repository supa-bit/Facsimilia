"""Builds data/voice_lines.json: every spoken line in the game.

Ledger answers (12 Oct 2026): What gets a spoken voice = the opening, the
tutorial, new ages, each wonder finished, and a line for every disaster,
war and peace in history's own events; A narrator for each age (an ancient
bard, a medieval chronicler, a modern newsreader); short war cries in the
people's own tongue with English subtitles. The tutorial is spoken by an
adviser at court, in period voice (answer to 'An adviser at court').

Each line: id, voice, plays (when), key (what it belongs to), text (what is
spoken, with eleven_v4 delivery tags in square brackets), subtitle (what is
shown, without tags), lang. Audio goes to assets/voice/<id>.mp3 (take A)
and assets/voice/takes/<id>_b.mp3 (take B).

Run: python3 tools/voice/build_lines.py
"""
import json, re, pathlib

ROOT = pathlib.Path(__file__).resolve().parents[2]
lines = []


def add(id, voice, plays, key, text, lang="en", subtitle=None):
    sub = subtitle or re.sub(r"\[[^\]]*\]\s*", "", text).strip()
    lines.append({"id": id, "voice": voice, "plays": plays, "key": key,
                  "text": text, "subtitle": sub, "lang": lang})


def narrator(year):
    """The bard before 600, the chronicler to 1800, the newsreader after."""
    return "bard" if year < 600 else "chronicler" if year < 1800 else "newsreader"


# ---------------------------------------------------------------- opening
OPENING = [
    ("gobekli", "[slowly] Listen. Before the first field was sown, before the first village, "
     "the hunters of the hills came together. On a ridge above the plain of Harran they raised "
     "great pillars of stone, carved with foxes, boars and vultures, and set them in rings. "
     "It was a place of gathering more than a home: they came to build, to feast, and to remember. "
     "This is Göbekli Tepe, more than eleven thousand years ago, the oldest place we know where "
     "many hands worked as one."),
    ("grain", "In those same hills grew wild wheat. Year after year the people gathered it, "
     "kept the best seed, and sowed it again, until the grass of the hills became einkorn, "
     "and the hunters became farmers. Sheep and goats followed them home. At Jericho, by a "
     "spring in the valley of the Jordan, men raised a wall and a tower of stone, ten thousand "
     "years ago. [pause] Bread had made them stay."),
    ("catal", "On the plain of Konya the houses of Çatalhöyük stood wall against wall, a town "
     "with no streets. Its people walked across the rooftops and climbed down ladders into their "
     "homes. They painted bulls and leopards on the plaster, set horns into the walls, and buried "
     "their dead beneath the floors where they slept. Thousands of people lived there, "
     "for more than a thousand years, and no house was greater than the rest. No palace. No king. "
     "[softly] Not yet."),
    ("rivers", "Then the farmers came down to the great rivers. Between the Tigris and the "
     "Euphrates there was little rain, but there was water, if you could lead it. They dug canals, "
     "and the mud gave back grain a hundredfold. At Eridu they built a shrine to the god of the "
     "sweet waters, and rebuilt it, layer upon layer, each one larger than the last. Copper came "
     "from the mountains. Someone had to share out the water, store the harvest, and judge "
     "between neighbours."),
    ("uruk", "Uruk grew until forty thousand people lived within its walls, the greatest city "
     "the world had seen. Its temples rose on platforms above the plain, and the priests who "
     "kept their storehouses pressed marks into clay to count the barley, the sheep, the beer "
     "and the workers. [pause] Counting became writing. And with writing came those who "
     "commanded what was written."),
    ("polity", "By five thousand years ago the towns had become something new. Kish and Ur, "
     "Lagash and Umma, each with its god, its ruler, its law, its fields and its border, and "
     "an army to hold them. On the Nile a king named Narmer wore the crowns of both Upper and "
     "Lower Egypt. The settlement had become a state: a power that could tax, build, and make "
     "war. [dramatically] Here, history begins."),
    ("bridge", "Three thousand years of kings followed. Bronze empires rose and fell together: "
     "Hittites and Egyptians, Mycenae and Ugarit, until the great collapse swept them away. "
     "Iron came, and alphabets, and coins. Assyria, Babylon and Persia each ruled the world they "
     "knew. Then Alexander of Macedon marched to the edge of India, and died at thirty-two, and "
     "his generals tore his empire apart. [pause] It is the year 300 before Christ. "
     "[dramatically] The world is yours to shape."),
]
for i, (k, t) in enumerate(OPENING):
    add(f"opening_{i + 1:02d}_{k}", "bard", "opening", k, t)

# --------------------------------------------------------------- tutorial
# The adviser at court, in period voice. Keys match Hud.Tutorial.cs.
TIPS = {
    "realm": "This is your treasury, my lord. Heavier taxes fill the chests, but they slow "
             "the growth of your people and stir them to anger. Below, the purity of your "
             "coin, and its worth in your neighbours' money.",
    "goods": "Here is all your people make, and all they need. Where they go short, they "
             "grow restless. Trade along the routes will fill the want.",
    "diplomacy": "A wise ruler finds a just cause before he strikes. A war without a "
                 "pretext angers every court in the world. The offers of other realms wait here.",
    "armies": "Each army has its own companies. Raise them, join them, and march them. "
              "An army must stand within reach before it can lay siege.",
    "research": "Your scholars await your command. Each art opens only in its time, and "
                "only once its foundations are known. Towns, schools and libraries give you scholars.",
    "goals": "These are the aims your history sets before you. Pursue them or not, as you "
             "will. Your fame is counted in people, land, wealth, culture, dynasty and deeds.",
    "realms": "Every realm of the world, side by side. Choose a column to order them; "
              "the chart shows who has led over the years.",
    "province": "A province: its people, its land, its buildings and its garrison. "
                "Your own may be taxed and built upon.",
}
for k, t in TIPS.items():
    add(f"tip_{k}", "adviser", "tip", k, t)

STEPS = [
    "My lord, first look to the treasury: the coins at the top. See what your taxes bring.",
    "Now call your scholars, with T, and choose an art for them to study.",
    "Choose one of your provinces, open its buildings, and set your people to raise something new.",
    "A realm needs swords. Open your armies and raise more soldiers.",
    "Look upon the peoples of your lands: choose the culture view, in the corner below.",
    "Let a year pass, with Advance Year or Enter, and hear what the chronicle tells.",
    "Open diplomacy, and see what just causes you hold against your neighbours.",
    "[firmly] Declare war upon a neighbour, with a pretext if you have one.",
    "Turn to conquest, with the key 3, and mark the land you mean to take, within reach of your army.",
    "Hold firm until a province is won, or make peace through diplomacy.",
    "[warmly] You know the essentials now, my lord. The rest is history, and it is yours to make.",
]
for i, t in enumerate(STEPS):
    add(f"tutorial_{i + 1:02d}", "adviser", "tutorial_step", str(i), t)

# ------------------------------------------------------------------- ages
AGES = [
    ("Antiquity", "bard", "The age of bronze and marble. Kings claim descent from gods, "
     "and the sea is a road for every ambition."),
    ("Late Antiquity", "bard", "[slowly] The old world changes. The emperors move east, new "
     "faiths fill the temples, and peoples beyond the rivers press against the frontiers. "
     "Late Antiquity begins."),
    ("The Middle Ages", "chronicler", "Here begins the account of the Middle Ages. The great "
     "empire of the west is no more; kings, abbots and caliphs divide its lands, and the sword "
     "and the cross and the crescent shall decide what follows."),
    ("The Early Modern Age", "chronicler", "Mark this year well. Printing presses carry words "
     "faster than any messenger, cannon break the oldest walls, and ships set out across oceans "
     "no one has charted. A new age is come."),
    ("The Modern Age", "newsreader", "Good evening. Steam, iron and the printed newspaper are "
     "changing the world faster than any age before. Revolutions have toppled kings. "
     "The Modern Age has begun."),
]
for name, v, t in AGES:
    add("age_" + re.sub(r"[^a-z]+", "_", name.lower()).strip("_"), v, "age", name, t)

# ---------------------------------------------------------------- wonders
# Spoken when a wonder is finished, by anyone; builder-neutral.
WONDERS = {
    "great_pyramid": "[slowly] The last capstone is set. The Great Pyramid rises from the sand "
                     "at Giza, a mountain made by hands, and it will outlast every king who sees it.",
    "hanging_gardens": "In Babylon, gardens climb terrace on terrace into the sky, green above "
                       "the desert, watered from the Euphrates by engines no traveller can explain.",
    "temple_artemis": "The Temple of Artemis at Ephesus stands again, its forest of marble "
                      "columns greater than before. Pilgrims and bankers crowd its steps.",
    "statue_zeus": "At Olympia, Zeus sits enthroned in gold and ivory. If he rose to his feet, "
                   "they say, he would lift the roof from his temple.",
    "mausoleum": "The tomb at Halicarnassus is finished, a temple raised on a tower, crowned "
                 "with a chariot of marble. From now on every great tomb will be called a mausoleum.",
    "colossus": "Over the harbour of Rhodes, the sun god stands in bronze, taller than any "
                "statue ever cast. The ships pass beneath his gaze.",
    "pharos": "On the island of Pharos, the great light is kindled. By night its fire, and by "
              "day its smoke, guide every ship to Alexandria.",
    "library": "The Library of Alexandria opens its doors. Every book that can be bought, "
               "borrowed or copied is gathered here, and scholars come from every land.",
    "serapeum": "The Serapeum of Alexandria is finished: a temple for Serapis, a god for "
                "Greeks and Egyptians alike.",
    "pergamon_altar": "On the hill of Pergamon, the great altar of Zeus is complete. Around "
                      "it, in stone, the gods make war upon the giants.",
    "mausoleum_augustus": "On the Campus Martius rises a tomb for the first of the emperors, "
                          "ringed with evergreen trees, crowned with his statue in bronze.",
    "herods_temple": "The Temple at Jerusalem shines again, rebuilt in white stone and gold, "
                     "the greatest sanctuary of the east.",
    "baalbek": "At Baalbek, the temple of Jupiter is finished, the largest in the Roman world, "
               "raised on stones no other people could move.",
    "al_khazneh": "At Petra, the Treasury stands carved from the rose-red cliff, its face "
                  "greeting every caravan that enters the gorge.",
    "temple_bel": "The temple of Bel is finished at Palmyra, where the caravan roads of east "
                  "and west meet in the desert.",
    "colosseum": "[excited] The great amphitheatre is finished! Fifty thousand voices fill "
                 "it for the opening games, a hundred days of spectacle.",
    "trajans_forum": "The new forum is complete, paid for with conquered gold. Its column "
                     "tells the whole war in stone, spiralling up to the sky.",
    "pantheon": "The Pantheon is finished, temple of all the gods. Its dome of concrete opens "
                "to the sky, and no one will build a greater one for thirteen hundred years.",
    "baths_caracalla": "The great baths are opened: hot rooms and cold, libraries and "
                       "gardens, sixteen hundred bathers at once.",
    "theodosian_walls": "The land walls of Constantinople are complete: a moat, an outer wall "
                        "and an inner wall, and ninety-six towers. They will hold for a thousand years.",
    "hagia_sophia": "[slowly] Hagia Sophia is finished. Its dome seems to hang from heaven on "
                    "a golden chain. The emperor, they say, cried out: Solomon, I have surpassed you.",
    "dome_rock": "Upon the Temple Mount, the Dome of the Rock is complete, its gold and "
                 "mosaics shining over Jerusalem.",
    "damascus_mosque": "The Great Mosque of Damascus is complete, its walls a garden of golden "
                       "mosaic, its minarets calling over the city.",
    "cordoba_mosque": "At Córdoba the great mosque has grown its last hall of arches, red and "
                      "white, a forest of stone in the heart of Andalus.",
    "aachen_chapel": "At Aachen the emperor's chapel is consecrated, its columns brought "
                     "from Ravenna and from Rome.",
    "st_marks": "In Venice, the church of Saint Mark is consecrated, glittering with gold "
                "won by trade across the sea.",
    "notre_dame": "On the island in the Seine, the cathedral of Notre-Dame is finished, its "
                  "towers rising over Paris.",
    "chartres": "Chartres cathedral stands complete. Its windows tell all of scripture in "
                "glass of a blue no one can match.",
    "alhambra": "Above Granada, the red palace is complete: courts of lions and myrtles, "
                "and water running through every hall.",
    "florence_dome": "Brunelleschi's dome is closed over Florence, raised without a frame "
                     "to hold it. Nothing like it has stood since the Pantheon.",
    "topkapi": "The new palace of the sultans stands above the Bosphorus, the seat of "
               "their government and their court.",
    "st_peters": "Saint Peter's in Rome is consecrated, after more than a century of "
                 "building, the greatest church in Christendom.",
    "suleymaniye": "Sinan's mosque for Suleiman is complete, with its schools, its hospital "
                   "and its library, crowning the hills of Istanbul.",
    "escorial": "The Escorial is finished: palace, monastery, library and tomb of kings, "
                "in grey granite in the hills of Castile.",
    "versailles": "The palace of Versailles is complete. The nobles of the realm now live "
                  "at court, under the eye of their king.",
    "st_pauls": "Saint Paul's cathedral is finished, its dome risen over a London rebuilt "
                "from the ashes of the Great Fire.",
    "westminster": "The new Palace of Westminster is complete. Parliament sits again beside "
                   "the Thames, under its great clock tower.",
    "eiffel": "[excited] The tower on the Champ de Mars is complete, three hundred metres "
              "of iron, the tallest structure ever built by human hands.",
}
wonders = json.load(open(ROOT / "data/wonders.json"))["wonders"]
for w in wonders:
    add(f"wonder_{w['id']}", narrator(w["done"]), "wonder_done", w["id"], WONDERS[w["id"]])
missing = [w["id"] for w in wonders if w["id"] not in WONDERS]
assert not missing, missing

# -------------------------------------------------------------- disasters
DISASTERS = [
    "The earth shook at Rhodes, and the great bronze Colossus has fallen, broken at the knees.",
    "As the armies met at Lake Trasimene, an earthquake shook Italy, and no soldier in the fighting felt it.",
    "Fire and smoke boil from the sea between Thera and Therasia, and a new island rises from the waves.",
    "The earth has shaken Antioch, and houses lie fallen in its streets.",
    "An earthquake has ruined Antioch, and many of its people lie beneath the stones.",
    "An earthquake strikes Judaea. Thousands are dead, and the cattle lie in the fields.",
    "[sadly] In a single night, twelve cities of Asia fell. Sardis is in ruins.",
    "The earth has shaken Pompeii and Herculaneum. Temples and houses lie cracked and broken.",
    "[gravely] Vesuvius has woken. A cloud like a great pine rose over the bay, and Pompeii "
    "and Herculaneum lie buried under ash.",
    "Antioch is levelled by an earthquake, while the emperor himself was within its walls.",
    "[sadly] The plague comes from the east with the returning legions. City after city counts its dead.",
    "A new pestilence spreads through the empire. In Alexandria, they say, the living are fewer than the dead.",
    "The earth shook Crete and Egypt, and the sea rose and cast ships onto the rooftops of Alexandria.",
    "[gravely] Antioch is fallen in fire and earthquake, on the very day of its festival.",
    "[gravely] A plague has come from Egypt to Constantinople. Each day the dead are counted in thousands.",
    "An earthquake and a great wave have destroyed Berytus, and its famous school of law.",
    "The plague returns, as it has before, and the cities mourn once more.",
    "Plague is fallen upon Persia, and the King of Kings himself is dead of it.",
    "Plague strikes the army at Amwas, in Palestine, and many of its commanders are dead.",
    "The plague comes again, the last great wave, and empties the cities of the east.",
    "The earth shook in Galilee. Whole cities by the lake have fallen.",
    "An earthquake has struck Damghan, in Persia, and many thousands are dead.",
    "Dvin, the capital of Armenia, is thrown down by an earthquake, and few are left alive.",
    "[sadly] The Nile has failed. Year after year the waters stay low, and Egypt starves. "
    "They call it the Great Calamity.",
    "Aleppo is shaken to the ground. Its walls and its citadel have fallen.",
    "The earth has shaken Catania. The cathedral fell upon the faithful at their prayers.",
    "The Nile is low, and famine lies on Egypt. The poor wander the roads in search of bread.",
    "Earthquake strikes all of Syria, from Acre to Damascus.",
    "The sea has risen over Crete and Egypt, and an earthquake throws down the great lighthouse at Alexandria.",
    "[gravely] A great pestilence comes out of the east, carried by ships. "
    "In every land, a third of the people are dying. Men call it the Great Mortality.",
    "The pestilence has returned. This time, it is the children who die.",
    "Plague is come again to Italy and to Egypt.",
    "An earthquake has struck Naples, and the towns of the Kingdom lie in ruin.",
    "The earth shook Constantinople for days on end. The people call it the Little Day of Judgement.",
    "Plague has come to Venice and Milan. The dead are carried out by the boatload.",
    "Plague came with the armies over the Alps, and the cities of the north of Italy are emptied.",
    "Vesuvius is awake once more, and towns at its foot are buried in fire and ash.",
    "Plague is in Naples. Half the city, they say, is dead.",
    "[gravely] Etna has broken open, and a river of fire flows over its villages to the walls of Catania.",
    "The earth has thrown down the cities of the Val di Noto in Sicily. Catania is in ruins.",
    "Plague has come to Marseille from a ship out of the Levant. Half the city is dead.",
    "Plague is in Messina, brought by a ship from the Morea.",
    "[gravely] Lisbon is fallen. An earthquake, then fire, then a wave from the sea have "
    "destroyed the city, on the morning of All Saints.",
    "Earthquakes strike Syria and Lebanon. Damascus and Safed suffer heavy losses.",
    "Earthquakes have struck Calabria and Messina, month after month. Whole towns are gone.",
    "Plague is reported in Constantinople and in Malta, where the harbour has been closed.",
    "Cholera has reached the Mediterranean. Quarantines are declared in the ports.",
    "Cholera has spread from the pilgrimage at Mecca, along the routes of the steamships.",
    "[gravely] An earthquake and a great wave have destroyed Messina and Reggio. "
    "Tens of thousands are feared dead.",
    "[gravely] A new influenza is sweeping the world in the last months of the war. "
    "No nation is spared.",
    "A massive earthquake has struck Erzincan, in eastern Turkey, in the depths of winter.",
    "An earthquake struck Izmit in the early hours. The death toll is rising.",
    "[gravely] Two great earthquakes have struck southern Turkey and northern Syria. "
    "Rescuers are searching the rubble across the region.",
]
dis = json.load(open(ROOT / "data/disasters.json"))["events"]
assert len(dis) == len(DISASTERS), (len(dis), len(DISASTERS))
for e, t in zip(dis, DISASTERS):
    y = e["year"]
    key = f"{y}:{e['kind']}"
    v = narrator(y)
    add(f"disaster_{'m' if y < 0 else ''}{abs(y)}_{e['kind']}", v, "disaster", key, t)

# ------------------------------------------------------- history's wars
# War: spoken when history's war begins. Peace: when it ends, whoever won.
WARS = [
    ("Rome marches against the Samnites, in the hills of central Italy. The third war between them has begun.",
     "Peace is made between Rome and the Samnites."),
    ("Rome turns north, against the Etruscan cities and their Umbrian allies.",
     "The Etruscan cities lay down their arms. There is peace with Rome."),
    ("Seleucus moves against Demetrius, the Besieger, for the coast of Syria and the passes of Cilicia.",
     "Peace between Seleucus and the house of Antigonus."),
    ("Demetrius the Besieger reaches for the crown of Macedon.",
     "The war for Macedon is over. There is peace among the Greeks."),
    ("Lysimachus crosses the Danube, against Dromichaetes, king of the Getae.",
     "Peace is made between Lysimachus and the Getae."),
    ("Pyrrhus of Epirus and Lysimachus fall upon Macedon, to share it between them.",
     "The war for Macedon is ended."),
    ("Lysimachus marches on the cities of Asia that Demetrius held.",
     "Peace between Lysimachus and Demetrius."),
    ("[dramatically] The last of Alexander's companions go to war: Seleucus, against Lysimachus.",
     "The war of the old kings is over. There is peace."),
    ("Rome marches south, against the Lucanians and the Bruttians.",
     "The Lucanians and the Bruttians make peace with Rome."),
    ("Tarentum has called on Pyrrhus of Epirus, and Rome is at war with the Greeks of Italy.",
     "The war with Tarentum is ended. There is peace in the south of Italy."),
    ("[gravely] The Gauls are on the march. A great host pours south out of the Danube lands, into Macedon and Greece.",
     "The Gallic host is gone from Greece."),
    ("Pyrrhus sails for Italy with his elephants, to fight for Tarentum against Rome.",
     "Pyrrhus makes peace with Rome."),
    ("The First Syrian War begins: Antiochus against Ptolemy, for the lands between them.",
     "The First Syrian War is ended."),
    ("[dramatically] Rome and Carthage are at war, for Sicily. The First Punic War has begun.",
     "Peace between Rome and Carthage. The First Punic War is over."),
    ("The Second Syrian War begins, between the Seleucid king and Ptolemy.",
     "The Second Syrian War is ended, sealed by a royal marriage."),
    ("Ptolemy the Third marches into Syria, to avenge his sister. The Third Syrian War has begun.",
     "The Third Syrian War is ended."),
    ("Rome reaches for Sardinia and Corsica, while Carthage is weak.",
     "Carthage yields, and makes peace with Rome over the islands."),
    ("Hamilcar Barca sails for Spain, to win Carthage a new empire.",
     "The Barcids' war in Spain is ended."),
    ("Rome sends her fleet against Queen Teuta of the Illyrians, and her pirates.",
     "Queen Teuta makes peace with Rome."),
    ("Rome marches north against the Gauls of the Po.",
     "The Gauls of the Po have made peace with Rome."),
    ("The Fourth Syrian War begins, between Antiochus and Ptolemy.",
     "The Fourth Syrian War is ended."),
    ("[dramatically] Hannibal has crossed the Ebro. Rome and Carthage are at war once more.",
     "Peace between Rome and Carthage. The Second Punic War is over."),
    ("Rome lays siege to Syracuse, defended by the engines of Archimedes.",
     "The war for Syracuse is over."),
    ("Antiochus the Great marches south. The Fifth Syrian War has begun.",
     "The Fifth Syrian War is ended."),
    ("Rome crosses the sea against Philip of Macedon.",
     "Peace between Rome and Macedon. The Greeks are declared free."),
    ("Rome goes to war with Antiochus the Great, king of Asia.",
     "Peace between Rome and Antiochus."),
    ("Rome marches into the uplands of Spain, against the Celtiberians.",
     "The war in Spain is over at last."),
    ("The Lusitanians rise against Rome, led by the shepherd Viriathus.",
     "The Lusitanian War is ended."),
    ("[gravely] Rome declares war on Carthage, for the third and last time.",
     "The Third Punic War is over."),
    ("Rome marches into southern Gaul, beyond the Alps.",
     "Peace in southern Gaul."),
    ("Rome goes to war with Jugurtha, king of Numidia.",
     "The war with Jugurtha is ended."),
    ("[dramatically] Mithridates of Pontus has risen against Rome, and the cities of Asia rise with him.",
     "The war with Mithridates is over."),
    ("Rome marches into Armenia, against Tigranes, the King of Kings.",
     "Tigranes makes peace with Rome."),
    ("Pompey marches into Syria. The last of the Seleucids face Rome.",
     "The war in Syria is over."),
    ("Caesar marches into Gaul.",
     "The war in Gaul is over."),
    ("Octavian goes to war with Antony and Cleopatra, for Egypt.",
     "The war for Egypt is over."),
    ("The emperor crosses the Danube against Decebalus and the Dacians.",
     "Peace on the Danube."),
    ("Rome marches into the kingdom of the Nabataeans.",
     "The Nabataean kingdom is at peace with Rome."),
]
goals = json.load(open(ROOT / "data/history_goals.json"))["goals"]
assert len(goals) == len(WARS), (len(goals), len(WARS))
for i, (g, (war, peace)) in enumerate(zip(goals, WARS)):
    key = f"{g['realm']}>{g['target']}@{g['from']}"
    base = f"war_{i + 1:02d}_{g['realm']}_{g['target']}"
    add(base + "_begin", narrator(g["from"]), "war_begin", key, war)
    add(base + "_peace", narrator(g["to"]), "war_peace", key, peace)

# ---------------------------------------------------------- war cries
# Short cries in their own tongues (reconstructed pronunciation), English
# subtitles. Played with the war horn when war is declared or battle joined.
CRIES = [
    ("latin_feri", "latin", "la", "[shouting] Feri! Feri!", "Strike! Strike!"),
    ("latin_ad_arma", "latin", "la", "[shouting] Ad arma! Ad arma!", "To arms! To arms!"),
    ("latin_roma", "latin", "la", "[shouting] Pro Roma! Pro patria!", "For Rome! For the fatherland!"),
    ("greek_alala", "greek", "grc", "[shouting] Alalalai! Alalalai!", "(the Greek war cry)"),
    ("greek_enyalios", "greek", "grc", "[shouting] Enyalie! Alalalai!", "Enyalios! (the god of battle)"),
    ("greek_paides", "greek", "grc", "[shouting] O paides Hellenon, ite!", "Sons of the Greeks, go forward!"),
    ("punic_baal", "punic", "phn", "[shouting] Baal Hammon! Baal Hammon!", "Baal Hammon! (the lord of Carthage)"),
    ("gaulish_taranis", "celtic", "xtg", "[shouting] Taranis! Taranis!", "Taranis! (the thunder god)"),
]
for k, culture, lang, t, sub in CRIES:
    add(f"cry_{k}", "warrior", "war_cry", culture, t, lang=lang, subtitle=sub)

out = {
    "about": __doc__.strip().splitlines()[0] + " Voices: bard (before 600), chronicler "
             "(600-1800), newsreader (from 1800), adviser (tutorial), warrior (war cries).",
    "lines": lines,
}
(ROOT / "data/voice_lines.json").write_text(json.dumps(out, ensure_ascii=False, indent=1) + "\n")
chars = sum(len(l["text"]) for l in lines)
by = {}
for l in lines:
    by[l["voice"]] = by.get(l["voice"], 0) + len(l["text"])
print(f"{len(lines)} lines, {chars} characters ({chars * 2} with two takes)", by)

# A readable copy of the whole script for the designer.
md = ["# The spoken script", "",
      "Every line the game speaks, built by `tools/voice/build_lines.py` "
      "into `data/voice_lines.json`. Words in [brackets] tell the voice how to "
      "speak; they are not spoken.", "",
      f"{len(lines)} lines, {chars:,} characters.", ""]
heads = {"opening": "The opening (the bard)", "tip": "Tips (the adviser at court)",
         "tutorial_step": "First steps (the adviser at court)", "age": "New ages",
         "wonder_done": "Wonders finished", "disaster": "Disasters",
         "war_begin": "History's wars: war begins", "war_peace": "History's wars: peace",
         "war_cry": "War cries (in their own tongue)"}
for plays, head in heads.items():
    md += [f"## {head}", ""]
    for l in lines:
        if l["plays"] == plays:
            extra = f" *({l['subtitle']})*" if l["voice"] == "warrior" else ""
            md.append(f"- **{l['id']}** ({l['voice']}): {l['text']}{extra}")
    md.append("")
(ROOT / "docs/Voice-script.md").write_text("\n".join(md))
