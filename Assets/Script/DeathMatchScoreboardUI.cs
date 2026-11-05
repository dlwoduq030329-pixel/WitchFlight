using System;
using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Always-active controller; every row is authored in the scene, never instantiated on Tab.
[DisallowMultipleComponent]
public sealed class DeathMatchScoreboardUI : MonoBehaviour
{
    [Serializable]
    public sealed class Row
    {
        public RectTransform root;
        public Image profile;
        public TMP_Text nickname, title, damage, taken, kills, deaths, rank;
        [NonSerialized] private PlayerData previous;
        [NonSerialized] private int revision = -1, previousRank, profileId = -1, titleId = -1;
        [NonSerialized] private NetworkString<_32> previousName;

        public void Display(PlayerData data, int place, int sibling, ProfileImageTable profiles)
        {
            if (root == null) return;
            if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
            if (sibling >= 0 && root.GetSiblingIndex() != sibling) root.SetSiblingIndex(sibling);
            bool changed = previous != data;
            if (changed || previousName != data.playerName)
            {
                previousName = data.playerName;
                if (nickname != null) { nickname.richText = false; nickname.text = data.playerName.ToString(); }
            }
            if (changed || profileId != data.playerprofile)
            {
                profileId = data.playerprofile;
                if (profile != null)
                {
                    profile.sprite = profiles != null ? profiles.GetSprite(profileId) : null;
                    profile.enabled = profile.sprite != null;
                }
            }
            if (changed || titleId != data.EquippedTitleId)
            {
                titleId = data.EquippedTitleId;
                if (title != null) title.text = TitleCatalog.Find(titleId)?.name ?? "";
            }
            if (changed || revision != data.BattleStatsRevision)
            {
                revision = data.BattleStatsRevision;
                if (damage != null) damage.text = Mathf.RoundToInt(data.BattleDamageDealt).ToString("N0");
                if (taken != null) taken.text = Mathf.RoundToInt(data.BattleDamageTaken).ToString("N0");
                if (kills != null) kills.SetText("{0}", data.BattleKills);
                if (deaths != null) deaths.SetText("{0}", data.BattleDeaths);
            }
            if (changed || previousRank != place)
            {
                previousRank = place;
                if (rank != null) rank.SetText("{0}", place);
            }
            previous = data;
        }

        public void Hide() { if (root != null && root.gameObject.activeSelf) root.gameObject.SetActive(false); }
    }

    [SerializeField] private RectTransform panel;
    [SerializeField] private RectTransform rowsRoot;
    [SerializeField] private VerticalLayoutGroup layout;
    [SerializeField] private Row mine;
    [SerializeField] private Row[] enemies = Array.Empty<Row>();
    [SerializeField] private ProfileImageTable profiles;
    [SerializeField] private GameObject scoreUI;
    [SerializeField] private Row firstClass;
    [SerializeField] private Row meClass;
    private readonly List<PlayerData> roster = new(NetworkGameManager.DeathMatchCapacity);
    private float nextRefresh;
    private int visibleRows = -1;
    private Vector2 lastPanelSize;
    private float rowWidth, rowHeight;

    private sealed class RankComparer : IComparer<PlayerData>
    {
        public static readonly RankComparer Instance = new();
        public int Compare(PlayerData a, PlayerData b)
        {
            int score = b.BattleKills.CompareTo(a.BattleKills);
            if (score != 0) return score;
            int joined = a.DeathMatchJoinOrder.CompareTo(b.DeathMatchJoinOrder);
            return joined != 0 ? joined : a.ParticipantRef.RawEncoded.CompareTo(b.ParticipantRef.RawEncoded);
        }
    }

    private void Awake()
    {
        if (panel != null) panel.gameObject.SetActive(false);
        firstClass?.Hide();
        meClass?.Hide();
        if (NetworkGameManager.Instance == null || !NetworkGameManager.Instance.IsDeathMatch)
        { enabled = false; return; }
        if (scoreUI != null) scoreUI.SetActive(false);
        if (profiles == null) profiles = ProfileImageTable.Default;
        if (mine?.root != null) { rowWidth = mine.root.rect.width; rowHeight = mine.root.rect.height; }
        mine?.Hide();
        foreach (var row in enemies) row?.Hide();
    }

    private void Update()
    {
        var manager = NetworkGameManager.Instance;
        bool show = manager != null && manager.IsDeathMatch && manager.Runner != null && manager.Runner.IsRunning &&
            Application.isFocused && !CombatPresentation.MenuOpen && !BattleEmoteUI.CapturesMouse && Input.GetKey(KeyCode.Tab);
        bool opening = show && panel != null && !panel.gameObject.activeSelf;
        if (panel != null && panel.gameObject.activeSelf != show) panel.gameObject.SetActive(show);
        if (manager == null || manager.Runner == null || !manager.Runner.IsRunning)
        { firstClass?.Hide(); meClass?.Hide(); return; }
        if (opening || Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + .15f;
            Refresh(manager.Runner);
        }
    }

    private void Refresh(NetworkRunner runner)
    {
        roster.Clear();
        // Registry contains connected player data even while their character is dead/despawned.
        foreach (var data in PlayerData.PerkPlayers)
            if (data != null && data.Object != null && data.Object.IsValid && data.Runner == runner &&
                data.ParticipantRef != PlayerRef.None && data.IsLoadoutInitialized && !data.IsMatchBot && data.DeathMatchJoinOrder > 0)
                roster.Add(data);
        roster.Sort(RankComparer.Instance);
        int myIndex = -1;
        for (int i = 0; i < roster.Count; i++)
            if (roster[i].ParticipantRef == runner.LocalPlayer) { myIndex = i; break; }
        if (roster.Count > 0) firstClass?.Display(roster[0], 1, -1, profiles);
        else firstClass?.Hide();
        if (myIndex >= 0) meClass?.Display(roster[myIndex], myIndex + 1, -1, profiles);
        else meClass?.Hide();
        if (panel == null || !panel.gameObject.activeSelf) return;
        int top = Mathf.Min(10, roster.Count), enemyIndex = 0, shown = 0;
        for (int i = 0; i < top; i++)
        {
            Row row = i == myIndex ? mine : enemyIndex < enemies.Length ? enemies[enemyIndex++] : null;
            if (row == null) continue;
            row.Display(roster[i], i + 1, shown++, profiles);
        }
        if (myIndex >= top) mine?.Display(roster[myIndex], myIndex + 1, shown++, profiles);
        else if (myIndex < 0) mine?.Hide();
        for (int i = enemyIndex; i < enemies.Length; i++) enemies[i]?.Hide();
        FitRows(shown);
    }

    private void FitRows(int count)
    {
        if (rowsRoot == null || layout == null || rowWidth <= 0f || rowHeight <= 0f) return;
        Vector2 available = panel.rect.size;
        if (visibleRows == count && lastPanelSize == available) return;
        visibleRows = count; lastPanelSize = available;
        float height = rowHeight * count + layout.spacing * Mathf.Max(0, count - 1);
        rowsRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, rowWidth);
        rowsRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        float scale = Mathf.Min(1f, available.x * .92f / rowWidth, available.y * .88f / Mathf.Max(1f, height));
        rowsRoot.localScale = Vector3.one * scale;
    }

    private void OnDisable() { if (panel != null) panel.gameObject.SetActive(false); }
}
