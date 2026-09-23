"""Calibrate the population engine's feedback weight (beta) and inertia (mu).

See MECHANICS.md, "Population & settlement engine". Fits (beta, mu) so a
barren node given best-in-world drivers enters the top 50 and top 10 in the
historical average times for founded cities. Toy node distribution for now;
rerun against the HYDE grid once tools/build_population_mask.py exists.

Usage: python3 tools/calibrate_population.py   (needs numpy)
"""
import statistics
import numpy as np

# (city, founded, years to world top-50, years to world top-10 or None)
# Approximate, from Chandler / Modelski historical city-population estimates.
rows=[
("Alexandria",      -331, 20, 50),
("Seleucia-on-Tigris",-305,25, 55),
("Antioch",         -300, 50,100),
("Pataliputra",     -490,None,190),
("Constantinople (refounded)",330,10,60),
("Baghdad",          762, 10, 30),
("Samarra",          836,  8, 15),
("Edo",             1603, 20, 50),
("St. Petersburg",  1703, 50,160),
("Calcutta",        1690,110,None),
("New York",        1624,190,226),
("Philadelphia",    1682,150,210),
("Chicago",         1833, 37, 57),
("Melbourne",       1835, 50,None),
("Los Angeles",     1781,140,170),
("Shenzhen",        1980, 20,None),
]

T50 = round(statistics.mean(r[2] for r in rows if r[2] is not None) / 2) * 2
T10 = round(statistics.mean(r[3] for r in rows if r[3] is not None) / 2) * 2

rng=np.random.default_rng(1)
N=5000; A=0.175; K=2
pop0=rng.lognormal(np.log(1/300),0.6,N)
cities=rng.choice(N,300,replace=False)
pop0[cities]=np.arange(1,301)**-0.6
Hh=np.sqrt(pop0/pop0.max())
barren=int(np.argsort(Hh)[N//2])
def step(pop,D,beta,mu):
    Q=(pop/pop.max())**(1/K)
    Hs=D+beta*Q; Hs/=Hs.max()
    Hf=(1-A)*Hs+A*Hh
    tgt=Hf**K/np.sum(Hf**K)
    return pop*(tgt/pop)**mu            # log-space relaxation
def rank(pop,i): return int((pop>pop[i]).sum())+1
def invest_run(beta, mu, years):
    D=Hh.copy(); D[barren]=1.0; pop=Hh**K/np.sum(Hh**K); r50=r10=None
    for t in range(years):
        pop=step(pop,D,beta,mu); rk=rank(pop,barren)
        if r50 is None and rk<=50: r50=t+1
        if r10 is None and rk<=10: r10=t+1; break
    return r50, r10

if __name__ == "__main__":
    print(f"historical targets: top-50 {T50}y, top-10 {T10}y")
    fits=[]
    for beta in np.arange(0.5, 3.01, 0.25):
        for mu in np.arange(0.01, 0.06, 0.0005):
            r50, r10 = invest_run(beta, mu, 300)
            if r50 and r10:
                fits.append((abs(r50-T50)+abs(r10-T10), beta, mu, r50, r10))
    for f in sorted(fits)[:5]:
        print("err=%d beta=%.2f mu=%.4f -> top50 %dy top10 %dy" % f)
