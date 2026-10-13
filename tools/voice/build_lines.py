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


# ---------------------------------------------------- the longer lesson
# Answer of 13 Oct 2026 (use the rest of the free voice time): the opening's
# bridge told at length, scene by scene, for the full history lesson.
LESSON = [
    ("sargon", "Some four thousand three hundred years ago, a cupbearer of the king of Kish rose up and "
     "took the crown. Sargon of Akkad marched from the Upper Sea to the Lower Sea, and for the first time "
     "one man ruled many cities and many peoples. [pause] The first empire was born, and it would not be the last."),
    ("ur", "When Akkad fell, the kings of Ur gathered the land again. They built a great stepped temple to "
     "the moon god, kept accounts of every sheep and every worker, and wrote down the oldest laws we know. "
     "Then famine and invaders came, and Ur too was lamented in song."),
    ("hammurabi", "In Babylon, King Hammurabi set his laws in stone for all to see: an eye for an eye, a "
     "fair price for a boat, a punishment for the careless builder. [pause] Justice, he said, so that "
     "the strong should not oppress the weak."),
    ("hattians", "In the high country of Anatolia lived the Hattians, who worshipped the sun goddess of "
     "Arinna and the storm god of the mountains. Merchants from Assyria came to trade tin and cloth at "
     "Kanesh, and left their letters in clay. Into these lands came a new people, speaking a new tongue."),
    ("hittites", "They took the Hattians' gods, their cities and their name. From Hattusa, behind walls "
     "of great stone, the Hittite kings ruled the land of Hatti. They rode in chariots, wrote in clay, "
     "and swore treaties in the names of a thousand gods. [dramatically] They even sacked Babylon."),
    ("kadesh", "Egypt and Hatti met at Kadesh, on the river Orontes, in the greatest chariot battle the "
     "world had seen. Both kings claimed the victory. [pause] Years later they made peace, and the "
     "treaty they swore is the oldest we still have."),
    ("ugarit", "On the coast of Syria stood Ugarit, a city of merchants. Ships from Cyprus and Crete "
     "crowded its harbour, and its scribes wrote in many scripts, among them an alphabet of only thirty "
     "letters. Kings across the world wrote to one another as brothers."),
    ("collapse", "[gravely] Then, some three thousand two hundred years ago, it all came apart. Drought "
     "and famine, earthquakes, raiders from the sea. The last letters of Ugarit beg for ships and "
     "soldiers that never came. Hattusa burned. Mycenae fell silent. Egypt alone held on, and barely."),
    ("iron", "Out of the ruins came smaller peoples and new ideas: Phoenicians who sailed to the edge of "
     "the world, Israelites with their one god, Greeks with their alphabet and their cities. And iron, "
     "which any village smith could work, armed whole peoples."),
    ("empires", "Assyria rose, and ruled by terror from Nineveh. Babylon rose again, and hung its gardens "
     "over the Euphrates. Then Cyrus the Persian took them all, and his heirs ruled from the Aegean to "
     "the Indus, by roads and by letters and by the King's peace."),
    ("alexander", "[dramatically] And then a young king came out of Macedon. In twelve years Alexander "
     "crossed the Persian empire from end to end, and reached the rivers of India. At Babylon, at "
     "thirty-two, he died. [pause] Asked to whom he left his empire, he said: to the strongest."),
]
for i, (k, t) in enumerate(LESSON):
    add(f"lesson_{i + 1:02d}_{k}", "bard", "lesson", k, t)

