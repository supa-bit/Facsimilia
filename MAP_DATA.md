# Facsimilia map revision

Only `Facsimilia/facsimilia-chatgpt-only` was changed. The Claude project is
preserved byte for byte in the delivered archive.

## Geography and ownership

The physical mask uses Natural Earth 1:10m land polygons. The historical
polygons use the bundled historical-basemaps 300 BC dataset. These are two
separate approximations: clipping one against the other removes claims at sea,
but does not fill slivers left between their disagreeing coastlines.

`tools/reconcile_map.py` now bakes `data/political_mask.png` on the identical
8192 x 5476 grid, longitude -10 to 55, latitude 48 to 10. It first applies a
small common displacement to inland label boundaries, so neighboring regions
share one boundary rather than independently wiggled edges. The exact Natural
Earth coastline remains unchanged. It then extends claims across unclaimed
coastal slivers, only within 40 cells of the coast and at most 56 land-connected
steps from an existing claim. It never crosses water, overwrites an existing
claim, or assigns an otherwise unclaimed island. This is a bounded cartographic
inference, not newly sourced historical territory. Larger unknown areas remain
unclaimed. The report is in `data/map_reconciliation.json`.

Godot loads this reconciled ownership raster for the initial state. Rendering,
selection, proposals and game logic use the same ownership grid. Cosmetic
terrain colors never determine ownership. Tribal frontier zones remain
explicitly approximate sketches; this revision does not make them historical
state boundaries. The bundled source also groups some distinct Greek polities.

## Terrain

The old random cloud texture has been replaced with Natural Earth I land-cover,
shaded relief and ocean-floor imagery:
https://www.naturalearthdata.com/downloads/10m-raster-data/10m-natural-earth-1/
https://naturalearth.s3.amazonaws.com/10m_raster/NE1_HR_LC_SR_W.zip

Natural Earth data are public domain. This is modern physical geography and
land cover, not a reconstruction of vegetation or coastal changes in 300 BC.
The 3900 x 2280 geographic source crop is included at
`tools/relief_source_crop.png`; it is resampled to the ownership grid. Upsampling
does not add survey detail. The terrain shader uses mipmaps and linear filtering
while ownership retains nearest filtering. Political tints preserve the relief
beneath them; borders scale with the screen footprint instead of thick black
pixel outlines. Ocean coloration follows the source bathymetric shading rather
than arbitrary noise or a uniform glow around every coastline.

## Population (HYDE)

The population engine (`src/World/PopulationEngine.cs`, design in
MECHANICS.md) reads HYDE 3.2.1 historical population grids, baseline
estimate, population counts (`popc_*`). HYDE is by PBL Netherlands
Environmental Assessment Agency / Utrecht University: Klein Goldewijk et
al. (2017), Earth System Science Data 9, 927-953, CC BY 3.0. The original
is `HYDE3_2_1-baseline.zip` on DANS, doi:10.17026/dans-25g-gez3. The
`popc` files are mirrored in this repository's `assets-v1` GitHub release,
alongside the Godot 4.7.2 Linux zip the session hook installs.

`tools/build_population_mask.py` crops each 5-arcminute snapshot from
300 BC onward to the map extent. The result is exactly 780 x 456 population
nodes (12 per degree), each ~10.5 x 12 ownership cells, in the same linear
lon/lat projection. Output goes to `data/population/`: `hyde_meta.json`
plus one gzip'd float32 grid per keyframe year, 66 keyframes from 300 BC
to 2017 AD.

HYDE has no 300 BC grid. Before 0 AD its snapshots are 1000 years apart,
so the 300 BC keyframe is derived from 1000 BC and 0 AD: per node, the
growth is taken as steady and exponential, which is the time-weighted
geometric mean. The 152 nodes that are empty at one end fall back to
linear. That gives 39.4 million people on the map at 300 BC, against 26.1
million at 1000 BC and 48.2 million at 0 AD; linear would give 41.5
million. `hyde_meta.json` marks the keyframe `derived`. Every later
keyframe is HYDE's own snapshot.

HYDE spreads each country's historical total over modern-derived
settlement patterns, so its densest ancient nodes are where cities are
today. At 300 BC the largest are Beirut (~108,000), the Tel Aviv coast,
Damascus, Tunis and Athens, not Alexandria or Babylon. That's HYDE's own
allocation, not an artifact of the crop.

Without `data/population/` the game runs without population: the engine
switches itself off, and saves and the HUD simply omit it.

## Geographic regions

`tools/build_region_mask.py` bakes the 38 regions of the ancient
geographers (Europa, Libya, Asia; see MECHANICS.md, "Geographic regions")
onto the same 780 x 456 population nodes. Land is exactly the population
grid's land (every node the 300 BC keyframe doesn't mark as sea). Each
region is a hand-drawn longitude/latitude outline in the script; where
outlines overlap, the earlier one in its priority list wins, and land no
outline covers takes its nearest region. Islands go whole to a named
region; small boxes cover the east Aegean islands that the 9 km grid
joins to Asia's coast. Neighbours are regions whose land touches or whose
coasts are within 300 km across water.

