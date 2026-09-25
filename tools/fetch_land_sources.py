"""Download the real-world data the land layer is baked from.

Every source here allows commercial use (the game may be sold). Each is
credited in data/land/land.json and MAP_DATA.md. What each gives:

  CHELSA V2.1 climatologies 1981-2010       CC0 1.0 (no conditions)
      temperature, rainfall, seasonality, growing season, frost, dryness,
      wind, plant growth. Karger et al. (2017), Scientific Data 4, 170122.
      (CHELSA-TraCE21k, the ancient-climate version, is CC BY-SA - "share
      alike" would bind the game's data files - so it isn't used; the
      modern averages are close to 300 BC's climate, and hand fixes cover
      the known differences.)
  SoilGrids 2.0 (ISRIC)                      CC BY 4.0
      soil texture, organic carbon, nitrogen, pH, nutrient holding,
      density, stones. Poggio et al. (2021), SOIL 7, 217-240.
  ETOPO 2022, 60 arc-seconds (NOAA)          public domain (US government)
      elevation and sea depth.
  KK10 land cover change (Kaplan et al.)     CC BY 3.0
      share of each 5' cell under human land use, year by year; 300 BC is
      read from the 17 GB file without downloading it all.
  Natural Earth 10m rivers and lakes         public domain
  USGS Mineral Resources Data System (MRDS)  public domain (US government)
      known mineral deposits and old mines.

Usage: python3 tools/fetch_land_sources.py [--cache DIR]
The cache (default ~/.cache/facsimilia-land, about 2.5 GB) is not part of
the repository; tools/build_land_layer.py bakes from it into data/land/.
Needs requests, numpy, h5py, fsspec, aiohttp.
"""
import argparse
import os
import sys
import time

import numpy as np
import requests

CHELSA = "https://os.zhdk.cloud.switch.ch/chelsav2/GLOBAL/climatologies/1981-2010/bio/CHELSA_{}_1981-2010_V.2.1.tif"
CHELSA_VARS = ["bio1", "bio5", "bio6", "bio12", "bio15", "gsl", "fcf", "ai",
               "sfcWind_mean", "sfcWind_max", "npp", "cmi_min"]
SOILGRIDS = ("https://maps.isric.org/mapserv?map=/map/{prop}.map&SERVICE=WCS&VERSION=2.0.1&REQUEST=GetCoverage"
             "&COVERAGEID={prop}_{depth}_mean&FORMAT=image/tiff&SUBSET=long(-10,55)&SUBSET=lat(10,48)"
             "&SUBSETTINGCRS=http://www.opengis.net/def/crs/EPSG/0/4326"
             "&OUTPUTCRS=http://www.opengis.net/def/crs/EPSG/0/4326&SCALESIZE=long(780),lat(456)")
SOIL_PROPS = ["clay", "sand", "silt", "soc", "nitrogen", "phh2o", "cec", "bdod", "cfvo"]
SOIL_DEPTHS = ["0-5cm", "5-15cm", "15-30cm", "30-60cm", "60-100cm"]
ETOPO = ("https://www.ngdc.noaa.gov/thredds/fileServer/global/ETOPO2022/60s/60s_surface_elev_netcdf/"
         "ETOPO_2022_v1_60s_N90W180_surface.nc")
NATURAL_EARTH = "https://naciscdn.org/naturalearth/10m/physical/{}.zip"
NE_LAYERS = ["ne_10m_rivers_lake_centerlines", "ne_10m_rivers_europe", "ne_10m_lakes"]
MRDS = "https://mrdata.usgs.gov/mrds/mrds-csv.zip"
KK10 = "https://hs.pangaea.de/model/ALCC/KK10.nc"
KK10_YEARS = [-300]


def download(url, path, retries=4):
    if os.path.exists(path) and os.path.getsize(path) > 0:
        return
    tmp = path + ".part"
    for attempt in range(retries):
        try:
            with requests.get(url, stream=True, timeout=120) as r:
                r.raise_for_status()
                with open(tmp, "wb") as f:
                    for chunk in r.iter_content(1 << 20):
                        f.write(chunk)
            os.replace(tmp, path)
            print(f"  {os.path.basename(path)}: {os.path.getsize(path) / 1e6:.1f} MB")
            return
        except requests.RequestException as e:
            print(f"  retry {attempt + 1} for {url}: {e}")
            time.sleep(2 ** (attempt + 1))
    sys.exit(f"could not download {url}")


def fetch_kk10(cache):
    """Reads single years out of the 17 GB KK10 file with ranged requests."""
    import fsspec
    import h5py
    for year in KK10_YEARS:
        out = os.path.join(cache, f"kk10_{year}.npy")
        if os.path.exists(out):
            continue
        f = fsspec.open(KK10, block_size=1 << 22).open()
        h = h5py.File(f, "r")
        years = h["year"][:]
        lat = h["lat"][:]   # ascending: south to north
        lon = h["lon"][:]
        t = int(np.where(years == year)[0][0])
        x0 = int(np.argmin(np.abs(lon - (-10 + 1 / 24))))
        y_top = int(np.argmin(np.abs(lat - (48 - 1 / 24))))
        rows = h["land_use"][t, y_top - 455:y_top + 1, x0:x0 + 780]
        np.save(out, rows[::-1].copy())   # north to south, like every other grid here
        print(f"  KK10 {year}: {rows.shape}")


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--cache", default=os.path.expanduser("~/.cache/facsimilia-land"))
    args = ap.parse_args(argv)
    os.makedirs(args.cache, exist_ok=True)
    print("SoilGrids (cropped on the server to the map)...")
    for prop in SOIL_PROPS:
        for depth in SOIL_DEPTHS:
            download(SOILGRIDS.format(prop=prop, depth=depth), os.path.join(args.cache, f"soil_{prop}_{depth}.tif"))
    print("KK10 land use...")
    fetch_kk10(args.cache)
    print("Natural Earth and MRDS...")
    for layer in NE_LAYERS:
        download(NATURAL_EARTH.format(layer), os.path.join(args.cache, layer + ".zip"))
    download(MRDS, os.path.join(args.cache, "mrds-csv.zip"))
    print("CHELSA climate (whole-planet files, ~100-500 MB each)...")
    for var in CHELSA_VARS:
        download(CHELSA.format(var), os.path.join(args.cache, f"chelsa_{var}.tif"))
    print("ETOPO 2022 elevation (~450 MB)...")
    download(ETOPO, os.path.join(args.cache, "etopo_2022_60s.nc"))
    print(f"all sources in {args.cache}")


if __name__ == "__main__":
    main()
