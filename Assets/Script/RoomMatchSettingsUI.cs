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
    private float nextModeLookup;
    private (int connected, int required, bool host)? displayedStatus;

    private void Awake() => manager = GetComponent<NetworkGameManager>();

    private void Update()
    {
        bool visible = manager != null && manager.IsWaitingInCodeRoom;
        if (visible) EnsureUI();
        if (modeDropdown != null && (!visible || !manager.IsRoomHost))
        {
            if (modeDropdown.IsExpanded) modeDropdown.Hide();
            modeDropdown.interactable = false;
        }
        if (!visible || modeDropdown == null) return;
        int selectedMode = BattleMatchRules.DropdownIndex(manager.CurrentMatchMode);
        if (modeDropdown.value != selectedMode) modeDropdown.SetValueWithoutNotify(selectedMode);
        modeDropdown.interactable = manager.IsRoomHost;
        var status = (manager.ConnectedPlayerCount, manager.RequiredPlayerCount, manager.IsRoomHost);
        if (statusText != null && displayedStatus != status)
        {
            displayedStatus = status;
            statusText.text = $"참가자 {manager.ConnectedPlayerCount} / {manager.RequiredPlayerCount}\n" +
                (manager.IsRoomHost ? "현재 참가자 수 이상의 정원을 선택할 수 있어요" : "방장이 선택한 설정입니다");
        }
    }

    public void SelectMode(int value)
    {
        if (manager != null && manager.IsWaitingInCodeRoom && manager.IsRoomHost && value >= 0 && value < BattleMatchRules.ModeOptionCount) manager.TrySetRoomMatchMode(value);
        if (modeDropdown != null && manager != null)
            modeDropdown.SetValueWithoutNotify(BattleMatchRules.DropdownIndex(manager.CurrentMatchMode));
    }

    private void EnsureUI()
    {
        if (modeDropdown == null)
        {
            if (Time.unscaledTime < nextModeLookup) return;
            nextModeLookup = Time.unscaledTime + .5f;
            foreach (linkuserinfo info in linkuserinfo.LoadedBridges)
            {
                if (info == null || info.CodeWaitingRoomPanel == null || info.CodeRoomModeDropdown == null) continue;
                modeDropdown = info.CodeRoomModeDropdown;
                break;
            }
        }
        if (modeDropdown == null || boundDropdown == modeDropdown) return;
        if (boundDropdown != null) boundDropdown.onValueChanged.RemoveListener(SelectMode);
        boundDropdown = modeDropdown;
        // Reuse the authored dropdown; update options without requiring a scene redesign.
        var options = new System.Collections.Generic.List<string>();
        for (int i = 0; i < BattleMatchRules.ModeOptionCount; i++) options.Add(BattleMatchRules.Label(BattleMatchRules.FromDropdown(i)));
        modeDropdown.ClearOptions();
        modeDropdown.AddOptions(options);
        modeDropdown.SetValueWithoutNotify(BattleMatchRules.DropdownIndex(manager.CurrentMatchMode));
        modeDropdown.interactable = manager != null && manager.IsWaitingInCodeRoom && manager.IsRoomHost;
        boundDropdown.onValueChanged.AddListener(SelectMode);
    }
    private void OnDestroy()
    {
        ReleaseTeamDropdown();
        if (boundDropdown != null) boundDropdown.onValueChanged.RemoveListener(SelectMode);
    }
}
