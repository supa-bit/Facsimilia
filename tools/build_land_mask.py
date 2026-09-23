"""Rasterize Natural Earth land onto the same grid as import_bc300.js.

Run from the project root: python tools/build_land_mask.py
Input: tools/ne_10m_land.geojson (Natural Earth, public domain).
"""

import json
from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / "tools" / "ne_10m_land.geojson"
OUTPUT = ROOT / "data" / "land_mask.png"
WIDTH, HEIGHT = 8192, 5476
LON_MIN, LON_MAX = -10, 55
LAT_MIN, LAT_MAX = 10, 48


def project(ring):
    return [((lon - LON_MIN) / (LON_MAX - LON_MIN) * WIDTH,
             (LAT_MAX - lat) / (LAT_MAX - LAT_MIN) * HEIGHT)
            for lon, lat in ring]


def main():
    features = json.loads(SOURCE.read_text())["features"]
    mask = Image.new("L", (WIDTH, HEIGHT), 0)
    draw = ImageDraw.Draw(mask)
    for feature in features:
        geometry = feature["geometry"]
        polygons = ([geometry["coordinates"]] if geometry["type"] == "Polygon"
                    else geometry["coordinates"])
        for polygon in polygons:
            draw.polygon(project(polygon[0]), fill=255)
            for hole in polygon[1:]:
                draw.polygon(project(hole), fill=0)
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    mask.save(OUTPUT, optimize=True)
    print(f"Wrote {OUTPUT} ({WIDTH}x{HEIGHT})")


if __name__ == "__main__":
    main()
