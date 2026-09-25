"""Bake the 38 ancient geographic regions onto the population node grid.

See MECHANICS.md, "Geographic regions - the ancient geographers' map". The
regions are the ones the Greco-Roman geographers drew (Hecataeus, Herodotus,
Eratosthenes, Strabo, Ptolemy), grouped under their three continents:
Europa, Libya (Africa) and Asia. They're a fixed geographic partition, not
political borders: conquest never changes which region a place is in.

How the partition is made:

1. Each region is a hand-drawn polygon in longitude/latitude, its borders
   following the ancient descriptions snapped to physical features (the
   Pyrenees, the Alpine crest, the Danube, the Sava, the Nestos, the Tanais
   (Don), the Caucasus, the Taurus, the Euphrates, the Zagros, the Nile /
   Isthmus line). Polygons are tested in PRIORITY order, so a polygon may
   overlap a neighbour's sea or border and the earlier one wins. The
   coastline itself comes from the land mask, not from these polygons.
2. Islands the grid joins to a nearby coast (Lesbos, Rhodes, ...) are
   set by small ISLAND_BOXES. Other islands are assigned whole: every land mass smaller than the mainland
   that contains one of the ISLANDS points goes to that point's region
   (Lesbos and Rhodes to Hellas although they lie off Asia's coast, Cyprus
   to Cilicia, Sardinia and Corsica to Italia, ...).
3. Any land node no polygon covers (slivers along shared borders) takes the
   region of the nearest assigned land node.

Land is the population grid's land: every node the baked HYDE start
keyframe (data/population/) doesn't mark as sea. That's the grid the
population engine runs on, 780 x 456 nodes over longitude -10 to 55 and
latitude 48 to 10, 12 nodes per degree.

Neighbours: two regions are neighbours if their land touches (8-connected),
or if a sea lane joins them - their coasts come within SEA_LANE_KM of each
other across water. Migration between regions only runs between
neighbours.

Output, under data/regions/:
  regions.json          id, name, continent, what's folded in, label
                        position (lon/lat) and neighbour ids per region;
                        grid size and extent
  region_nodes.u8.gz    gzip'd uint8 region id per node, row-major from the
                        north-west corner; 0 = sea

Usage: python3 tools/build_region_mask.py [--preview out.png]
Needs numpy and scipy (and Pillow for --preview).
"""
import argparse
import gzip
import json
import math
import os

import numpy as np
from scipy import ndimage
from scipy.spatial import cKDTree

ROOT = os.path.join(os.path.dirname(__file__), "..")
POP_DIR = os.path.join(ROOT, "data", "population")
OUT_DIR = os.path.join(ROOT, "data", "regions")
SEA_LANE_KM = 300.0   # coasts closer than this across water share a sea lane
KM_PER_DEGREE = 111.2

