"""Cut real historical ornament out of photographs of the original objects, for the game's interface.

The designer's tool (14 Oct 2026): the bands, dividers and frames of the
interface come from photographs of real objects made by the people
themselves (painted pottery, mosaics, glazed tiles, textiles, carved stone,
manuscripts), varied by people and age. Every strip keeps its museum record
(what the object is, who made it, where, when, the museum's number and
page) as its proof. See .claude/skills/ornament/SKILL.md.

The designer's rules: no 19th-century pattern-book plates (Owen Jones,
Racinet, Meyer and the like are European artists' redrawings, not the
ornament itself), no modern reconstructions, facsimiles, casts or copies,
and if there isn't enough proof of how it looked, don't use it.

Commands (each prints what it did; files go under --out, default
assets/ui/ornament/work):

  search met|cma QUERY      Search the Met or the Cleveland Museum of Art
                            (both public domain / CC0 photos) and list
                            objects: number, title, people, date, material.
                            Copies, facsimiles, casts and reconstructions are
                            listed as REJECTED with the reason.
  fetch met|cma ID NAME     Download that object's photograph as NAME.jpg and
                            record its museum record in sources.json.
  cut PHOTO NAME x0 y0 x1 y1 [--edge top|bottom] [--mirror]
                            Save that box of the photograph as NAME.png.
  straighten PHOTO NAME  x1 y1 x2 y2 x3 y3 x4 y4 WIDTH HEIGHT
                            Flatten a band seen at an angle (four corners,
                            clockwise from top left) into a straight strip.
  tile NAME                 Find the strip's horizontal repeat and save
                            NAME_tile.png, one seamless repeat, for tiling.
  sheet [NAME...]           A contact sheet of strips (all, if none named).
  credits                   Write CREDITS.md from the recorded sources.

Only public-domain and CC0 photographs are kept. Nothing is redrawn: a
strip is a cut of a real photograph, at most straightened and evened out.

Needs Pillow and numpy.
"""
import argparse
import json
import os
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

import numpy as np
from PIL import Image, ImageDraw

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
UA = "FacsimiliaGameAssets/1.0 (https://github.com/supa-bit/Facsimilia; credits kept in the game)"
PAUSE = 3.0
SOURCES = "sources.json"   # what each file is, where it came from, its licence


# --- polite downloading ------------------------------------------------------------

def get(url, binary=False):
    """One request; when told "too many requests", wait longer and try again."""
    for wait in (20, 60, 180, None):
        time.sleep(PAUSE)
        try:
            req = urllib.request.Request(url, headers={"User-Agent": UA})
            with urllib.request.urlopen(req, timeout=90) as r:
                data = r.read()
            return data if binary else data.decode("utf-8", "replace")
        except urllib.error.HTTPError as e:
            if e.code != 429 or wait is None:
                raise
            print(f"  (asked to slow down; waiting {wait}s)", file=sys.stderr)
            time.sleep(wait)


# Words in a museum record that mean the object is not the original ornament.
NOT_ORIGINAL = [
    (r"facsimile", "a facsimile (modern copy)"),
    (r"\breplica\b|\breproduction\b", "a replica"),
    (r"\b(modern|later|museum|painted|full-size|scale) copy\b|\bcopy (of|after) (an?|the)\b"
     r"|copied (from|after) (an?|the) (original|wall|tomb|painting)|after an? (ancient|original)", "a copy"),
    (r"plaster cast|\bcast of\b|electrotype", "a cast"),
    (r"reconstruct", "a reconstruction"),
    (r"restored|restoration", "heavily restored: check the record"),
    (r"imitation|in the style of|forgery|forged|pastiche|revival", "an imitation or revival"),
    (r"\b(18|19|20)th[- ]century (copy|drawing|watercolou?r)", "a later drawing"),
]