# ------------------------------------------------------- moments in play
# The narrator of the age speaks of your own realm at the game's moments.
# Generic lines (no names); the chronicle shows the particulars.
MOMENTS = {
    "war_declared": {
        "bard": ["We have gone to war. Let the gods judge between us.",
                 "[dramatically] The spears are taken down from the walls. We march to war.",
                 "War is declared. May our cause be as strong as our arms."],
        "chronicler": ["In this year war was declared, and the realm made ready.",
                       "The banners are raised; our lord has gone to war.",
                       "War is proclaimed, and the levies gather."],
        "newsreader": ["Good evening. The government has declared war.",
                       "War has been declared. Mobilisation is under way.",
                       "Our nation is at war tonight."],
    },
    "war_on_us": {
        "bard": ["[gravely] An enemy marches against us. To arms!",
                 "War has come to our borders, unasked.",
                 "They have declared war upon us. Gather the men."],
        "chronicler": ["In this year an enemy made war upon the realm.",
                       "War is come upon us, and the people are afraid.",
                       "Our neighbours have broken the peace and taken up arms against us."],
        "newsreader": ["We interrupt this broadcast. A foreign power has declared war on our nation.",
                       "War has been declared upon us. The army is on alert.",
                       "Hostile forces have crossed our border."],
    },
    "peace": {
        "bard": ["The war is over. Let the fields be sown again.",
                 "Peace is sworn, and the gates are opened.",
                 "[softly] The spears are laid down. There is peace."],
        "chronicler": ["In this year peace was made, and the realm gave thanks.",
                       "A treaty is sealed, and the war is ended.",
                       "Peace is restored, praise be."],
        "newsreader": ["A peace treaty has been signed. The war is over.",
                       "Hostilities have ended this evening.",
                       "Crowds have gathered in the streets to celebrate the peace."],
    },
    "battle_won": {
        "bard": ["[excited] Victory! The enemy breaks and flees from the field.",
                 "The field is ours. The enemy's standards lie in the dust.",
                 "Our army has won the day. Songs will be sung of it."],
        "chronicler": ["In this battle our host prevailed, and the enemy was put to flight.",
                       "A great victory, and many prisoners taken.",
                       "The day is ours, and the enemy is scattered."],
        "newsreader": ["Reports from the front tell of a decisive victory.",
                       "Our forces have won a major battle today.",
                       "The enemy is in retreat after a hard-fought engagement."],
    },
    "battle_lost": {
        "bard": ["[sadly] Our army is broken. The survivors stream home.",
                 "The day is lost, and many brave men with it.",
                 "Defeat. The enemy holds the field."],
        "chronicler": ["In this battle our host was overthrown, and many were slain.",
                       "A heavy defeat, and great lamentation in the land.",
                       "The enemy prevailed, and our army fled."],
        "newsreader": ["Our forces have suffered a serious defeat.",
                       "Heavy losses are reported after today's battle.",
                       "The army is falling back after a grave reverse."],
    },
    "siege_begun": {
        "bard": ["The walls are surrounded. Now begins the long hunger.",
                 "We lay siege. Let them count their grain."],
        "chronicler": ["The city is besieged, and its gates are shut.",
                       "Our army sits down before the walls."],
        "newsreader": ["The city is under siege tonight.",
                       "Our forces have encircled the city."],
    },
    "city_taken": {
        "bard": ["[excited] The gates are open. The city is ours!",
                 "The walls have fallen, and the city bows to us."],
        "chronicler": ["The city is taken, and its keys are brought to our lord.",
                       "After long siege, the city has surrendered."],
        "newsreader": ["The city has fallen to our forces.",
                       "Our troops have entered the city."],
    },
    "city_lost": {
        "bard": ["[sadly] The city has fallen to the enemy.",
                 "Our city is lost. Its people weep in the streets."],
        "chronicler": ["The city is lost to the enemy, God help its people.",
                       "Our city has surrendered to the foe."],
        "newsreader": ["The city has fallen to enemy forces.",
                       "We have lost the city after a long siege."],
    },
    "revolt": {
        "bard": ["[gravely] The people have risen. A province is in revolt.",
                 "Rebellion! They have thrown off our rule.",
                 "Smoke rises over the province. The people are in arms."],
        "chronicler": ["In this year the province rose in rebellion.",
                       "The commons are risen, and the lord's officers are driven out.",
                       "Rebellion breaks out, and there is great disorder."],
        "newsreader": ["Unrest has turned to open revolt in one of our provinces.",
                       "Rioting is reported, and the authorities have lost control.",
                       "A rebellion has broken out tonight."],
    },
    "ruler_died": {
        "bard": ["[sadly] The king is dead. Let the mourning begin.",
                 "Our ruler has gone to the gods."],
        "chronicler": ["In this year our lord departed this life. May he rest in peace.",
                       "The ruler is dead, and the bells are tolled."],
        "newsreader": ["We regret to announce the death of our head of state.",
                       "The nation is in mourning tonight."],
    },
    "new_ruler": {
        "bard": ["A new ruler takes the throne. Long may they reign.",
                 "The crown passes on. Hail the new ruler!"],
        "chronicler": ["The heir is crowned, and the lords swear their oaths.",
                       "A new reign begins."],
        "newsreader": ["A new head of state has taken office.",
                       "The new ruler has been sworn in today."],
    },
    "new_house": {
        "bard": ["The old line is ended. A new house rules.",
                 "No heir remained. Another family takes the throne."],
        "chronicler": ["The old dynasty is no more, and a new house is raised to the throne.",
                       "With no heir of the blood, the crown passes to another house."],
        "newsreader": ["A new dynasty has come to power.",
                       "The succession has passed to a new family."],
    },
    "heir_born": {
        "bard": ["[warmly] A child is born to the ruler's house.",
                 "The house has an heir. Let there be feasting."],
        "chronicler": ["In this year a child was born to our lord, and there was rejoicing.",
                       "An heir is born, and the bells are rung."],
        "newsreader": ["The royal family has announced a birth.",
                       "A new heir has been born to the ruling house."],
    },
    "marriage": {
        "bard": ["A marriage is made, and two houses are joined.",
                 "Wedding songs ring through the palace."],
        "chronicler": ["In this year a marriage was made, binding two great houses.",
                       "The wedding is celebrated with great pomp."],
        "newsreader": ["A royal wedding took place today.",
                       "The marriage was celebrated before a large crowd."],
    },
    "tech_learnt": {
        "bard": ["Our scholars have learned a new art.",
                 "[curious] A new art is mastered. Who knows what it will bring?",
                 "The wise have found a new way. The realm grows stronger."],
        "chronicler": ["In this year our learned men mastered a new art.",
                       "New knowledge is come to the realm.",
                       "The scholars report a new discovery."],
        "newsreader": ["Scientists have announced a breakthrough.",
                       "A new invention is changing the way we live.",
                       "Researchers report a major advance."],
    },
    "goal_reached": {
        "bard": ["[warmly] An ambition is fulfilled. History will remember it.",
                 "What our forebears dreamed, we have done."],
        "chronicler": ["A great aim of the realm is accomplished.",
                       "Let it be written: the ambition is achieved."],
        "newsreader": ["A historic goal has been achieved.",
                       "The government has hailed a historic achievement."],
    },
    "building_done": {
        "bard": ["The work is finished, and the builders are paid.",
                 "New walls rise where there were none."],
        "chronicler": ["The building is completed.",
                       "The masons have finished their work."],
        "newsreader": ["The new building has been opened.",
                       "Construction has been completed."],
    },
    "wonder_begun": {
        "bard": ["A great work is begun. It will outlast us all.",
                 "The foundations are laid of a wonder of the world."],
        "chronicler": ["In this year the foundations were laid of a great work.",
                       "A wonder is begun, and many hands are called to it."],
        "newsreader": ["Work has begun on an ambitious new monument.",
                       "Construction has started on a landmark project."],
    },
    "wonder_fell": {
        "bard": ["[sadly] A wonder of the world has fallen.",
                 "What was raised to last forever lies in ruins."],
        "chronicler": ["A great work has fallen, and the people mourn it.",
                       "The wonder lies in ruins."],
        "newsreader": ["A famous landmark has been destroyed.",
                       "One of the world's great monuments has collapsed."],
    },
    "debt": {
        "bard": ["The treasury is empty. We live on borrowed silver.",
                 "Our debts grow heavier each year."],
        "chronicler": ["The treasury is bare, and the realm is in debt.",
                       "The lenders press for their money."],
        "newsreader": ["The national debt has reached a new high.",
                       "The treasury has run out of funds."],
    },
    "tribute": {
        "bard": ["Tribute arrives, silver and gifts.",
                 "Our enemies pay what they owe."],
        "chronicler": ["Tribute is brought to the treasury.",
                       "The tribute is paid in full."],
        "newsreader": ["Reparations have been received.",
                       "The payment has arrived in full."],
    },
    "trade_treaty": {
        "bard": ["Merchants will come and go in peace. A treaty is made.",
                 "Our markets are opened to their ships."],
        "chronicler": ["A treaty of trade is concluded.",
                       "The merchants rejoice at the new agreement."],
        "newsreader": ["A trade agreement has been signed.",
                       "The new trade deal opens our markets."],
    },
    "coin_debased": {
        "bard": ["The mint has thinned the silver in our coin.",
                 "Less silver, more coins. The merchants will notice."],
        "chronicler": ["The coinage is debased, and prices rise.",
                       "The money is made lighter, and the people grumble."],
        "newsreader": ["The currency has been devalued.",
                       "The government has cut the value of the currency."],
    },
    "mercenaries": {
        "bard": ["Hired swords join our army, for a price.",
                 "Mercenaries are paid, and they march with us."],
        "chronicler": ["Companies of hired soldiers enter our service.",
                       "The mercenaries are paid their wages."],
        "newsreader": ["Foreign volunteers have joined our forces.",
                       "Contract soldiers have been hired."],
    },
    "alliance": {
        "bard": ["An alliance is sworn. Their enemies are ours.",
                 "Oaths are sworn over the sacrifice. We stand together."],
        "chronicler": ["A league is sworn, and the realms are bound in friendship.",
                       "An alliance is made, sealed with oaths."],
        "newsreader": ["A new alliance has been signed.",
                       "The two nations have agreed to stand together."],
    },
    "plague": {
        "bard": ["[gravely] Plague is among us. The dead are many.",
                 "Sickness spreads through the cities."],
        "chronicler": ["The pestilence is come upon the realm.",
                       "Plague, and the dead lie unburied."],
        "newsreader": ["An epidemic is spreading through the country.",
                       "Health officials report a serious outbreak."],
    },
    "earthquake": {
        "bard": ["The earth has shaken, and houses have fallen.",
                 "[gravely] An earthquake strikes. The gods are angry."],
        "chronicler": ["The earth shook, and many buildings fell.",
                       "An earthquake has struck the realm."],
        "newsreader": ["A powerful earthquake has struck.",
                       "Rescue teams are searching collapsed buildings."],
    },
    "famine": {
        "bard": ["[sadly] The harvest has failed. Hunger walks the land.",
                 "The granaries are empty."],
        "chronicler": ["Famine is in the land, and bread is dear.",
                       "The harvest failed, and the poor are starving."],
        "newsreader": ["Food shortages are reported across the country.",
                       "The harvest has failed, and prices have soared."],
    },
    "eruption": {
        "bard": ["The mountain spits fire, and ash falls like snow.",
                 "A volcano has woken."],
        "chronicler": ["The mountain has burst into fire.",
                       "Fire pours from the mountain, and ash darkens the sky."],
        "newsreader": ["A volcanic eruption has forced thousands to flee.",
                       "The volcano has erupted."],
    },
    "game_end": {
        "bard": ["[slowly] So ends our tale. Others will tell what came after.",
                 "The story is told. Let it be remembered."],
        "chronicler": ["Here ends the chronicle.",
                       "Thus ends the account of these years."],
        "newsreader": ["And that is the end of the news.",
                       "That is all for tonight. Good night."],
    },
}
# Answer of 13 Oct 2026 (use the rest of the credits for more lines):
# more moments, two ways each for each narrator.
MOMENTS.update({
    "naval_won": {
        "bard": ["The sea is ours. Their ships burn on the water.",
                 "Our oars beat faster, and their fleet is broken."],
        "chronicler": ["In this year our fleet met the enemy at sea and prevailed.",
                       "The enemy's ships are taken or sunk."],
        "newsreader": ["Our navy has won a decisive battle at sea.",
                       "The enemy fleet has been defeated."],
    },
    "naval_lost": {
        "bard": ["[sadly] The sea has taken our ships, and our sailors with them.",
                 "Our fleet is scattered, and the enemy rules the waves."],
        "chronicler": ["Our fleet was defeated at sea, and many ships were lost.",
                       "The enemy has broken our fleet."],
        "newsreader": ["Our navy has suffered a heavy defeat.",
                       "Several of our ships have been lost at sea."],
    },
    "fleet_storm": {
        "bard": ["The sea god is angry. A storm has swallowed our ships.",
                 "Wind and wave have done what no enemy could."],
        "chronicler": ["A great storm arose, and many of our ships were wrecked.",
                       "The fleet was lost to the tempest."],
        "newsreader": ["A storm has sunk part of our fleet.",
                       "Severe weather has caused heavy losses at sea."],
    },
    "army_destroyed": {
        "bard": ["[gravely] An army of ours is no more. Mourn them.",
                 "Not one company came home."],
        "chronicler": ["Our army was destroyed, and few escaped.",
                       "The host is lost, and the land laments."],
        "newsreader": ["One of our armies has been destroyed.",
                       "Heavy casualties are reported after the collapse of our forces."],
    },
    "general_died": {
        "bard": ["A great captain has fallen. The soldiers weep.",
                 "Our general is dead, and the camp is silent."],
        "chronicler": ["Our general fell in this year, and was mourned by his men.",
                       "The commander of our army is dead."],
        "newsreader": ["A senior general has died.",
                       "The army is mourning the loss of its commander."],
    },
    "revolt_crushed": {
        "bard": ["The rebels are scattered. Order returns.",
                 "The rising is put down, and the leaders are taken."],
        "chronicler": ["The rebellion was crushed, and peace restored.",
                       "The rebels were defeated and dispersed."],
        "newsreader": ["The uprising has been put down.",
                       "Government forces have restored order."],
    },
    "independence": {
        "bard": ["A province has broken away. It answers to us no more.",
                 "They have thrown off our rule, and named their own lord."],
        "chronicler": ["In this year a province threw off our rule.",
                       "The rebels have made themselves masters of their land."],
        "newsreader": ["A region has declared its independence.",
                       "A new state has proclaimed itself on our former territory."],
    },
    "vassal_gained": {
        "bard": ["They bow before us now, and pay what they owe.",
                 "A new realm swears to follow us."],
        "chronicler": ["A lesser lord has sworn fealty to our crown.",
                       "They are become our vassals, and owe us tribute."],
        "newsreader": ["A neighbouring state has accepted our protection.",
                       "A new client state has signed a treaty of subordination."],
    },
    "alliance_broken": {
        "bard": ["[angrily] They have broken their oath. Let the gods punish them.",
                 "The alliance is over. Friends become strangers."],
        "chronicler": ["The alliance was broken, and the oaths forgotten.",
                       "Our former allies have turned from us."],
        "newsreader": ["The alliance has collapsed.",
                       "Our former ally has withdrawn from the treaty."],
    },
    "truce_broken": {
        "bard": ["The peace is broken. Who can trust their word now?",
                 "They swore peace, and now they march again."],
        "chronicler": ["The truce was broken, and the war renewed.",
                       "The peace has not held."],
        "newsreader": ["The ceasefire has broken down.",
                       "Fighting has resumed despite the truce."],
    },
    "trade_route": {
        "bard": ["New roads for our merchants, and new markets for our goods.",
                 "Caravans and ships carry our wares to distant lands."],
        "chronicler": ["A new road of trade was opened in this year.",
                       "Our merchants travel to new markets."],
        "newsreader": ["A new trade route has opened.",
                       "Exports are expected to grow along the new route."],
    },
    "pirates": {
        "bard": ["Pirates prowl our coasts and take our ships.",
                 "Sea wolves raid our harbours."],
        "chronicler": ["Pirates have attacked our merchants at sea.",
                       "The coasts are troubled by raiders from the sea."],
        "newsreader": ["Piracy is disrupting our shipping.",
                       "Pirates have seized another merchant vessel."],
    },
    "raid": {
        "bard": ["Raiders came in the night, and burned the fields.",
                 "[gravely] The villages smoke. The raiders have come and gone."],
        "chronicler": ["Raiders have plundered our borderlands.",
                       "The marches were ravaged by the enemy."],
        "newsreader": ["Border regions have been attacked by raiders.",
                       "Villages near the border have been looted."],
    },
    "flood": {
        "bard": ["The river has risen, and swallowed the fields.",
                 "The waters cover the land, and the people flee to the hills."],
        "chronicler": ["A great flood came, and the river covered the land.",
                       "The waters rose, and many houses were carried away."],
        "newsreader": ["Severe flooding has hit the region.",
                       "Thousands have been forced from their homes by rising waters."],
    },
    "drought": {
        "bard": ["No rain has fallen. The earth cracks and the wells run dry.",
                 "The sky is brass, and the fields are dust."],
        "chronicler": ["There was great drought in this year, and the crops withered.",
                       "No rain fell, and the rivers shrank."],
        "newsreader": ["A severe drought is affecting the harvest.",
                       "Water shortages are reported after months without rain."],
    },
    "fire": {
        "bard": ["Fire runs through the city, from roof to roof.",
                 "[gravely] The city burns, and the people flee."],
        "chronicler": ["A great fire broke out in the city.",
                       "Much of the city was consumed by fire."],
        "newsreader": ["A major fire has swept through the city.",
                       "Firefighters are battling a huge blaze."],
    },
    "good_harvest": {
        "bard": ["[warmly] The granaries overflow. The gods are kind this year.",
                 "A golden harvest. Let the people feast."],
        "chronicler": ["The harvest was plentiful, and bread was cheap.",
                       "God gave a good harvest in this year."],
        "newsreader": ["Farmers report a record harvest.",
                       "Food prices are falling after an excellent harvest."],
    },
    "golden_age": {
        "bard": ["[warmly] These are the golden days. Songs will be sung of them.",
                 "The realm has never been so rich, nor so proud."],
        "chronicler": ["The realm flourishes as never before.",
                       "These were years of plenty and of peace."],
        "newsreader": ["The country is enjoying a period of unprecedented prosperity.",
                       "Economists are calling it a golden age."],
    },
    "assassination": {
        "bard": ["[gravely] A knife in the dark. A great one is murdered.",
                 "Treachery! Blood is spilled in the palace."],
        "chronicler": ["In this year a great lord was murdered by his enemies.",
                       "He was slain by treachery."],
        "newsreader": ["A prominent figure has been assassinated.",
                       "Police are investigating the killing."],
    },
    "civil_war": {
        "bard": ["[gravely] Brother fights brother. The realm is torn in two.",
                 "Our own people have taken up arms against us."],
        "chronicler": ["Civil war broke out in the realm.",
                       "The kingdom is divided, and each side has its army."],
        "newsreader": ["The country has descended into civil war.",
                       "Rival factions are fighting for control of the state."],
    },
    "integrated": {
        "bard": ["They speak our tongue now, and honour our gods.",
                 "The conquered have become our own."],
        "chronicler": ["The people of the province now hold to our ways.",
                       "The conquered province is at last at one with the realm."],
        "newsreader": ["The province is now fully integrated.",
                       "Officials say the region has been successfully absorbed."],
    },
    "city_founded": {
        "bard": ["A new city rises. May it stand a thousand years.",
                 "We have marked out the walls of a new town."],
        "chronicler": ["In this year a new town was founded.",
                       "A city was built where there was none."],
        "newsreader": ["A new city has been officially founded.",
                       "Construction has begun on a planned new town."],
    },
    "religion": {
        "bard": ["New gods come to our land, and the people listen.",
                 "Strange priests preach a new faith in our cities."],
        "chronicler": ["A new faith spreads among the people.",
                       "Many have turned to a new religion."],
        "newsreader": ["A new religious movement is gaining followers.",
                       "Religious change is sweeping the country."],
    },
    "great_victory": {
        "bard": ["[dramatically] A victory for the ages! Our name will be remembered.",
                 "Never was such a victory, nor such a defeat for our foes."],
        "chronicler": ["In this year was won a great victory, the like of which was never seen.",
                       "The enemy was utterly routed."],
        "newsreader": ["In a historic victory, our forces have routed the enemy.",
                       "Celebrations are breaking out across the country."],
    },
})

