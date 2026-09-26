# Builds data/places.json from the Pleiades dumps (pleiades-places-latest.csv.gz and pleiades-names-latest.csv.gz,
# downloaded from https://atlantides.org/downloads/pleiades/dumps/ into the current folder). Pleiades is CC BY 3.0.
import csv,gzip,json,collections
csv.field_size_limit(10**9)
MODERN={'tr','es','it','fr','de','en','pt','ca','el','bg','sr','hr','ro','ru','uk','sq','mk','sl','hu','nl','cs','pl'}
places=list(csv.DictReader(gzip.open('pleiades.csv.gz','rt',encoding='utf-8')))
names=collections.defaultdict(list)
for n in csv.DictReader(gzip.open('names.csv.gz','rt',encoding='utf-8')):
    if n['nameLanguage'] in MODERN: continue
    pid=n['pid'].split('/')[-1]
    try: a=int(float(n['minDate'])); b=int(float(n['maxDate']))
    except: continue
    t=(n['nameTransliterated'] or n['nameAttested']).split(',')[0].strip()
    if not t or '?' in t or t.startswith(('Untitled','Unnamed')) or a>=1700: continue
    names[pid].append([a,b,t])
out=[]
for r in places:
    try: lat=float(r['reprLat']); lon=float(r['reprLong']); pa=int(float(r['minDate'])); pb=int(float(r['maxDate']))
    except: continue
    if not(-10<=lon<=55 and 10<=lat<=48): continue
    if not any(t.strip() in ('settlement','urban') for t in r['featureTypes'].split(',')): continue
    if r['locationPrecision'] not in ('precise','related'): continue
    ns=sorted(names.get(r['id'],[]))
    if not ns: continue
    w=len([x for x in (r['connectsWith']+','+r['hasConnectionsWith']).split(',') if x.strip()])
    if 'urban' in r['featureTypes']: w+=10
    out.append([round(lon,3),round(lat,3),w,int(r['id']),max(pa,ns[0][0]),pb,[[a,b,t] for a,b,t in ns]])
print(len(out))
d={"about":"Ancient places from the Pleiades gazetteer (https://pleiades.stoa.org), licensed CC BY 3.0: 'Pleiades: A Gazetteer of Past Places', ed. R. Talbert, T. Elliott, S. Gillies and others. Settlements located inside the map, as [lon, lat, importance (links to other places, +10 for cities Pleiades calls urban), Pleiades id, from year, to year, names], each name [from year, to year, name] as Pleiades attests it. In a year a place shows the newest name attested then, or else the newest one known before (Poseidonia in 300 BC, Paestum under Rome); modern-language names and names first attested after 1700 are left out. Rebuilt by tools/build_places.py.","source":"https://atlantides.org/downloads/pleiades/dumps/","places":out}
json.dump(d,open('/home/user/Facsimilia/data/places.json','w'),ensure_ascii=False,separators=(',',':'))
