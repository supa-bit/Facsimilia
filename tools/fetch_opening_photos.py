"""Fetch a free photograph for each of the opening's ancient places.

For each place in data/opening.json this reads its Wikimedia Commons
category (CATEGORIES, else ARTICLES below) and keeps the first photograph
whose licence on its Commons page allows any use, commercial included,
with credit: public domain, CC0, CC BY or CC BY-SA (never
"non-commercial" or "no derivatives"). Maps and drawings are skipped.

Writes assets/opening/sites/<key>.jpg (at most 960 pixels wide) and
data/opening_photos.json, which names each photo's file, author, licence
and page: the credits the game shows beside the picture and in
assets/opening/sites/CREDITS.md.

Wikimedia asks for a slow, named client: one request at a time, with a
pause. Places already fetched are kept; delete a .jpg to fetch it again.
Overrides: OVERRIDE maps a place to a Commons file name to use instead.

Usage: python3 tools/fetch_opening_photos.py
Needs Pillow.
"""
import html
import io
import json
import os
import re
import sys
import time
import urllib.parse
import urllib.error
import urllib.request

from PIL import Image

ROOT = os.path.join(os.path.dirname(__file__), "..")
OUT = os.path.join(ROOT, "assets", "opening", "sites")
INDEX = os.path.join(ROOT, "data", "opening_photos.json")
UA = "FacsimiliaGameAssets/1.0 (https://github.com/supa-bit/Facsimilia; credits kept in the game)"
PAUSE = 4.0   # Wikimedia throttles shared cloud addresses hard

ARTICLES = {
    "abu_hureyra": "Tell Abu Hureyra", "franchthi": "Franchthi Cave", "gobekli_tepe": "Göbekli Tepe",
    "jericho": "Tell es-Sultan", "karahan_tepe": "Karahan Tepe", "cayonu": "Çayönü",
    "nevali_cori": "Nevalı Çori", "asikli": "Aşıklı Höyük", "ganj_dareh": "Ganj Dareh",
    "nabta_playa": "Nabta Playa", "ain_ghazal": "'Ain Ghazal", "catalhoyuk": "Çatalhöyük",
    "khirokitia": "Choirokoitia", "jarmo": "Jarmo", "sesklo": "Sesklo", "hacilar": "Hacılar",
    "yumuktepe": "Yumuktepe", "lepenski_vir": "Lepenski Vir", "halaf": "Tell Halaf",
    "samarra": "Samarra culture", "sialk": "Tepe Sialk", "eridu": "Eridu", "vinca": "Vinča-Belo Brdo",
    "ubaid": "Tell al-'Ubaid", "byblos": "Byblos", "merimde": "Merimde", "cucuteni": "Cucuteni–Trypillia culture",
    "varna": "Varna Necropolis", "ghassul": "Teleilat Ghassul", "hamoukar": "Hamoukar",
    "badari": "Badarian culture", "susa": "Susa", "tell_brak": "Tell Brak", "uruk": "Uruk",
    "naqada": "Naqada", "hierakonpolis": "Nekhen", "arslantepe": "Arslantepe", "ggantija": "Ġgantija",
}
# Commons categories where they differ from the article's name.
CATEGORIES = {
    "ain_ghazal": "Ain Ghazal", "ubaid": "Tell al-Ubaid", "merimde": "Merimde Beni Salama",
    "cucuteni": "Cucuteni-Trypillia culture", "hierakonpolis": "Hierakonpolis", "vinca": "Vinča-Belo Brdo",
}
OVERRIDE = {}

NOT_FREE = re.compile(r"(-nc\b|-nd\b|noncommercial|non-commercial|fair use|non-free)", re.I)
SKIP_NAME = re.compile(r"(map|karte|carte|plan\b|location|\.svg$|\.tif|diagram|chart|logo|flag)", re.I)


def get(url, binary=False):
    """One polite request; when told "too many requests", wait longer and try again."""
    for wait in (30, 90, 240, None):
        time.sleep(PAUSE)
        try:
            req = urllib.request.Request(url, headers={"User-Agent": UA})
            with urllib.request.urlopen(req, timeout=60) as r:
                data = r.read()
            return data if binary else data.decode("utf-8", "replace")
        except urllib.error.HTTPError as e:
            if e.code != 429 or wait is None:
                raise
            print(f"  (asked to slow down; waiting {wait}s)", file=sys.stderr)
            time.sleep(wait)