for moment, by in MOMENTS.items():
    for v, texts in by.items():
        for i, t in enumerate(texts):
            add(f"moment_{moment}_{v}_{i + 1}", v, "moment", moment, t)

# --------------------------------------------------- the adviser's lessons
# Answer 'A guided first game covering every system over its first years'.
LESSONS = {
    "taxes": "Taxes are the lifeblood of the realm, my lord, but bleed the people too much and they "
             "will not grow, nor love you. Raise them in war, lower them in peace.",
    "goods": "Every province makes something: grain, wine, timber, iron. What we lack, we must buy, "
             "and what we have in plenty, we may sell. A hungry city is a dangerous city.",
    "trade": "Merchants follow the routes by land and sea. Send your own along them, and keep the "
             "pirates away, and silver will flow into your markets.",
    "coin": "Our coin is trusted for its silver. Thin the silver and you may pay more soldiers today, "
            "but prices will rise, and the merchants will remember.",
    "debt": "If the treasury runs dry, we may borrow, but lenders want their silver back, with more. "
            "Debt is a fine servant and a cruel master.",
    "people": "Your people are farmers, craftsmen, merchants and slaves, and each want different "
              "things. Feed them, keep them safe, and they will grow in number.",
    "cultures": "Not all your subjects share your tongue and your gods. Conquered peoples need years "
                "to accept your rule. Be patient, or be ready for rebellion.",
    "unrest": "Watch the unrest in your provinces, my lord. Heavy taxes, hunger, war and foreign rule "
              "all stir it. When it boils over, the province rises.",
    "buildings": "Build granaries against famine, walls against raiders, markets for trade and temples "
                 "for the gods. Each takes silver and time, and some need what we have not yet learned.",
    "roads": "A good road brings your soldiers, your tax collectors and your merchants faster. Draw one "
             "from town to town, and the far provinces will feel nearer.",
    "provinces": "Each province may be given a purpose: farms, mines, trade or soldiers. And when they "
                 "grow too large or too small, you may redraw them.",
    "research": "Your scholars study one art at a time. Each opens the way to others. Choose those that "
                "serve your aims, and build schools and libraries to find more scholars.",
    "armies": "An army is made of companies, each with its arms and its training. Keep them paid and "
              "fed, or they will melt away, or worse, turn on you.",
    "manpower": "Every soldier is a man taken from the fields. Our manpower refills slowly. Spend it "
                "carelessly, and you will have no army when you need one most.",
    "generals": "A good general is worth a legion. Choose them for their skill, not their birth, and "
                "watch that none grows more loved by the soldiers than you are.",
    "marching": "Armies march slowly over mountains and swiftly along roads and rivers. Plan your "
                "campaigns with the seasons; winter kills more men than battle.",
    "supply": "An army eats its way across the land. Far from home, without supply, it will starve. "
              "Keep it near your own provinces, or near the sea and your ships.",
    "sieges": "Walled cities do not fall in a day. Surround them, starve them, and storm them only when "
              "the walls are breached. Patience takes more cities than courage.",
    "fleets": "Ships carry your armies and guard your merchants. But the sea is cruel in winter, and "
              "storms have sunk more fleets than any enemy.",
    "pretexts": "Before you strike, find a just cause: an old claim, an insult, a broken treaty. The "
                "other kings are watching, and they remember who starts wars without one.",
    "peace": "When the war has gone on long enough, offer terms. Take what you have won, demand "
             "tribute, or make them your vassals. A war too long ruins the victor too.",
    "allies": "Allies will come to your aid, and you to theirs. Choose them well; an alliance with the "
              "weak drags you into their wars.",
    "court": "Your family is your strength and your danger. Marry them wisely, educate your heirs, and "
             "keep an eye on ambitious cousins.",
    "wonders": "A wonder of the world brings glory, pilgrims and the favour of the gods. But they cost "
               "a fortune and take a generation to build.",
    "history": "History pulls the world along its old course. Other realms will try to do what they did "
               "in their time. You may follow history, or break it.",
    "goals": "Your realm has ambitions of its own, as it did in history. Fulfil them, and your fame "
             "will grow. Ignore them, and choose your own.",
    "score": "In the end, your reign will be judged: by your people, your lands, your wealth, your "
             "culture, your dynasty and your deeds.",
}
for k, t in LESSONS.items():
    add(f"lesson_adviser_{k}", "adviser", "lesson_adviser", k, t)

