# Facsimilia's data files

Everything that describes the world lives in plain text files in the
`data` folder, in a format called JSON (lists in `[ ]`, named fields in
`{ }`, text in "quotes"). You can open them in VS Code and change them.
Each file starts with an `"about"` field that explains it.

After changing a file:

1. Save it, and run the game (or the tests, see CLAUDE.md). The test
   `src/Tests/DataTest.cs` checks that the files agree with each other
   (every realm has a culture, every tech a tech's prerequisites exist,
   and so on) and names anything wrong.
2. VS Code marks broken JSON (a missing comma or quote) in red.
3. Keep sources and licences in mind: the game may be sold, so data
   must be our own, public domain, CC0 or CC BY (credited on the main
   menu).

## The world in 300 BC

| File | What it holds | To add one |
|---|---|---|
| `realms_bc300.json` | Every realm: its key, name, people (`culture`, from cultures.json), naming tradition (`names`: Latin, Greek, Punic, Italic, Etruscan, Thracian, Illyrian, Berber, Persian, Caucasian, Arabian, Celtic, Iberian, Scythian, Meroitic, Nabataean), map colour, capital, ruler and family, `tribal` (no provinces at start), and the historical `districts` it held (name, longitude, latitude, optional reach in km). | Copy a realm, change it, then run `python3 tools/build_realm_mask.py` to redraw the borders. Also give it an entry in `start_realms.json`. |
| `start_realms.json` | Each realm's army (units by role, in thousands), starting silver (`treasury_years`), manpower and army spending; the alliances and rivalries of 300 BC. | Add a line under `realms` with the realm's key. |
| `cultures.json` | Cultures and religions, which people lives in each geographic region, provinces whose people differ, and related cultures (`kin`). | Add a culture to `cultures`, then use its key in regions or realms. |
| `province_seeds.json` | The historical regions that seed and name provinces. | Add a name with longitude and latitude. |
| `places.json` | 6,203 ancient towns from Pleiades with their names over time. Rebuilt by `tools/build_places.py`; don't edit by hand. | |
| `coins.json` | Each realm's coins over time and the silver in each, in grams. | Add a realm key with a list of coins (`from` year, `name`, `one`, `grams`). |

## Economy and land

| File | What it holds |
|---|---|
| `goods.json` | Every good: family, price, raw or made, recipes (inputs and labour), needs per person. |
| `crops.json` | Crops, orchards, herds and fisheries: where they grow and what they yield. |
| `labour.json` | The share of people dependent or enslaved in each region over time; abolition dates. |
| `buildings.json` | The core buildings: cost (in talents of 26.2 kg of silver), years to build, upkeep, what land they need, their effects. |
| `trade_routes.json` | Trade routes as chains of real places (name, longitude, latitude), the year each opens (and closes). |
| `land/` | The land layer (climate, soil, terrain, resources), built by `tools/build_land_layer.py`. Don't edit by hand. |
| `population/` | HYDE population data, built by `tools/build_population_mask.py`. Don't edit by hand. |

## War, knowledge and history

| File | What it holds |
|---|---|
| `units.json` | Every people's units: role, might, men, cost, upkeep, which cultures can raise them, and a tech they need. |
| `techs.json` | 732 technologies in 15 branches: year, cost, prerequisites, effects; `start` lists what each culture knows in 300 BC. |
| `disasters.json` | History's plagues, earthquakes, eruptions and famines (year, place, reach, deaths), and the quake belts for chance ones. |
| `history_goals.json` | History's campaigns: which realm attacks which, where, and when. |
| `goals.json` | Each realm's optional goals and the common ones; how points add to the score. |

## Keys that must match

- A realm's `key` in `realms_bc300.json` is used in `start_realms.json`,
  `goals.json`, `history_goals.json` and `coins.json`.
- A realm's `culture` must be a key in `cultures.json`'s `cultures`.
- A unit's `requires`, a building's `requires` and a tech's `requires`
  must be tech `id`s in `techs.json`.
