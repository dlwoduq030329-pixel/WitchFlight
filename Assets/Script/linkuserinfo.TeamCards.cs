using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public sealed partial class linkuserinfo
{
    [SerializeField] private GameObject[] roomBackdropPanels;
    private readonly Dictionary<GameObject, bool> roomBackdropStates = new();

    private void UpdateRoomBackdrops(bool waiting)
    {
        if (!waiting) { RestoreRoomBackdrops(); return; }
        if (roomBackdropPanels == null) return;
        foreach (GameObject panel in roomBackdropPanels)
        {
            if (panel == null || panel == gameObject || transform.IsChildOf(panel.transform) ||
                (waitingRoomPanel != null && waitingRoomPanel.transform.IsChildOf(panel.transform))) continue;
            if (!roomBackdropStates.ContainsKey(panel)) roomBackdropStates.Add(panel, panel.activeSelf);
            if (panel.activeSelf) panel.SetActive(false);
        }
    }

    private void RestoreRoomBackdrops()
    {
        foreach (var entry in roomBackdropStates)
            if (entry.Key != null) entry.Key.SetActive(entry.Value);
        roomBackdropStates.Clear();
    }

    private sealed class TeamCard
    {
        public Transform root;
        public GameObject occupied, empty, crown;
        public TMP_Text nickname;
        public Image profile;
        public Button button;
        public UnityAction click;
        public PlayerData player;
        public int team, index;
    }
    private readonly List<TeamCard> teamCards = new();
    private readonly Dictionary<PlayerRef, TeamCard> participantCards = new();
    private Transform blueCards, redCards, versusImage;
    private TMP_Text roomHeaderName, roomHeaderCount, roomHeaderMode;
    private bool teamCardBindingsReady;
    private int expandedBlueCapacity, expandedRedCapacity;
    public bool UsesTeamCards => waitingRoomPanel != null &&
        waitingRoomPanel.transform.Find("TeamsLayoutGroup/BlueTeamLayoutGroup") != null;

    private static Transform FindCardChild(Transform root, string name)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (string.Equals(child.name, name, System.StringComparison.OrdinalIgnoreCase)) return child;
        return null;
    }

    private void BindTeamCards()
    {
        if (teamCardBindingsReady) return;
        Transform panel = waitingRoomPanel.transform;
        blueCards = panel.Find("TeamsLayoutGroup/BlueTeamLayoutGroup");
        redCards = panel.Find("TeamsLayoutGroup/RedTeamLayoutGroup");
        versusImage = panel.Find("TeamsLayoutGroup/vsimage");
        roomHeaderName = panel.Find("Panel/NicnameLayoutGroup/Nickname")?.GetComponent<TMP_Text>();
        roomHeaderCount = panel.Find("Panel/PeopleCountText")?.GetComponent<TMP_Text>();
        roomHeaderMode = panel.Find("Panel/CurrentMode")?.GetComponent<TMP_Text>();
        teamCards.Clear();
        for (int team = 1; team <= 2; team++)
        {
            Transform group = team == 1 ? blueCards : redCards;
            if (group == null) continue;
            for (int i = 0; i < group.childCount; i++)
            {
                Transform root = group.GetChild(i);
                if (!root.name.StartsWith("Card", System.StringComparison.Ordinal) ||
                    !int.TryParse(root.name.Substring(4), out int number)) continue;
                Transform occupied = FindCardChild(root, "SetActive");
                var card = new TeamCard {
                    root = root, team = team, index = number - 1,
                    occupied = occupied?.gameObject,
                    empty = FindCardChild(root, "EmptyText")?.gameObject,
                    crown = (FindCardChild(root, "Crown") ?? FindCardChild(root, "CrownImage"))?.gameObject,
                    nickname = occupied != null ? FindCardChild(occupied, "Nickname")?.GetComponent<TMP_Text>() : null,
                    profile = occupied != null ? FindCardChild(occupied, "Profile")?.GetComponent<Image>() : null,
                    button = root.GetComponent<Button>()
                };
                card.click = () => SelectCardTeam(card);
                if (card.button != null) card.button.onClick.AddListener(card.click);
                teamCards.Add(card);
            }
        }
        teamCards.Sort((a, b) => a.team != b.team ? a.team.CompareTo(b.team) : a.index.CompareTo(b.index));
        teamCardBindingsReady = true;
    }

    private void SelectCardTeam(TeamCard card)
    {
        var manager = NetworkGameManager.Instance;
        var local = PlayerData.Local;
        if (manager == null || !manager.IsWaitingInCodeRoom || (manager.IsRaceMode || manager.IsRaidMode) ||
            local == null || local.Runner != manager.Runner || !local.IsLoadoutInitialized ||
            card.player != null || !card.root.gameObject.activeInHierarchy || local.teamIndex == card.team) return;
        local.RequestWaitingTeam(card.team); // Existing owner-only RPC validates capacity on the host.
    }

    private bool RefreshTeamCards(NetworkGameManager manager, NetworkRunner runner, PlayerData owner, bool waiting)
    {
        if (!UsesTeamCards) return false;
        BindTeamCards();
        participantCards.Clear();
        BattleMatchMode mode = manager != null ? manager.CurrentMatchMode : BattleMatchMode.TwoVsTwo;
        bool shared = mode == BattleMatchMode.Race || mode == BattleMatchMode.GiantMeowRaid;
        int capacity = BattleMatchRules.PlayerCount(mode);
        int blueCapacity = shared ? capacity : capacity / 2;
        int redCapacity = shared ? 0 : capacity / 2;
        if (expandedBlueCapacity != blueCapacity || expandedRedCapacity != redCapacity)
        {
            EnsureTeamCardCapacity(blueCards, 1, blueCapacity);
            EnsureTeamCardCapacity(redCards, 2, redCapacity);
            expandedBlueCapacity = blueCapacity; expandedRedCapacity = redCapacity;
        }
        if (redCards != null) redCards.gameObject.SetActive(!shared);
        if (versusImage != null) versusImage.gameObject.SetActive(!shared);
        foreach (var card in teamCards) card.player = null;
        if (waiting)
            foreach (PlayerRef participant in waitingParticipants)
            {
                PlayerData data = GetPlayerData(participant);
                if (!IsReady(data)) continue;
                int team = shared ? 1 : data.teamIndex;
                TeamCard card = teamCards.Find(c => c.team == team && c.player == null &&
                    c.index < (team == 1 ? blueCapacity : redCapacity));
                if (card == null) continue;
                card.player = data;
                participantCards[participant] = card;
            }
        foreach (var card in teamCards)
        {
            bool visible = card.index < (card.team == 1 ? blueCapacity : redCapacity);
            bool occupied = waiting && card.player != null;
            card.root.gameObject.SetActive(visible);
            SetSlotName(card.nickname, occupied ? card.player.playerName.ToString() : "");
            ApplyProfile(card.profile, occupied ? card.player : null);
            PlayerTitleLabel.Bind(card.root, occupied ? card.player : null);
            if (card.crown != null) card.crown.SetActive(occupied && card.player.IsRoomOwner);
            if (card.empty != null) card.empty.SetActive(!occupied);
            if (card.occupied != null) card.occupied.SetActive(occupied);
            if (card.button != null) card.button.interactable = waiting && visible && !occupied &&
                !shared && IsReady(localPlayerData) && localPlayerData.teamIndex != card.team;
        }
        string ownerName = waiting ? owner.playerName.ToString() : "";
        SetSlotName(roomHeaderName, ownerName);
        SetSlotName(roomkeepername, ownerName);
        string count = waiting ? $"{manager.ConnectedPlayerCount}/{manager.RequiredPlayerCount}" : "";
        SetSlotName(roomHeaderCount, count);
        SetSlotName(waitingRoomCountText, count);
        SetSlotName(roomHeaderMode, waiting ? BattleMatchRules.Label(mode) : "");
        SetSlotName(waitingRoomCodeText, waiting ? runner.SessionInfo.Name : "");
        return true;
    }

    private void ReleaseTeamCards()
    {
        foreach (var card in teamCards)
            if (card.button != null) card.button.onClick.RemoveListener(card.click);
        teamCards.Clear();
        participantCards.Clear();
        teamCardBindingsReady = false;
        expandedBlueCapacity = expandedRedCapacity = 0;
    }

    private void EnsureTeamCardCapacity(Transform group, int team, int capacity)
    {
        if (group == null || capacity == 0) return;
        var slots = teamCards.FindAll(c => c.team == team);
        if (slots.Count == 0) return;
        bool grew = false;
        for (int i = slots.Count; i < capacity; i++)
        {
            Transform root = ParticipantUiSlots.Clone(slots[0].root, "Card" + (i + 1));
            var card = new TeamCard { root = root, team = team, index = i };
            var occupied = FindCardChild(root, "SetActive");
            card.occupied = occupied?.gameObject;
            card.empty = FindCardChild(root, "EmptyText")?.gameObject;
            card.crown = (FindCardChild(root, "Crown") ?? FindCardChild(root, "CrownImage"))?.gameObject;
            card.nickname = occupied != null ? FindCardChild(occupied, "Nickname")?.GetComponent<TMP_Text>() : null;
            card.profile = occupied != null ? FindCardChild(occupied, "Profile")?.GetComponent<Image>() : null;
            card.button = root.GetComponent<Button>();
            card.click = () => SelectCardTeam(card);
            if (card.button != null) { card.button.onClick = new Button.ButtonClickedEvent(); card.button.onClick.AddListener(card.click); }
            slots.Add(card); teamCards.Add(card); grew = true;
        }
        if (grew || slots.Count > 2)
        {
            var rows = new List<RectTransform>();
            for (int i = 0; i < capacity; i++) rows.Add(slots[i].root as RectTransform);
            ParticipantUiSlots.FitVertical(rows, group as RectTransform);
        }
    }
}
