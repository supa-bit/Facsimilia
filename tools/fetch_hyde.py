"""Download the HYDE 3.2.1 population grids tools/build_population_mask.py bakes.

Writes one `popc_<year>.zip` per HYDE snapshot (a single ESRI ASCII
`popc_<year>.asc` inside, standard Deflate) plus HYDE's per-country and
per-region totals, `popc_c.txt` / `popc_r.txt`, into OUT_DIR.

Two sources:
  release  (default) this repository's `assets-v1` GitHub release, which
           mirrors exactly these files.
  dans     (--from-dans) the original, HYDE3_2_1-baseline.zip on DANS
           (doi:10.17026/dans-25g-gez3). That archive is 5.3 GB; this reads
           only the popc members out of it with HTTP range requests (~700 MB)
           and re-zips each one. Its members are Deflate64, which Python's
           zipfile can't read on its own: pip install zipfile-deflate64.
           Use this to rebuild the release assets.

By default only the snapshots the game needs are fetched: the last one
before the start year (the derived start keyframe is built from it) and
every one after. --all fetches all 75, back to 10,000 BCE.

HYDE 3.2.1: Klein Goldewijk, K., Beusen, A., Doelman, J. and Stehfest, E.
(2017), Anthropogenic land use estimates for the Holocene - HYDE 3.2, Earth
System Science Data 9, 927-953. CC BY 3.0.

Usage: python3 tools/fetch_hyde.py OUT_DIR [--from-dans] [--all] [--start-year -300]
"""
import argparse
import io
import os
import re
import sys
import time
import urllib.error
import urllib.request
import zipfile
from concurrent.futures import ThreadPoolExecutor

RELEASE_URL = "https://github.com/supa-bit/Facsimilia/releases/download/assets-v1/"
DANS_BASELINE = "https://archaeology.datastations.nl/api/access/datafile/5490328"
TABLES = ["popc_c.txt", "popc_r.txt"]
# HYDE 3.2.1's snapshot years: 1000-year steps BCE, then 100 to 1700,
# 10 to 2000, then yearly to 2017.
YEARS = (list(range(-10000, 1, 1000)) + list(range(100, 1701, 100))
         + list(range(1710, 2001, 10)) + list(range(2001, 2018)))
YEAR_RE = re.compile(r"popc_(\d+)(BC|AD)\.asc$")


def year_tag(year):
    return f"{-year}BC" if year < 0 else f"{year}AD"


def wanted_years(start_year, everything):
    if everything:
        return YEARS
    first = max(y for y in YEARS if y <= start_year)
    return [y for y in YEARS if y >= first]


def retry(fn, what):
    for delay in (2, 4, 8, 16, None):
        try:
            return fn()
        except (urllib.error.URLError, OSError) as e:
            if delay is None or (isinstance(e, urllib.error.HTTPError) and 400 <= e.code < 500):
                raise
            print(f"  {what}: {e}; retrying in {delay}s", file=sys.stderr)
            time.sleep(delay)


def write_atomic(path, data):
    with open(path + ".part", "wb") as f:
        f.write(data)
    os.replace(path + ".part", path)


class HttpRangeFile(io.RawIOBase):
    """Seekable read-only view of a remote file, over HTTP range requests.
    Re-resolves the redirect (DANS hands out signed S3 links that expire
    after an hour) whenever a read fails."""

    def __init__(self, url):
        self.url, self.pos = url, 0
        self._resolve()

    def _resolve(self):
        req = urllib.request.Request(self.url, headers={"Range": "bytes=0-0"})
        with urllib.request.urlopen(req, timeout=120) as r:
            self.real = r.geturl()
            self.size = int(r.headers["Content-Range"].rsplit("/", 1)[1])

    def readable(self):
        return True

    def seekable(self):
        return True

    def tell(self):
        return self.pos

    def seek(self, offset, whence=0):
        self.pos = (offset, self.pos + offset, self.size + offset)[whence]
        return self.pos

    def read(self, n=-1):
        if n is None or n < 0:
            n = self.size - self.pos
        n = min(n, self.size - self.pos)
        if n <= 0:
            return b""

        def get():
            try:
                req = urllib.request.Request(self.real, headers={"Range": f"bytes={self.pos}-{self.pos + n - 1}"})
                with urllib.request.urlopen(req, timeout=300) as r:
                    return r.read()
            except (urllib.error.URLError, OSError):
                self._resolve()
                raise

        data = retry(get, "range read")
        self.pos += len(data)
        return data

    def readinto(self, b):
        data = self.read(len(b))
        b[:len(data)] = data
        return len(data)


def from_release(out_dir, names):
    def one(name):
        path = os.path.join(out_dir, name)
        if os.path.exists(path):
            return
        data = retry(lambda: urllib.request.urlopen(RELEASE_URL + name, timeout=300).read(), name)
        write_atomic(path, data)
        print(f"{name}: {len(data):,} bytes", flush=True)

    try:
        with ThreadPoolExecutor(6) as ex:
            list(ex.map(one, names))
    except urllib.error.HTTPError as e:
        sys.exit(f"{e.url}: HTTP {e.code}. If the assets-v1 release isn't there, use --from-dans.")


def from_dans(out_dir, years):
    try:
        import zipfile_deflate64  # noqa: F401  (registers Deflate64 with zipfile)
    except ImportError:
        sys.exit("--from-dans needs Deflate64 support: pip install zipfile-deflate64")
    members = {}
    for info in zipfile.ZipFile(HttpRangeFile(DANS_BASELINE)).infolist():
        base = os.path.basename(info.filename)
        m = YEAR_RE.match(base)
        if m and info.filename.startswith("baseline/asc/"):
            members[base] = info.filename
        elif base in TABLES and info.filename.startswith("baseline/txt/"):
            members[base] = info.filename
    jobs = [(f"popc_{year_tag(y)}.asc", f"popc_{year_tag(y)}.zip") for y in years] + [(t, t) for t in TABLES]
    missing = [src for src, _ in jobs if src not in members]
    if missing:
        sys.exit(f"not in the DANS archive: {', '.join(missing)}")

    def one(job):
        src, name = job
        path = os.path.join(out_dir, name)
        if os.path.exists(path):
            return
        data = zipfile.ZipFile(HttpRangeFile(DANS_BASELINE)).read(members[src])
        if name.endswith(".zip"):
            buf = io.BytesIO()
            with zipfile.ZipFile(buf, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
                z.writestr(src, data)
            data = buf.getvalue()
        write_atomic(path, data)
        print(f"{name}: {len(data):,} bytes", flush=True)

    with ThreadPoolExecutor(6) as ex:
        list(ex.map(one, jobs))


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("out_dir")
    ap.add_argument("--from-dans", action="store_true", help="read from the original DANS archive")
    ap.add_argument("--all", action="store_true", help="every snapshot, back to 10,000 BCE")
    ap.add_argument("--start-year", type=int, default=-300)
    args = ap.parse_args(argv)

    os.makedirs(args.out_dir, exist_ok=True)
    years = wanted_years(args.start_year, args.all)
    if args.from_dans:
        from_dans(args.out_dir, years)
    else:
        from_release(args.out_dir, [f"popc_{year_tag(y)}.zip" for y in years] + TABLES)
    print(f"{len(years)} snapshots ({year_tag(years[0])}..{year_tag(years[-1])}) in {args.out_dir}")


if __name__ == "__main__":
    main()
