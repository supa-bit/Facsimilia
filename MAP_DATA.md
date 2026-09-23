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

## Rebuild

Python dependencies: Pillow, NumPy, SciPy. From this project directory:

1. `node tools/import_bc300.js` if changing the historical source.
2. `python tools/build_land_mask.py` if changing physical geography.
3. `python tools/reconcile_map.py` after either of those changes.
4. `python tools/build_relief_texture.py` to rebuild from the included crop.
5. Open `project.godot` in Godot and allow the assets to import.

The project includes the completed assets; Python and Node are not required to
play. Remove stale `.godot` cache only if an existing local editor fails to pick
up replaced textures. The delivered ChatGPT folder omits generated caches.

## Validation

Godot 4.5.2: terrain sanity test passed; 52 ownership-image samples, zero
mismatches. The actual OpenGL shader was rendered at regional zoom using
software rendering. Brush and clear operations passed ownership/display checks.
Coast-repair regression checks verified connected filling, no water crossing,
and preservation of existing claims. The map is not independently audited for
historical accuracy.

Realm label anchors are selected on their own territory instead of using
unconstrained centroids. All twelve anchors were checked against ownership.
