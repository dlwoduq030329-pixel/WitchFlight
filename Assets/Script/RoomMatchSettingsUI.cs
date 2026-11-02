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
                (manager.IsRoomHost ? (manager.ConnectedPlayerCount > 2 ? "1대1 변경은 2명 이하일 때 가능" : "방장만 대전 방식을 변경할 수 있어요") : "방장이 선택한 설정입니다");
        }
    }

    public void SelectMode(int value)
    {
        if (manager != null && manager.IsWaitingInCodeRoom && manager.IsRoomHost && (value >= 0 && value <= 5)) manager.TrySetRoomMatchMode(value);
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
        // Options are authored in Main. Rebind the scene reference after returning from battle.
        modeDropdown.interactable = manager != null && manager.IsWaitingInCodeRoom && manager.IsRoomHost;
        boundDropdown.onValueChanged.AddListener(SelectMode);
    }
    private void OnDestroy()
    {
        ReleaseTeamDropdown();
        if (boundDropdown != null) boundDropdown.onValueChanged.RemoveListener(SelectMode);
    }
}
