"""Bake the land layer: what every place on the map *is*, from real data.

See MECHANICS.md, "Resources and the land". Six parts - terrain, climate,
soil, water, living habitat, geology - each a handful of numbers per
population node (the same 780 x 456 grid, 5 arc-minutes, ~9 km, as
population and regions). Sources are fetched by tools/fetch_land_sources.py
(every one allows commercial use; see there), and tools/land_sites.json
adds the historical mines, quarries, salt works and special habitats no
global dataset records, plus hand fixes where modern data is wrong for
300 BC.

Measured values come straight from the data (averaged over each node);
"derived" values are computed here from measured ones by simple, stated
rules, and are marked so in land.json. Climate is modern (1981-2010),
which is close to the Roman Warm Period's; habitat is 300 BC (KK10 land use
taken out of the natural cover the climate supports).

Output, under data/land/:
  land.json        every field: part, unit, how the byte maps to a value,
                   description, source and licence; the historical sites
                   with their node and years; imports from beyond the map
  <field>.u8.gz    one byte per node, row-major from the north-west corner;
                   value = min + byte / 255 * (max - min); sea nodes are 0

Usage: python3 tools/build_land_layer.py [--cache DIR] [--preview DIR]
Needs numpy, scipy, rasterio, netCDF4, pyshp (and Pillow for --preview).
"""
import argparse
import csv
import gzip
import io
import json
import math
import os
import zipfile

import numpy as np
from scipy import ndimage

ROOT = os.path.join(os.path.dirname(__file__), "..")
POP_DIR = os.path.join(ROOT, "data", "population")
OUT_DIR = os.path.join(ROOT, "data", "land")
SITES = os.path.join(os.path.dirname(__file__), "land_sites.json")

LON_MIN, LON_MAX, LAT_MIN, LAT_MAX = -10, 55, 10, 48
W, H = 780, 456
STEP = 1 / 12
KM_PER_DEG = 111.2
LAT = LAT_MAX - (np.arange(H) + 0.5) * STEP
LON = LON_MIN + (np.arange(W) + 0.5) * STEP
LONS, LATS = np.meshgrid(LON, LAT)

SOURCES = {
    "chelsa": ("CHELSA V2.1 climatologies 1981-2010 (Karger et al. 2017, Scientific Data 4:170122)", "CC0 1.0"),
    "soilgrids": ("SoilGrids 2.0, ISRIC (Poggio et al. 2021, SOIL 7:217-240)", "CC BY 4.0"),
    "etopo": ("ETOPO 2022 60 arc-second, NOAA National Centers for Environmental Information", "public domain"),
    "kk10": ("KK10 anthropogenic land cover change (Kaplan et al. 2011, The Holocene 21:775-791)", "CC BY 3.0"),
    "naturalearth": ("Natural Earth 10m rivers and lakes", "public domain"),
    "mrds": ("USGS Mineral Resources Data System", "public domain"),
    "sites": ("Facsimilia historical sites (tools/land_sites.json), from standard reference works", "project's own"),
}

FIELDS = []   # (name, part, unit, lo, hi, values, description, source, derived)


def add(name, part, unit, lo, hi, values, description, source, derived=False):
    FIELDS.append((name, part, unit, lo, hi, np.asarray(values, dtype=np.float64), description, source, derived))


# --- Readers -------------------------------------------------------------------

def load_land():
    with open(os.path.join(POP_DIR, "hyde_meta.json")) as f:
        meta = json.load(f)
    first = min(meta["keyframes"], key=lambda k: k["year"])
    with gzip.open(os.path.join(POP_DIR, first["file"]), "rb") as f:
        nodes = np.frombuffer(f.read(), dtype="<f4").reshape(H, W)
    return nodes != meta["no_data"]


def chelsa(cache, var):
    """Mean over each node of a 30-arc-second CHELSA grid (10 x 10 cells per node)."""
    import rasterio
    from rasterio.windows import from_bounds
    with rasterio.open(os.path.join(cache, f"chelsa_{var}.tif")) as src:
        win = from_bounds(LON_MIN, LAT_MIN, LON_MAX, LAT_MAX, src.transform)
        raw = src.read(1, window=win, out_shape=(H * 10, W * 10)).astype(np.float64)
        scale, offset = src.scales[0], src.offsets[0]
    vals = raw * scale + offset
    vals[(raw >= 65535) | (vals <= -9999)] = np.nan   # the files' "no data" markers
    blocks = vals.reshape(H, 10, W, 10)
    with np.errstate(invalid="ignore"):
        return np.nanmean(blocks, axis=(1, 3))


