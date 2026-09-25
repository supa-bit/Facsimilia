"""Turn MECHANICS.md into a readable PDF: docs/Facsimilia-Design.pdf.

Adds a cover, a clickable contents page, a plain-English summary at the
start of each chapter, and coloured tags for (decided) / (proposed) /
(open). Uses the game's own fonts (assets/fonts) and headless Chromium.

    pip install markdown
    python3 tools/build_docs_pdf.py [path/to/chromium]

Re-run it whenever MECHANICS.md changes; the summaries below are keyed by
chapter title, so a new chapter just appears without one.
"""
import datetime
import pathlib
import re
import subprocess
import sys
import tempfile

import markdown

ROOT = pathlib.Path(__file__).resolve().parent.parent
SOURCE = ROOT / "MECHANICS.md"
OUTPUT = ROOT / "docs" / "Facsimilia-Design.pdf"
CHROMIUM_CANDIDATES = [
    "/opt/pw-browsers/chromium-1194/chrome-linux/chrome",
    "chromium", "chromium-browser", "google-chrome",
]

# Plain-English summaries, keyed by the start of each "## " chapter title.
SUMMARIES = {
    "Design decisions": "Your answers to the big design questions: sandbox with optional goals, realms "
        "that make their own choices but lean on history, full war and diplomacy, historical new realms plus "
        "revolts, culture and religion, player-chosen pacing for long games, tribes that start unorganized, "
        "and personal unions guided by history.",
    "Core interaction principle": "You shape the world with one tool: a round brush. Conquest, provinces and "
        "anything added later all use that same brush, instead of menus or separate editors.",
    "Layers": "The map is stored as stacked layers over the same 45-million-cell grid: who owns each spot, "
        "which province it belongs to, and how many people and resources are there. A province's numbers are "
        "always added up from its cells, so redrawing a border can never break anything. “Provinces as "
        "built” describes what is in the game today.",
    "Unclaimed vs. unorganized": "Unclaimed land has no owner at all. Unorganized land has an owner but no "
        "province yet, so it is weaker: no taxes, only dirt roads, just a local militia. That weakness is the "
        "reason to organize land you take.",
    "Population & settlement engine": "How many people live where, year by year. It starts from real "
        "historical estimates (HYDE). Your realm can grow beyond real history; other realms can never hold "
        "more people in a place than really lived there at that date. Cities grow at realistic speeds, "
        "checked against real cities such as Alexandria and Baghdad, and capitals get a head start. The "
        "biggest places get names on the map. The growth drivers are built: each of the 38 ancient regions "
        "grows at its own historical rate unless play changes it, and people drift toward the most attractive "
        "places, within limits the balance tests enforce.",
    "Resources and the land": "What every place is (terrain, climate, soil, water, 300 BC forests and "
        "farms, mines and special goods), baked from real scientific data that allows the game to be sold, "
        "with hand fixes where it's wrong for 300 BC. Each crop, tree and herd has its own needs, and from "
        "them the game knows how many people every place could feed; that limits how far a region can grow. "
        "You can see all of it on the map.",
    "Dynasties and characters": "Ruling families live, marry, have children and die. The crown passes by "
        "real inheritance rules, and a new house takes over when a line dies out or a throne is seized. "
        "Built and in the game.",
    "New concepts needed": "A shopping list of things later systems will need: named settlements, capitals, "
        "resources, roads, Might, map overlays and the conquest panel.",
    "The five simulation systems": "The five big systems a realm runs on: population, resources, economy, "
        "technology and military.",
    "Might": "One strength score per realm or piece of land, split into Land and Naval (Air and Space later), "
        "compared whenever someone tries to take land. Unowned land defends with its local population.",
    "Annexation UX": "You can't see a target's strength until you start painting over it. Then a floating "
        "panel shows the strength and resources of exactly what you've painted, province by province, "
        "before you confirm.",
    "Annexation resolution": "Conquest stays a brush stroke. On confirm, each province you painted is "
        "compared against your Might: win and you take the whole province, lose and the paint snaps back "
        "with losses to your army.",
    "Suggested build order": "The order the systems are built in, each checked in the real game before the "
        "next begins. Step 1 (provinces) is done; step 2 (population) is partly done.",
}

TAGS = [("decided", "decided"), ("proposed", "proposed"), ("open", "open"), ("retune", "open")]