def not_original(*texts):
    t = " ".join(x for x in texts if x)
    for pat, why in NOT_ORIGINAL:
        if re.search(pat, t, re.I):
            return why
    return None


def met_search(q, limit):
    data = json.loads(get("https://collectionapi.metmuseum.org/public/collection/v1.1/search?"
                          + urllib.parse.urlencode({"q": q, "hasImages": "true", "limit": limit})))
    return [met_record(i) for i in (data.get("objectIDs") or [])[:limit]]


def met_record(oid):
    d = json.loads(get(f"https://collectionapi.metmuseum.org/public/collection/v1/objects/{oid}"))
    place = ", ".join(x for x in (d.get("country"), d.get("region"), d.get("city"), d.get("excavation")) if x)
    return {"museum": "The Metropolitan Museum of Art", "id": str(oid), "number": d.get("accessionNumber"),
            "title": d.get("title"), "people": d.get("culture") or d.get("period") or d.get("dynasty"),
            "place": place, "date": d.get("objectDate"), "material": d.get("medium"),
            "page": d.get("objectURL"), "image": d.get("primaryImage"),
            "licence": "Public domain (Met Open Access, CC0)" if d.get("isPublicDomain") else None,
            "rejected": not_original(d.get("title"), d.get("medium"), d.get("creditLine"),
                                     d.get("objectName"), d.get("classification"))}


def cma_search(q, limit):
    data = json.loads(get("https://openaccess-api.clevelandart.org/api/artworks/?"
                          + urllib.parse.urlencode({"q": q, "cc0": 1, "has_image": 1, "limit": limit})))
    return [cma_one(d) for d in data.get("data", [])]


def cma_record(oid):
    return cma_one(json.loads(get(f"https://openaccess-api.clevelandart.org/api/artworks/{oid}"))["data"])


def cma_one(d):
    img = (d.get("images") or {})
    best = (img.get("print") or img.get("web") or {}).get("url")
    return {"museum": "The Cleveland Museum of Art", "id": str(d.get("id")), "number": d.get("accession_number"),
            "title": d.get("title"), "people": "; ".join(d.get("culture") or []),
            "place": "", "date": d.get("creation_date"), "material": d.get("technique"),
            "page": d.get("url"), "image": best,
            "licence": "CC0 (Cleveland Museum of Art Open Access)" if d.get("share_license_status") == "CC0" else None,
            "rejected": not_original(d.get("title"), d.get("technique"), d.get("tombstone"))}


MUSEUMS = {"met": (met_search, met_record), "cma": (cma_search, cma_record)}


def search(museum, q, limit=20):
    for r in MUSEUMS[museum][0](q, limit):
        flag = f"REJECTED ({r['rejected']})" if r["rejected"] else ("" if r["licence"] else "REJECTED (not public domain)")
        print(f"{r['id']:>8}  {r['title']}  |  {r['people'] or '?'}  |  {r['date'] or '?'}  |  {r['material'] or ''}  {flag}")


def fetch(out, museum, oid, name):
    r = MUSEUMS[museum][1](oid)
    if r["rejected"] or not r["licence"]:
        sys.exit(f"{r['title']}: not used ({r['rejected'] or 'not public domain'})")
    if not r["image"]:
        sys.exit(f"{r['title']}: the museum has no photograph to download")
    path = os.path.join(out, f"{name}.jpg")
    with open(path, "wb") as f:
        f.write(get(r["image"], binary=True))
    Image.open(path).verify()
    srcs = load_sources(out)
    srcs[f"{name}.jpg"] = {k: v for k, v in r.items() if k != "rejected"}
    save_sources(out, srcs)
    print(f"{name}.jpg <- {r['title']}, {r['people']}, {r['date']} ({r['museum']} {r['number']})")


# --- the record of sources ------------------------------------------------------------

def load_sources(out):
    p = os.path.join(out, SOURCES)
    return json.load(open(p, encoding="utf-8")) if os.path.exists(p) else {}