# ------------------------------------------------------- adviser warnings
ALERTS = {
    "idle_army": "My lord, an army stands idle. Shall it march, or be sent home to the fields?",
    "empty_treasury": "My lord, the treasury is nearly empty.",
    "unrest": "My lord, a province is close to revolt.",
    "shortage": "My lord, our people go short of what they need.",
    "unpaid_army": "My lord, the soldiers have not been paid. They grumble.",
    "manpower_low": "My lord, there are few men left to fill the ranks.",
    "no_research": "My lord, the scholars sit idle. Give them something to study.",
    "war_exhaustion": "My lord, the people are weary of this war.",
    "rival_arming": "My lord, our rival is gathering armies on our border.",
    "siege_starving": "My lord, the besieged city is starving. It will not hold much longer.",
    "loan_due": "My lord, a loan falls due, and the lenders are waiting.",
    "heir_missing": "My lord, you have no heir. Think of the succession.",
    "enemy_near": "My lord, an enemy army is marching towards us.",
    "pirates_near": "My lord, pirates are troubling our coasts.",
    "plague_near": "My lord, a plague rages in a neighbouring land. It may reach us.",
    "winter": "My lord, winter is coming. The armies should seek their quarters.",
    "storm_season": "My lord, the sea is dangerous in this season. Keep the ships in harbour.",
    "ally_asks": "My lord, our ally calls for our help.",
    "truce_ending": "My lord, the truce will soon end.",
    "coin_distrusted": "My lord, the merchants no longer trust our coin.",
    "general_ambitious": "My lord, a general grows too popular with his soldiers.",
    "court_plot": "My lord, there are whispers of a plot at court.",
    "fleet_idle": "My lord, our fleet lies idle in harbour.",
    "research_done": "My lord, the scholars have finished their work. What shall they study next?",
    "building_done": "My lord, the builders have finished. What shall they build next?",
    "debt_high": "My lord, our debts are growing dangerous.",
    "famine_coming": "My lord, the granaries are low. If the harvest fails, the people will starve.",
}
for k, t in ALERTS.items():
    add(f"alert_{k}", "adviser", "alert", k, t)