def category_files(category):
    """The photographs in a Commons category, in the order the category page lists them."""
    url = "https://commons.wikimedia.org/wiki/" + urllib.parse.quote("Category:" + category.replace(" ", "_"))
    try:
        page = get(url)
    except Exception as e:  # noqa: BLE001
        print(f"  category {category}: {e}", file=sys.stderr)
        return []
    names = []
    for m in re.finditer(r'href="/wiki/File:([^"#?]+)"', page):
        name = urllib.parse.unquote(m.group(1)).replace("_", " ")
        if name not in names and re.search(r"\.jpe?g$", name, re.I) and not SKIP_NAME.search(name):
            names.append(name)
    return names


def licence(file_name):
    """The free licence and author of a Commons file, read from its page; None unless free for any use."""
    page = get("https://commons.wikimedia.org/wiki/" + urllib.parse.quote("File:" + file_name.replace(" ", "_")))
    shorts = [html.unescape(re.sub(r"<[^>]+>", "", m)).strip()
              for m in re.findall(r'licensetpl(?:_|&#95;)short"[^>]*>(.*?)</span>', page, re.S)]
    if not shorts or any(NOT_FREE.search(x) or re.search(r"\b(NC|ND)\b", x) for x in shorts):
        return None
    free = [x for x in shorts if re.match(r"(CC BY|CC0|Public domain|PD)", x, re.I)]
    if not free:
        return None
    a = re.search(r'fileinfotpl(?:_|&#95;)aut".*?</td>\s*<td[^>]*>(.*?)</td>', page, re.S)
    author = html.unescape(re.sub(r"<[^>]+>", " ", a.group(1))) if a else "unknown"
    author = re.sub(r"\s+", " ", author).strip()[:120] or "unknown"
    return {"licence": free[0], "author": author}


def download(file_name, key):
    q = urllib.parse.quote(file_name.replace(" ", "_"))
    # Wikipedia's own summary gives a working thumbnail address; build one the same way.
    import hashlib
    md5 = hashlib.md5(file_name.replace(" ", "_").encode()).hexdigest()
    url = f"https://upload.wikimedia.org/wikipedia/commons/thumb/{md5[0]}/{md5[:2]}/{q}/960px-{q}"
    try:
        raw = get(url, binary=True)
    except Exception:  # small originals have no 1280px thumbnail
        raw = get(f"https://upload.wikimedia.org/wikipedia/commons/{md5[0]}/{md5[:2]}/{q}", binary=True)
    img = Image.open(io.BytesIO(raw)).convert("RGB")
    if img.width > 960:
        img = img.resize((960, round(img.height * 960 / img.width)), Image.LANCZOS)
    img.save(os.path.join(OUT, f"{key}.jpg"), quality=86, optimize=True)
    return img.size


def main():
    os.makedirs(OUT, exist_ok=True)
    index = json.load(open(INDEX, encoding="utf-8")) if os.path.exists(INDEX) else {}
    places = json.load(open(os.path.join(ROOT, "data", "opening.json"), encoding="utf-8"))["sites"]
    for place in places:
        key = place["key"]
        if key in index and os.path.exists(os.path.join(OUT, f"{key}.jpg")):
            continue
        candidates = [OVERRIDE[key]] if key in OVERRIDE else category_files(CATEGORIES.get(key, ARTICLES[key]))
        for name in candidates[:8]:
            try:
                lic = licence(name)
                if not lic:
                    print(f"  {key}: {name} not free for any use, skipped")
                    continue
                w, h = download(name, key)
            except Exception as e:  # noqa: BLE001
                print(f"  {key}: {name}: {e}", file=sys.stderr)
                continue
            index[key] = {"file": name, **lic,
                          "page": "https://commons.wikimedia.org/wiki/File:" + urllib.parse.quote(name.replace(" ", "_"))}
            print(f"{key}: {name} ({lic['licence']}, {lic['author']}) {w}x{h}")
            break
        else:
            print(f"{key}: NO FREE PHOTO FOUND")
        with open(INDEX, "w", encoding="utf-8") as f:
            json.dump(dict(sorted(index.items())), f, ensure_ascii=False, indent=1)
    with open(os.path.join(OUT, "CREDITS.md"), "w", encoding="utf-8") as f:
        f.write("# Photographs of the opening's places\n\nFrom Wikimedia Commons, each under the licence named; "
                "used with credit as the licence asks.\n\n")
        names = {p["key"]: p["name"] for p in places}
        for key, p in sorted(index.items()):
            f.write(f"- **{names.get(key, key)}** (`{key}.jpg`): \"{p['file']}\" by {p['author']}, {p['licence']}, {p['page']}\n")


if __name__ == "__main__":
    main()
