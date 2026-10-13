"""Fetch a free photograph for each of the opening's ancient places.

For each place in data/opening.json this looks up its Wikipedia article
(ARTICLES below), takes the photographs the article shows, and keeps the
first whose licence on Wikimedia Commons allows any use, commercial
included, with credit: public domain, CC0, CC BY or CC BY-SA (never
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
PAUSE = 6.0   # Wikimedia throttles shared cloud addresses hard

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
OVERRIDE = {}

FREE = re.compile(r"\{\{\s*(cc-zero|cc0|pd[-\w]*|public domain|cc-by(-sa)?-[\d.]+[\w,-]*|cc-by(-sa)?-all|"
                  r"self\|[^}]*?(cc-by(-sa)?-[\d.]+|cc-zero|pd)[^}]*|fal|attribution|copyrighted free use)", re.I)
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


def article_images(title):
    t = urllib.parse.quote(title.replace(" ", "_"), safe="")
    names = []
    try:
        media = json.loads(get(f"https://en.wikipedia.org/api/rest_v1/page/media-list/{t}"))
        for item in media.get("items", []):
            if item.get("type") == "image" and item.get("title", "").startswith("File:"):
                names.append(item["title"][5:])
    except Exception as e:  # noqa: BLE001
        print(f"  media-list {title}: {e}", file=sys.stderr)
    return [n for n in names if not SKIP_NAME.search(n)]


def licence(file_name):
    """The free licence and author of a Commons file, or None."""
    title = urllib.parse.quote(("File:" + file_name).replace(" ", "_"), safe=":")
    text = get(f"https://commons.wikimedia.org/w/index.php?title={title}&action=raw")
    if NOT_FREE.search(text):
        return None
    m = FREE.search(text)
    if not m:
        return None
    lic = m.group(1).split("|")[-1] if m.group(1).lower().startswith("self") else m.group(1)
    a = re.search(r"\|\s*[Aa]uthor\s*=\s*(.+)", text)
    author = clean(a.group(1)) if a else "unknown"
    if author.lower() in ("author", "unknown", "own work", "self"):
        u = re.search(r"\[\[\s*[Uu]ser:([^|\]]+)", a.group(1) if a else text)
        author = u.group(1).strip() if u else author
    return {"licence": lic.strip().upper().replace("CC-", "CC ").replace("PD-", "Public domain: "),
            "author": author}


def clean(wiki):
    s = re.sub(r"\[\[(?:[^|\]]*\|)?([^\]]+)\]\]", r"\1", wiki)
    s = re.sub(r"\[https?://\S+ ([^\]]+)\]", r"\1", s)
    s = re.sub(r"\{\{[Uu]ser:[^}|]*\|?([^}]*)\}\}", r"\1", s)
    s = re.sub(r"\{\{[^}]*\|([^}|]+)\}\}", r"\1", s)
    s = re.sub(r"<[^>]+>|'''?|\{\{|\}\}", "", s)
    return s.strip()[:120] or "unknown"


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
        candidates = [OVERRIDE[key]] if key in OVERRIDE else article_images(ARTICLES[key])
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