# (name, continent, what else is folded in). The id is the 1-based position.
REGIONS = [
    ("Hispania", "Europa", "with the Balearic islands"),
    ("Gallia", "Europa", "with the Helvetii"),
    ("Raetia", "Europa", "with Noricum and the land north of the upper Rhine"),
    ("Italia", "Europa", "with Cisalpine Gaul, Istria, Sardinia and Corsica"),
    ("Sicilia", "Europa", "with Malta and the small islands around it"),
    ("Pannonia", "Europa", "the land between the Alps, the Danube and the Sava"),
    ("Illyricum", "Europa", "with Dalmatia, Dardania and Upper Moesia"),
    ("Dacia", "Europa", "with the Tisza plain and the land to the Tyras (Dniester)"),
    ("Thracia", "Europa", "with Lower Moesia and the Dobruja"),
    ("Macedonia", "Europa", "with Paeonia, Upper Macedonia and Chalcidice"),
    ("Hellas", "Europa", "with Epirus, Crete and the Aegean islands"),
    ("Scythia", "Europa", "from the Tyras to the Tanais (Don), with the Tauric Chersonese (Crimea)"),
    ("Mauretania", "Libya", "the land of the Mauri, west of the Mulucha"),
    ("Numidia", "Libya", "the Masaesyli and Massyli, from the Mulucha to the Tusca"),
    ("Africa", "Libya", "the Carthaginian heartland"),
    ("Syrtica", "Libya", "the coast between the two Syrtes, with Djerba"),
    ("Cyrenaica", "Libya", "the Greek Pentapolis"),
    ("Marmarica", "Libya", "from the Chersonese to Mareotis, with the oasis of Ammon"),
    ("Aegyptus", "Libya", "the Nile valley and delta, the western oases and the eastern desert"),
    ("Libya Interior", "Libya", "the Sahara, with the Garamantes"),
    ("Aethiopia", "Libya", "everything south of Egypt and of the Sahara, Kush included"),
    ("Lydia", "Asia", "with Ionia, Aeolis, Mysia, the Troad, Caria and Lycia"),
    ("Phrygia", "Asia", "with Bithynia, Galatia, Pisidia and Lycaonia"),
    ("Cilicia", "Asia", "with Pamphylia and Cyprus"),
    ("Cappadocia", "Asia", "with Pontus and Paphlagonia"),
    ("Colchis", "Asia", "the Caucasus: Colchis, Iberia and Albania"),
    ("Armenia", "Asia", "with Sophene"),
    ("Syria", "Asia", "with Commagene, Phoenicia and Judaea"),
    ("Mesopotamia", "Asia", "with Assyria"),
    ("Babylonia", "Asia", "with Chaldaea and the Sealand"),
    ("Arabia Petraea", "Asia", "with Sinai, the Negev and Nabataea"),
    ("Arabia Deserta", "Asia", "the Syrian desert and the Nafud"),
    ("Arabia Felix", "Asia", "the rest of the peninsula, with Socotra"),
    ("Media", "Asia", "with Atropatene and the Caspian Gates"),
    ("Susiana", "Asia", "Elam"),
    ("Persis", "Asia", "with Carmania"),
    ("Hyrcania", "Asia", "with Parthia and the Dahae east of the Caspian"),
    ("Sarmatia", "Asia", "beyond the Tanais: the steppe north of the Caucasus and the Caspian"),
]
ID = {name: i + 1 for i, (name, _, _) in enumerate(REGIONS)}