# ------------------------------------------- the opening: places to open
# Answers 'What the opening shows' (b: pause on a site to read about it) and
# 13 Oct 2026 (the most important part of each explanation is spoken aloud
# with the text). The bard speaks each, when you pause and open the place.
SITES = [
    ("gobekli_tepe", -9600, "Göbekli Tepe, on a ridge above the plain of Harran. More than eleven thousand years "
     "ago, hunters raised rings of great T-shaped pillars here, some taller than a man, carved with foxes, "
     "boars, snakes and vultures. Few lived here for long; people came together to build and to feast. "
     "[pause] It is the oldest monument of its kind we know."),
    ("karahan_tepe", -9400, "Karahan Tepe, a day's walk from Göbekli Tepe, is just as old. Its builders cut a "
     "chamber into the living rock and left a forest of pillars rising from its floor, watched over by a carved "
     "human head. The hills of Harran hold a dozen such places; Göbekli Tepe was not alone."),
    ("nevali_cori", -8400, "Nevalı Çori lay in a valley now drowned beneath a dam on the Euphrates. Its people "
     "lived in long stone houses and built a shrine with carved pillars, like those of Göbekli Tepe. On the "
     "slopes of Karacadağ nearby, wild einkorn was first tamed into a crop."),
    ("abu_hureyra", -9500, "Abu Hureyra, on the Euphrates in Syria. Its people hunted gazelle and gathered "
     "wild grain, and when a cold, dry age came, they may have begun to sow it themselves. It grew into one of "
     "the largest villages of its time. Its mound now lies beneath the waters of a lake."),
    ("jericho", -8000, "Jericho, beside a spring in the Jordan valley. About ten thousand years ago its people "
     "built a stone wall and a round tower eight metres high, with a stair inside. No one knows whether it "
     "guarded against enemies, floods, or simply showed what the town could do together. People have lived "
     "here ever since."),
    ("ganj_dareh", -8000, "Ganj Dareh, high in the Zagros mountains of Iran. Here, ten thousand years ago, "
     "people began to herd goats, killing the young males and keeping the females for their kids and milk. "
     "It is the earliest sign we have of goats being tamed."),
    ("cayonu", -8600, "Çayönü, in the hills above the Tigris. Its people built their houses on stone grills to "
     "keep them dry, and hammered native copper into beads and pins nearly ten thousand years ago, some of "
     "the first worked metal in the world. Here too, wild pigs were among the first to be tamed."),
    ("asikli", -8200, "Aşıklı Höyük, in Cappadocia, beside a river beneath the volcanoes. Its people lived in "
     "close-packed houses of mud brick and worked obsidian, the black volcanic glass that gave the sharpest "
     "blades of the age. Here we can see sheep being penned and kept, among the first."),
    ("ain_ghazal", -7200, "'Ain Ghazal, near Amman in Jordan, was one of the largest villages of its age, home "
     "to perhaps two or three thousand people. They modelled statues of plaster over frames of reed, with wide "
     "eyes of shell and bitumen, and buried them with care. Then the land grew dry, and the village dwindled."),
    ("catalhoyuk", -7100, "Çatalhöyük, on the plain of Konya. For more than a thousand years, thousands of "
     "people lived here in houses packed so close there were no streets; they came and went through their "
     "roofs. They painted hunts and leopards on their walls, set bulls' horns in the plaster, and buried their "
     "dead beneath their floors. [pause] No house was grander than the rest."),
    ("khirokitia", -7000, "Khirokitia, on Cyprus. The island's first farmers came by boat, bringing wheat, "
     "sheep, goats, pigs and even deer to a land that had none. Here they built round houses of stone and mud "
     "brick on a hillside, behind a wall, and lived there for some fifteen hundred years."),
    ("jarmo", -7000, "Jarmo, in the foothills of the Zagros. A village of some twenty houses, where people grew "
     "wheat and barley, kept goats and sheep, and in time made some of the first pottery of the region. It was "
     "one of the first places where scholars went looking for the beginnings of farming."),
    ("hacilar", -6700, "Hacılar, in the lake country of Anatolia. Its villagers made lively painted pots and "
     "little clay figures of women, and lived in houses of mud brick on stone footings. Fire destroyed the "
     "village more than once, and each time it was built again."),
    ("sesklo", -6800, "Sesklo, in Thessaly, one of the first farming villages of Europe. Its people came from "
     "Anatolia across the Aegean, bringing wheat, sheep and goats, and lived in stone-footed houses with fine "
     "painted pottery. From villages like this, farming spread across the continent."),
    ("franchthi", -7000, "Franchthi Cave, in the Argolid of Greece. People lived here for twenty thousand "
     "years: as hunters, then as fishermen who sailed to the island of Melos for obsidian, and at last as "
     "farmers. It holds one of the oldest records of seafaring in the Mediterranean."),
    ("lepenski_vir", -7000, "Lepenski Vir, on the Danube in the Iron Gates gorge. Fishers of the great river "
     "built houses with floors of red plaster, and carved river boulders into faces half man and half fish. "
     "They lived on the river's bounty for two thousand years before farmers came."),
    ("samarra", -6000, "Samarra, on the Tigris. About six thousand years before Christ, farmers here dug "
     "canals to carry river water to their fields, for there was too little rain to grow grain without it. "
     "Their pots were painted with whirling women, fish and birds. Irrigation would make Mesopotamia bloom."),
    ("halaf", -6000, "Tell Halaf, in northern Syria, gave its name to a people of farming villages that "
     "stretched from the Mediterranean to the Zagros some eight thousand years ago. They made the finest "
     "painted pottery of their age, lived in round houses, and traded obsidian and shells across great "
     "distances."),
    ("merimde", -5000, "Merimde Beni Salama, on the western edge of the Nile delta, was one of Egypt's first "
     "farming villages. Its people grew wheat and barley, kept cattle, pigs and sheep, and buried their dead "
     "among their houses."),
    ("nabta_playa", -5000, "Nabta Playa, in the desert west of the Nile. When the Sahara was green, cattle "
     "herders gathered here beside a seasonal lake, and set up a circle of stones that may have marked the "
     "summer sun. When the rains failed, they moved east, to the Nile."),
    ("eridu", -5400, "Eridu, on the edge of the marshes in the far south of Mesopotamia. The Sumerians "
     "remembered it as the first city, where kingship came down from heaven. For three thousand years its "
     "people rebuilt the temple of Enki, god of the sweet waters, each one larger, on top of the last: "
     "eighteen temples, one above another."),
    ("ubaid", -5500, "Tell al-Ubaid, near Ur, gave its name to the people who first settled the south of "
     "Mesopotamia. They drained the marshes, dug canals and built temples on platforms, and their ways and "
     "their pottery spread up the rivers as far as Syria, and across the Gulf."),
    ("byblos", -5000, "Byblos, on the coast of Lebanon. Fishermen and farmers settled here seven thousand "
     "years ago. It would become one of the oldest cities still lived in, trading cedar with Egypt, and the "
     "Greeks would name papyrus after it, and so the Bible too."),
    ("vinca", -5400, "Vinča, on the Danube near Belgrade. Seven thousand years ago it was one of the great "
     "settlements of Europe, with hundreds of houses. Its people smelted copper earlier than almost anyone, "
     "made masked clay figures, and scratched signs on their pots that some think a kind of writing."),
    ("yumuktepe", -4900, "Yumuktepe, at Mersin on the coast of Cilicia, was lived in for eight thousand "
     "years. Some seven thousand years ago it was a walled fortress, with narrow windows for archers: one of "
     "the oldest forts we know."),
    ("ghassul", -4500, "Teleilat Ghassul, near the Dead Sea, gave its name to a people who painted the walls "
     "of their houses with stars and masked figures, and cast copper in moulds. A hoard of their copper maces "
     "and crowns was hidden in a cave by the Dead Sea, and found six thousand years later."),
    ("varna", -4500, "Varna, on the Black Sea coast. In a cemetery more than six thousand years old lay a man "
     "buried with a sceptre, bracelets and more than a kilogram of gold, the oldest worked gold in the world. "
     "Someone here was far richer than the rest, and was buried so."),
    ("cucuteni", -4800, "Cucuteni and Trypillia, between the Carpathians and the Dnieper. Their farmers built "
     "the largest settlements of their age, thousands of houses in great rings, and then, every few "
     "generations, burned them to the ground on purpose. Why, no one knows."),
    ("badari", -4400, "El-Badari, in Upper Egypt, gave its name to farmers who made thin, polished pots with "
     "blackened rims, and laid their dead in graves with palettes for green eye paint. They were the "
     "forerunners of the Egyptians."),
    ("tell_brak", -4400, "Tell Brak, in the north of Mesopotamia, grew into one of the first cities, perhaps "
     "even before Uruk. Six thousand years ago it already sprawled across the plain. Its people raised the Eye "
     "Temple, filled with hundreds of small stone figures with staring eyes."),
    ("susa", -4200, "Susa, on the plain of Khuzestan in Iran, was founded on a great platform of mud brick "
     "more than six thousand years ago. Its people buried their dead with beautiful painted beakers. It would "
     "become the heart of Elam, and much later a capital of the Persian kings."),
    ("sialk", -4000, "Tepe Sialk, near Kashan on the Iranian plateau, was lived in for five thousand years. "
     "Its villagers were among the first to smelt copper from ore, and painted their pots with leaping ibex."),
    ("naqada", -4000, "Naqada, in Upper Egypt. Over a thousand years, its people grew from villages into "
     "small kingdoms, with walled towns, painted boats on their pots, and rich graves for their chiefs. Their "
     "way of life spread down the whole Nile, and from it came the pharaohs."),
    ("hierakonpolis", -3500, "Nekhen, which the Greeks called Hierakonpolis, the city of the falcon. Its "
     "chiefs were buried in a painted tomb, kept wild animals, and worshipped Horus. From here the kings of "
     "Upper Egypt set out to unite the land."),
    ("ggantija", -3600, "Ġgantija, on the island of Gozo, by Malta. More than five and a half thousand years "
     "ago, farmers raised temples of huge stones here, older than the pyramids. Later islanders said that "
     "giants had built them."),
    ("hamoukar", -3500, "Hamoukar, in north-eastern Syria, was a town of obsidian workers. About three "
     "thousand five hundred years before Christ it was attacked and burned; clay sling bullets by the "
     "thousand lie in its ruins. [gravely] It is one of the oldest battles we know."),
    ("arslantepe", -3400, "Arslantepe, near Malatya in eastern Anatolia. Its rulers built a palace where "
     "they handed out food from storerooms, sealed with thousands of clay seals. Some of the oldest swords in "
     "the world, of arsenic bronze, were found there."),
    ("uruk", -3500, "Uruk, on the Euphrates. Three and a half thousand years before Christ, it was the "
     "greatest settlement on Earth, its temples of Eanna and Anu rising above the plain. Here scribes first "
     "wrote on clay, and here, the legend says, King Gilgamesh would one day build its walls."),
]
for k, year, t in SITES:
    add(f"site_{k}", "bard", "opening_site", k, t)

