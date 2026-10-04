"""Writes data/works_history.json: history's canals, great works and frontier walls (run from the repository root)."""
import json
W = []
def w(id, name, kind, builder, start, done, pts, effect, note="", fall=None, fall_why=""):
    d = {"id": id, "name": name, "kind": kind, "builder": builder, "start": start, "done": done,
         "points": [[n, lo, la] for n, lo, la in pts], "effect": effect, "note": note}
    if fall is not None:
        d["fall"] = fall; d["fall_why"] = fall_why
    W.append(d)
# Canals
w("diolkos","The Diolkos of Corinth","canal",None,-600,-600,[("Lechaion",22.89,37.93),("Schoinos",22.98,37.92)],"Ships hauled across the Isthmus: a short way between the gulfs.","A paved slipway, standing in 300 BC.")
w("pharaohs_canal","Canal of the Pharaohs (restored)","canal","egypt",-280,-270,[("Bubastis",31.51,30.57),("Lake Timsah",32.30,30.58),("Arsinoe",32.55,29.97)],"Opens the Nile to the Red Sea: Indian trade without the desert.","Begun by Necho, finished by Darius; restored by Ptolemy II.",fall=-100,fall_why="silted up; Trajan and later Amr ibn al-As reopened it until 767")
w("trajans_canal","Trajan's River (the canal reopened)","canal","rome",106,112,[("Babylon (Cairo)",31.23,30.01),("Lake Timsah",32.30,30.58),("Clysma",32.55,29.97)],"As the Pharaohs' canal.",fall=767,fall_why="closed by the caliph al-Mansur in 767")
w("nero_corinth","Nero's Corinth canal","canal","rome",67,68,[("Lechaion",22.89,37.93),("Schoinos",22.98,37.92)],"Would join the gulfs; abandoned at Nero's death.","Begun with 6,000 prisoners of the Jewish war, abandoned in 68.")
w("naviglio_grande","Naviglio Grande","canal",None,1177,1272,[("Tornavento",8.72,45.58),("Milano",9.19,45.46)],"Water for Milan's fields and mills, and a waterway for its marble.")
w("canal_du_midi","Canal du Midi","canal",None,1666,1681,[("Toulouse",1.44,43.60),("Carcassonne",2.35,43.21),("Béziers",3.22,43.34),("Sète",3.70,43.40)],"Joins the Atlantic's rivers to the Mediterranean.")
w("mahmudiyah","Mahmudiyah Canal","canal",None,1817,1820,[("Alexandria",29.92,31.20),("Fuwwah",30.55,31.20)],"Joins Alexandria to the Nile again.")
w("suez","Suez Canal","canal",None,1859,1869,[("Port Said",32.30,31.26),("Ismailia",32.27,30.60),("Suez",32.55,29.97)],"The sea route from the Mediterranean to India.")
w("corinth_canal","Corinth Canal","canal",None,1881,1893,[("Lechaion",22.89,37.93),("Schoinos",22.98,37.92)],"Joins the Gulf of Corinth and the Saronic Gulf.")
w("danube_black_sea","Danube-Black Sea Canal","canal",None,1949,1984,[("Cernavoda",28.03,44.34),("Agigea",28.63,44.09)],"A short way from the Danube to the sea.")
# Great works
w("fucine_drain","Draining the Fucine Lake","great_work","rome",41,52,[("Lacus Fucinus",13.53,41.99),("Liris",13.44,41.92)],"New farmland from the lake bed.","Claudius' tunnel, 5.6 km, dug by 30,000 men over 11 years.")
w("pontine_marshes","Draining the Pontine Marshes","great_work",None,1930,1939,[("Latina",12.90,41.47)],"New farmland and an end to malaria; attempted since Appius Claudius.")
w("aswan_dam","Aswan Low Dam","great_work",None,1898,1902,[("Aswan",32.88,24.03)],"Holds the Nile flood: two harvests a year.")
w("aswan_high_dam","Aswan High Dam","great_work",None,1960,1970,[("Aswan",32.88,23.97)],"Holds the whole flood; power for Egypt.")
w("ataturk_dam","Atatürk Dam","great_work",None,1983,1990,[("Bozova",38.32,37.48)],"Water and power for the upper Euphrates.")
# Frontier walls
w("long_walls_athens","The Long Walls of Athens","wall",None,-461,-457,[("Athens",23.73,37.97),("Piraeus",23.64,37.94)],"Keeps Athens fed by sea under siege.","Pulled down in 404 BC, rebuilt by Conon in 393 BC.",fall=-86,fall_why="destroyed by Sulla in 86 BC")
w("fossatum_africae","Fossatum Africae","wall","rome",117,138,[("Tobna",5.35,35.35),("Gemellae",5.53,34.64),("Thabudeos",5.88,34.79),("Ad Majores",7.37,34.28)],"A ditch and wall along the desert's edge: slows raiders from the south.")
w("strata_diocletiana","Strata Diocletiana","wall","rome",293,305,[("Sura",38.81,35.91),("Palmyra",38.27,34.55),("Damascus",36.29,33.51),("Bostra",36.48,32.52)],"A fortified road of forts along the Syrian desert.")
w("hexamilion","The Hexamilion","wall","rome",408,450,[("Lechaion",22.89,37.93),("Isthmia",22.99,37.92)],"A wall across the Isthmus: the Peloponnese held against invaders from the north.",fall=1446,fall_why="broken by the Ottomans in 1446")
w("anastasian_wall","The Anastasian Wall","wall","rome",500,512,[("Black Sea shore",28.57,41.49),("Selymbria",28.24,41.08)],"A second line before Constantinople.",fall=600,fall_why="abandoned for lack of men in the 7th century")
w("gorgan_wall","The Great Wall of Gorgan","wall",None,420,530,[("Caspian shore",54.0,37.10),("Pishkamar",55.0,37.45)],"Holds the steppe raiders from Iran.","The Sasanians' 'Red Snake', about 195 km; its end lies beyond the map's edge.")
w("derbent_walls","The Walls of Derbent","wall",None,552,570,[("Derbent",48.29,42.06),("Caucasus",48.0,41.98)],"Closes the Caspian Gates against the peoples of the north.")
for x in W:
    for n, lo, la in x["points"]:
        assert -10 <= lo <= 55 and 10 <= la <= 48, (x["id"], n)
json.dump({"about": "History's canals, great works and frontier walls (the Ledger topic 'Buildings and works': 'Buildable great works' and 'Draw fortified lines'). Each has its real places (lon, lat), the years history built it, its builder (a civilization key, or null), and what it did. Who else builds them, how walls are drawn and what they do wait on the decisions 'Great works others build', 'How frontier walls are drawn', 'What frontier walls do' and 'History's frontier walls'. Hadrian's and the Antonine Walls, the Danevirke and the Kiel canal lie north of the map's edge (48 degrees) and come with 'The whole globe: order'. Not yet used by the game. Made by tools/history/works.py. Sources: Barrington Atlas (Talbert, 2000); Tacitus and Suetonius on the Fucine works; Procopius, 'Buildings'; Sauer and others, 'Persia's Imperial Power in Late Antiquity' (2013) on the Gorgan Wall. Our own summary, free for any use.", "works": W},
          open("data/works_history.json", "w"), indent=1, ensure_ascii=False)
print(len(W), "works")
