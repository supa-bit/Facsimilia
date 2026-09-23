"""Calibrate the population engine: base feedback/inertia and capital bonuses.

See MECHANICS.md, "Population & settlement engine". Fits:
  1. (beta, mu) so a barren node given best-in-world drivers enters the
     world top 50 / top 10 in the historical average times for non-capital
     founded cities.
  2. Country-capital resettlement + driver bonus to the capital averages.
  3. Province-capital resettlement + driver bonus to the midpoint between.
Two node distributions:
  toy   (default) 5,000 nodes, a rural background plus 300 cities on an
        ancient rank-size curve - what the constants were first fitted on.
  hyde  (--hyde [DIR]) the real map: every land node of the baked HYDE
        start keyframe (tools/build_population_mask.py, default
        data/population/). History is held at that keyframe, and the test
        node is the median populated land node.
Ranks are within the map, as in the game.

Usage: python3 tools/calibrate_population.py [--hyde [DIR]] [--year -300]
                                             [--check-only]   (needs numpy)
  --check-only  skip the grid searches; just report the timings at the
                engine's current constants.
"""
import argparse
import gzip
import json
import os
import statistics
import numpy as np

# (city, capital from founding?, years to world top-50, years to world top-10)
# Approximate, from Chandler / Modelski historical city-population estimates.
rows = [
    ("Alexandria",          True,   20,   50),
    ("Seleucia-on-Tigris",  True,   25,   55),
    ("Antioch",             True,   50,  100),
    ("Pataliputra",         True, None,  190),
    ("Constantinople",      True,   10,   60),
    ("Baghdad",             True,   10,   30),
    ("Samarra",             True,    8,   15),
    ("Edo",                 True,   20,   50),
    ("St. Petersburg",      True,   50,  160),
    ("Calcutta",           False,  110, None),
    ("New York",           False,  190,  226),
    ("Philadelphia",       False,  150,  210),
    ("Chicago",            False,   37,   57),
    ("Melbourne",          False,   50, None),
    ("Los Angeles",        False,  140,  170),
    ("Shenzhen",           False,   20, None),
]

def even(x):
    return round(x / 2) * 2

def targets(capital):
    t50 = statistics.mean(r[2] for r in rows if r[1] == capital and r[2] is not None)
    t10 = statistics.mean(r[3] for r in rows if r[1] == capital and r[3] is not None)
    return even(t50), even(t10)

CAPITAL = targets(True)
ORDINARY = targets(False)
PROVINCE = (even((CAPITAL[0] + ORDINARY[0]) / 2), even((CAPITAL[1] + ORDINARY[1]) / 2))

N, ALPHA, K = 5000, 0.175, 2
# The engine's current constants (scripts/world/population_engine.gd).
ENGINE_BETA, ENGINE_MU = 0.25, 0.0115
ENGINE_CAPITALS = {"country": dict(transfer=0.05, bonus=0.20),
                   "province": dict(transfer=0.01, bonus=0.15)}

def toy_world():
    """Rural background + 300 cities on an ancient rank-size curve."""
    rng = np.random.default_rng(1)
    pop0 = rng.lognormal(np.log(1 / 300), 0.6, N)
    pop0[rng.choice(N, 300, replace=False)] = np.arange(1, 301) ** -0.6
    return pop0

def hyde_world(hyde_dir, year):
    """Every land node of the baked HYDE keyframe for `year`."""
    with open(os.path.join(hyde_dir, "hyde_meta.json")) as f:
        meta = json.load(f)
    frame = next((k for k in meta["keyframes"] if k["year"] == year), None)
    if frame is None:
        raise SystemExit(f"no keyframe for year {year} in {hyde_dir}")
    with gzip.open(os.path.join(hyde_dir, frame["file"]), "rb") as f:
        nodes = np.frombuffer(f.read(), dtype="<f4").astype(np.float64)
    land = nodes[nodes != meta["no_data"]]
    # Sea is never simulated; nodes HYDE has empty get a trace population,
    # as the engine's TARGET_FLOOR does, so the log-scale step stays finite.
    return np.maximum(land, 0.5)