# The stages of the opening, as the map comes to life.
STAGES = [
    ("hunters", "[slowly] Twelve thousand years ago, the ice was retreating. Hunters followed gazelle and wild "
     "cattle across the hills, gathering wild grain, nuts and lentils as the seasons turned."),
    ("gathering", "Then people began to gather in great numbers, to build together and to feast. And some of "
     "them stayed."),
    ("villages", "The first villages rose where the wild grain grew thick: round houses, then square ones, "
     "clustered close together."),
    ("crops", "Year after year they kept the best seed and sowed it again. Einkorn, emmer and barley, lentils, "
     "peas, chickpeas and flax: the founder crops, which feed us still."),
    ("herds", "Goats in the Zagros, sheep in the Taurus, cattle and pigs in the valleys of the great rivers. "
     "The wild herds became flocks, and the hunters became herders."),
    ("pottery", "About eight and a half thousand years ago, people learned to fire clay into pots, to store "
     "grain and to cook. From now on, every village leaves its broken pottery for us to read."),
    ("spread", "Farming spread out of Anatolia: by sea to Greece and Cyprus, up the Danube into Europe, along "
     "the coasts to Italy and Spain, and up the Nile."),
    ("sahara", "In the green Sahara, herders drove their cattle between lakes and grasslands. When the rains "
     "moved south, they moved with them, and some came to the Nile."),
    ("irrigation", "In the dry south of Mesopotamia, grain grows only where water is led to it. Canal by "
     "canal, marsh and desert were turned to fields."),
    ("copper", "Copper was first hammered cold, then melted, then smelted from green stone. Metal made its "
     "owners rich, and those who held it, powerful."),
    ("temples", "Temples grew larger, rebuilt again and again on the same holy ground. Their priests stored "
     "the harvest, and shared it out."),
    ("rank", "Graves began to differ: most with a pot or two, a few with gold and copper. Some families were "
     "rising above the rest."),
    ("war", "Walls rose around the towns, and sling stones piled up in their ruins. Where there was wealth to "
     "take, there was war."),
    ("plough", "The plough drawn by oxen, the wheel, the donkey to carry, wool from the sheep and milk from "
     "the herds: in a few centuries, the farmer's world was changed."),
    ("sails", "Sails appeared on the Nile and in the Gulf. Boats carried copper, timber, stone and grain "
     "farther than anyone could walk."),
    ("cities", "In Uruk and its neighbours, villages became towns, and towns became cities, with thousands of "
     "people who did not farm at all."),
    ("writing", "To keep count of grain, beer and workers, the temple scribes pressed signs into clay. In "
     "time those signs became writing, and history begins to speak for itself."),
    ("polities", "[dramatically] The settlements have become states: lands with rulers, gods, laws, borders "
     "and armies. The lesson is over. [pause] Your reign begins."),
]
for k, t in STAGES:
    add(f"stage_{k}", "bard", "opening_stage", k, t)

