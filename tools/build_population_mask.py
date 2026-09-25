"""Bake HYDE historical population grids into the game's population keyframes.

HYDE (History Database of the Global Environment, PBL / Utrecht University)
publishes gridded population counts (`popc_<year>.asc`, ESRI ASCII, 5 arc-
minutes, people per cell) from 10,000 BCE to the present. This crops each
snapshot to the map's extent (longitude -10 to 55, latitude 48 to 10 - the
same linear lon/lat projection as tools/import_bc300.js and
tools/build_land_mask.py) and writes one keyframe per HYDE year from the
game's 300 BC start onward. See MECHANICS.md, "Population & settlement
engine": these keyframes are H_hist, the historical ceiling, and the 300 BC
starting population all at once.

At 5 arcminutes the map extent is exactly 780 x 456 population nodes (12
per degree). Each node covers ~10.5 x 12 ownership-grid cells.

Input: a directory holding HYDE's baseline population files, either zips
(e.g. HYDE's `0AD_pop.zip`, or this repo's `popc_0AD.zip`) or the extracted
`popc_<year>.asc` files. The game is baked from HYDE 3.2.1, mirrored as the
`popc_*.zip` assets of this repository's `assets-v1` GitHub release; the
original is `HYDE3_2_1-baseline.zip` on DANS (doi:10.17026/dans-25g-gez3,
baseline/asc/<year>_pop/popc_<year>.asc).

HYDE has no snapshot for most years before 1700: BCE steps are 1000 years,
so there is no 300 BC grid. When the start year falls between snapshots its
keyframe is derived from the two that bracket it (1000 BC and 0 AD for the
game's start), per node, assuming steady exponential growth: the geometric
mean weighted by distance in time. A node empty at either end has no growth
rate, so it falls back to linear. hyde_meta.json marks the keyframe
"derived". Later keyframes are HYDE's own snapshots, untouched.

Output, under data/population/:
  hyde_meta.json        grid size, extent, and the list of keyframe years
  popc_<year>.f32.gz    gzip'd little-endian float32 people per node,
                        row-major from the north-west corner; -1 = sea/no data

Usage: python3 tools/build_population_mask.py <hyde_dir> [--start-year -300]
Needs numpy.
"""
import argparse
import gzip
import json
import os
import re
import sys
import zipfile

import numpy as np

LON_MIN, LON_MAX = -10, 55
LAT_MIN, LAT_MAX = 10, 48
NODES_PER_DEGREE = 12  # HYDE's 5-arcminute resolution
WIDTH = (LON_MAX - LON_MIN) * NODES_PER_DEGREE    # 780
HEIGHT = (LAT_MAX - LAT_MIN) * NODES_PER_DEGREE   # 456
NO_DATA = -1.0

OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "data", "population")
YEAR_RE = re.compile(r"popc_(\d+)(BC|AD|CE|BCE)\.asc$", re.IGNORECASE)


def parse_year(name):
    m = YEAR_RE.search(os.path.basename(name))
    if not m:
        return None
    n = int(m.group(1))
    return -n if m.group(2).upper().startswith("B") else n


def year_tag(year):
    return f"{-year}BC" if year < 0 else f"{year}AD"


def find_sources(hyde_dir):
    """Yield (year, name, opener) for every popc_*.asc, loose or zipped."""
    for root, _, files in os.walk(hyde_dir):
        for f in sorted(files):
            path = os.path.join(root, f)
            if f.lower().endswith(".asc"):
                year = parse_year(f)
                if year is not None:
                    yield year, path, (lambda p=path: open(p, "rb").read())
            elif f.lower().endswith(".zip"):
                with zipfile.ZipFile(path) as z:
                    for member in z.namelist():
                        year = parse_year(member)
                        if year is not None:
                            yield year, f"{path}:{member}", (
                                lambda p=path, m=member: zipfile.ZipFile(p).read(m))


def read_asc(raw):
    """Parse an ESRI ASCII grid. Returns (header dict, 2-D float array)."""
    text = raw.decode("ascii", errors="replace")
    lines = text.split("\n", 6)
    header = {}
    for line in lines[:6]:
        key, value = line.split()
        header[key.lower()] = float(value)
    data = np.array(lines[6].split(), dtype=np.float64)
    ncols, nrows = int(header["ncols"]), int(header["nrows"])
    if data.size != ncols * nrows:
        raise ValueError(f"expected {ncols * nrows} values, got {data.size}")
    return header, data.reshape(nrows, ncols)


