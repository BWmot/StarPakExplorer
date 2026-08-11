import sqlite3, sys

db = sys.argv[1]
c = sqlite3.connect(db)
c.row_factory = sqlite3.Row
rows = c.execute("SELECT source, target, entry_source, language FROM glossary WHERE source COLLATE NOCASE = 'apex'").fetchall()
print("Rows matching 'apex' (case-insensitive):")
for r in rows:
    print(repr(r["source"]), repr(r["target"]), "src=", r["entry_source"], "lang=", r["language"])
if not rows:
    print("  (none)")
