"""Bake data/political_mask.png from data/realms_bc300.json (our own 300 BC borders).

Each realm's districts grow over the land at once (a shortest-path race),
slowed by mountains, marsh and desert and barely crossing narrow straits,
until they meet a neighbour or run out of reach. Codes 1..N follow the
realm order in realms_bc300.json; 0 is unclaimed land or sea. Runs at a
quarter of the map's resolution, then is scaled up and fitted to the
full-resolution coastline. Requires Pillow, NumPy, SciPy.

    python3 tools/build_realm_mask.py
"""
import gzip, heapq, json, math
from pathlib import Path
import numpy as np
from PIL import Image
from scipy.ndimage import distance_transform_edt, zoom

ROOT = Path(__file__).resolve().parent.parent
LON_MIN, LON_MAX, LAT_MIN, LAT_MAX = -10, 55, 10, 48
SCALE = 4
SEA_COST = 8.0        # a km of sea costs this many km of land: straits yes, open sea no
SEA_HOP_CELLS = 3     # sea farther than this from land can't be crossed


def land_field(name, meta, w, h):
    f = next(f for f in meta['fields'] if f['name'] == name)
    raw = np.frombuffer(gzip.open(ROOT / f'data/land/{name}.u8.gz').read(), dtype=np.uint8)
    v = raw.reshape(meta['height'], meta['width']).astype(np.float32) / 255 * (f['max'] - f['min']) + f['min']
    return zoom(v, (h / meta['height'], w / meta['width']), order=1)


def main():
    data = json.loads((ROOT / 'data/realms_bc300.json').read_text())
    full = np.asarray(Image.open(ROOT / 'data/land_mask.png').convert('L')) > 127
    H, W = full.shape
    h, w = H // SCALE, W // SCALE
    land = full[:h * SCALE, :w * SCALE].reshape(h, SCALE, w, SCALE).mean(axis=(1, 3)) >= 0.5
    meta = json.loads((ROOT / 'data/land/land.json').read_text())
    slope = land_field('slope', meta, w, h)
    desert = land_field('desert', meta, w, h)
    marsh = land_field('marsh', meta, w, h)
    elev = land_field('elevation', meta, w, h)
    # km per unit of cost on each cell: rough ground costs more.
    rough = 1 + np.clip(slope, 0, 30) / 12 + np.clip(elev, 0, 5000) / 3000 + 3.0 * np.clip(desert, 0, 1) ** 2 + 1.0 * np.clip(marsh, 0, 1)
    near_land = distance_transform_edt(~land) <= SEA_HOP_CELLS
    cost = np.where(land, rough, np.where(near_land, SEA_COST, np.inf)).astype(np.float64)
    km_y = (LAT_MAX - LAT_MIN) / h * 111.2
    lat = LAT_MAX - (np.arange(h) + 0.5) / h * (LAT_MAX - LAT_MIN)
    km_x = (LON_MAX - LON_MIN) / w * 111.2 * np.cos(np.radians(lat))

    best = np.full(h * w, np.inf)
    owner = np.zeros(h * w, dtype=np.int32)
    reach = []
    heap = []
    for code, r in enumerate(data['realms'], 1):
        default = data['tribal_reach_km'] if r['tribal'] else data['default_reach_km']
        for d in r['districts']:
            lon, la = d[1], d[2]
            x = int((lon - LON_MIN) / (LON_MAX - LON_MIN) * w); y = int((LAT_MAX - la) / (LAT_MAX - LAT_MIN) * h)
            x = min(max(x, 0), w - 1); y = min(max(y, 0), h - 1)
            if not land[y, x]:   # a port: start on the nearest land
                ys, xs = np.nonzero(land[max(0, y - 8):y + 9, max(0, x - 8):x + 9])
                if len(ys) == 0:
                    print('district at sea, skipped:', r['key'], d[0]); continue
                k = np.argmin((ys - min(8, y)) ** 2 + (xs - min(8, x)) ** 2)
                y, x = max(0, y - 8) + ys[k], max(0, x - 8) + xs[k]
            reach.append(d[3] if len(d) > 3 else default)
            heapq.heappush(heap, (0.0, y * w + x, code, len(reach) - 1))
    flat_cost = cost.ravel()
    while heap:
        c, i, code, di = heapq.heappop(heap)
        if c >= best[i]:
            continue
        best[i] = c; owner[i] = code
        y, x = divmod(i, w)
        for dy, dx in ((-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (-1, 1), (1, -1), (1, 1)):
            ny, nx = y + dy, x + dx
            if 0 <= ny < h and 0 <= nx < w:
                j = ny * w + nx
                step = math.hypot(dx * km_x[y], dy * km_y) * 0.5 * (flat_cost[i] + flat_cost[j])
                nc = c + step
                if nc < best[j] and nc <= reach[di] and step != math.inf:
                    heapq.heappush(heap, (nc, j, code, di))
    owner = owner.reshape(h, w)
    owner[~land] = 0
    # Scale up and fit to the full-resolution coastline: coastal land left out takes its nearest owner.
    big = np.zeros((H, W), dtype=np.int32)
    big[:h * SCALE, :w * SCALE] = np.repeat(np.repeat(owner, SCALE, axis=0), SCALE, axis=1)
    missing = full & (big == 0)
    dist, (iy, ix) = distance_transform_edt(big == 0, return_indices=True)
    fill = missing & (dist <= 3 * SCALE)
    big[fill] = big[iy[fill], ix[fill]]
    big[~full] = 0
    Image.fromarray(big.astype(np.uint8), 'L').save(ROOT / 'data/political_mask.png')
    counts = np.bincount(big.ravel(), minlength=len(data['realms']) + 1)
    report = {r['key']: int(counts[i]) for i, r in enumerate(data['realms'], 1)}
    report['_unclaimed_land'] = int((full & (big == 0)).sum())
    (ROOT / 'data/realm_mask_report.json').write_text(json.dumps(report, indent=1))
    print(json.dumps(report))


if __name__ == '__main__':
    main()