def soil(cache, prop, depths=("0-5cm", "5-15cm", "15-30cm")):
    """SoilGrids property, thickness-weighted over the given depths (the topsoil by default)."""
    import rasterio
    thick = {"0-5cm": 5, "5-15cm": 10, "15-30cm": 15, "30-60cm": 30, "60-100cm": 40}
    total = np.zeros((H, W))
    weight = np.zeros((H, W))
    for d in depths:
        with rasterio.open(os.path.join(cache, f"soil_{prop}_{d}.tif")) as src:
            a = src.read(1).astype(np.float64)
        ok = a > 0
        total[ok] += a[ok] * thick[d]
        weight[ok] += thick[d]
    with np.errstate(invalid="ignore"):
        return np.where(weight > 0, total / np.maximum(weight, 1), np.nan)


def etopo(cache):
    """ETOPO 2022 at 60 arc-seconds over the map: 5 x 5 cells per node, north to south."""
    import netCDF4
    ds = netCDF4.Dataset(os.path.join(cache, "etopo_2022_60s.nc"))
    lat = ds["lat"][:]
    lon = ds["lon"][:]
    y0 = int(np.argmin(np.abs(lat - (LAT_MIN + 1 / 120))))
    x0 = int(np.argmin(np.abs(lon - (LON_MIN + 1 / 120))))
    z = np.asarray(ds["z"][y0:y0 + H * 5, x0:x0 + W * 5], dtype=np.float64)
    if lat[1] > lat[0]:
        z = z[::-1]
    return z


def fill_nan(a, land):
    """Nodes a dataset misses (coastal slivers) take their nearest valid neighbour."""
    missing = land & ~np.isfinite(a)
    if missing.any():
        good = np.isfinite(a)
        _, (iy, ix) = ndimage.distance_transform_edt(~good, return_indices=True)
        a = a.copy()
        a[missing] = a[iy[missing], ix[missing]]
    return a


