using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Always-active controller for the authored WaitingRoomUI/Dropdown_mode.
[DisallowMultipleComponent]
public sealed partial class RoomMatchSettingsUI : MonoBehaviour
{
    [SerializeField] private TMP_Dropdown modeDropdown;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_FontAsset uiFont;
    private NetworkGameManager manager;
    private TMP_Dropdown boundDropdown;

    private void Update()
    {
        if (manager == null) manager = GetComponent<NetworkGameManager>();
        bool visible = manager != null && manager.IsWaitingInCodeRoom;
        if (visible) EnsureUI();
        if (modeDropdown != null && (!visible || !manager.IsRoomHost))
        {
            if (modeDropdown.IsExpanded) modeDropdown.Hide();
            modeDropdown.interactable = false;
        }
        if (!visible || modeDropdown == null) return;
        modeDropdown.SetValueWithoutNotify(BattleMatchRules.DropdownIndex(manager.CurrentMatchMode));
        modeDropdown.interactable = manager.IsRoomHost;
        if (statusText != null)
            statusText.text = $"참가자 {manager.ConnectedPlayerCount} / {manager.RequiredPlayerCount}\n" +
                (manager.IsRoomHost ? (manager.ConnectedPlayerCount > 2 ? "1대1 변경은 2명 이하일 때 가능" : "방장만 대전 방식을 변경할 수 있어요") : "방장이 선택한 설정입니다");
    }

    public void SelectMode(int value)
    {
        if (manager != null && manager.IsWaitingInCodeRoom && manager.IsRoomHost && (value >= 0 && value <= 3)) manager.TrySetRoomMatchMode(value);
        if (modeDropdown != null && manager != null)
            modeDropdown.SetValueWithoutNotify(BattleMatchRules.DropdownIndex(manager.CurrentMatchMode));
    }

    private void EnsureUI()
    {
        if (modeDropdown == null)
            foreach (linkuserinfo info in FindObjectsByType<linkuserinfo>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (info.CodeWaitingRoomPanel == null) continue;
                foreach (TMP_Dropdown dropdown in info.CodeWaitingRoomPanel.GetComponentsInChildren<TMP_Dropdown>(true))
                    if (dropdown.name == "Dropdown_mode") { modeDropdown = dropdown; break; }
                if (modeDropdown != null) break;
            }
        if (modeDropdown == null || boundDropdown == modeDropdown) return;
        if (boundDropdown != null) boundDropdown.onValueChanged.RemoveListener(SelectMode);
        boundDropdown = modeDropdown;
        modeDropdown.ClearOptions();
        modeDropdown.AddOptions(new List<string> { "1 vs 1", "2 vs 2", "1 vs 1 (BOT)", "2 vs 2 (BOT)" });
        modeDropdown.interactable = manager != null && manager.IsWaitingInCodeRoom && manager.IsRoomHost;
        boundDropdown.onValueChanged.AddListener(SelectMode);
    }
    private void OnDestroy()
    {
        ReleaseTeamDropdown();
        if (boundDropdown != null) boundDropdown.onValueChanged.RemoveListener(SelectMode);
    }
}
