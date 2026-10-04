"""Writes data/roads_history.json: history's roads and railways (run from the repository root)."""
import json
R = []
def r(id, name, kind, builder, year, pts, note=""):
    R.append({"id": id, "name": name, "kind": kind, "builder": builder, "year": year,
              "points": [[n, lo, la] for n, lo, la in pts], "note": note})
Ro = "rome"
r("royal_road","The Royal Road","road",None,-500,[("Sardis",28.04,38.49),("Ancyra",32.86,39.93),("Mazaca",35.49,38.72),("Melitene",38.35,38.35),("Nisibis",41.22,37.07),("Arbela",44.01,36.19),("Susa",48.26,32.19)],"The Achaemenid post road, about 2,700 km; standing in 300 BC.")
r("via_latina","Via Latina","via",Ro,-330,[("Roma",12.50,41.89),("Anagnia",13.16,41.74),("Casinum",13.83,41.49),("Casilinum",14.22,41.08)],"Among the oldest roads of Rome; standing in 300 BC.")
r("via_appia","Via Appia","via",Ro,-312,[("Roma",12.50,41.89),("Tarracina",13.25,41.29),("Formiae",13.61,41.26),("Capua",14.25,41.08)],"Appius Claudius' road to Capua, 312 BC.")
r("via_valeria","Via Valeria","via",Ro,-307,[("Roma",12.50,41.89),("Tibur",12.80,41.96),("Carsioli",13.08,42.10),("Alba Fucens",13.41,42.08),("Corfinium",13.84,42.12)])
r("via_aurelia","Via Aurelia","via",Ro,-241,[("Roma",12.50,41.89),("Cosa",11.29,42.41),("Pisae",10.40,43.72)])
r("via_flaminia","Via Flaminia","via",Ro,-220,[("Roma",12.50,41.89),("Narnia",12.52,42.52),("Fanum Fortunae",13.02,43.84),("Ariminum",12.57,44.06)],"Gaius Flaminius' road over the Apennines, 220 BC.")
r("via_appia_ext","Via Appia to Brundisium","via",Ro,-190,[("Capua",14.25,41.08),("Beneventum",14.78,41.13),("Venusia",15.81,40.96),("Tarentum",17.24,40.47),("Brundisium",17.94,40.64)])
r("via_aemilia","Via Aemilia","via",Ro,-187,[("Ariminum",12.57,44.06),("Bononia",11.34,44.49),("Mutina",10.93,44.65),("Placentia",9.69,45.05)])
r("via_cassia","Via Cassia","via",Ro,-154,[("Roma",12.50,41.89),("Clusium",11.95,43.02),("Arretium",11.88,43.46),("Florentia",11.25,43.77)])
r("via_postumia","Via Postumia","via",Ro,-148,[("Genua",8.93,44.41),("Dertona",8.86,44.90),("Cremona",10.02,45.13),("Verona",10.99,45.44),("Aquileia",13.37,45.77)])
r("via_egnatia","Via Egnatia","via",Ro,-146,[("Dyrrachium",19.45,41.32),("Lychnidos",20.80,41.11),("Heraclea Lyncestis",21.34,41.01),("Pella",22.52,40.76),("Thessalonica",22.94,40.64),("Amphipolis",23.85,40.82),("Byzantium",28.98,41.01)],"Rome's road across the Balkans, begun after 146 BC.")
r("via_popilia","Via Popilia","via",Ro,-132,[("Capua",14.25,41.08),("Nuceria",14.67,40.74),("Consentia",16.25,39.30),("Rhegium",15.65,38.11)])
r("via_domitia","Via Domitia","via",Ro,-118,[("Tarasco",4.66,43.81),("Nemausus",4.36,43.84),("Narbo",3.00,43.18),("Summum Pyrenaeum",2.86,42.48)],"The first Roman road in Gaul.")
r("via_julia_augusta","Via Julia Augusta","via",Ro,-13,[("Placentia",9.69,45.05),("Dertona",8.86,44.90),("Genua",8.93,44.41),("Albintimilium",7.60,43.79),("Forum Iulii",6.74,43.43),("Arelate",4.63,43.68)])
r("via_augusta","Via Augusta","via",Ro,-8,[("Summum Pyrenaeum",2.86,42.48),("Tarraco",1.25,41.12),("Valentia",-0.38,39.47),("Corduba",-4.78,37.88),("Hispalis",-5.99,37.39),("Gades",-6.29,36.53)],"Augustus' road down the length of Hispania.")
r("via_sebaste","Via Sebaste","via",Ro,-6,[("Perge",30.85,36.96),("Comama",30.38,37.55),("Antiochia Pisidiae",31.19,38.31),("Iconium",32.49,37.87)])
r("via_militaris","Via Militaris","via",Ro,33,[("Singidunum",20.46,44.82),("Naissus",21.90,43.32),("Serdica",23.32,42.70),("Philippopolis",24.75,42.15),("Hadrianopolis",26.56,41.68),("Byzantium",28.98,41.01)],"The road from the Danube to the Bosporus.")
r("via_claudia","Via Claudia Augusta","via",Ro,47,[("Altinum",12.39,45.55),("Tridentum",11.12,46.07),("Pons Drusi",11.35,46.50),("Foetes",10.70,47.57)],"Over the Alps toward the Danube (it ends at Augusta Vindelicum, just beyond the map's edge).")
r("via_traiana","Via Traiana","via",Ro,109,[("Beneventum",14.78,41.13),("Canusium",16.07,41.22),("Barium",16.87,41.12),("Brundisium",17.94,40.64)])
r("via_nova_traiana","Via Nova Traiana","via",Ro,111,[("Bostra",36.48,32.52),("Philadelphia",35.93,31.95),("Petra",35.44,30.32),("Aila",35.00,29.53)],"Trajan's road through Arabia Petraea.")
r("via_hadriana","Via Hadriana","via",Ro,137,[("Antinoopolis",30.88,27.81),("Myos Hormos",34.24,26.16),("Berenice",35.47,23.91)],"From the Nile to the Red Sea.")
r("darb_zubaydah","Darb Zubaydah","road",None,780,[("Kufa",44.40,32.03),("Faid",42.03,27.12),("Mecca",39.83,21.42)],"The Abbasid pilgrim road, with wells and stations along it.")
r("naples_portici","Naples-Portici Railway","railway",None,1839,[("Napoli",14.27,40.85),("Portici",14.34,40.82)],"The first railway in Italy.")
r("cairo_alexandria","Alexandria-Cairo Railway","railway",None,1856,[("Alexandria",29.92,31.20),("Kafr el-Zayat",30.82,30.83),("Cairo",31.24,30.04)],"The first railway in Africa and the Middle East.")
r("anatolian_railway","Anatolian and Baghdad Railway","railway",None,1903,[("Haydarpasa",29.02,41.00),("Eskisehir",30.52,39.78),("Konya",32.49,37.87),("Adana",35.32,37.00),("Aleppo",37.16,36.20),("Mosul",43.13,36.34),("Baghdad",44.37,33.31)],"Built 1888 to 1940.")
r("hejaz_railway","Hejaz Railway","railway",None,1908,[("Damascus",36.29,33.51),("Daraa",36.10,32.62),("Ma'an",35.73,30.19),("Medina",39.61,24.47)])
for x in R:
    for n, lo, la in x["points"]:
        assert -10 <= lo <= 55 and 10 <= la <= 48, (x["id"], n)
json.dump({"about": "History's roads and railways, for other realms to build on their dates (the Ledger topic 'Buildings and works', your answer 'Other realms build history's roads on their dates'). Each is a chain of real places (lon, lat); 'builder' is the civilization that built it (null: no realm of the game did, or it stood before 300 BC); 'kind' waits on the decision 'Which kinds of road'. Not yet used by the game. Made by tools/history/roads.py. Sources: the Barrington Atlas of the Greek and Roman World (Talbert, 2000); Chevallier, 'Roman Roads' (1976); the Antonine Itinerary; standard railway histories. Our own summary, free for any use.", "roads": R},
          open("data/roads_history.json", "w"), indent=1, ensure_ascii=False)
print(len(R), "roads")
