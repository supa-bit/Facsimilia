"""Calibrate the population engine: base feedback/inertia and capital bonuses.

See MECHANICS.md, "Population & settlement engine". Fits:
  1. (beta, mu) so a barren node given best-in-world drivers enters the
     world top 50 / top 10 in the historical average times for non-capital
     founded cities.
  2. Country-capital resettlement + driver bonus to the capital averages.
  3. Province-capital resettlement + driver bonus to the midpoint between.
Toy node distribution for now; rerun against the HYDE grid once
tools/build_population_mask.py exists.

Usage: python3 tools/calibrate_population.py   (needs numpy)
"""
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

# Toy world: rural background + 300 cities on an ancient rank-size curve.
rng = np.random.default_rng(1)
N, ALPHA, K = 5000, 0.175, 2
pop0 = rng.lognormal(np.log(1 / 300), 0.6, N)
pop0[rng.choice(N, 300, replace=False)] = np.arange(1, 301) ** -0.6
Hh = np.sqrt(pop0 / pop0.max())
barren = int(np.argsort(Hh)[N // 2])

def run(beta, mu, years=400, bonus=0.0, transfer=0.0):
    D = Hh.copy(); D[barren] = 1.0 + bonus
    pop = Hh ** K / np.sum(Hh ** K)
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

if __name__ == "__main__":
    base = best(ORDINARY, [dict(beta=b, mu=m) for b in np.arange(0.25, 1.01, 0.25)
                           for m in np.arange(0.009, 0.016, 0.0005)])
    show("ordinary cities (beta, mu)", ORDINARY, base)
    fixed = {k: float(v) for k, v in base[0][1].items()}
    grid = [dict(transfer=t, bonus=b) for t in np.arange(0, 0.101, 0.01) for b in np.arange(0, 0.51, 0.05)]
    show("country capitals", CAPITAL, best(CAPITAL, grid, **fixed))
    show("province capitals", PROVINCE, best(PROVINCE, grid, **fixed))