# Polygons as (lon, lat) vertices. See the module docstring for how overlaps
# resolve. Shared borders reuse the same vertices on both sides.
POLYGONS = {
    "Sicilia": [(11.7, 38.5), (15.3, 38.5), (15.66, 38.28), (15.45, 37.9), (15.7, 36.5),
                (14.8, 35.4), (11.7, 35.4)],
    "Hispania": [(-10.5, 44.5), (-1.8, 43.4), (0.0, 42.75), (1.5, 42.5), (3.4, 42.45), (5.5, 41.0),
                 (5.5, 38.3), (-1.8, 36.55), (-5.35, 36.0), (-10.5, 35.95)],
    "Italia": [(7.5, 43.78), (6.9, 44.5), (6.8, 45.2), (7.0, 45.85), (7.8, 45.9), (8.4, 46.45),
               (9.0, 46.4), (10.5, 46.5), (11.5, 47.0), (12.3, 46.75), (13.7, 46.5), (14.5, 46.45),
               (14.5, 45.6), (14.2, 45.3), (14.2, 44.7), (14.3, 43.8), (15.6, 42.4), (18.0, 41.3),
               (19.0, 39.8), (17.0, 37.3), (11.5, 37.8), (8.0, 38.6), (7.8, 41.5), (8.0, 42.5)],
    "Gallia": [(-10.5, 48.2), (7.6, 48.2), (7.6, 47.6), (9.0, 47.7), (9.0, 46.4), (8.4, 46.45),
               (7.8, 45.9), (7.0, 45.85), (6.8, 45.2), (6.9, 44.5), (7.5, 43.78), (8.0, 42.5),
               (3.0, 41.0), (-10.5, 43.0)],
    "Raetia": [(7.6, 48.2), (16.0, 48.2), (16.0, 46.6), (14.5, 46.45), (13.7, 46.5), (12.3, 46.75),
               (11.5, 47.0), (10.5, 46.5), (9.0, 46.4), (9.0, 47.7), (7.6, 47.6)],
    "Pannonia": [(16.0, 48.2), (18.8, 48.2), (18.8, 47.8), (19.05, 47.5), (18.9, 46.0), (19.0, 45.35),
                 (19.85, 45.25), (20.5, 44.8), (19.3, 44.9), (18.0, 45.1), (16.0, 45.5), (14.5, 46.1),
                 (14.5, 46.45), (16.0, 46.6)],
    "Illyricum": [(14.5, 46.1), (16.0, 45.5), (18.0, 45.1), (19.3, 44.9), (20.5, 44.8), (22.5, 44.55),
                  (22.7, 44.1), (22.4, 43.0), (22.4, 42.3), (21.0, 42.1), (20.6, 41.5), (20.3, 40.9),
                  (20.0, 40.5), (19.3, 40.4), (18.0, 41.3), (15.6, 42.4), (14.3, 43.8), (14.2, 44.7),
                  (14.2, 45.3), (14.5, 45.6)],
    "Macedonia": [(19.3, 40.4), (20.0, 40.5), (20.3, 40.9), (20.6, 41.5), (21.0, 42.1), (22.4, 42.3),
                  (23.3, 42.0), (23.8, 41.6), (24.2, 41.5), (24.8, 40.85), (24.6, 39.8), (22.9, 39.85),
                  (22.7, 40.0), (21.2, 40.0), (20.6, 40.2)],
    "Thracia": [(22.7, 44.1), (22.9, 43.95), (24.9, 43.65), (25.95, 43.8), (27.3, 44.05), (27.95, 44.3),
                (27.9, 44.7), (28.2, 45.45), (29.6, 45.4), (30.5, 45.0), (29.5, 42.5), (29.2, 41.3),
                (29.05, 41.0), (27.5, 40.8), (26.7, 40.4), (26.25, 40.05), (25.8, 40.0), (24.8, 40.85),
                (24.2, 41.5), (23.8, 41.6), (23.3, 42.0), (22.4, 42.3), (22.4, 43.0)],
    "Dacia": [(18.8, 48.2), (27.8, 48.2), (29.0, 47.0), (30.2, 46.3), (29.6, 45.4), (28.2, 45.45),
              (27.9, 44.7), (27.95, 44.3), (27.3, 44.05), (25.95, 43.8), (24.9, 43.65), (22.9, 43.95),
              (22.5, 44.55), (20.5, 44.8), (19.85, 45.25), (19.0, 45.35), (18.9, 46.0), (19.05, 47.5),
              (18.8, 47.8)],
    "Hellas": [(19.3, 40.4), (20.0, 40.5), (20.6, 40.2), (21.2, 40.0), (22.7, 40.0), (22.9, 39.85),
               (24.6, 39.8), (25.6, 39.6), (25.6, 38.0), (26.5, 36.6), (26.8, 35.3), (26.5, 34.5),
               (23.0, 34.5), (19.0, 37.0), (19.0, 39.5)],
    "Scythia": [(27.8, 48.2), (43.0, 48.2), (42.1, 47.65), (41.1, 47.6), (40.0, 47.35), (39.3, 47.1),
                (38.3, 46.4), (36.62, 45.35), (36.5, 44.8), (33.0, 44.0), (30.5, 45.0), (29.6, 45.4),
                (30.2, 46.3), (29.0, 47.0)],
    "Sarmatia": [(43.0, 48.2), (56.0, 48.2), (56.0, 43.5), (49.0, 43.5), (47.5, 43.0), (46.0, 43.3),
                 (44.0, 43.6), (42.0, 44.0), (40.0, 44.4), (38.5, 44.6), (37.3, 44.9), (36.5, 45.1),
                 (36.62, 45.35), (38.3, 46.4), (39.3, 47.1), (40.0, 47.35), (41.1, 47.6), (42.1, 47.65)],
    "Colchis": [(37.3, 44.9), (38.5, 44.6), (40.0, 44.4), (42.0, 44.0), (44.0, 43.6), (46.0, 43.3),
                (47.5, 43.0), (50.5, 42.0), (50.5, 39.3), (49.3, 39.3), (48.5, 40.0), (47.05, 40.75),
                (46.3, 41.1), (45.0, 41.2), (43.5, 41.1), (42.5, 41.4), (41.55, 41.5), (40.5, 42.0),
                (36.5, 44.5)],
    "Armenia": [(41.55, 41.5), (42.5, 41.4), (43.5, 41.1), (45.0, 41.2), (46.3, 41.1), (47.05, 40.75),
                (48.5, 40.0), (48.0, 39.6), (46.5, 39.0), (45.4, 39.2), (44.8, 39.4), (44.3, 38.5),
                (44.3, 37.3), (43.5, 37.5), (42.0, 38.0), (40.0, 38.2), (38.5, 37.9), (38.75, 38.8),
                (39.5, 39.75), (40.5, 40.3), (41.4, 41.0)],
    "Lydia": [(25.5, 40.3), (26.7, 40.4), (27.5, 40.8), (28.6, 40.5), (29.2, 39.8), (29.5, 39.0),
              (29.8, 38.0), (30.3, 37.1), (30.3, 35.5), (26.8, 35.3), (26.5, 36.6), (25.6, 38.0),
              (25.6, 39.6)],
    "Phrygia": [(28.6, 40.5), (27.5, 40.8), (29.05, 41.0), (29.2, 41.3), (29.5, 42.0), (32.2, 42.2),
                (32.2, 41.6), (33.0, 41.0), (33.5, 40.0), (33.5, 38.3), (34.0, 37.6), (33.5, 37.4),
                (31.5, 37.4), (30.3, 37.1), (29.8, 38.0), (29.5, 39.0), (29.2, 39.8)],
    "Cilicia": [(30.3, 35.5), (30.3, 37.1), (31.5, 37.4), (33.5, 37.4), (34.0, 37.6), (35.5, 37.6),
                (36.4, 37.4), (36.25, 36.75), (35.8, 36.4), (34.0, 35.5)],
    "Cappadocia": [(32.2, 42.2), (32.2, 41.6), (33.0, 41.0), (33.5, 40.0), (33.5, 38.3), (34.0, 37.6),
                   (35.5, 37.6), (36.5, 37.9), (37.5, 37.9), (38.5, 37.9), (38.75, 38.8), (39.5, 39.75),
                   (40.5, 40.3), (41.4, 41.0), (41.55, 41.5), (41.6, 42.2)],
    "Syria": [(36.25, 36.75), (36.4, 37.4), (36.5, 37.9), (37.5, 37.9), (38.5, 37.9), (38.5, 37.5),
              (37.9, 37.0), (38.0, 36.8), (38.05, 36.0), (39.0, 35.95), (40.15, 35.33), (40.9, 34.45),
              (38.5, 33.3), (37.0, 32.6), (36.0, 31.6), (35.5, 31.2), (34.9, 31.2), (34.25, 31.3),
              (33.8, 32.0), (34.5, 34.0), (35.5, 35.5), (35.8, 36.4)],
    "Mesopotamia": [(38.5, 37.9), (40.0, 38.2), (42.0, 38.0), (43.5, 37.5), (44.3, 37.3), (45.0, 36.5),
                    (45.5, 35.5), (45.8, 34.5), (44.5, 34.0), (43.9, 34.1), (42.8, 33.6), (40.9, 34.45),
                    (40.15, 35.33), (39.0, 35.95), (38.05, 36.0), (38.0, 36.8), (37.9, 37.0), (38.5, 37.5)],
    "Babylonia": [(42.8, 33.6), (43.9, 34.1), (44.5, 34.0), (45.8, 34.5), (46.5, 33.2), (47.5, 32.0),
                  (47.8, 31.0), (48.0, 30.4), (48.6, 29.9), (47.9, 29.9), (47.0, 30.0), (45.8, 30.8),
                  (44.5, 31.8), (43.8, 32.6)],
    "Susiana": [(46.5, 33.2), (47.3, 33.6), (48.6, 33.2), (49.8, 32.3), (50.3, 31.2), (49.8, 30.1),
                (49.0, 29.6), (48.6, 29.9), (48.0, 30.4), (47.8, 31.0), (47.5, 32.0)],
    "Persis": [(50.3, 31.2), (51.5, 32.0), (53.0, 32.2), (56.0, 32.2), (56.0, 26.1), (55.5, 26.1),
               (52.0, 27.0), (50.5, 28.5), (49.8, 29.5), (49.8, 30.1)],
    "Media": [(44.3, 37.3), (44.3, 38.5), (44.8, 39.4), (45.4, 39.2), (46.5, 39.0), (48.0, 39.6),
              (48.5, 40.0), (49.3, 39.3), (50.5, 39.3), (51.0, 38.0), (50.5, 37.2), (50.8, 36.5),
              (52.0, 35.9), (53.5, 35.6), (56.0, 35.3), (56.0, 32.2), (53.0, 32.2), (51.5, 32.0),
              (50.3, 31.2), (49.8, 32.3), (48.6, 33.2), (47.3, 33.6), (46.5, 33.2), (45.8, 34.5),
              (45.5, 35.5), (45.0, 36.5)],
    "Hyrcania": [(50.5, 37.2), (50.8, 36.5), (52.0, 35.9), (53.5, 35.6), (56.0, 35.3), (56.0, 43.5),
                 (50.0, 43.5), (51.5, 40.5), (51.0, 38.0)],
    "Aegyptus": [(29.0, 32.0), (32.6, 31.6), (32.55, 29.9), (33.2, 28.6), (33.9, 27.6), (35.9, 24.0),
                 (28.0, 24.0), (28.0, 29.5), (29.0, 30.3), (29.0, 31.3)],
    "Arabia Petraea": [(32.6, 31.6), (34.25, 31.3), (34.9, 31.2), (35.5, 31.2), (36.0, 31.6), (37.0, 32.6),
                       (38.0, 31.0), (38.5, 29.5), (38.5, 26.2), (36.0, 26.2), (33.9, 27.6), (33.2, 28.6),
                       (32.55, 29.9)],
    "Arabia Deserta": [(37.0, 32.6), (38.5, 33.3), (40.9, 34.45), (42.8, 33.6), (43.8, 32.6), (44.5, 31.8),
                       (45.8, 30.8), (47.0, 30.0), (47.9, 29.9), (48.6, 29.9), (49.8, 27.5), (38.5, 27.5),
                       (38.5, 29.5), (38.0, 31.0)],
    "Arabia Felix": [(36.0, 26.2), (38.5, 26.2), (38.5, 27.5), (49.8, 27.5), (52.0, 27.0), (55.5, 26.1),
                     (56.0, 26.1), (56.0, 13.0), (45.0, 12.2), (43.35, 12.6), (42.5, 13.5), (40.5, 16.0),
                     (38.0, 20.0), (35.9, 24.0)],
    "Mauretania": [(-11.0, 35.9), (-5.4, 35.95), (-2.35, 35.6), (-2.35, 35.1), (-2.0, 34.0), (-2.0, 31.0),
                   (-5.0, 29.0), (-11.0, 28.0)],
    "Numidia": [(-2.35, 35.1), (-2.35, 35.9), (0.0, 36.3), (5.0, 37.3), (8.75, 37.5), (8.75, 36.95),
                (9.0, 36.0), (8.3, 35.2), (8.0, 34.3), (5.0, 34.3), (2.0, 34.0), (-2.0, 34.0)],
    "Africa": [(8.75, 37.5), (11.4, 37.3), (11.6, 35.5), (11.0, 34.0), (8.0, 34.0), (8.0, 34.3),
               (8.3, 35.2), (9.0, 36.0), (8.75, 36.95)],
    "Syrtica": [(8.0, 34.0), (11.0, 34.0), (11.8, 34.0), (15.5, 33.5), (19.0, 31.5), (19.0, 30.0),
                (15.0, 29.5), (10.0, 30.5), (8.0, 32.0)],
    "Cyrenaica": [(19.0, 30.0), (19.0, 31.5), (20.0, 33.5), (23.2, 33.5), (23.2, 29.0), (21.0, 29.0)],
    "Marmarica": [(23.2, 33.5), (29.0, 32.0), (29.0, 31.3), (29.0, 30.3), (28.0, 29.5), (28.0, 28.8),
                  (23.2, 28.8)],
    "Aethiopia": [(-11.0, 16.0), (24.0, 16.0), (24.0, 24.0), (35.9, 24.0), (38.0, 20.0), (40.5, 16.0),
                  (42.5, 13.5), (43.35, 12.6), (45.0, 12.2), (56.0, 13.0), (56.0, 9.0), (-11.0, 9.0)],
    "Libya Interior": [(-11.0, 16.0), (30.0, 16.0), (30.0, 34.5), (-11.0, 34.5)],
}