def smooth01(x, a, b):
    """0 below a, 1 above b, a smooth ramp between."""
    t = np.clip((np.asarray(x, dtype=np.float64) - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def node_distance_km(mask):
    """Distance from every node to the nearest True node, in km."""
    km_x = KM_PER_DEG * STEP * np.cos(np.radians(LATS)).mean()
    d = ndimage.distance_transform_edt(~mask, sampling=(KM_PER_DEG * STEP, km_x))
    return d


def spot(lon, lat, radius_km):
    """A soft disc around a point: 1 at the centre, fading to 0 at twice the radius."""
    dx = (LONS - lon) * KM_PER_DEG * math.cos(math.radians(lat))
    dy = (LATS - lat) * KM_PER_DEG
    return np.exp(-(dx * dx + dy * dy) / (2 * radius_km ** 2))


def rasterize_shapes(zip_path, value_of, all_touched=True, oversample=1):
    """Draws a Natural Earth shapefile onto the node grid; value_of(record) -> value, max wins."""
    import rasterio.features
    import shapefile   # pyshp
    from rasterio.transform import from_bounds
    out = np.zeros((H * oversample, W * oversample))
    transform = from_bounds(LON_MIN, LAT_MIN, LON_MAX, LAT_MAX, W * oversample, H * oversample)
    with zipfile.ZipFile(zip_path) as z:
        stem = next(n for n in z.namelist() if n.endswith(".shp"))[:-4]
        reader = shapefile.Reader(shp=io.BytesIO(z.read(stem + ".shp")), dbf=io.BytesIO(z.read(stem + ".dbf")),
                                  shx=io.BytesIO(z.read(stem + ".shx")), encoding="latin-1")
        shapes = []
        for sr in reader.iterShapeRecords():
            if not sr.shape.points:
                continue
            v = value_of(sr.record.as_dict())
            if v > 0:
                shapes.append((sr.shape.__geo_interface__, v))
    for geom, v in sorted(shapes, key=lambda s: s[1]):
        r = rasterio.features.rasterize([(geom, v)], out_shape=out.shape, transform=transform,
                                        all_touched=all_touched, fill=0, dtype="float64")
        np.maximum(out, r, out=out)
    if oversample > 1:
        out = out.reshape(H, oversample, W, oversample)
    return out


# --- The six parts --------------------------------------------------------------

def terrain(cache, land):
    z = etopo(cache)                          # 60" cells
    zb = z.reshape(H, 5, W, 5)
    land_cell = zb > 0
    elev = np.where(land, np.nanmean(np.where(land_cell, zb, np.nan), axis=(1, 3)), 0)
    elev = np.where(land & ~np.isfinite(elev), np.maximum(zb.max(axis=(1, 3)), 0), elev)
    # Slope per 60" cell from neighbouring elevations.
    cell_y = KM_PER_DEG * 1000 / 60
    lat_cells = np.repeat(LAT, 5)
    cell_x = (cell_y * np.cos(np.radians(lat_cells)))[:, None]
    gy, gx = np.gradient(z)
    dzdx = gx / cell_x
    dzdy = gy / cell_y
    slope = np.degrees(np.arctan(np.hypot(dzdx, dzdy)))
    sb = slope.reshape(H, 5, W, 5)
    with np.errstate(invalid="ignore"):
        slope_mean = np.nanmean(np.where(land_cell, sb, np.nan), axis=(1, 3))
        flat = np.nanmean(np.where(land_cell, sb < 3.0, np.nan), axis=(1, 3))
        rugged = np.nanstd(np.where(land_cell, zb, np.nan), axis=(1, 3))
        # Rows run north to south, so +gy points south: south-facing slopes
        # have elevation falling southward, i.e. dz/dy(south) < 0.
        south = -dzdy / np.maximum(np.hypot(dzdx, dzdy), 1e-9)
        steep = land_cell & (sb > 2.0)
        southness = np.nanmean(np.where(steep.reshape(H * 5, W * 5), south, np.nan).reshape(H, 5, W, 5), axis=(1, 3))
    slope_mean = fill_nan(slope_mean, land)
    flat = fill_nan(flat, land)
    rugged = fill_nan(rugged, land)
    southness = np.nan_to_num(southness)

    sea = ~land
    coast_km = node_distance_km(sea)
    # Coastal shallows: share of the sea within ~15 km that is under 50 m deep.
    shallow_cell = (z < 0) & (z > -50)
    sea_cell = z <= 0
    shallow = shallow_cell.reshape(H, 5, W, 5).mean(axis=(1, 3))
    seaf = sea_cell.reshape(H, 5, W, 5).mean(axis=(1, 3))
    k = np.ones((5, 5))
    near_sh = ndimage.convolve(shallow, k, mode="constant")
    near_sea = ndimage.convolve(seaf, k, mode="constant")
    with np.errstate(invalid="ignore", divide="ignore"):
        shelf = np.where(near_sea > 0.2, near_sh / np.maximum(near_sea, 1e-9), 0) * (coast_km <= 15)

    add("elevation", "Terrain", "m", -500, 5500, elev, "Average height above sea level", "etopo")
    add("slope", "Terrain", "degrees", 0, 30, slope_mean, "Average steepness", "etopo")
    add("flat_share", "Terrain", "share", 0, 1, flat, "Share of the land gentle enough to plough (under 3 degrees)", "etopo")
    add("ruggedness", "Terrain", "m", 0, 800, rugged, "How much the height varies within the place", "etopo")
    add("southness", "Terrain", "-1..1", -1, 1, southness, "Whether slopes face south (sunny, good for vines) or north", "etopo")
    add("coast_km", "Terrain", "km", 0, 250, np.where(land, coast_km, 0), "Distance to the sea", "etopo", True)
    add("shallows", "Terrain", "share", 0, 1, np.where(land, shelf, 0),
        "Share of the nearby sea shallower than 50 m: fish nurseries, shellfish, easy landings", "etopo", True)
    return dict(elev=elev, slope=slope_mean, flat=flat, rugged=rugged, coast_km=coast_km, shelf=shelf)


def climate(cache, land):
    c = {}
    for var in ("bio1", "bio5", "bio6", "bio12", "bio15", "gsl", "fcf", "ai", "sfcWind_mean", "sfcWind_max", "npp",
                "cmi_min"):
        c[var] = fill_nan(chelsa(cache, var), land)
    rain = c["bio12"]
    # Year-to-year rainfall varies more where it's scarce: the coefficient of
    # variation of annual rainfall runs roughly 150 / sqrt(mm) percent across
    # the Mediterranean and the Near East (e.g. ~15% at 1000 mm, ~35% at 200).
    rain_cv = np.clip(1.5 / np.sqrt(np.maximum(rain, 10)), 0, 0.8)
    # Drought: chance of a year under two-thirds of the average rain, if
    # annual rain is roughly normally distributed.
    from scipy.stats import norm
    drought = norm.cdf(-(1 / 3) / np.maximum(rain_cv, 1e-3))
    add("temp_mean", "Climate", "°C", -10, 35, c["bio1"], "Average yearly temperature", "chelsa")
    add("temp_summer", "Climate", "°C", 0, 50, c["bio5"], "Average daily high in the hottest month", "chelsa")
    add("temp_winter", "Climate", "°C", -30, 25, c["bio6"], "Average nightly low in the coldest month (frost below 0)", "chelsa")
    add("rain", "Climate", "mm/year", 0, 2500, rain, "Rain and snow in a year", "chelsa")
    add("rain_seasonality", "Climate", "%", 0, 200, c["bio15"],
        "How unevenly rain falls across the year (Mediterranean: wet winters, dry summers)", "chelsa")
    add("growing_days", "Climate", "days", 0, 366, c["gsl"], "Length of the growing season", "chelsa")
    add("frost_days", "Climate", "days", 0, 250, c["fcf"], "Days a year the temperature crosses freezing", "chelsa")
    add("aridity", "Climate", "index", 0, 3, c["ai"],
        "Rain against what the heat could evaporate: under 0.05 desert, 0.2-0.5 semi-arid, over 0.65 humid", "chelsa")
    add("wind", "Climate", "m/s", 0, 12, c["sfcWind_mean"], "Average wind speed: sailing, winnowing, windbreaks", "chelsa")
    add("storms", "Climate", "m/s", 0, 25, c["sfcWind_max"], "Wind speed in the windiest month: storm exposure", "chelsa")
    add("plant_growth", "Climate", "g C/m²/year", 0, 1500, c["npp"], "How much plants grow in a year (net primary production)", "chelsa")
    add("drought_risk", "Climate", "chance/year", 0, 0.5, drought,
        "Chance in any year of a drought (under two-thirds of the average rain)", "chelsa", True)
    add("rain_variability", "Climate", "share", 0, 0.8, rain_cv, "How much the yearly rain varies from year to year", "chelsa", True)
    return c


def soils(cache, land, t, c):
    clay = fill_nan(soil(cache, "clay") / 10, land)            # g/kg -> %
    sand = fill_nan(soil(cache, "sand") / 10, land)
    silt = fill_nan(soil(cache, "silt") / 10, land)
    soc = fill_nan(soil(cache, "soc") / 10, land)               # dg/kg -> g/kg
    nitrogen = fill_nan(soil(cache, "nitrogen") / 100, land)    # cg/kg -> g/kg
    ph = fill_nan(soil(cache, "phh2o") / 10, land)
    cec = fill_nan(soil(cache, "cec") / 10, land)               # mmol(c)/kg -> cmol(c)/kg
    bdod = fill_nan(soil(cache, "bdod") / 100, land)            # cg/cm3 -> g/cm3
    stones = fill_nan(soil(cache, "cfvo") / 10, land)           # cm3/dm3 -> %
    deep_stones = fill_nan(soil(cache, "cfvo", ("60-100cm",)) / 10, land)

    # Derived, 0-1 each.
    depth = np.clip(1 - deep_stones / 60, 0, 1) * np.clip(1 - (t["slope"] - 5) / 25, 0.2, 1)
    drainage = np.clip(0.25 + sand / 100 * 0.9 - clay / 100 * 0.7 + np.clip(t["slope"] / 15, 0, 0.3), 0, 1)
    ph_good = np.exp(-((ph - 6.8) / 1.3) ** 2)
    fertility = np.clip(0.35 * smooth01(nitrogen, 0.3, 2.5) + 0.25 * smooth01(soc, 3, 25)
                        + 0.25 * smooth01(cec, 5, 30) + 0.15 * ph_good, 0, 1)
    # Salt builds up where evaporation beats rain on flat, low, poorly
    # drained ground and in alkaline soils; the Mesopotamian fix adds the
    # irrigation salting history recorded.
    basin = smooth01(-(t["elev"] - ndimage.uniform_filter(t["elev"], size=15)), 0, 60)   # low ground among higher
    salinity = (smooth01(0.5 - c["ai"], 0.1, 0.45) * t["flat"] * smooth01(ph, 7.6, 8.6)
                * (1 - 0.5 * drainage) * np.clip(1 - t["elev"] / 1500, 0.2, 1) * (0.25 + 0.75 * basin))
    # Erosion: steep, bare-ish ground under heavy, seasonal rain.
    erosion = np.clip(smooth01(t["slope"], 2, 20) * smooth01(c["bio12"], 200, 1200)
                      * (0.5 + 0.5 * smooth01(c["bio15"], 30, 90)), 0, 1)
    compaction = smooth01(bdod, 1.35, 1.7)

    add("clay", "Soil", "%", 0, 80, clay, "Clay in the topsoil: holds water and nutrients, heavy to plough", "soilgrids")
    add("sand", "Soil", "%", 0, 100, sand, "Sand in the topsoil: drains fast, light to work, poor in nutrients", "soilgrids")
    add("silt", "Soil", "%", 0, 80, silt, "Silt in the topsoil: the rich, fine stuff of floodplains and loess", "soilgrids")
    add("organic_matter", "Soil", "g/kg", 0, 60, soc, "Organic carbon in the topsoil", "soilgrids")
    add("nitrogen", "Soil", "g/kg", 0, 6, nitrogen, "Nitrogen in the topsoil: the main nutrient crops use up", "soilgrids")
    add("ph", "Soil", "pH", 4, 9, ph, "Acidity: under 5.5 acid, 6-7.5 ideal, over 8 alkaline", "soilgrids")
    add("nutrient_holding", "Soil", "cmol/kg", 0, 50, cec,
        "How well the soil holds on to nutrients (cation exchange capacity)", "soilgrids")
    add("stones", "Soil", "%", 0, 60, stones, "Stones and gravel in the topsoil", "soilgrids")
    add("soil_depth", "Soil", "index", 0, 1, depth,
        "Rooting depth (1 deep, 0 shallow): from stones deep down and slope", "soilgrids", True)
    add("drainage", "Soil", "index", 0, 1, drainage, "How fast water drains away (1 fast, 0 waterlogged)", "soilgrids", True)
    add("fertility", "Soil", "index", 0, 1, fertility, "Natural fertility: nitrogen, organic matter, nutrient holding, pH", "soilgrids", True)
    add("salinity", "Soil", "index", 0, 1, salinity, "Salt in the soil: harms most crops (barley and dates tolerate it)", "soilgrids", True)
    add("erosion", "Soil", "index", 0, 1, erosion, "How easily the soil washes away once cleared", "soilgrids", True)
    add("compaction", "Soil", "index", 0, 1, compaction, "Dense, compacted soil that roots struggle through", "soilgrids", True)
    return dict(drainage=drainage, fertility=fertility, salinity=salinity, clay=clay, sand=sand)


def water(cache, land, t, c):
    def river_class(rec):
        rank = rec.get("scalerank", rec.get("ScaleRank", 12)) or 12
        rank = float(rank)
        if rank <= 2:
            return 1.0
        if rank <= 5:
            return 0.7
        if rank <= 8:
            return 0.45
        return 0.25

    rivers = rasterize_shapes(os.path.join(cache, "ne_10m_rivers_lake_centerlines.zip"), river_class)
    rivers = np.maximum(rivers, rasterize_shapes(os.path.join(cache, "ne_10m_rivers_europe.zip"),
                                                 lambda r: 0.25))
    lakes = rasterize_shapes(os.path.join(cache, "ne_10m_lakes.zip"), lambda r: 1.0, all_touched=False,
                             oversample=4).mean(axis=(1, 3))
    rivers = np.where(land, rivers, 0)
    river_km = node_distance_km(rivers >= 0.45)
    big_near = ndimage.maximum_filter(rivers, size=3)
    # Floodplain: flat, low ground right beside a large river.
    local_low = t["elev"] - ndimage.uniform_filter(t["elev"], size=9)
    floodplain = (smooth01(big_near, 0.5, 0.9) * t["flat"] * smooth01(-local_low, -20, 40)
                  * smooth01(1500 - t["elev"], 0, 800))
    # Wetland: deltas and lake shores (flat, very low, by big water).
    delta = (t["coast_km"] < 40) & (t["elev"] < 8) & (big_near >= 0.7)
    wetland = np.clip(np.maximum(delta * 0.6 * t["flat"], ndimage.maximum_filter(lakes, size=3) * 0.3 * t["flat"]), 0, 1)
    # Groundwater: shallow where it's low and wet, or fed by a river.
    floodplain = fixed("floodplain", floodplain, land)
    wetland = fixed("wetland", wetland, land)
    groundwater = np.clip(0.5 * smooth01(c["cmi_min"], -150, 10) + 0.4 * floodplain
                          + 0.2 * smooth01(-local_low, 0, 60) + 0.2 * smooth01(c["ai"], 0.2, 1.0), 0, 1)
    irrigation = np.clip(np.maximum(floodplain, smooth01(big_near, 0.4, 1.0) * t["flat"] * 0.8)
                         + 0.25 * groundwater * t["flat"], 0, 1)
    irrigation = fixed("irrigation", irrigation, land)
    springs = np.clip(smooth01(t["rugged"], 50, 300) * smooth01(c["bio12"], 300, 900), 0, 1)
    quality = np.clip(1 - 0.7 * salinity_proxy(c, t) - 0.3 * wetland, 0, 1)

    add("river", "Water", "class", 0, 1, rivers,
        "Largest river through the place: 1 great river (Nile, Euphrates), 0.7 major, 0.45 medium, 0.25 small",
        "naturalearth")
    add("river_km", "Water", "km", 0, 250, np.where(land, river_km, 0), "Distance to a medium or larger river", "naturalearth", True)
    add("lake", "Water", "share", 0, 1, lakes, "Share of the place under lakes", "naturalearth")
    add("floodplain", "Water", "index", 0, 1, floodplain, "Flat land a great river floods: renewed soil, easy irrigation", "naturalearth", True)
    add("wetland", "Water", "index", 0, 1, wetland, "Marsh and delta: reeds, fish, fowl, papyrus - and malaria", "naturalearth", True)
    add("groundwater", "Water", "index", 0, 1, groundwater, "How near the water table is: wells, oases, date palms", "chelsa", True)
    add("irrigation", "Water", "index", 0, 1, irrigation, "Water that can be led onto fields", "naturalearth", True)
    add("springs", "Water", "index", 0, 1, springs, "Springs from wet hills", "etopo", True)
    add("water_quality", "Water", "index", 0, 1, quality, "Fresh, clean water (brackish or marsh water is worse)", "chelsa", True)
    return dict(river=rivers, floodplain=floodplain, wetland=wetland, groundwater=groundwater, irrigation=irrigation)


def salinity_proxy(c, t):
    return smooth01(0.5 - c["ai"], 0.1, 0.45) * t["flat"]


def habitat(cache, land, t, c, s, w):
    rain, tmin, tmean = c["bio12"], c["bio6"], c["bio1"]
    seasonal = smooth01(c["bio15"], 40, 90)          # summer-dry Mediterranean regime
    wet = smooth01(rain, 350, 750)
    # What the climate and ground would grow unaided.
    desert = 1 - smooth01(rain, 40, 220)
    # River, canal or groundwater makes the desert green: the Nile valley,
    # Mesopotamia and the oases were farmland, not sand.
    watered = np.clip(np.maximum.reduce([w["floodplain"], 0.9 * w["irrigation"], 0.6 * w["groundwater"]]), 0, 1)
    desert = desert * (1 - watered)
    trees = wet * (1 - 0.6 * seasonal * (1 - smooth01(rain, 600, 1000))) * (1 - desert)
    trees *= smooth01(c["gsl"], 60, 150) * (0.4 + 0.6 * s["drainage"])
    scrub = smooth01(rain, 200, 450) * (1 - smooth01(rain, 700, 1000)) * seasonal * (1 - desert)
    grass = (1 - desert) * np.clip(1 - trees - scrub, 0, 1)
    wetland = np.clip(w["wetland"], 0, 1)
    natural = np.stack([trees, scrub, grass, desert])
    natural = natural / np.maximum(natural.sum(axis=0), 1e-9) * (1 - wetland)

    grass = np.maximum(grass, watered * (1 - trees - scrub))     # watered desert: meadow and reeds before farming
    natural = np.stack([trees, scrub, grass, desert])
    natural = natural / np.maximum(natural.sum(axis=0), 1e-9) * (1 - wetland)

    kk = np.load(os.path.join(cache, "kk10_-300.npy")).astype(np.float64)
    used = np.where(kk < 0, 0, kk * 1e-4)
    used = np.where(land, fill_nan(np.where(kk < 0, np.nan, used), land), 0)
    used = fixed("farmed", used, land)   # KK10 misses irrigated river valleys
    # People farm and graze first where it's green; the desert share stays.
    usable = 1 - natural[3] - wetland
    used = np.minimum(used, np.maximum(usable, 0))
    scale = np.where(usable > 0, 1 - used / np.maximum(usable, 1e-9), 1)
    woodland, scrubland, grassland = natural[0] * scale, natural[1] * scale, natural[2] * scale
    desert_share = natural[3]
    # A fix can raise farming past what the natural cover left room for:
    # the other shares make way so every place adds up to exactly 1.
    rest = woodland + scrubland + grassland + desert_share + wetland
    room = np.clip(1 - used, 0, 1)
    shrink = np.where(rest > room, room / np.maximum(rest, 1e-9), 1)
    woodland, scrubland, grassland, desert_share, wetland = (a * shrink for a in
                                                            (woodland, scrubland, grassland, desert_share, wetland))
    # Conifers (fir, pine, cedar: long straight timber, resin and pitch) rule
    # the cold and the high ground; oak and other broadleaves elsewhere.
    conifer = np.clip(smooth01(-tmin, -2, 8) * 0.6 + smooth01(t["elev"], 700, 1800) * 0.7
                      + 0.2 * seasonal * smooth01(rain, 400, 900), 0, 1)

    add("farmed", "Habitat", "share", 0, 1, used, "Share of the land farmed or grazed by people in 300 BC", "kk10")
    add("woodland", "Habitat", "share", 0, 1, woodland, "Share under forest in 300 BC", "kk10", True)
    add("conifer", "Habitat", "share of woodland", 0, 1, conifer,
        "How much of the forest is conifer (fir, pine, cedar): ship timber, resin, pitch", "chelsa", True)
    add("scrub", "Habitat", "share", 0, 1, scrubland, "Share under scrub (maquis, garrigue): browse for goats, herbs, honey", "chelsa", True)
    add("grassland", "Habitat", "share", 0, 1, grassland, "Share under grass and steppe: pasture", "chelsa", True)
    add("marsh", "Habitat", "share", 0, 1, wetland, "Share under marsh and reeds", "naturalearth", True)
    add("desert", "Habitat", "share", 0, 1, desert_share, "Share that is bare desert", "chelsa", True)
    return dict(woodland=woodland, conifer=conifer)


def geology(cache, land, sites):
    goods = {}

    def put(good, field):
        goods[good] = np.maximum(goods.get(good, np.zeros((H, W))), field)

    # USGS MRDS: known deposits (many worked in antiquity, many only found
    # later). Counted as potential, at half strength.
    names = {"Iron": "iron", "Copper": "copper", "Tin": "tin", "Lead": "lead", "Silver": "silver", "Gold": "gold",
             "Salt": "salt", "Sulfur": "sulfur", "Mercury": "cinnabar", "Stone, Dimension": "building_stone",
             "Marble": "marble", "Clay": "clay", "Sand and Gravel, Construction": "gravel", "Gypsum": "gypsum",
             "Alum": "alum"}
    with zipfile.ZipFile(os.path.join(cache, "mrds-csv.zip")) as z:
        name = next(n for n in z.namelist() if n.endswith(".csv"))
        with z.open(name) as f:
            reader = csv.DictReader(io.TextIOWrapper(f, encoding="latin-1"))
            for row in reader:
                try:
                    lon, lat = float(row["longitude"]), float(row["latitude"])
                except (ValueError, KeyError):
                    continue
                if not (LON_MIN <= lon < LON_MAX and LAT_MIN <= lat < LAT_MAX):
                    continue
                x, y = int((lon - LON_MIN) / STEP), int((LAT_MAX - lat) / STEP)
                for key in ("commod1", "commod2", "commod3"):
                    for part in (row.get(key) or "").split(","):
                        good = names.get(part.strip())
                        if good:
                            g = goods.setdefault(good, np.zeros((H, W)))
                            g[y, x] = max(g[y, x], 0.5)
    for good in list(goods):
        goods[good] = ndimage.gaussian_filter(goods[good], 1.2) * 4.5   # spread over ~20 km
    for site in sites["sites"]:
        radius = site.get("radius_km", 15)
        for good in site["goods"]:
            put(good, site["strength"] * spot(site["lon"], site["lat"], radius))
    for good in sorted(goods):
        add(f"res_{good}", "Resources", "index", 0, 1, np.clip(goods[good], 0, 1) * land,
            f"Where {good.replace('_', ' ')} can be had (known deposits and historical sites)", "mrds+sites", True)


def fix_mask(fix):
    """Where a hand fix applies: a disc, or a band along a line of points."""
    if "along" in fix:
        mask = np.zeros((H, W))
        pts = fix["along"]
        for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
            for t in np.linspace(0, 1, 40):
                mask = np.maximum(mask, spot(x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, fix["radius_km"]))
        return mask
    return spot(fix["lon"], fix["lat"], fix["radius_km"])


FIXES = []
EARLY_FIXES = {"wetland", "floodplain", "irrigation", "farmed"}   # applied by fixed() as the fields are computed


def fixed(field, values, land):
    """Applies the hand fixes for one field (raising values toward the fix, never lowering)."""
    for fix in FIXES:
        if fix["field"] == field:
            values = np.where(land, np.maximum(values, fix["value"] * fix_mask(fix)), values)
    return values


def apply_fixes(sites, land):
    by_name = {f[0]: i for i, f in enumerate(FIELDS)}
    for fix in sites["fixes"]:
        if fix["field"] in EARLY_FIXES:
            continue   # already applied where the field is computed, so the habitat shares stay balanced
        field = fix["field"]
        i = by_name[field]
        name, part, unit, lo, hi, values, desc, source, derived = FIELDS[i]
        mask = fix_mask(fix)
        values = np.where(land, np.maximum(values, fix["value"] * mask), 0)
        FIELDS[i] = (name, part, unit, lo, hi, values, desc, source + "+fixes", derived)


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--cache", default=os.path.expanduser("~/.cache/facsimilia-land"))
    ap.add_argument("--preview", help="write one PNG per field into this directory")
    args = ap.parse_args(argv)

    land = load_land()
    with open(SITES) as f:
        sites = json.load(f)
    FIXES.extend(sites["fixes"])
    print("terrain..."); t = terrain(args.cache, land)
    print("climate..."); c = climate(args.cache, land)
    print("soil..."); s = soils(args.cache, land, t, c)
    print("water..."); w = water(args.cache, land, t, c)
    print("habitat..."); habitat(args.cache, land, t, c, s, w)
    print("geology..."); geology(args.cache, land, sites)
    apply_fixes(sites, land)

    os.makedirs(OUT_DIR, exist_ok=True)
    for old in os.listdir(OUT_DIR):
        if old.endswith(".u8.gz"):
            os.remove(os.path.join(OUT_DIR, old))
    fields = []
    for name, part, unit, lo, hi, values, desc, source, derived in FIELDS:
        v = np.where(land & np.isfinite(values), values, lo)
        q = np.round(np.clip((v - lo) / (hi - lo), 0, 1) * 255).astype(np.uint8)
        q[~land] = 0
        with gzip.open(os.path.join(OUT_DIR, f"{name}.u8.gz"), "wb", compresslevel=9) as f:
            f.write(q.tobytes())
        srcs = [SOURCES[k][0] + f" ({SOURCES[k][1]})" for k in source.replace("+fixes", "+sites").split("+")]
        fields.append({"name": name, "part": part, "unit": unit, "min": lo, "max": hi, "description": desc,
                       "derived": derived, "sources": srcs})
        lv = v[land]
        print(f"  {part:9s} {name:20s} {unit:14s} land range {np.nanmin(lv):9.2f} .. {np.nanmax(lv):9.2f}, "
              f"median {np.nanmedian(lv):8.2f}")
    placed = []
    for site in sites["sites"]:
        x = int((site["lon"] - LON_MIN) / STEP)
        y = int((LAT_MAX - site["lat"]) / STEP)
        placed.append(dict(site, node=y * W + x))
    with open(os.path.join(OUT_DIR, "land.json"), "w") as f:
        json.dump({
            "about": "The land layer: see tools/build_land_layer.py and MECHANICS.md, 'Resources and the land'.",
            "width": W, "height": H, "lon_min": LON_MIN, "lon_max": LON_MAX, "lat_min": LAT_MIN, "lat_max": LAT_MAX,
            "encoding": "one byte per node; value = min + byte / 255 * (max - min); sea = 0",
            "sources": {k: {"name": v[0], "license": v[1]} for k, v in SOURCES.items()},
            "fields": fields, "sites": placed, "imports": sites["imports"],
        }, f, indent=1, ensure_ascii=False)
    print(f"wrote {len(fields)} fields to {OUT_DIR}")

    if args.preview:
        from PIL import Image
        os.makedirs(args.preview, exist_ok=True)
        for name, *_ in FIELDS:
            with gzip.open(os.path.join(OUT_DIR, f"{name}.u8.gz")) as f:
                q = np.frombuffer(f.read(), np.uint8).reshape(H, W)
            img = np.stack([q, q, q], -1)
            img[~land] = (20, 30, 50)
            Image.fromarray(img).save(os.path.join(args.preview, f"{name}.png"))


if __name__ == "__main__":
    main()
