import sqlite3
import sys

db = sys.argv[1] if len(sys.argv) > 1 else r'artifacts\publish\win-x64-verify\global_glossary.db'
con = sqlite3.connect(db)
cur = con.cursor()
print('--- tables ---')
for r in cur.execute("SELECT name FROM sqlite_master WHERE type='table'"):
    print(r)
print('--- count by language ---')
for r in cur.execute('SELECT language, COUNT(*) FROM glossary GROUP BY language'):
    print(r)
print('--- total ---')
print(cur.execute('SELECT COUNT(*) FROM glossary').fetchone())
print('--- sample 10 ---')
for r in cur.execute('SELECT source, target, language, entry_source FROM glossary LIMIT 10'):
    print(r)
con.close()
