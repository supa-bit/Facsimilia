"""Bake one land-clipped ownership raster; repair only bounded coastal slivers.

Run after import_bc300.js. Requires Pillow, NumPy, SciPy.
Codes 1..9 follow ancient_bc300.json region order; 0 is unclaimed.
Coastal reconciliation is an explicit cartographic inference, not new history.
"""
from collections import deque
import json
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw
from scipy.ndimage import binary_dilation, distance_transform_edt, map_coordinates

ROOT = Path(__file__).resolve().parent.parent
COAST_BAND = 40
MAX_EXTENSION = 56


def repair_coast(owners, land, coastal):
    eligible = land & coastal & (owners == 0)
    seeds = (owners > 0) & binary_dilation(eligible)
    h, w = owners.shape
    queue = deque((int(y), int(x), 0) for y, x in np.argwhere(seeds))
    before = owners.copy()
    while queue:
        y, x, distance = queue.popleft()
        if distance == MAX_EXTENSION:
            continue
        owner = owners[y, x]
        for ny, nx in ((y-1, x), (y+1, x), (y, x-1), (y, x+1)):
            if 0 <= ny < h and 0 <= nx < w and eligible[ny, nx]:
                eligible[ny, nx] = False
                owners[ny, nx] = owner
                queue.append((ny, nx, distance + 1))
    assert np.all(owners[~land] == 0)
    assert np.array_equal(owners[before > 0], before[before > 0])
    return (before == 0) & (owners != 0)


def main():
    data = json.loads((ROOT / 'data/ancient_bc300.json').read_text())
    w, h = data['grid_width'], data['grid_height']
    land = np.asarray(Image.open(ROOT / 'data/land_mask.png')) > 127
    canvas = Image.new('L', (w, h))
    draw = ImageDraw.Draw(canvas)
    for code, region in enumerate(data['regions'], 1):
        for poly in region['polygons']:
            draw.polygon([tuple(p) for p in poly], fill=code)
    raw = np.asarray(canvas).copy()
    inland = distance_transform_edt(land).astype(np.float32)
    # One continuous displacement of the entire label field preserves shared
    # borders. Independent polygon jitter would open seams or create overlaps.
    owners = np.empty_like(raw)
    x = np.arange(w, dtype=np.float32)[None, :]
    for start in range(0, h, 128):
        end = min(h, start + 128)
        y = np.arange(start, end, dtype=np.float32)[:, None]
        taper = np.clip((inland[start:end] - 30) / 70, 0, 1)
        dx = (8*np.sin(y/61 + x/147) + 3*np.sin(y/19 - x/47))*taper
        dy = (8*np.sin(x/73 - y/163) + 3*np.sin(x/23 + y/53))*taper
        coords = np.array([np.broadcast_to(y, dx.shape)+dy,
                           np.broadcast_to(x, dx.shape)+dx])
        owners[start:end] = map_coordinates(raw, coords, order=0, mode='nearest')
    owners[~land] = 0
    repaired = repair_coast(owners, land, inland <= COAST_BAND)
    Image.fromarray(owners).save(ROOT / 'data/political_mask.png', optimize=True)
    counts = {r['key']: int(np.count_nonzero(owners == i))
              for i, r in enumerate(data['regions'], 1)}
    report = {'coast_band_cells': COAST_BAND, 'max_extension_cells': MAX_EXTENSION,
              'repaired_cells': int(repaired.sum()), 'region_cells': counts,
              'sea_cells_claimed': int(np.count_nonzero(owners[~land]))}
    (ROOT / 'data/map_reconciliation.json').write_text(json.dumps(report, indent=2)+'\n')
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