# Earlier wins where polygons overlap.
PRIORITY = [
    "Sicilia", "Hispania", "Italia", "Gallia", "Raetia", "Pannonia", "Illyricum", "Macedonia",
    "Thracia", "Dacia", "Hellas", "Scythia", "Sarmatia", "Colchis", "Armenia", "Lydia", "Phrygia",
    "Cilicia", "Cappadocia", "Syria", "Mesopotamia", "Babylonia", "Susiana", "Persis", "Media",
    "Hyrcania", "Aegyptus", "Arabia Petraea", "Arabia Deserta", "Arabia Felix", "Mauretania",
    "Numidia", "Africa", "Syrtica", "Cyrenaica", "Marmarica", "Aethiopia", "Libya Interior",
]

# A point on each island (lon, lat) and the region the whole island joins.
ISLANDS = [
    ((2.9, 39.6), "Hispania"), ((4.1, 39.95), "Hispania"), ((1.4, 39.0), "Hispania"),
    ((9.0, 40.0), "Italia"), ((9.1, 42.2), "Italia"), ((10.25, 42.78), "Italia"),
    ((14.2, 37.5), "Sicilia"), ((14.4, 35.9), "Sicilia"),
    ((24.9, 35.25), "Hellas"), ((26.3, 39.2), "Hellas"), ((26.0, 38.4), "Hellas"),
    ((26.8, 37.72), "Hellas"), ((26.15, 37.6), "Hellas"), ((27.1, 36.85), "Hellas"),
    ((28.0, 36.2), "Hellas"), ((27.15, 35.6), "Hellas"), ((25.25, 39.9), "Hellas"),
    ((24.7, 40.68), "Hellas"), ((23.8, 38.5), "Hellas"), ((19.85, 39.6), "Hellas"),
    ((20.55, 38.25), "Hellas"),
    ((25.9, 40.18), "Thracia"), ((25.55, 40.47), "Thracia"),
    ((33.2, 35.0), "Cilicia"),
    ((10.9, 33.8), "Syrtica"), ((11.2, 34.7), "Africa"),
    ((53.8, 12.5), "Arabia Felix"), ((50.55, 26.05), "Arabia Felix"),
]