def save_sources(out, s):
    with open(os.path.join(out, SOURCES), "w", encoding="utf-8") as f:
        json.dump(dict(sorted(s.items())), f, ensure_ascii=False, indent=1)


# --- finding and cutting strips --------------------------------------------------------

def cut(out, photo, name, box, edge="any", flip=False, spec=None):
    """Saves a box of a photograph as a strip. edge says which edge of a panel
    the strip may sit on: "top" for a band that hangs, "bottom" for one that
    stands, "any" for symmetric bands. Keep the object's own orientation:
    never turn a strip over to make it look right. flip=True only mirrors
    left to right (for symmetric pairs)."""
    path = photo if os.path.exists(photo) else os.path.join(out, photo)
    im = Image.open(path).convert("RGB")
    strip = spec if spec is not None else im.crop(tuple(box))
    if flip:
        strip = strip.transpose(Image.FLIP_LEFT_RIGHT)
    strip.save(os.path.join(out, f"{name}.png"))
    srcs = load_sources(out)
    src = srcs.get(os.path.basename(path), {})
    srcs[f"{name}.png"] = {**src, "cut_from": os.path.basename(path), "box": list(box),
                           "edge": edge, "mirrored": flip}
    save_sources(out, srcs)
    print(f"{name}.png  {strip.size[0]}x{strip.size[1]}  from {os.path.basename(path)}  edge: {edge}")


def straighten(out, photo, name, corners, size, edge="any"):
    """Flattens a band photographed at an angle (a pot's frieze, a wall seen
    from below) into a straight strip: four corners clockwise from top left."""
    path = photo if os.path.exists(photo) else os.path.join(out, photo)
    im = Image.open(path).convert("RGB")
    w, h = size
    src = np.array(corners, float).reshape(4, 2)
    dst = np.array([(0, 0), (w, 0), (w, h), (0, h)], float)
    # solve the projective transform that maps the output rectangle onto the four corners
    A, B = [], []
    for (x, y), (u, v) in zip(dst, src):
        A += [[x, y, 1, 0, 0, 0, -u * x, -u * y], [0, 0, 0, x, y, 1, -v * x, -v * y]]
        B += [u, v]
    coeffs = np.linalg.solve(np.array(A), np.array(B))
    strip = im.transform((w, h), Image.PERSPECTIVE, tuple(coeffs), Image.BICUBIC)
    cut(out, photo, name, [int(c) for c in corners], edge, False, spec=strip)