def slug(text: str) -> str:
    return re.sub(r"[^a-z0-9]+", "-", re.sub(r"<[^>]+>", "", text).lower()).strip("-")


def build_html() -> str:
    source = SOURCE.read_text(encoding="utf-8")
    body = markdown.markdown(source, extensions=["tables", "fenced_code", "sane_lists"])

    # Drop the document's own H1 and intro paragraph: the cover replaces them.
    body = re.sub(r"^<h1>.*?</h1>\s*", "", body, count=1, flags=re.S)
    intro_match = re.match(r"<p>(.*?)</p>\s*", body, flags=re.S)
    intro = intro_match.group(1) if intro_match else ""
    body = body[intro_match.end():] if intro_match else body

    # Coloured tags for decision status: <strong>(decided ...)</strong>.
    def tag(m: re.Match) -> str:
        inner = m.group(1)
        low = inner.lower()
        for word, cls in TAGS:
            if low.startswith("(" + word):
                return f'<span class="tag {cls}">{inner}</span>'
        return m.group(0)
    body = re.sub(r"<strong>(\(.*?\))</strong>", tag, body)

    # Chapter ids, summaries, and the contents list.
    contents = []

    def heading(m: re.Match) -> str:
        level, text = m.group(1), m.group(2)
        plain = re.sub(r"<[^>]+>", "", text).strip()
        hid = slug(plain)
        contents.append((level, plain, hid))
        out = f'<h{level} id="{hid}">{text}</h{level}>'
        if level == "2":
            for key, summary in SUMMARIES.items():
                if plain.startswith(key):
                    out += f'<aside class="summary"><b>In short</b>{summary}</aside>'
                    break
        return out
    body = re.sub(r"<h([23])>(.*?)</h\1>", heading, body)

    toc = "".join(
        f'<li class="l{lvl}"><a href="#{hid}">{re.sub(r" [(].*$", "", title)}</a></li>'
        for lvl, title, hid in contents)
    today = datetime.date.today().strftime("%-d %B %Y")
    fonts = (ROOT / "assets" / "fonts").as_uri()
    return f"""<!doctype html><html><head><meta charset="utf-8"><title>Facsimilia: Game Design</title>
<style>
@font-face {{ font-family: Cinzel; src: url("{fonts}/Cinzel.ttf"); font-weight: 400 900; }}
@font-face {{ font-family: Alegreya; src: url("{fonts}/Alegreya.ttf"); font-weight: 400 900; }}
@font-face {{ font-family: Alegreya; font-style: italic; src: url("{fonts}/Alegreya-Italic.ttf"); font-weight: 400 900; }}
@page {{
  size: A4; margin: 22mm 20mm 20mm 22mm;
  @bottom-center {{ content: counter(page); font: 10pt Alegreya, Georgia, serif; color: #8a7556; }}
  @top-right {{ content: "Facsimilia \\2014 Game Design"; font: 9pt Cinzel, Georgia, serif; color: #a58c62; letter-spacing: 0.08em; }}
}}
@page :first {{ @bottom-center {{ content: none; }} @top-right {{ content: none; }} }}
html {{ font: 11.2pt/1.55 Alegreya, Georgia, serif; color: #2a2016; }}
body {{ margin: 0; }}
h1, h2, h3, h4 {{ font-family: Cinzel, Georgia, serif; color: #5b3f12; line-height: 1.25; }}
h2 {{ font-size: 19pt; margin: 0 0 10pt; padding-top: 4pt; border-bottom: 1.2pt solid #c4a063; padding-bottom: 5pt; break-before: page; }}
h3 {{ font-size: 13.5pt; margin: 18pt 0 6pt; break-after: avoid; }}
h4 {{ font-size: 11.5pt; margin: 14pt 0 4pt; break-after: avoid; }}
p {{ margin: 0 0 8pt; orphans: 3; widows: 3; }}
ul, ol {{ margin: 0 0 8pt; padding-left: 18pt; }}
li {{ margin-bottom: 3pt; }}
code {{ font-family: "DejaVu Sans Mono", Consolas, monospace; font-size: 9pt; background: #f1e8d6; padding: 0 2pt; border-radius: 2pt; }}
pre {{ background: #f6efe1; border-left: 3pt solid #c4a063; padding: 7pt 10pt; font-size: 9pt; white-space: pre-wrap; break-inside: avoid; }}
pre code {{ background: none; padding: 0; }}
table {{ border-collapse: collapse; width: 100%; margin: 6pt 0 12pt; font-size: 9.6pt; break-inside: auto; }}
th, td {{ border: 0.6pt solid #cdb68c; padding: 4pt 6pt; vertical-align: top; text-align: left; }}
th {{ background: #efe3c9; font-family: Cinzel, Georgia, serif; font-size: 8.6pt; letter-spacing: 0.03em; }}
tr {{ break-inside: avoid; }}
.summary {{ display: block; background: #f7f0e1; border: 0.8pt solid #d9c294; border-radius: 3pt; padding: 8pt 11pt; margin: 0 0 14pt; font-size: 11.6pt; }}
.summary b {{ display: block; font-family: Cinzel, Georgia, serif; font-size: 8.5pt; letter-spacing: 0.12em; text-transform: uppercase; color: #8c6519; margin-bottom: 2pt; }}
.tag {{ font-family: Cinzel, Georgia, serif; font-size: 7.6pt; font-weight: 700; letter-spacing: 0.05em; text-transform: uppercase; padding: 0.5pt 4pt; border-radius: 2pt; white-space: nowrap; }}
.tag.decided {{ color: #2f5f23; background: #e3eed8; }}
.tag.proposed {{ color: #7a4b00; background: #f6e3b8; }}
.tag.open {{ color: #9a3b12; background: #f6d9c9; }}
.cover {{ height: 245mm; display: flex; flex-direction: column; justify-content: center; }}
.cover h1 {{ font-size: 42pt; letter-spacing: 0.08em; margin: 0; color: #6b4a14; }}
.cover .sub {{ font-size: 16pt; font-style: italic; color: #6d5d45; margin: 6pt 0 20pt; }}
.cover .rule {{ height: 1.2pt; background: #c4a063; width: 60%; margin-bottom: 20pt; }}
.cover .intro {{ max-width: 135mm; font-size: 11.5pt; }}
.cover .meta {{ margin-top: 30pt; font-size: 10pt; color: #8a7556; }}
.key {{ margin-top: 18pt; display: grid; gap: 5pt; font-size: 10.5pt; max-width: 140mm; }}
.contents {{ break-before: page; }}
.contents h2 {{ break-before: avoid; }}
.contents ul {{ list-style: none; padding: 0; columns: 1; }}
.contents li.l2 {{ font-family: Cinzel, Georgia, serif; font-size: 11pt; margin: 9pt 0 2pt; }}
.contents li.l3 {{ padding-left: 14pt; font-size: 10.5pt; margin: 1pt 0; }}
.contents a {{ color: inherit; text-decoration: none; }}
a {{ color: #7a5314; }}
</style></head><body>
<section class="cover">
  <h1>FACSIMILIA</h1>
  <p class="sub">Game design: how the world works</p>
  <div class="rule"></div>
  <p class="intro">This is the design plan for the game: every system, what it does, and the reasoning behind it.
  Each chapter opens with a short summary in plain English; the detail after it is for when you want the full picture.</p>
  <div class="key">
    <div><span class="tag decided">(decided)</span> settled and either built or ready to build</div>
    <div><span class="tag proposed">(proposed)</span> drafted, waiting for your approval</div>
    <div><span class="tag open">(open)</span> not decided yet, or a value to tune later</div>
  </div>
  <p class="meta">Made from MECHANICS.md on {today}.</p>
</section>
<nav class="contents"><h2>Contents</h2><ul>{toc}</ul></nav>
{body}
</body></html>"""


def main() -> None:
    chromium = sys.argv[1] if len(sys.argv) > 1 else next(
        (c for c in CHROMIUM_CANDIDATES if pathlib.Path(c).exists() or subprocess.call(
            ["which", c], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL) == 0), None)
    if chromium is None:
        sys.exit("No Chromium found; pass its path as the first argument.")
    OUTPUT.parent.mkdir(exist_ok=True)
    with tempfile.NamedTemporaryFile("w", suffix=".html", delete=False, encoding="utf-8") as f:
        f.write(build_html())
        html = f.name
    subprocess.run([chromium, "--headless", "--no-sandbox", "--disable-gpu", "--allow-file-access-from-files",
                    "--no-pdf-header-footer", f"--print-to-pdf={OUTPUT}", pathlib.Path(html).as_uri()],
                   check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    print(f"Wrote {OUTPUT.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
