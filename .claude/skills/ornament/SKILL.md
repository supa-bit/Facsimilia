---
name: ornament
description: Cut real historical ornament (borders, friezes, bands) out of museum photographs of original objects, with each object's record kept as proof, for Facsimilia's interface. Use when the designer wants interface decoration, borders or patterns for a people or an age, or asks for "the ornament tool".
---

# The ornament tool

The designer asked (14 Oct 2026) for an interface built from real historical
ornament, not shapes drawn by AI, and varied **by people and by age**. This
skill is how to get it. `tools/ornament/ornament.py` does five things:

- searches museum collections whose photographs are free for any use;
- downloads the photograph of an original object, keeping its museum record;
- cuts a band of ornament out of it;
- finds where the pattern repeats, so it can tile along a panel edge;
- keeps the credits.

## The designer's rules (do not bend these)

- **Only the original objects**, made by the people themselves: painted
  pottery, mosaics, glazed tiles, textiles, carved stone or wood, metalwork,
  manuscripts.
- **No 19th-century pattern books.** Owen Jones's *Grammar of Ornament*,
  Racinet, Meyer and the like are European artists' redrawings of other
  peoples' work, not the ornament itself. The designer rejected them.
- **No modern recreations**: no facsimiles (such as the Met's painted copies
  of Egyptian tombs), replicas, casts, reconstructions, "in the style of" or
  revival pieces.
- **If there isn't enough proof of how it looked, don't use it.** The museum
  record is the proof: the object, the people, the place, the date and the
  museum's number. Read the record and look at the photograph. Heavy
  restoration or repainting counts against it.
- **Never redraw.** A strip is a cut of a real photograph. It may be
  straightened (a band on a curved pot) and evened out in light, and nothing
  more. Cracks and wear stay.
- **Keep the object's own way up.** Never turn a strip over to make it
  "look right". If a band hangs (points down), it goes on a top edge
  (`--edge top`). For a bottom edge, find a real band that stands.
- **Match the date to the people and age** it is used for. The tool shows
  the date, and the date decides.

## Commands

Run them from the repository folder:

```
python3 tools/ornament/ornament.py COMMAND ...
```

Files go to `assets/ui/ornament/work/` unless `--out FOLDER` is given (put
`--out` before the command). The tool needs Pillow and numpy.

1. **Search.** `search met "Attic Geometric krater meander"` or
   `search cma "Roman mosaic"`.
   - Each result line shows the museum's id, title, people, date and
     material.
   - Facsimiles, copies, casts, reconstructions and pictures that aren't
     public domain are marked REJECTED, with the reason. The tool can't
     catch everything, so read the record too.
2. **Fetch.** `fetch met 248904 met_248904_attic_krater` downloads the
   photograph (full size) and stores the museum record in `sources.json`.
   It refuses a rejected object.
3. **Look.** Make a small copy of the photograph and Read it to find the
   band. Then work out the box in the full-size photograph's pixels.
4. **Cut.** `cut met_248904_attic_krater.jpg greek_geometric_meander 1000 640 2240 890`
   (x0 y0 x1 y1). Options:
   - `--edge top` for a band that hangs;
   - `--edge bottom` for one that stands;
   - `--mirror` flips left to right, for symmetric pairs only.

   On a round pot, cut from the middle of the band, where it faces the
   camera.
5. **Straighten** (when the band is seen at an angle):
   `straighten PHOTO NAME x1 y1 x2 y2 x3 y3 x4 y4 WIDTH HEIGHT`. Give the
   four corners clockwise from top left; the band comes out as a flat
   rectangle.
6. **Tile.** `tile greek_geometric_meander` finds the repeat and saves
   `NAME_tile.png`.
   - A match of 0.6 or more means a clean repeat.
   - Hand-painted ornament is uneven, so always paste the tile four times
     side by side and look at the joins.
   - If the joins show, use the whole strip instead of a tile.
7. **Sheet.** `sheet` saves `sheet.jpg`, a contact sheet of every strip.
8. **Credits.** `credits` writes `CREDITS.md` with each strip's object,
   people, date, museum, number and page. Strips used in the game also go
   in the repository's `CREDITS.md`.

## Sources that work from this cloud machine

- **The Metropolitan Museum of Art** (`met`). Open Access photographs are
  public domain (CC0).
  - Search uses API v1.1. Object records use `/v1/objects/ID`; v1.1
    object pages say "Not Found".
- **The Cleveland Museum of Art** (`cma`). Open Access, CC0.
- **Art Institute of Chicago.** Search works, but its images refuse
  downloads from here (403). If a Chicago object is needed, ask the
  designer to download it on their computer.
- **Wikimedia Commons**, for photographs of original objects (a mosaic
  floor, a temple frieze) taken by a photographer under CC0, CC BY or
  CC BY-SA.
  - Credit the photographer, and never use NC or ND.
  - Commons throttles this machine: go slowly, and read normal
    `/wiki/File:` pages rather than the API.
  - Not built into the tool yet.
- **Still to try:** Smithsonian Open Access, the Rijksmuseum, Paris Musées,
  the British Museum (its licence is CC BY-NC-SA, so not allowed), and
  national museums of the peoples themselves (Egypt, Iran, India, China,
  Mexico and others). Check each licence before use.

## The look it feeds

The aim is the "Fresco" interface the designer liked:

- warm plaster panels;
- a real band across the top of each panel;
- a short rule under headings;
- rows told apart by a faint tint, not boxes;
- revival fonts with open licences (IM Fell, EB Garamond, Cinzel).

The bands change with the player's people and the age, each from that
people's own objects of that time. Keep the list of which strip goes with
which people and age beside the strips in `assets/ui/ornament/`, and record
it in the Ledger, under "Interface: the look".
