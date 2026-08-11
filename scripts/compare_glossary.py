import json
import sqlite3

db = r'artifacts\publish\win-x64-verify\global_glossary.db'
jf = r'artifacts\publish\win-x64-verify\global_glossary.json'

con = sqlite3.connect(db)
dbrows = {r[0]: (r[1], r[2]) for r in con.execute('SELECT source, target, entry_source FROM glossary')}
con.close()

jd = json.load(open(jf, encoding='utf-8'))
jrows = {x['source']: (x['target'], x.get('entrySource')) for x in jd}

print('DB count:', len(dbrows), ' JSON count:', len(jrows))
print('in JSON but NOT in DB (by source):', len(set(jrows) - set(dbrows)))
for s in sorted(set(jrows) - set(dbrows))[:10]:
    print('   MISSING:', repr(s), '->', jrows[s])

print('in DB but NOT in JSON:', len(set(dbrows) - set(jrows)))
print('same source but different target:', sum(1 for s in set(dbrows) & set(jrows) if dbrows[s][0] != jrows[s][0]))