# The years, as they run on screen.
YEARS = [(10000, "ten thousand"), (9000, "nine thousand"), (8000, "eight thousand"), (7000, "seven thousand"),
         (6000, "six thousand"), (5000, "five thousand"), (4000, "four thousand"),
         (3500, "three thousand five hundred")]
for y, words in YEARS:
    add(f"year_bc{y}", "bard", "opening_year", str(-y), f"[slowly] {words[0].upper() + words[1:]} years before Christ.")

HELP = [
    ("pause", "Pause whenever you wish, and open any place that is lit, to hear its story."),
    ("speed", "Slow the years, or hurry them on. Or skip ahead, if you know this tale already."),
    ("resume", "And so the years run on."),
]
for k, t in HELP:
    add(f"opening_help_{k}", "bard", "opening_help", k, t)

# ------------------------------------------------- the realms of 300 BC
# Spoken when you choose a realm to play.
REALMS = {
    "rome": "Rome, a republic of farmers and soldiers on the Tiber. Its consuls lead the legions, its allies "
            "fill the ranks, and the whole of Italy is within its reach. Carthage and the Samnites stand in "
            "its way.",
    "samnites": "The Samnite League, hardy hill peoples of the Apennines. They have fought Rome twice "
                "and will not bow easily.",
    "etruscans": "The Etruscan League, twelve cities of an ancient and wealthy people, masters of bronze, "
                 "augury and the sea. Their glory is fading as Rome rises beside them.",
    "cisalpine": "The Cisalpine Gauls, the Boii and their kin, who took the Po valley from the Etruscans. Their "
                 "warriors once burned Rome itself.",
    "veneti": "The Veneti, horse breeders and traders at the head of the Adriatic, through whose lands the "
              "amber road runs south.",
    "ligurians": "The Ligurians, fierce mountain peoples between the Alps and the sea, who yield to no one "
                 "for long.",
    "lucanians": "The Lucanians and Bruttians, Oscan peoples of the far south of Italy, pressing hard on the "
                 "Greek cities of the coast.",
    "iapygians": "The Messapians and Peucetians of the heel of Italy, a proud people with their own tongue, "
                 "rivals of Tarentum.",
    "tarentum": "Tarentum and the Italiote League, the richest Greek city of Italy, with a great harbour and "
                "fine cavalry, but few soldiers of its own. It hires generals from across the sea.",
    "syracuse": "Syracuse, the greatest city of Sicily, under the tyrant Agathokles, who has fought Carthage "
                "in Sicily and in Africa itself, and now calls himself king.",
    "massalia": "Massalia, a Greek city on the coast of Gaul, founded by the Phocaeans. Its merchants trade "
                "wine and tin up the Rhône, and its ally is Rome.",
    "gaul": "The Gallic tribes, many peoples, many chiefs, rich in iron, gold and warriors. United, they "
            "could shake the world.",
    "danube_celts": "The Celts of the Danube, warrior peoples on the move. Their eyes are turned south, to "
                    "Macedon and the riches of Greece.",
    "iberia": "The Iberian peoples of the east coast of Spain, towns of farmers, silversmiths and fierce "
              "warriors, long in contact with Greeks and Phoenicians.",
    "celtiberia": "The Celtiberians of the Spanish meseta, hill fort peoples famed for their swords. They will "
                  "make any invader pay dearly.",
    "lusitania": "The Lusitanians and Gallaeci of the far west, herders and raiders of the mountains and the "
                 "Atlantic coast.",
    "carthage": "Carthage, queen of the western sea. A great merchant republic of Phoenician blood, with "
                "fleets, trading posts on every coast and hired armies from every shore.",
    "massylii": "The Massylian Numidians of the east, horsemen of the North African hills, whose riders are "
                "the best in the world.",
    "masaesyli": "The Masaesylian Numidians of the west, a great kingdom of horsemen between Carthage and the "
                 "Mauri.",
    "mauri": "The Mauri of the far west of Africa, beyond the pillars of Herakles, a land of mountains and "
             "elephants.",
    "garamantes": "The Garamantes of the Sahara, who dig tunnels for water beneath the desert, and drive their "
                  "chariots to the far south.",
    "egypt": "Egypt, ruled by Ptolemy, the Macedonian who seized it after Alexander's death. The richest land "
             "on Earth, with Alexandria, its great new city, rising by the sea.",
    "kush": "The Kingdom of Kush, on the Nile beyond Egypt, ruled from Napata and Meroë. Land of gold, iron, ebony and "
            "archers, whose kings once ruled Egypt itself.",
    "greek_world": "Macedon, held by Kassander, Alexander's old enemy. The homeland of the conquerors, and "
                   "master of much of Greece.",
    "antigonus": "The sea kingdom of Demetrios, the Besieger of Cities. Beaten at Ipsus, he still holds a great "
                 "fleet, Cyprus and cities across the Aegean, and dreams of more.",
    "aetolia": "The Aetolian League, a union of tough mountain cantons in western Greece, who once drove back "
               "the Macedonians.",
    "sparta": "Sparta, once the greatest military power in Greece, now a shadow of itself, but still proud, "
              "and still dangerous.",
    "epirus": "Epirus, kingdom of the Molossians, whose young king Pyrrhos is cousin to Alexander, and "
              "dreams of glory to match.",
    "rhodes": "Rhodes, a free island republic of merchants and sailors, which has just withstood a great "
              "siege, and will raise a giant bronze statue of the sun god in thanks.",
    "crete": "The cities of Crete, an island of ancient memory, now famous for its archers and its pirates.",
    "illyria": "The Illyrian kingdom on the Adriatic coast, a land of warriors and raiders by land and sea.",
    "dardania": "The Dardanians and Triballi, warlike peoples of the central Balkans.",
    "paeonia": "Paeonia, a small kingdom in the valley of the Axios, squeezed between greater neighbours.",
    "odrysia": "The Odrysian kingdom of Thrace, under Seuthes, who rules from his new city of Seuthopolis and "
               "will not bend the knee to Lysimachos.",
    "getae": "The Getae of the lower Danube, horsemen and farmers, whose king Dromichaites will humble "
             "Lysimachos himself.",
    "lysimachus": "The kingdom of Lysimachos, Alexander's bodyguard, now king of Thrace and of much of "
                  "Anatolia after his victory at Ipsus.",
    "byzantium": "Byzantium, a Greek city on the Bosporus, holding the gate between the Black Sea and the "
                 "Aegean, and growing rich on the tolls.",
    "bithynia": "Bithynia, a Thracian kingdom in north-western Anatolia, where Zipoites defies the "
                "Macedonian kings.",
    "heraclea": "Heraclea Pontica, a rich Greek city on the Black Sea coast, with a fleet of its own.",
    "pontus": "Pontus, on the southern shore of the Black Sea, where Mithridates, of Persian royal blood, is "
              "founding a kingdom of his own.",
    "cappadocia": "Cappadocia, a land of horses and high plateaus in the heart of Anatolia, ruled by a Persian "
                  "dynasty that has outlasted Alexander.",
    "armenia": "Armenia, the high mountain kingdom of the Orontids, rich in horses and in metal.",
    "atropatene": "Atropatene, in the north of Media, held by its Persian satrap Atropates as a kingdom of his "
                  "own.",
    "kartli": "Kartli, in the Caucasus, where Parnavaz is founding the first Georgian kingdom.",
    "colchis": "Colchis, on the eastern shore of the Black Sea, the land of the golden fleece, rich in gold "
               "and timber.",
    "albania": "Caucasian Albania, the peoples of the eastern Caucasus and the Kura valley.",
    "bosporus": "The Bosporan kingdom on the Cimmerian Bosporus, which feeds Athens with its grain.",
    "scythia": "The Scythians, the great horse archers of the steppe, now pressed back into the Crimea.",
    "sarmatians": "The Sarmatians, steppe horsemen from beyond the Don, whose armoured lancers are moving "
                  "west.",
    "seleucid": "The kingdom of Seleukos, the greatest of Alexander's heirs, from the Mediterranean to the "
                "Indus. Babylon, Syria and Persia are his, and he has five hundred war elephants.",
    "nabatea": "The Nabataeans, Arab masters of the incense road, who hide their water in cisterns and their "
               "riches at Petra.",
    "lihyan": "Lihyan, an Arab kingdom at the oasis of Dedan, on the incense road to the north.",
    "saba": "Saba, the kingdom of Sheba, in the green mountains of Arabia, rich from incense and myrrh, with "
            "its great dam at Marib.",
    "main": "Ma'in, a kingdom of caravan merchants in Arabia, whose traders carry incense to Gaza and Egypt.",
    "qataban": "Qataban, a kingdom of southern Arabia, rich in incense, ruled from Timna.",
    "hadramawt": "Hadramawt, land of frankincense in southern Arabia, ruled from Shabwa.",
}
for k, t in REALMS.items():
    add(f"realm_{k}", "bard", "realm_intro", k, t)

