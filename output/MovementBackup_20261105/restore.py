from pathlib import Path
import json,hashlib,zipfile
checkpoint=Path(__file__).resolve().parent
root=checkpoint.parent.parent
expected=json.loads((checkpoint/'after-sha256.json').read_text(encoding='utf-8'))
changed=[name for name,sha in expected.items() if not (root/name).exists() or hashlib.sha256((root/name).read_bytes()).hexdigest()!=sha]
if changed:raise SystemExit('Later edits detected; reverse-merge movement-only.patch: '+', '.join(changed))
with zipfile.ZipFile(checkpoint/'before.zip') as z:
    for name in expected:(root/name).write_bytes(z.read(name))
print('Movement-only rollback complete. Character enlargement remains intact.')