Output goes to `data/regions/`: `region_nodes.u8.gz` (one byte per node,
the region id 1-38; 0 = sea) and `regions.json` (names, continents,
what's folded into each, where its name goes, neighbours). Run
`python tools/build_region_mask.py --preview regions.png` to also draw a
coloured check image. The population engine loads the regions with the
HYDE data; without them it treats the whole map as one region.

## Land layer

`tools/fetch_land_sources.py` downloads the sources into a cache outside
the repository (`~/.cache/facsimilia-land`, about 2.5 GB; soils come
already cropped from ISRIC's server, and KK10's 300 BC slice is read out
of its 17 GB file without downloading the rest). `tools/build_land_layer.py`
bakes 83 values per population node into `data/land/` (6.4 MB), with
`tools/land_sites.json` adding historical sites and hand fixes. Sources
and licences, all cleared for a commercial game:

- CHELSA V2.1 climatologies 1981-2010: CC0. Karger, D.N. et al. (2017)
  Climatologies at high resolution for the earth's land surface areas.
  Scientific Data 4, 170122.
- SoilGrids 2.0, ISRIC World Soil Information: CC BY 4.0. Poggio, L. et
  al. (2021) SoilGrids 2.0. SOIL 7, 217-240.
- ETOPO 2022 60 arc-second global relief, NOAA NCEI: public domain.
- KK10 anthropogenic land cover change: CC BY 3.0. Kaplan, J.O. and
  Krumhardt, K.M. (2011), PANGAEA, doi:10.1594/PANGAEA.871369.
- Natural Earth 10m rivers and lakes: public domain.
- USGS Mineral Resources Data System: public domain.

The CC BY sources are credited on the main menu, as their licences
require. CHELSA's ancient-climate series (TraCE21k) is CC BY-SA and is
deliberately not used. `src/Tests/LandLayerTest.cs` fails if a source
with another licence is ever added.

## Rebuild

Python dependencies: Pillow, NumPy, SciPy. From this project directory:

1. `node tools/import_bc300.js` if changing the historical source.
2. `python tools/build_land_mask.py` if changing physical geography.
3. `python tools/reconcile_map.py` after either of those changes.
4. `python tools/build_relief_texture.py` to rebuild from the included crop.
5. `python tools/fetch_hyde.py <dir>` downloads the HYDE `popc` zips from
   the `assets-v1` release (`--from-dans` reads them out of the original
   DANS archive instead; needs `pip install zipfile-deflate64`). Then
   `python tools/build_population_mask.py <dir>` bakes the population
   keyframes (needs NumPy only).
6. `python tools/build_region_mask.py` bakes the geographic regions onto
   the population grid (needs NumPy and SciPy; run after step 5).
7. `python tools/fetch_land_sources.py`, then
   `python tools/build_land_layer.py` bakes the land layer (needs
   rasterio, netCDF4, h5py, fsspec, aiohttp, requests and pyshp too).
8. Open `project.godot` in the Godot 4.7.2 **.NET** build (the C# one;
   the standard build can't run the project's C# code) and allow the
   assets to import. Building needs the .NET 10 SDK: `dotnet build`, or
   the default build task in VS Code (`.vscode/`, which also has Play,
   Editor and Test launch configurations; set `GODOT` to the Godot .NET
   executable).

The project includes the completed assets; Python and Node are not required to
play. Remove stale `.godot` cache only if an existing local editor fails to pick
up replaced textures. The delivered ChatGPT folder omits generated caches.

## Code

The game is C# on Godot 4.7.2 .NET (`Facsimilia.csproj`, .NET 10):
`src/World/` holds the map, ownership grid, population engine, saves and
the game flow; `src/Dynasties/` the characters, dynasties and realms;
`src/UI/` the theme and screens; `src/Tests/` the headless test suites.
Scenes are in `scenes/`, the border/terrain shader in `shaders/`.

## Validation

Godot 4.5.2: terrain sanity test passed; 52 ownership-image samples, zero
mismatches. The actual OpenGL shader was rendered at regional zoom using
software rendering. Brush and clear operations passed ownership/display checks.
Coast-repair regression checks verified connected filling, no water crossing,
and preservation of existing claims. The map is not independently audited for
historical accuracy.

Realm label anchors are selected on their own territory instead of using
unconstrained centroids. All twelve anchors were checked against ownership.

## UI assets

Fonts (SIL Open Font License, license texts beside them in
`assets/fonts/`): Cinzel by Natanael Gama for headings, Alegreya by Juan
Pablo del Peral / Huerta Tipográfica for body text, both from Google Fonts.
Icons: game-icons.net by Lorc and Delapouite, CC BY 3.0, recolored to
white so the game can tint them; the per-file list is
`assets/icons/CREDITS.md`. `src/UI/ThemeAncient.cs` builds the theme
from them.