# --------------------------------------------- the Bronze Age (to come)
# Answers 'How the collapse plays' (from its causes): the bard tells them.
BRONZE = {
    "sea_peoples": ["[gravely] Ships have come from the sea, and the coast is burning.",
                    "The peoples of the sea are on the move, with their families and their ox carts."],
    "tin_cut": ["The tin no longer comes. Without it, there is no bronze.",
                "The caravans from the east have stopped, and the smiths sit idle."],
    "palace_burned": ["The palace burns, and with it the storerooms and the tablets.",
                      "The great house has fallen. The scribes are gone."],
    "bronze_learnt": ["Copper and tin, melted together, make bronze, harder than either.",
                      "Our smiths have learned to make bronze."],
    "iron_learnt": ["Iron, from common stone, in the hands of every smith.",
                    "Our smiths have learned to work iron."],
    "writing_learnt": ["Our scribes have learned to write.",
                       "Now our deeds will be written down, and remembered."],
    "chariots": ["Horses and chariots: the lords of war ride to battle.",
                 "The chariot has come to our land."],
}
for k, texts in BRONZE.items():
    for i, t in enumerate(texts):
        add(f"moment_{k}_bard_{i + 1}", "bard", "moment", k, t)

# ------------------------------------------------------- adviser replies
REPLIES = {
    "yes_1": "As you command, my lord.",
    "yes_2": "It shall be done.",
    "yes_3": "At once, my lord.",
    "yes_4": "A wise choice, my lord.",
    "yes_5": "I will see to it.",
    "doubt_1": "If you are certain, my lord.",
    "doubt_2": "[hesitantly] That is... bold, my lord.",
    "cannot_1": "We cannot afford it, my lord.",
    "cannot_2": "That is not yet possible, my lord.",
    "cannot_3": "Our scholars do not yet know how.",
    "greet_1": "Good morning, my lord. The realm awaits.",
    "greet_2": "Welcome back, my lord.",
    "greet_3": "My lord, there is much to do.",
    "year_1": "Another year has passed, my lord.",
    "year_2": "A new year begins.",
    "saved": "The chronicle is written, my lord. It will keep.",
}
for k, t in REPLIES.items():
    add(f"reply_{k}", "adviser", "adviser_reply", k, t)

# ------------------------------------------------------- victory cheers
CHEERS = [
    ("latin_io", "latin", "la", "[shouting] Io triumphe! Io triumphe!", "Hail, triumph!"),
    ("latin_victoria", "latin", "la", "[shouting] Victoria! Victoria!", "Victory! Victory!"),
    ("greek_nike", "greek", "grc", "[shouting] Nike! Nike!", "Victory! Victory!"),
    ("greek_paian", "greek", "grc", "[shouting] Ie Paian! Ie Paian!", "Hail, Healer! (the victory hymn)"),
]
for k, culture, lang, t, sub in CHEERS:
    add(f"cheer_{k}", "warrior", "victory_cry", culture, t, lang=lang, subtitle=sub)

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
         "war_cry": "War cries (in their own tongue)",
         "lesson": "The longer history lesson (the bard)", "moment": "Moments in play (each age's narrator)",
         "lesson_adviser": "The guided first game (the adviser)", "alert": "Warnings (the adviser)",
         "adviser_reply": "Replies and greetings (the adviser)",
         "opening_stage": "The opening: its stages (the bard)", "opening_year": "The opening: the years",
         "opening_site": "The opening: places you can open when paused (the bard)",
         "opening_help": "The opening: how to watch it", "realm_intro": "The realms of 300 BC (the bard)",
         "victory_cry": "Victory cries (in their own tongue)"}
for plays, head in heads.items():
    md += [f"## {head}", ""]
    for l in lines:
        if l["plays"] == plays:
            extra = f" *({l['subtitle']})*" if l["voice"] == "warrior" else ""
            md.append(f"- **{l['id']}** ({l['voice']}): {l['text']}{extra}")
    md.append("")
(ROOT / "docs/Voice-script.md").write_text("\n".join(md))