def tile(out, name):
    """Finds the horizontal repeat of a strip and saves one seamless repeat.

    Tries every shift from a twentieth to half the strip's width and scores
    how closely the strip matches itself moved by that much (whole picture,
    all colours, not just an average per column, which missed repeats whose
    columns all look alike). The best score wins; 1.0 is a perfect match."""
    im = Image.open(os.path.join(out, f"{name}.png")).convert("RGB")
    a = np.asarray(im).astype(float)
    n = a.shape[1]
    spread = a.std() or 1.0
    best, period = -1.0, n
    for p in range(max(8, n // 20), n // 2 + 1):
        diff = np.abs(a[:, p:] - a[:, :-p]).mean()
        score = 1.0 - diff / (2 * spread)
        if score > best + 0.02:   # prefer the shortest repeat among near-equal scores
            best, period = score, p
    # Hand-printed plates drift a little: nudge the cut so the tile's last
    # column runs on into its first one with the smallest visible seam.
    def seam(q):
        return np.abs(a[:, q] - a[:, 0]).mean() if q < n else 1e9
    period = min(range(max(2, period - 4), period + 5), key=seam)
    rep = im.crop((0, 0, period, im.height))
    rep.save(os.path.join(out, f"{name}_tile.png"))
    print(f"{name}_tile.png: repeat {period} px of {n} (match {best:.2f}; under 0.6 means no clean repeat)")


def sheet(out, names):
    files = [f"{n}.png" for n in names] if names else sorted(
        f for f in os.listdir(out) if f.endswith(".png") and not f.endswith("_tile.png"))
    W = 520
    ims = [(f, Image.open(os.path.join(out, f)).convert("RGB")) for f in files]
    ims = [(f, i.resize((W, max(1, int(i.height * W / i.width))))) for f, i in ims]
    cols = [[], []]
    hts = [0, 0]
    for f, i in ims:
        c = 0 if hts[0] <= hts[1] else 1
        cols[c].append((f, i))
        hts[c] += i.height + 26
    s = Image.new("RGB", (2 * W + 20, max(hts) + 10), "white")
    d = ImageDraw.Draw(s)
    for c in (0, 1):
        y = 0
        for f, i in cols[c]:
            d.text((c * (W + 20), y), f, fill="black")
            s.paste(i, (c * (W + 20), y + 16))
            y += i.height + 26
    path = os.path.join(out, "sheet.jpg")
    s.save(path, quality=85)
    print(path)


def credits(out):
    srcs = load_sources(out)
    lines = ["# Ornament sources\n",
             "Every strip is cut from a photograph of the original object (tools/ornament/ornament.py). "
             "The museum record is the proof of what it is.\n"]
    for k, v in srcs.items():
        if k.endswith(".png"):
            where = ", ".join(x for x in (v.get("people"), v.get("place"), v.get("date")) if x)
            lines.append(f"- `{k}`: {v.get('title', '?')} ({where}); {v.get('material') or ''}. "
                         f"{v.get('museum', '?')} {v.get('number') or ''}, {v.get('page') or ''}. {v.get('licence', '?')}."
                         + (f" Sits on the {v['edge']} edge." if v.get("edge") in ("top", "bottom") else ""))
    with open(os.path.join(out, "CREDITS.md"), "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    print(os.path.join(out, "CREDITS.md"))


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--out", default=os.path.join(ROOT, "assets", "ui", "ornament", "work"))
    sub = ap.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("search"); p.add_argument("museum", choices=list(MUSEUMS)); p.add_argument("query")
    p.add_argument("--limit", type=int, default=20)
    p = sub.add_parser("fetch"); p.add_argument("museum", choices=list(MUSEUMS)); p.add_argument("id"); p.add_argument("name")
    for cmd in ("cut", "straighten"):
        p = sub.add_parser(cmd); p.add_argument("photo"); p.add_argument("name")
        p.add_argument("numbers", nargs="+", type=int,
                       help="cut: x0 y0 x1 y1; straighten: four corners then width and height")
        p.add_argument("--edge", choices=["any", "top", "bottom"], default="any",
                       help="top = a band that hangs, bottom = one that stands")
        if cmd == "cut":
            p.add_argument("--mirror", action="store_true", help="mirror left to right")
    p = sub.add_parser("tile"); p.add_argument("name")
    p = sub.add_parser("sheet"); p.add_argument("names", nargs="*")
    sub.add_parser("credits")
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    if a.cmd == "search":
        search(a.museum, a.query, a.limit)
    elif a.cmd == "fetch":
        fetch(a.out, a.museum, a.id, a.name)
    elif a.cmd == "cut":
        if len(a.numbers) != 4:
            sys.exit("cut needs x0 y0 x1 y1")
        cut(a.out, a.photo, a.name, a.numbers, a.edge, a.mirror)
    elif a.cmd == "straighten":
        if len(a.numbers) != 10:
            sys.exit("straighten needs four corners (8 numbers) then width and height")
        straighten(a.out, a.photo, a.name, a.numbers[:8], a.numbers[8:], a.edge)
    elif a.cmd == "tile":
        tile(a.out, a.name)
    elif a.cmd == "sheet":
        sheet(a.out, a.names)
    elif a.cmd == "credits":
        credits(a.out)


if __name__ == "__main__":
    main()
