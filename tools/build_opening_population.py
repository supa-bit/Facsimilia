"""Bake HYDE's earliest population grids into the opening's frames.

The opening (MECHANICS.md, "The opening") shows people spreading from
10,000 BC to the game's start. HYDE 3.2.1 has a snapshot every 1000 years
before Christ; this crops each one to the map (as
tools/build_population_mask.py does) and stores it as a small greyscale
PNG, data/opening/pop_<year>.png, 780 x 456: the brightness is
log10(1 + people per node) / log10(1 + 20000), so mixing two frames in the
game is the same as steady growth between them. The last frame, 300 BC, is
the game's own starting population (data/population/popc_300BC.f32.gz),
so the opening hands over exactly the world the game begins with.

Input: a directory holding HYDE's popc_<year>BC.zip files for 10,000 to
1000 BC (tools/fetch_hyde.py OUT_DIR --from-dans --all fetches them).

Usage: python3 tools/build_opening_population.py <hyde_dir>
Needs numpy and Pillow.
"""
import gzip
import os
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(__file__))
import build_population_mask as bpm  # noqa: E402

ROOT = os.path.join(os.path.dirname(__file__), "..")
OUT = os.path.join(ROOT, "data", "opening")
TOP = np.log10(1 + 20000.0)


def save(year, people):
    v = np.where(people > 0, np.log10(1 + np.maximum(people, 0)) / TOP, 0.0)
    img = Image.fromarray(np.clip(np.round(v * 255), 0, 255).astype(np.uint8), "L")
    path = os.path.join(OUT, f"pop_{bpm.year_tag(year)}.png")
    img.save(path, optimize=True)
    print(f"{path}: {people[people > 0].sum():,.0f} people")


def main(hyde_dir):
    os.makedirs(OUT, exist_ok=True)
    wanted = set(range(-10000, -999, 1000))
    done = set()
    for year, name, read in bpm.find_sources(hyde_dir):
        if year in wanted and year not in done:
            header, grid = bpm.read_asc(read())
            save(year, bpm.crop_to_map(header, grid))
            done.add(year)
    missing = wanted - done
    if missing:
        sys.exit(f"missing HYDE years: {sorted(missing)}")
    with gzip.open(os.path.join(ROOT, "data", "population", "popc_300BC.f32.gz")) as f:
        start = np.frombuffer(f.read(), dtype="<f4").reshape(bpm.HEIGHT, bpm.WIDTH)
    save(-300, start)


if __name__ == "__main__":
    main(sys.argv[1])
