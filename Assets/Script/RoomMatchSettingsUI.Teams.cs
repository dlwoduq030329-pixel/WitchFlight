using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class RoomMatchSettingsUI
{
    [Header("Team code room: select my team (empty = automatic)")]
    [SerializeField] private TMP_Dropdown teamDropdown;
    [Tooltip("선택: 본인 초상화 RectTransform. 비우면 현재 대기방의 내 카드에서 자동으로 찾습니다.")]
    [SerializeField] private RectTransform teamDropdownAnchor;
    [SerializeField] private Vector2 teamDropdownOffset = new Vector2(0, -8);
    private TMP_Dropdown boundTeamDropdown;
    private bool generatedTeamDropdown;
    private bool allowTeamA, allowTeamB;
    private (int teamA, int teamB, int mine, int capacity)? teamOptionsKey;
    private bool suppressedForTeamCards;
    private readonly System.Collections.Generic.List<Toggle> teamOptionToggles = new();
    private linkuserinfo waitingRoomInfo;
    private float nextWaitingRoomLookup;
    private bool teamListWasExpanded;
    private Toggle teamAOption, teamBOption;
    private readonly System.Collections.Generic.Dictionary<Fusion.PlayerRef, TMP_Dropdown> otherTeamDropdowns = new();

    private void LateUpdate()
    {
        foreach (linkuserinfo info in linkuserinfo.LoadedBridges)
            if (info != null && info.UsesTeamCards)
            {
                // The authored cards own selection; do not overlay legacy dropdowns.
                if (!suppressedForTeamCards) OnDisable();
                suppressedForTeamCards = true;
                return;
            }
        suppressedForTeamCards = false;
        PlayerData local = PlayerData.Local;
        bool show = manager != null && manager.IsWaitingInCodeRoom &&
            BattleMatchRules.IsTeamMode(manager.CurrentMatchMode) && local != null &&
            local.Object != null && local.Object.IsValid && local.Object.HasInputAuthority &&
            local.Runner == manager.Runner && local.IsLoadoutInitialized;
        RectTransform anchor = teamDropdownAnchor;
        if (show && anchor == null)
        {
            if ((waitingRoomInfo == null || !waitingRoomInfo.isActiveAndEnabled) && Time.unscaledTime >= nextWaitingRoomLookup)
            {
                nextWaitingRoomLookup = Time.unscaledTime + .5f;
                waitingRoomInfo = null;
                foreach (linkuserinfo info in linkuserinfo.LoadedBridges)
                    if (info != null && info.isActiveAndEnabled && info.CodeWaitingRoomPanel != null && info.CodeWaitingRoomPanel.activeInHierarchy)
                    { waitingRoomInfo = info; break; }
            }
            if (waitingRoomInfo != null && waitingRoomInfo.isActiveAndEnabled)
                anchor = waitingRoomInfo.GetCodeWaitingRoomCard(local.ParticipantRef);
        }
        show &= anchor != null && anchor.gameObject.activeInHierarchy;
        if (show) EnsureTeamDropdown(anchor);
        RefreshOtherTeamDropdowns(show, local);
        if (teamDropdown == null) return;
        if (!show)
        {
            teamListWasExpanded = false;
            teamAOption = teamBOption = null;
            // TMP Hide() selects even an inactive dropdown, stealing input focus every frame.
            // OnDisable already destroys its open list and blocker without selecting it.
            if (teamDropdown.gameObject.activeSelf) teamDropdown.gameObject.SetActive(false);
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
        int capacity = BattleMatchRules.PlayerCount(manager.CurrentMatchMode) / 2;
        allowTeamA = local.teamIndex == 1 || teamA < capacity;
        allowTeamB = local.teamIndex == 2 || teamB < capacity;
        var key = (teamA, teamB, local.teamIndex, capacity);
        if (key != teamOptionsKey)
        {
            // Close a stale menu when another player's request claims its last free place.
            if (teamDropdown.IsExpanded) teamDropdown.Hide();
            teamOptionsKey = key;
            teamDropdown.options[0].text = $"A팀 ({teamA}/{capacity})" + (!allowTeamA ? " · 정원" : "");
            teamDropdown.options[1].text = $"B팀 ({teamB}/{capacity})" + (!allowTeamB ? " · 정원" : "");
            teamDropdown.SetValueWithoutNotify(local.teamIndex == 1 ? 0 : 1);
            teamDropdown.RefreshShownValue();
        }
        teamDropdown.interactable = true;
        // TMP creates its live toggles only when expanded; disable full-team choices too.
        // The owner-only RPC still revalidates capacity if two clients click simultaneously.
        if (teamDropdown.IsExpanded && !teamListWasExpanded)
        {
            teamAOption = teamBOption = null;
            Transform list = teamDropdown.transform.Find("Dropdown List");
            if (list != null)
            {
                list.GetComponentsInChildren(false, teamOptionToggles);
                foreach (Toggle option in teamOptionToggles)
                    if (option.name.StartsWith("Item 0:")) teamAOption = option;
                    else if (option.name.StartsWith("Item 1:")) teamBOption = option;
            }
        }
        teamListWasExpanded = teamDropdown.IsExpanded;
        if (teamAOption != null) teamAOption.interactable = allowTeamA;
        if (teamBOption != null) teamBOption.interactable = allowTeamB;
    }

    // TMP_Dropdown values: 0 = A, 1 = B. Existing Inspector events are preserved.
    public void SelectTeam(int value)
    {
        PlayerData local = PlayerData.Local;
        if (local == null || local.Object == null || !local.Object.IsValid || !local.IsLoadoutInitialized) return;
        if (manager != null && manager.IsWaitingInCodeRoom && BattleMatchRules.IsTeamMode(manager.CurrentMatchMode) &&
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
            if (uiFont == null && waitingRoomInfo != null)
                uiFont = waitingRoomInfo.CodeWaitingRoomPanel.GetComponentInChildren<TMP_Text>(true)?.font;
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
            PlaceTeamDropdown(teamDropdown, anchor);
        }
        if (boundTeamDropdown != teamDropdown)
        {
            teamListWasExpanded = false;
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
        teamListWasExpanded = false;
        teamAOption = teamBOption = null;
        nextWaitingRoomLookup = nextModeLookup = 0f;
        foreach (var dropdown in otherTeamDropdowns.Values)
            if (dropdown != null) dropdown.gameObject.SetActive(false);
        if (teamDropdown != null && teamDropdown.gameObject.activeSelf)
            teamDropdown.gameObject.SetActive(false);
    }

    private void ReleaseTeamDropdown()
    {
        foreach (var dropdown in otherTeamDropdowns.Values)
            if (dropdown != null) Destroy(dropdown.gameObject);
        otherTeamDropdowns.Clear();
        if (boundTeamDropdown != null) boundTeamDropdown.onValueChanged.RemoveListener(SelectTeam);
        if (generatedTeamDropdown && teamDropdown != null) Destroy(teamDropdown.gameObject);
    }

    private void RefreshOtherTeamDropdowns(bool show, PlayerData local)
    {
        // Every occupied card shows its replicated team; only its owner can edit it.
        foreach (var pair in otherTeamDropdowns)
        {
            PlayerData data = show ? PlayerData.FindPerkData(manager.Runner, pair.Key) : null;
            if (pair.Value != null) pair.Value.gameObject.SetActive(data != null && data != local);
        }
        if (!show || waitingRoomInfo == null) return;
        foreach (Fusion.PlayerRef participant in manager.Runner.ActivePlayers)
        {
            PlayerData data = PlayerData.FindPerkData(manager.Runner, participant);
            if (data == null || data == local) continue;
            RectTransform card = waitingRoomInfo.GetCodeWaitingRoomCard(participant);
            if (card == null || !card.gameObject.activeInHierarchy) continue;
            if (!otherTeamDropdowns.TryGetValue(participant, out var dropdown) || dropdown == null)
            {
                var root = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources());
                root.name = "ParticipantTeamDropdown";
                dropdown = root.GetComponent<TMP_Dropdown>();
                dropdown.ClearOptions();
                dropdown.AddOptions(new System.Collections.Generic.List<string> { "A팀", "B팀" });
                dropdown.interactable = false;
                foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
                { if (uiFont != null) text.font = uiFont; text.fontSize = 20; text.color = new Color(.03f,.13f,.18f); }
                otherTeamDropdowns[participant] = dropdown;
            }
            PlaceTeamDropdown(dropdown, card);
            dropdown.SetValueWithoutNotify(data.teamIndex == 1 ? 0 : 1);
            dropdown.gameObject.SetActive(true);
        }
    }

    private void PlaceTeamDropdown(TMP_Dropdown dropdown, RectTransform card)
    {
        // Sibling of the cards: a portrait/card Mask must never clip the selection bar.
        Transform parent = waitingRoomInfo != null ? waitingRoomInfo.CodeWaitingRoomPanel.transform : card.parent;
        var rect = dropdown.GetComponent<RectTransform>();
        if (rect.parent != parent) rect.SetParent(parent, false);
        rect.pivot = new Vector2(.5f, 1);
        rect.sizeDelta = new Vector2(220, 34);
        rect.position = card.TransformPoint(new Vector3(card.rect.center.x, card.rect.yMin, 0));
        rect.anchoredPosition += teamDropdownOffset;
        rect.SetAsLastSibling();
    }
}
