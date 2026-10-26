"""Validate authored companion assets and tutorial button links without Unity."""
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(path):
    return (ROOT / path).read_text(encoding="utf-8-sig")


def guid(path):
    return re.search(r"^guid: (\w+)$", read(path + ".meta"), re.M)[1]


def button_paths(prefab):
    blocks = {m[1]: m[2] for m in re.findall(
        r"--- !u!(\d+) &(-?\d+)\n(.*?)(?=--- !u!|\Z)", read(prefab), re.S)}
    names = {i: re.search(r"  m_Name: (.*)", b)[1]
             for i, b in blocks.items() if b.startswith("GameObject:")}
    transforms, parents = {}, {}
    for i, block in blocks.items():
        if block.startswith(("RectTransform:", "Transform:")):
            go = re.search(r"m_GameObject: \{fileID: (\d+)", block)[1]
            transforms[i] = go
            parents[go] = re.search(r"m_Father: \{fileID: (\d+)", block)[1]

    def path(go):
        parent = transforms.get(parents.get(go))
        return (path(parent) + "/" if parent else "") + names[go]

    return {path(re.search(r"m_GameObject: \{fileID: (\d+)", b)[1])
            for b in blocks.values() if "m_OnClick:" in b}


table = read("Assets/Resources/CatCompanionTable.asset")
names = re.search(r"  catNames:\n(.*?)(?=  \w+:)", table, re.S)[1]
assert re.findall(r"^  - (.+)$", names, re.M) == [
    name + " 먀녀" for name in ("쿠로", "치즈", "먼지", "자반", "고등어", "턱시도", "시로")]
for broom in ("StandardBroom", "SlowBroom", "FastBroom"):
    prefab = read(f"Assets/Weapon/Prefabs(usethis)/Broom/{broom}.prefab")
    assert prefab.count("  m_Name: CatSeat\n") == 1, broom + " authored flight-pose seat"
for field, asset in {
    "catPrefab": "Assets/Ch/WitchCat.prefab",
    "paperAirplanePrefab": "Assets/Art/PaperAirplane/PaperAirplane.prefab",
    "tutorialPrefab": "Assets/UI/Prefab/Tutorial.prefab",
}.items():
    reference = re.search(rf"  {field}: \{{fileID: (\d+), guid: (\w+)", table)
    assert reference and reference[2] == guid(asset), field
    assert re.search(rf"--- !u!1 &{reference[1]}\b", read(asset)), field + " root"

controller = read("Assets/Ch/cat/Meshs/CatAnimator.controller")
states = re.findall(r"    stateName: (.+)", table)
assert len(states) == 12 and len(set(states)) == 12
for state in states:
    assert f"  m_Name: {state}\n" in controller, state
for clip in ("CatKnead", "CatCheer"):
    asset = f"Assets/Ch/cat/Meshs/{clip}.anim"
    assert guid(asset) in controller, clip + " controller reference"
    text = read(asset)
    assert "foreleg.IK.L" in text and "foreleg.IK.R" in text, clip + " paw bindings"
    assert "m_StopTime: 2.4" in text, clip + " duration"
cheer = read("Assets/Ch/cat/Meshs/CatCheer.anim")
assert "spine.001" in cheer and "spine.002" in cheer and "neck" in cheer, "upright celebration pose"

lobby_table, training_table = table.split("  trainingTutorial:", 1)
steps = re.split(r"^  - id: ", lobby_table, flags=re.M)[1:]
assert len(steps) == 17
for step in steps:
    lines = re.findall(r'^    - (".+")$', step, re.M)
    assert len(lines) == 7 and all(json.loads(line).strip() for line in lines), step.splitlines()[0]

training_steps = re.split(r"^  - id: ", training_table, flags=re.M)[1:]
assert len(training_steps) == 15
assert "waitForTargetClick: 1" not in training_table
assert not any(word in training_table for word in ("해보라", "눌러보라", "써보라", "골라보라", "다음을 누르면"))
for step in training_steps:
    lines = re.findall(r'^    - (".+")$', step, re.M)
    assert len(lines) == 7 and len(set(lines)) == 7, step.splitlines()[0]
    for line in lines:
        assert json.loads(line).strip() and "코브라" not in json.loads(line)
for id, token in [("quick_brake", "{brake}"), ("emote", "{emote}"), ("parry", "{parry}")]:
    step = next(s for s in training_steps if s.startswith(id + "\n"))
    assert step.count(token) == 7, id

director = read("Assets/Script/CatTutorialDirector.cs")
bindings = re.findall(r'Bind\(CatTutorialTarget\.\w+, "([^"]+)", "([^"]+)"\)', director)
assert len(bindings) == 8
for root, child in bindings:
    assert root + "/" + child in button_paths(f"Assets/UI/Prefab/{root}.prefab"), child
tutorial = read("Assets/UI/Prefab/Tutorial.prefab")
for name in ("CatImageRaw", "ExplanationText"):
    assert f"  m_Name: {name}\n" in tutorial, name
print("Cat assets passed: 3 prefab roots, 12 states, 2 paw animations, 224 dialogue lines, 8 live button paths.")
