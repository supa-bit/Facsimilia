"""Crop Natural Earth I to the exact game projection, with no invented relief.

Usage: python tools/build_relief_texture.py /path/to/NE1_HR_LC_SR_W.tif
Download: https://naturalearth.s3.amazonaws.com/10m_raster/NE1_HR_LC_SR_W.zip
The geographic crop is retained in tools/relief_source_crop.png for rebuilds.
Requires Pillow, NumPy, SciPy. Terrain is visual, never an ownership mask.
"""
import sys
from pathlib import Path
import numpy as np
from PIL import Image
from scipy.ndimage import distance_transform_edt

ROOT = Path(__file__).resolve().parent.parent
W, H = 8192, 5476
Image.MAX_IMAGE_PIXELS = None
crop_path = ROOT / 'tools/relief_source_crop.png'
if len(sys.argv) > 1:
    source = Image.open(sys.argv[1])
    sw, sh = source.size
    crop = source.crop((round(170/360*sw), round(42/180*sh),
                        round(235/360*sw), round(80/180*sh))).convert('RGB')
    crop.save(crop_path)
else:
    crop = Image.open(crop_path)
rgb = np.asarray(crop.resize((W, H), Image.Resampling.LANCZOS)).copy()
land = np.asarray(Image.open(ROOT / 'data/land_mask.png')) > 127
# Natural Earth's raster and vector coastlines differ slightly. Extend each
# terrain surface from its own valid pixels, avoiding pale offshore fringes.
water_source = (rgb[:,:,2].astype(np.int16) - rgb[:,:,0] > 9)
for surface, valid in ((land, land & ~water_source), (~land, ~land & water_source)):
    bad = surface & ~valid
    if np.any(bad):
        nearest = distance_transform_edt(~valid, return_distances=False, return_indices=True)
        yy, xx = np.where(bad)
        rgb[yy, xx] = rgb[nearest[0, yy, xx], nearest[1, yy, xx]]
        del nearest
# Water tint retains actual ocean-floor relief. Fine paper grain is deliberately
# <1 digital level: it must never substitute for topography or form cloudy blobs.
for start in range(0, H, 128):
    end = min(H, start + 128)
    tile = rgb[start:end].astype(np.float32)/255
    sea = ~land[start:end]
    depth = np.clip((tile[:,:,0] - .10)/.70, 0, 1)
    deep = np.array([.10, .23, .29], np.float32)
    shallow = np.array([.29, .46, .49], np.float32)
    ocean = deep + depth[:,:,None]*(shallow-deep)
    # Mild warming keeps land readable under political tints.
    earth = tile*.94 + np.array([.035, .023, .004])
    rgb[start:end] = np.uint8(np.clip(np.where(sea[:,:,None], ocean, earth)*255, 0, 255))
Image.fromarray(rgb).save(ROOT / 'data/terrain_texture.png', optimize=True)
print('Wrote geographically registered terrain_texture.png', (W,H), 'source crop', crop.size)
