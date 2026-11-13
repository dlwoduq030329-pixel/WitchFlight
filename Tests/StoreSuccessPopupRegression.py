"""Validate the authored store popup lifecycle without spending live currency."""
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def blocks(path):
    text = (ROOT / path).read_text(encoding="utf-8-sig")
    return {
        match[1]: match[0]
        for match in re.finditer(r"^--- !u!\d+ &(\d+).*?(?=^--- !u!|\Z)", text, re.M | re.S)
    }


scene = blocks("Assets/Scenes/Main.unity")
prefab = blocks("Assets/UI/Prefab/StoreUI.prefab")
popup = scene["1041427794"]
popup_rect = scene["1041427795"]
assert "m_Name: SuccessPopup" in popup and "m_IsActive: 0" in popup

# CancelPurchase hides PurchaseCheck before onPurchaseSucceeded shows SuccessPopup.
# The success popup must stay under the store, not under that hidden confirmation.
assert "m_Father: {fileID: 4051277237182944939}" in popup_rect
assert "fileID: 1624466656350343378," in scene["4051277237182944939"]
assert "m_Father: {fileID: 1624466656350343378}" in prefab["8223062063922647946"]
instance = scene["218581912103391646"]
assert re.search(
    r"targetCorrespondingSourceObject: \{fileID: 1624466656350343378,[^\n]+\n"
    r"      insertIndex: -1\n      addedObject: \{fileID: 1041427795\}", instance
), "Prefab-instance parenting must agree with the popup's Transform parent"

for controller in ("897650132268094000", "5100545705664200588"):
    success = scene[controller].split("  onPurchaseSucceeded:", 1)[1]
    assert "m_Target: {fileID: 1041427794}" in success
    assert "m_Target: {fileID: 1041427801}" in success
    assert "m_StringArgument: ChulSuccess" in success

# Opening the success animation starts its finite close animation, which only
# deactivates SuccessPopup, leaving the next product's confirmation independent.
assert "m_StringArgument: Circle" in scene["1041427801"]
close = scene["1041427796"]
assert "  id: Circle" in close and "  loops: 1" in close
assert "  hasOnComplete: 1" in close
assert "m_Target: {fileID: 1041427794}" in close and "m_BoolArgument: 0" in close
assert "m_Target: {fileID: 271306132}" not in close
print("PASS: emote/voice success popup survives confirmation close and closes independently.")

feedback_id = "897650132268094101"
feedback = scene[feedback_id]
assert "popup: {fileID: 1041427794}" in feedback
assert "messageText: {fileID: 1176300308}" in feedback
assert "entryAnimation: {fileID: 1041427801}" in feedback
assert f"addedObject: {{fileID: {feedback_id}}}" in instance
for controller in ("897650132268094000", "5100545705664200588"):
    assert f"purchaseFeedback: {{fileID: {feedback_id}}}" in scene[controller]

catalog = (ROOT / "Assets/Resources/ItemCatalog.asset").read_text(encoding="utf-8-sig")
for button, item, pack, folder, name in (
    ("1266833902", "3.3", "VoiceAjumma", "Ajumma", "노련한 목소리"),
    ("90777703", "3.4", "VoiceGumsaGirl", "GumsaGirl", "강인한 목소리"),
):
    assert "m_MethodName: SelectVoice" in scene[button]
    assert f"m_StringArgument: {item}" in scene[button]
    assert f"button: {{fileID: {button}}}\n    itemId: {item}" in scene["5100545705664200588"]
    product = re.search(r"^  - id: " + re.escape(item) + r"\n.*?(?=^  - id:|\Z)", catalog, re.M | re.S)[0]
    pack_path = ROOT / f"Assets/Resources/{pack}.asset"
    guid = re.search(r"guid: (\w+)", Path(str(pack_path) + ".meta").read_text())[1]
    assert name in product and "price: 3400" in product and guid in product
    pack_text = pack_path.read_text(encoding="utf-8-sig")
    clip_guids = set(re.findall(r"fileID: 8300000, guid: (\w+)", pack_text))
    source_guids = {
        re.search(r"^guid: (\w+)", path.read_text(), re.M)[1]
        for path in (ROOT / f"Assets/Sound/Voice/{folder}").rglob("*.mp3.meta")
    }
    assert clip_guids and clip_guids == source_guids, f"Incomplete/wrong-folder clips: {pack}"
print("PASS: insufficient-funds feedback and both new voice cards/packs are wired in Main.")