def crop_to_map(header, grid):
    """Crop a global (or larger-than-map) HYDE grid to the map's node grid."""
    cs = header["cellsize"]
    if abs(cs * NODES_PER_DEGREE - 1.0) > 1e-3:
        raise ValueError(f"cell size {cs} is not 5 arcminutes")
    xll = header.get("xllcorner", header.get("xllcenter", 0) - cs / 2)
    yll = header.get("yllcorner", header.get("yllcenter", 0) - cs / 2)
    top = yll + grid.shape[0] * cs
    col0 = int(round((LON_MIN - xll) / cs))
    row0 = int(round((top - LAT_MAX) / cs))
    if col0 < 0 or row0 < 0 or col0 + WIDTH > grid.shape[1] or row0 + HEIGHT > grid.shape[0]:
        raise ValueError("HYDE grid does not cover the map extent")
    out = grid[row0:row0 + HEIGHT, col0:col0 + WIDTH].astype(np.float32)
    nodata = header.get("nodata_value", -9999.0)
    out[(out == nodata) | ~np.isfinite(out) | (out < 0)] = NO_DATA
    return out


def derive_between(year, y0, p0, y1, p1):
    """Keyframe for `year` from the snapshots at y0 < year < y1 (see top)."""
    f = (year - y0) / (y1 - y0)
    land = (p0 != NO_DATA) & (p1 != NO_DATA)
    a, b = np.where(land, p0, 0.0), np.where(land, p1, 0.0)
    both = (a > 0) & (b > 0)
    out = (1 - f) * a + f * b
    with np.errstate(divide="ignore"):
        out[both] = np.exp((1 - f) * np.log(a[both]) + f * np.log(b[both]))
    out = out.astype(np.float32)
    out[~land] = NO_DATA
    return out


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("hyde_dir")
    ap.add_argument("--start-year", type=int, default=-300)
    ap.add_argument("--out-dir", default=OUT_DIR)
    ap.add_argument("--source", default="HYDE 3.2.1 baseline popc (PBL Netherlands Environmental Assessment "
                    "Agency / Utrecht University), CC BY 3.0, doi:10.17026/dans-25g-gez3")
    args = ap.parse_args(argv)

    available = {}
    for year, name, opener in find_sources(args.hyde_dir):
        available.setdefault(year, (name, opener))

    def load(year):
        name, opener = available[year]
        return crop_to_map(*read_asc(opener())), name

    frames = {y: None for y in available if y >= args.start_year}
    derived = None
    if args.start_year not in available:
        before = [y for y in available if y < args.start_year]
        after = [y for y in available if y > args.start_year]
        if not before or not after:
            sys.exit(f"no HYDE snapshots around the start year {year_tag(args.start_year)} in {args.hyde_dir}")
        y0, y1 = max(before), min(after)
        (p0, n0), (p1, n1) = load(y0), load(y1)
        frames[args.start_year] = (derive_between(args.start_year, y0, p0, y1, p1),
                                   f"derived from {n0} and {n1}")
        derived = {"year": args.start_year, "from": [y0, y1], "method": "per-node exponential (linear where either end is 0)"}

    os.makedirs(args.out_dir, exist_ok=True)
    land = None
    years = sorted(frames)
    for year in years:
        nodes, name = frames[year] or load(year)
        # Sea/no-data is fixed by the start snapshot, so every keyframe
        # shares one node mask (HYDE's land mask is constant across years).
        if land is None:
            land = nodes != NO_DATA
        nodes[~land] = NO_DATA
        nodes[land & (nodes == NO_DATA)] = 0.0
        path = os.path.join(args.out_dir, f"popc_{year_tag(year)}.f32.gz")
        with gzip.open(path, "wb", compresslevel=9) as f:
            f.write(nodes.astype("<f4").tobytes())
        print(f"{year_tag(year):>7}: {nodes[land].sum():14,.0f} people on the map  <- {name}")

    meta = {
        "source": args.source,
        "width": WIDTH,
        "height": HEIGHT,
        "lon_min": LON_MIN, "lon_max": LON_MAX,
        "lat_min": LAT_MIN, "lat_max": LAT_MAX,
        "no_data": NO_DATA,
        "keyframes": [{"year": y, "file": f"popc_{year_tag(y)}.f32.gz"} for y in years],
    }
    if derived:
        meta["derived"] = derived
    with open(os.path.join(args.out_dir, "hyde_meta.json"), "w") as f:
        json.dump(meta, f, indent=1)
    print(f"wrote {len(years)} keyframes, {WIDTH}x{HEIGHT} nodes, to {os.path.normpath(args.out_dir)}")


if __name__ == "__main__":
    main()