# Islands so close to another coast that the population grid joins them to
# it: (lon_min, lat_min, lon_max, lat_max) boxes that override the polygons.
ISLAND_BOXES = [
    ((25.8, 38.95, 26.62, 39.42), "Hellas"),   # Lesbos
    ((25.83, 38.15, 26.17, 38.6), "Hellas"),   # Chios
    ((26.55, 37.63, 27.0, 37.81), "Hellas"),   # Samos
    ((26.95, 36.73, 27.37, 36.9), "Hellas"),   # Kos
    ((27.68, 35.87, 28.25, 36.47), "Hellas"),  # Rhodes
    ((24.5, 40.53, 24.8, 40.82), "Hellas"),    # Thasos
]


def points_in_polygon(lon, lat, poly):
    """Even-odd rule, vectorized over the node centres."""
    inside = np.zeros(lon.shape, dtype=bool)
    n = len(poly)
    for k in range(n):
        x1, y1 = poly[k]
        x2, y2 = poly[(k + 1) % n]
        crosses = (y1 > lat) != (y2 > lat)
        with np.errstate(divide="ignore", invalid="ignore"):
            x_at = x1 + (lat - y1) * (x2 - x1) / (y2 - y1)
        inside ^= crosses & (lon < x_at)
    return inside


def load_land():
    with open(os.path.join(POP_DIR, "hyde_meta.json")) as f:
        meta = json.load(f)
    first = min(meta["keyframes"], key=lambda k: k["year"])
    with gzip.open(os.path.join(POP_DIR, first["file"]), "rb") as f:
        nodes = np.frombuffer(f.read(), dtype="<f4").reshape(meta["height"], meta["width"])
    return meta, nodes != meta["no_data"], nodes


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--preview", help="also write a colour preview PNG here")
    args = ap.parse_args(argv)

    assert sorted(PRIORITY) == sorted(POLYGONS) == sorted(ID), "every region needs one polygon"
    meta, land, pop = load_land()
    h, w = land.shape
    lon_min, lon_max, lat_min, lat_max = meta["lon_min"], meta["lon_max"], meta["lat_min"], meta["lat_max"]
    step_x = (lon_max - lon_min) / w
    step_y = (lat_max - lat_min) / h
    lon = lon_min + (np.arange(w) + 0.5) * step_x
    lat = lat_max - (np.arange(h) + 0.5) * step_y
    LON, LAT = np.meshgrid(lon, lat)

    # 1. Polygons, in priority order.
    region = np.zeros((h, w), dtype=np.uint8)
    for name in PRIORITY:
        hit = points_in_polygon(LON, LAT, POLYGONS[name]) & land & (region == 0)
        region[hit] = ID[name]

    for (x0, y0, x1, y1), name in ISLAND_BOXES:
        region[land & (LON >= x0) & (LON <= x1) & (LAT >= y0) & (LAT <= y1)] = ID[name]

    # 2. Islands go whole to their region.
    labels, count = ndimage.label(land, structure=np.ones((3, 3)))
    sizes = np.bincount(labels.ravel())
    mainland = int(np.argmax(sizes[1:]) + 1)
    for (plon, plat), name in ISLANDS:
        x = int((plon - lon_min) / step_x)
        y = int((lat_max - plat) / step_y)
        # The point may fall just offshore at this resolution: look nearby.
        window = labels[max(0, y - 2):y + 3, max(0, x - 2):x + 3]
        found = [l for l in np.unique(window) if l not in (0, mainland)]
        if not found:
            print(f"  note: {name} {plon},{plat} is not a separate island on this grid (polygon or box decides)")
        for l in found:
            region[labels == l] = ID[name]

    # 3. Uncovered land takes the nearest assigned land node's region.
    unassigned = land & (region == 0)
    if unassigned.any():
        _, (iy, ix) = ndimage.distance_transform_edt(region == 0, return_indices=True)
        region[unassigned] = region[iy[unassigned], ix[unassigned]]
    print(f"{int(unassigned.sum()):,} border nodes filled from their nearest region")

    # Neighbours by land contact (8-connected).
    neighbours = {i: set() for i in range(1, len(REGIONS) + 1)}
    for dy, dx in ((0, 1), (1, 0), (1, 1), (1, -1)):
        # a = region[y, x], b = region[y + dy, x + dx], over every valid (y, x)
        x0, x1 = max(0, -dx), w - max(0, dx)
        a = region[0:h - dy, x0:x1]
        b = region[dy:h, x0 + dx:x1 + dx]
        pairs = np.unique(np.stack([a.ravel(), b.ravel()], 1), axis=0)
        for p, q in pairs:
            if p and q and p != q:
                neighbours[int(p)].add(int(q))
                neighbours[int(q)].add(int(p))
    land_only = {k: set(v) for k, v in neighbours.items()}

    # Sea lanes: coasts within SEA_LANE_KM of each other.
    sea = ~land
    coast = land & ndimage.binary_dilation(sea, structure=np.ones((3, 3)))
    xy = {}
    for i in range(1, len(REGIONS) + 1):
        ys, xs = np.nonzero(coast & (region == i))
        if len(ys):
            lat_c = lat[ys]
            xy[i] = np.stack([lon[xs] * np.cos(np.radians(lat_c)), lat_c], 1) * KM_PER_DEGREE
    trees = {i: cKDTree(p) for i, p in xy.items()}
    for i in xy:
        for j in xy:
            if j <= i or j in neighbours[i]:
                continue
            d, _ = trees[j].query(xy[i], k=1, distance_upper_bound=SEA_LANE_KM)
            if np.isfinite(d).any():
                neighbours[i].add(j)
                neighbours[j].add(i)

    # Label anchor: the point deepest inside the region's largest piece.
    out_regions = []
    for i, (name, continent, folded) in enumerate(REGIONS, start=1):
        mask = region == i
        count_nodes = int(mask.sum())
        assert count_nodes > 0, f"{name} got no land"
        parts, n_parts = ndimage.label(mask)
        largest = np.argmax(np.bincount(parts.ravel())[1:]) + 1
        depth = ndimage.distance_transform_edt(np.pad(parts == largest, 1))[1:-1, 1:-1]
        y, x = np.unravel_index(np.argmax(depth), depth.shape)
        people = float(np.maximum(pop[mask], 0).sum())
        out_regions.append({
            "id": i, "name": name, "continent": continent, "includes": folded,
            "label_lon": round(float(lon[x]), 3), "label_lat": round(float(lat[y]), 3),
            "nodes": count_nodes,
            "neighbours": sorted(neighbours[i]),
            "sea_lanes": sorted(neighbours[i] - land_only[i]),
        })
        print(f"{i:2d} {name:15s} {continent:6s} {count_nodes:7,d} nodes {people / 1e6:6.2f} M people (300 BC)  "
              f"neighbours: {', '.join(REGIONS[j - 1][0] for j in sorted(neighbours[i]))}")

    os.makedirs(OUT_DIR, exist_ok=True)
    with gzip.open(os.path.join(OUT_DIR, "region_nodes.u8.gz"), "wb", compresslevel=9) as f:
        f.write(region.astype(np.uint8).tobytes())
    with open(os.path.join(OUT_DIR, "regions.json"), "w") as f:
        json.dump({
            "source": "tools/build_region_mask.py: the ancient geographers' regions, see MECHANICS.md",
            "width": w, "height": h,
            "lon_min": lon_min, "lon_max": lon_max, "lat_min": lat_min, "lat_max": lat_max,
            "file": "region_nodes.u8.gz",
            "regions": out_regions,
        }, f, indent=1)
    print(f"wrote {OUT_DIR}/regions.json and region_nodes.u8.gz ({len(REGIONS)} regions)")

    if args.preview:
        from PIL import Image, ImageDraw
        rng = np.random.default_rng(7)
        palette = np.vstack([[20, 30, 50], rng.integers(60, 240, (len(REGIONS), 3))]).astype(np.uint8)
        img = palette[region]
        edge = np.zeros_like(land)
        edge[:, 1:] |= region[:, 1:] != region[:, :-1]
        edge[1:, :] |= region[1:, :] != region[:-1, :]
        img[edge & land] = 0
        im = Image.fromarray(img).resize((w * 2, h * 2), Image.NEAREST)
        draw = ImageDraw.Draw(im)
        for r in out_regions:
            x = (r["label_lon"] - lon_min) / step_x * 2
            y = (lat_max - r["label_lat"]) / step_y * 2
            draw.text((x - 3 * len(r["name"]), y - 5), r["name"], fill=(255, 255, 255))
        im.save(args.preview)
        print(f"preview: {args.preview}")


if __name__ == "__main__":
    main()
