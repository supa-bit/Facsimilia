# Map data

The 300 BC political polygons in `data/ancient_bc300.json` come from the
included `tools/world_bc300.geojson`. `tools/import_bc300.js` projects them to
the 8192 × 5476 game grid. These polygons are historical approximations,
including some long and artificial borders. They do not define coastlines.

The land and sea mask in `data/land_mask.png` comes from
`tools/ne_10m_land.geojson`, Natural Earth's public domain 1:10m land dataset:
https://www.naturalearthdata.com/downloads/10m-physical-vectors/10m-land/
The bundled source is from
https://github.com/nvkelso/natural-earth-vector/blob/master/geojson/ne_10m_land.geojson.
It depicts a modern coastline, which is sufficient for this broad scale but
does not reconstruct coastal changes since 300 BC.

The mask uses the same simple longitude and latitude projection as the
political importer: longitude −10° to 55°, latitude 48° to 10°. Black is sea;
white is land. The game seeds the sea first and paints political territories
only on land. To rebuild the mask, install Pillow and run
`python tools/build_land_mask.py`. To rebuild historical boundaries, run
`node tools/import_bc300.js`. If the map dimensions or geographic bounds
change, update both tools and `scripts/world/map_view.gd` together.
