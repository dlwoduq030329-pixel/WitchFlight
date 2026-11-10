"""Check short dialogue, tutorial sequencing and live Main-scene button bindings."""
from pathlib import Path
import json
import re

ROOT = Path(__file__).resolve().parents[1]


def read(path):
    return (ROOT / path).read_text(encoding="utf-8-sig")


def steps(section):
    return list(re.finditer(r"(?ms)^  - id: ([^\n]+)\n.*?(?=^  - id: |\Z)", section))


table = read("Assets/Resources/CatCompanionTable.asset")
_, lobby = table.split("  tutorial:\n", 1)
lobby, training = lobby.split("  trainingTutorial:\n", 1)
longest = 0
for section in (lobby, training):
    entries = steps(section)
    assert len({m[1] for m in entries}) == len(entries), "Duplicate tutorial step ID"
    for step in entries:
        lines = [json.loads(s) for s in re.findall(r"^    - (.+)$", step[0], re.M)]
        assert len(lines) == 7 and len(set(lines)) == 7, step[1]
        for line in lines:
            assert 0 < len(line) <= 90, (step[1], len(line))
            assert "Cleaner" not in line and "{accelerate}" not in line, step[1]
            longest = max(longest, len(line))

entries = steps(lobby)
ids = [m[1] for m in entries]
start = ids.index("battle_menu")
menu = ["random_match", "death_match", "create_room", "join_room"]
assert ids[start + 1:start + 5] == menu
assert ids[start + 5] == "return"
expected = {
    "random_match": (7, "RandomMatch", "Button_RandomMatch"),
    "death_match": (9, "DeathMatch", "Button_DeathMatch"),
    "create_room": (6, "CreateRoom", "Button_MakeRoom"),
    "join_room": (10, "JoinRoom", "Button_EntraceRoom"),
}
director = read("Assets/Script/CatTutorialDirector.cs")
enum = re.search(r"enum CatTutorialTarget \{([^}]+)\}", read("Assets/Script/CatCompanionTable.cs"))[1]
targets = [s.strip() for s in enum.split(",")]
scene = read("Assets/Scenes/Main.unity")
blocks = {m[2]: m[3] for m in re.finditer(
    r"(?ms)^--- !u!(\d+) &(-?\d+)[^\n]*\n(.*?)(?=^--- !u!|\Z)", scene)}
names, transforms, parents = {}, {}, {}
for key, block in blocks.items():
    if block.startswith("GameObject:"):
        name = re.search(r"^  m_Name: (.*)$", block, re.M)
        if name:
            names[key] = name[1]
    elif block.startswith(("RectTransform:", "Transform:")):
        go = re.search(r"m_GameObject: \{fileID: (\d+)", block)
        parent = re.search(r"m_Father: \{fileID: (\d+)", block)
        if go and parent:
            transforms[key], parents[go[1]] = go[1], parent[1]


def path(go):
    parent = transforms.get(parents.get(go))
    return (path(parent) + "/" if parent else "") + names.get(go, "<prefab>")


for step_id, (target_id, target_name, button_name) in expected.items():
    block = next(m[0] for m in entries if m[1] == step_id)
    assert f"    highlight: {target_id}\n" in block
    assert "    waitForTargetClick: 0\n" in block, "Do not start matchmaking during explanation"
    assert targets[target_id] == target_name, "Preserve serialized enum IDs"
    relative = "Buttons/" + button_name
    assert f'Bind(CatTutorialTarget.{target_name}, "Multi", "{relative}")' in director
    candidates = [go for go, name in names.items() if name == button_name
                  and path(go).endswith("/Multi/" + relative)]
    assert len(candidates) == 1, (step_id, candidates)
    components = re.findall(r"component: \{fileID: (\d+)", blocks[candidates[0]])
    assert any("m_OnClick:" in blocks.get(c, "") for c in components), "Needs a real UI Button"

practices = {m[1]: int(re.search(r"practice: (\d+)", m[0])[1]) for m in steps(training)}
parry = next(m[0] for m in steps(training) if m[1] == "parry")
assert parry.count("{parry}을 눌러 타이밍에 맞춰 공격을 막아보라옹~") == 7
assert "노랗" not in parry
for name, action in {"speed": 1, "aim": 2, "slots": 3, "parry": 4,
                     "perk_select": 5, "boost": 6, "quick_brake": 7, "emote": 8}.items():
    assert practices[name] == action, "Keep interactive practice linked"
update = director.split("    private void UpdateHighlight()", 1)[1].split("    private string Dialogue", 1)[0]
assert "new Vector3[" not in update and "GetComponentInParent" not in update
assert "highlightedCanvas != null" in update
assert "GetComponentInParent<Canvas>(true)" in director, "Menu can still be inactive during its transition"
print(f"Tutorial checks passed: 36 short steps, 252 lines (max {longest} chars), "
      "4 Main-scene buttons, enum IDs and practice actions.")
