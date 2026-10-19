using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class RoomMatchSettingsUI
{
    [Header("2v2 code room: select my team (empty = automatic)")]
    [SerializeField] private TMP_Dropdown teamDropdown;
    [Tooltip("선택: 본인 초상화 RectTransform. 비우면 현재 대기방의 내 카드에서 자동으로 찾습니다.")]
    [SerializeField] private RectTransform teamDropdownAnchor;
    [SerializeField] private Vector2 teamDropdownOffset = new Vector2(0, -8);
    private TMP_Dropdown boundTeamDropdown;
    private bool generatedTeamDropdown;
    private bool allowTeamA, allowTeamB;
    private string teamOptionsKey;
    private linkuserinfo waitingRoomInfo;

    private void LateUpdate()
    {
        PlayerData local = PlayerData.Local;
        bool show = manager != null && manager.IsWaitingInCodeRoom &&
            manager.CurrentMatchMode == BattleMatchMode.TwoVsTwo && local != null &&
            local.Object != null && local.Object.IsValid && local.Object.HasInputAuthority &&
            local.Runner == manager.Runner && local.IsLoadoutInitialized;
        RectTransform anchor = teamDropdownAnchor;
        if (show && anchor == null)
        {
            if (waitingRoomInfo == null || !waitingRoomInfo.isActiveAndEnabled)
            {
                waitingRoomInfo = null;
                foreach (linkuserinfo info in FindObjectsByType<linkuserinfo>(FindObjectsSortMode.None))
                    if (info.CodeWaitingRoomPanel != null && info.CodeWaitingRoomPanel.activeInHierarchy)
                    { waitingRoomInfo = info; break; }
            }
            Image profile = waitingRoomInfo != null ? waitingRoomInfo.GetCodeWaitingRoomProfile(local.Object.InputAuthority) : null;
            if (profile != null) anchor = profile.rectTransform;
        }
        show &= anchor != null && anchor.gameObject.activeInHierarchy;
        if (show) EnsureTeamDropdown(anchor);
        if (teamDropdown == null) return;
        if (!show)
        {
            teamDropdown.Hide();
            teamDropdown.gameObject.SetActive(false);
            teamOptionsKey = null;
            return;
        }
        teamDropdown.gameObject.SetActive(true);
        int teamA = 0, teamB = 0;
        foreach (Fusion.PlayerRef player in manager.Runner.ActivePlayers)
        {
            PlayerData data = PlayerData.FindPerkData(manager.Runner, player);
            if (data == null) continue;
            if (data.teamIndex == 1) teamA++; else if (data.teamIndex == 2) teamB++;
        }
        allowTeamA = local.teamIndex == 1 || teamA < 2;
        allowTeamB = local.teamIndex == 2 || teamB < 2;
        string key = $"{teamA}/{teamB}/{local.teamIndex}";
        if (key != teamOptionsKey)
        {
            // Close a stale menu when another player's request claims its last free place.
            teamDropdown.Hide();
            teamOptionsKey = key;
            teamDropdown.options[0].text = $"A팀 ({teamA}/2)" + (!allowTeamA ? " · 정원" : "");
            teamDropdown.options[1].text = $"B팀 ({teamB}/2)" + (!allowTeamB ? " · 정원" : "");
            teamDropdown.SetValueWithoutNotify(local.teamIndex == 1 ? 0 : 1);
            teamDropdown.RefreshShownValue();
        }
        teamDropdown.interactable = true;
        // TMP creates its live toggles only when expanded; disable full-team choices too.
        // The owner-only RPC still revalidates capacity if two clients click simultaneously.
        Transform list = teamDropdown.transform.Find("Dropdown List");
        if (list != null)
            foreach (Toggle option in list.GetComponentsInChildren<Toggle>())
                if (option.name.StartsWith("Item 0:")) option.interactable = allowTeamA;
                else if (option.name.StartsWith("Item 1:")) option.interactable = allowTeamB;
    }

    // TMP_Dropdown values: 0 = A, 1 = B. Existing Inspector events are preserved.
    public void SelectTeam(int value)
    {
        PlayerData local = PlayerData.Local;
        if (manager != null && manager.IsWaitingInCodeRoom && manager.CurrentMatchMode == BattleMatchMode.TwoVsTwo &&
            local != null && local.Runner == manager.Runner && (value == 0 ? allowTeamA : value == 1 && allowTeamB))
            local.RequestWaitingTeam(value + 1);
        // Never predict a team change; show the replicated host decision on the next refresh.
        if (teamDropdown != null && local != null)
            teamDropdown.SetValueWithoutNotify(local.teamIndex == 1 ? 0 : 1);
    }

    private void EnsureTeamDropdown(RectTransform anchor)
    {
        if (teamDropdown == null)
        {
            var root = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources());
            root.name = "MyTeamDropdown";
            teamDropdown = root.GetComponent<TMP_Dropdown>();
            generatedTeamDropdown = true;
            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            { if (uiFont != null) text.font = uiFont; text.fontSize = 20; text.color = new Color(.03f,.13f,.18f); }
            teamDropdown.template.sizeDelta = new Vector2(teamDropdown.template.sizeDelta.x, 80);
            teamDropdown.itemText.rectTransform.parent.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 36);
            Transform arrow = root.transform.Find("Arrow");
            if (arrow != null && arrow.TryGetComponent(out Image image)) image.enabled = false;
        }
        if (generatedTeamDropdown)
        {
            RectTransform rect = teamDropdown.GetComponent<RectTransform>();
            if (rect.parent != anchor) { teamDropdown.Hide(); rect.SetParent(anchor, false); }
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0);
            rect.pivot = new Vector2(.5f, 1);
            rect.anchoredPosition = teamDropdownOffset; rect.sizeDelta = new Vector2(220, 32);
        }
        if (boundTeamDropdown != teamDropdown)
        {
            if (boundTeamDropdown != null) boundTeamDropdown.onValueChanged.RemoveListener(SelectTeam);
            boundTeamDropdown = teamDropdown;
            teamDropdown.ClearOptions();
            teamDropdown.AddOptions(new System.Collections.Generic.List<string> { "A팀", "B팀" });
            teamDropdown.onValueChanged.AddListener(SelectTeam);
            teamOptionsKey = null;
        }
    }

    private void OnDisable()
    {
        if (teamDropdown != null) { teamDropdown.Hide(); teamDropdown.gameObject.SetActive(false); }
    }

    private void ReleaseTeamDropdown()
    {
        if (boundTeamDropdown != null) boundTeamDropdown.onValueChanged.RemoveListener(SelectTeam);
        if (generatedTeamDropdown && teamDropdown != null) Destroy(teamDropdown.gameObject);
    }
}