def pick_barren(pop0, hh, populated_min):
    """The median populated node: an ordinary rural site."""
    populated = np.flatnonzero(pop0 >= populated_min)
    return int(populated[np.argsort(hh[populated])[populated.size // 2]])

pop0 = toy_world()
Hh = np.sqrt(pop0 / pop0.max())
barren = pick_barren(pop0, Hh, 0.0)

def use_world(p, populated_min):
    global pop0, Hh, barren
    pop0 = p
    Hh = np.sqrt(pop0 / pop0.max())
    barren = pick_barren(pop0, Hh, populated_min)

def run(beta, mu, years=400, bonus=0.0, transfer=0.0):
    D = Hh.copy(); D[barren] = 1.0 + bonus
    pop = Hh ** K / np.sum(Hh ** K)   # = pop0 / sum(pop0): the fixed-point start
    pop[barren] += transfer * pop.max()
    r50 = r10 = None
    for t in range(years):
        Q = (pop / pop.max()) ** (1 / K)
        Hs = D + beta * Q; Hs /= Hs.max()
        Hf = (1 - ALPHA) * Hs + ALPHA * Hh
        tgt = Hf ** K / np.sum(Hf ** K)
        pop = pop * (tgt / pop) ** mu          # log-space relaxation
        rank = int((pop > pop[barren]).sum()) + 1
        if r50 is None and rank <= 50: r50 = t + 1
        if r10 is None and rank <= 10: r10 = t + 1; break
    return r50, r10

def best(target, grid, **fixed):
    fits = []
    for params in grid:
        r50, r10 = run(**fixed, **params)
        if r50 and r10:
            fits.append((abs(r50 - target[0]) + abs(r10 - target[1]), params, r50, r10))
    return sorted(fits, key=lambda f: f[0])[:3]

def show(label, target, fits):
    print(f"{label}: target top-50 {target[0]}y, top-10 {target[1]}y")
    for err, p, r50, r10 in fits:
        print("   err=%d %s -> %dy / %dy" % (err, {k: round(float(v), 4) for k, v in p.items()}, r50, r10))

def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--hyde", nargs="?", const=os.path.join(os.path.dirname(__file__), "..", "data", "population"),
                    help="calibrate on the baked HYDE grid (default dir: data/population)")
    ap.add_argument("--year", type=int, default=-300, help="HYDE keyframe to use (default -300)")
    ap.add_argument("--check-only", action="store_true", help="only report timings at the current constants")
    args = ap.parse_args()

    if args.hyde:
        use_world(hyde_world(args.hyde, args.year), 1.0)  # at least one person
        label = f"HYDE grid, year {args.year}"
    else:
        label = "toy world"
    order = np.sort(pop0)[::-1]
    print(f"{label}: {pop0.size:,} nodes, total {pop0.sum():,.4g}; test node {pop0[barren]:,.4g}, "
          f"largest {order[0]:,.4g} ({order[0] / pop0[barren]:,.0f}x), #10 {order[9]:,.4g}, #50 {order[49]:,.4g}")

    print(f"at the engine's constants (beta={ENGINE_BETA}, mu={ENGINE_MU}):")
    r = run(ENGINE_BETA, ENGINE_MU)
    print(f"   ordinary:         {r[0]}y / {r[1]}y   (target {ORDINARY[0]} / {ORDINARY[1]})")
    for kind, target in (("country", CAPITAL), ("province", PROVINCE)):
        r = run(ENGINE_BETA, ENGINE_MU, **ENGINE_CAPITALS[kind])
        print(f"   {kind + ' capital:':17} {r[0]}y / {r[1]}y   (target {target[0]} / {target[1]})")
    if args.check_only:
        return

    base = best(ORDINARY, [dict(beta=b, mu=m) for b in np.arange(0.25, 1.01, 0.25)
                           for m in np.arange(0.009, 0.016, 0.0005)])
    show("ordinary cities (beta, mu)", ORDINARY, base)
    fixed = {k: float(v) for k, v in base[0][1].items()}
    grid = [dict(transfer=t, bonus=b) for t in np.arange(0, 0.101, 0.01) for b in np.arange(0, 0.51, 0.05)]
    show("country capitals", CAPITAL, best(CAPITAL, grid, **fixed))
    show("province capitals", PROVINCE, best(PROVINCE, grid, **fixed))


if __name__ == "__main__":
    main()
