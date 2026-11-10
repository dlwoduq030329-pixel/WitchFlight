"""Offline scene isolation audit. Does not substitute for Unity play-mode tests."""
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
LAB_GUID = "6ec0620091114869af3901a7730ff49f"
assets, forbidden = {}, set()
for folder in (ROOT / "Assets", ROOT / "Packages"):
    for meta in folder.rglob("*.meta"):
        match = re.search(r"^guid: (\w+)", meta.read_text(encoding="utf-8-sig", errors="replace"), re.M)
        if not match:
            continue
        guid, path = match[1], Path(str(meta)[:-5])
        assets[guid] = path
        if path.suffix in (".cs", ".dll") and ("Script" in path.parts or "Photon" in path.parts):
            forbidden.add(guid)
forbidden.discard(LAB_GUID)


def docs(text):
    result = {}
    for m in re.finditer(r"(?ms)^--- !u!(\d+) &(-?\d+)[^\n]*\n.*?(?=^--- !u!|\Z)", text):
        assert m[2] not in result, f"Duplicate ID: {m[2]}"
        result[m[2]] = (int(m[1]), m[0])
    return result


cache = {}
def gameplay_prefab(guid, seen=None):
    if guid in cache:
        return cache[guid]
    path = assets.get(guid)
    if not path or path.suffix != ".prefab" or not path.exists():
        return False
    seen = set() if seen is None else seen.copy()
    if guid in seen:
        return False
    seen.add(guid)
    text = path.read_text(encoding="utf-8-sig")
    scripts = set(re.findall(r"m_Script: \{fileID: -?\d+, guid: (\w+)", text))
    children = re.findall(r"m_SourcePrefab: \{fileID: \d+, guid: (\w+)", text)
    result = bool(scripts & forbidden) or any(gameplay_prefab(child, seen) for child in children)
    cache[guid] = result
    return result


scene = (ROOT / "Assets/Scenes/MagicBalanceLab.unity").read_text(encoding="utf-8-sig")
scene_docs = docs(scene)
remaining = set(re.findall(r"m_Script: \{fileID: -?\d+, guid: (\w+)", scene)) & forbidden
assert not remaining, f"Active-capable gameplay scripts remain: {[assets[g] for g in remaining]}"
assert LAB_GUID in scene
assert "76178c115a6a4dcd9033e7826ba477db" not in scene, "Deleted lab manager reference"
checked = 0
for _, (kind, block) in scene_docs.items():
    if kind != 1001:
        continue
    source = re.search(r"m_SourcePrefab: \{fileID: \d+, guid: (\w+)", block)
    if source is None or not gameplay_prefab(source[1]):
        continue
    prefab = assets[source[1]]
    roots = []
    for _, (child_kind, child) in docs(prefab.read_text(encoding="utf-8-sig")).items():
        if child_kind in (4, 224) and "m_Father: {fileID: 0}" in child:
            go = re.search(r"m_GameObject: \{fileID: (\d+)\}", child)
            if go:
                roots.append(go[1])
    assert roots, f"Cannot verify root of gameplay prefab: {prefab}"
    for root in roots:
        inactive = re.search(r"target: \{fileID: " + root + r", guid: " + source[1] +
                             r", type: 3\}\s+propertyPath: m_IsActive\s+value: 0\b", block)
        assert inactive, f"Gameplay prefab can activate on scene load: {prefab}"
    checked += 1

# Check local hierarchy/component references after removing combat components.
for _, (_, block) in scene_docs.items():
    for component in re.findall(r"- component: \{fileID: (-?\d+)\}", block):
        assert component == "0" or component in scene_docs, f"Missing component: {component}"

for name in ("Session", "Actor", "Shot"):
    code = (ROOT / f"Assets/Script/MagicBalanceLab{name}.cs").read_text(encoding="utf-8-sig")
    code = re.sub(r"//[^\n]*", "", code)
    assert not re.search(r"using Fusion|\bNetworkRunner\b|\bNetworkObject\b|\bNetworkBehaviour\b|\bStartGame\s*\(", code)
    assert not re.search(r"Instantiate\s*\(\s*(characterPrefab|visualSource)", code)
print(f"Offline scene: {checked} gameplay prefab instances disabled; no local combat/network scripts; component references valid")
