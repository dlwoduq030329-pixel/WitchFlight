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
    [Header("WaitingRoomUI / Panel / MapImage")]
    [SerializeField] private Image mapImage;
    [Tooltip("1vs1부터 5vs5까지 순서대로. 같은 인원수의 봇전도 이 이미지를 사용합니다.")]
    [SerializeField] private Sprite[] teamModeSprites = new Sprite[5];
    [SerializeField] private Sprite raidModeSprite;
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
        BattleMatchMode mode = manager.CurrentMatchMode;
        int selectedMode = BattleMatchRules.DropdownIndex(mode);
        RefreshModeImage(mode);
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
                mapImage = info.CodeWaitingRoomPanel.transform.Find("Panel/MapImage")?.GetComponent<Image>();
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
    private void RefreshModeImage(BattleMatchMode mode)
    {
        if (mapImage == null) return;
        Sprite sprite;
        if (mode == BattleMatchMode.GiantMeowRaid) sprite = raidModeSprite;
        else
        {
            int index = BattleMatchRules.PlayerCount(mode) / 2 - 1;
            sprite = teamModeSprites != null && index >= 0 && index < teamModeSprites.Length
                ? teamModeSprites[index] : null;
        }
        // Reuse the scene Image and imported sprites. No portrait cloning or texture
        // composition at runtime; network mode changes are already observed above.
        if (sprite == null || mapImage.sprite == sprite) return;
        mapImage.sprite = sprite;
        mapImage.preserveAspect = true;
    }
    private void OnDestroy()
    {
        ReleaseTeamDropdown();
        if (boundDropdown != null) boundDropdown.onValueChanged.RemoveListener(SelectMode);
    }
}
