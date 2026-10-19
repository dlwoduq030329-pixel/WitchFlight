using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public sealed class TeamIntroCard
{
    public RawImage portrait;
    public Image profile;
    public TMP_Text playerName;
    public TMP_Text magicNames;
}

public sealed partial class BattleIntroPresentation
{
    [Header("2v2 VS presentation (empty = generated)")]
    [SerializeField] private GameObject teamIntroPanel;
    [Tooltip("나, 우리 팀원, 상대 1, 상대 2 순서입니다. 커스텀 패널을 넣으면 이 네 카드도 연결해주세요.")]
    [SerializeField] private TeamIntroCard[] teamCards = new TeamIntroCard[4];
    [SerializeField] private TMP_Text teamCountdownText;
    [SerializeField] private ProfileImageTable teamProfileTable;
    private Canvas teamCanvas;
    private CanvasGroup teamIntroGroup;
    private TMP_Text teamHeading;
    private float teamIntroStarted;
    private readonly PortraitSnapshot[] teamSnapshots = new PortraitSnapshot[4];
    private readonly List<PlayerData> teamRoster = new();
    private bool IsTeamBattle => BattleManager.Instance != null && BattleManager.Instance.ExpectedPlayerCount > 2;

    private void PrepareTeamPresentation(BattleStartPhase phase)
    {
        NetworkGameManager network = NetworkGameManager.Instance;
        if (network == null || !network.IsBattleSceneLoaded) return;
        BattleMatchRules.CollectRoster(network.Runner, teamRoster);
        if (!BattleMatchRules.IsFullBalanced(teamRoster, 4) || !teamRoster[0].Object.HasInputAuthority) return;
        EnsureTeamIntroUI();
        if (teamProfileTable == null) teamProfileTable = ProfileImageTable.Default;
        bool allReady = true;
        Player localPlayer = null;
        for (int i = 0; i < 4; i++)
        {
            PlayerData data = teamRoster[i];
            if (!network.Runner.TryGetPlayerObject(data.Object.InputAuthority, out var obj) || obj == null || !obj.IsValid)
            { allReady = false; continue; }
            Player player = obj.GetComponent<Player>();
            if (player == null || !player.IsPresentationReady) { allReady = false; continue; }
            if (i == 0) localPlayer = player;
            if (!EnsureSnapshot(ref teamSnapshots[i], player, i)) { allReady = false; continue; }
            allReady &= teamSnapshots[i].IsReady;
            TeamIntroCard view = teamCards != null && i < teamCards.Length ? teamCards[i] : null;
            if (view == null) continue;
            if (view.portrait != null) view.portrait.texture = teamSnapshots[i].Texture;
            if (view.profile != null)
            {
                view.profile.sprite = teamProfileTable != null ? teamProfileTable.GetSprite(data.playerprofile) : null;
                view.profile.enabled = view.profile.sprite != null;
            }
            if (view.playerName != null) { view.playerName.richText = false; view.playerName.text = data.playerName.ToString(); }
            if (view.magicNames != null)
            {
                MagicStatTable table = MagicStatTable.Default;
                view.magicNames.text = $"{(table != null ? table.GetStats(data.magic1).displayName : data.magic1.ToString())}\n" +
                    (table != null ? table.GetStats(data.magic2).displayName : data.magic2.ToString());
            }
        }
        if (teamHeading != null) teamHeading.text = $"2 vs 2  ·  ROUND {BattleManager.Instance.RoundNumber}";
        if (phase == BattleStartPhase.WaitingForPlayers && allReady && localPlayer != null)
            localPlayer.ReportBattleSceneReady();
    }

    private void SetTeamUIPhase(BattleStartPhase phase)
    {
        if (IsTeamBattle && (phase == BattleStartPhase.WaitingForPlayers || phase == BattleStartPhase.Intro || phase == BattleStartPhase.Countdown)) EnsureTeamIntroUI();
        if (teamIntroPanel != null) teamIntroPanel.SetActive(IsTeamBattle && (phase == BattleStartPhase.WaitingForPlayers || phase == BattleStartPhase.Intro));
        if (teamCountdownText != null) teamCountdownText.gameObject.SetActive(IsTeamBattle && phase == BattleStartPhase.Countdown);
        if (phase == BattleStartPhase.Intro) teamIntroStarted = Time.unscaledTime;
    }

    private void UpdateTeamIntroAnimation(BattleStartPhase phase)
    {
        if (teamIntroGroup != null && IsTeamBattle)
            teamIntroGroup.alpha = phase == BattleStartPhase.Intro ? Mathf.Clamp01((Time.unscaledTime - teamIntroStarted) / .35f) : .8f;
    }

    private Transform TeamCanvasRoot()
    {
        if (teamCanvas == null)
        {
            var root = new GameObject("Generated Team VS UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            root.transform.SetParent(transform, false);
            teamCanvas = root.GetComponent<Canvas>(); teamCanvas.renderMode = RenderMode.ScreenSpaceOverlay; teamCanvas.sortingOrder = 95;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920,1080); scaler.matchWidthOrHeight = .5f;
        }
        return teamCanvas.transform;
    }

    private static RectTransform TeamRect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent,false); rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
        rect.anchoredPosition = position; rect.sizeDelta = size; return rect;
    }

    private TMP_Text TeamLabel(string name, Transform parent, Vector2 position, Vector2 size, float fontSize, string value)
    {
        var text = TeamRect(name,parent,position,size).gameObject.AddComponent<TextMeshProUGUI>();
        TMP_FontAsset font = countdownFont != null ? countdownFont : GetComponent<BattleHud>()?.FeedbackFont;
        if (font != null) text.font = font;
        text.fontSize = fontSize; text.color = Color.white; text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false; text.richText = false; text.text = value; return text;
    }

    private void EnsureTeamIntroUI()
    {
        if (teamIntroPanel == null)
        {
            var root = TeamRect("TeamVSPanel", TeamCanvasRoot(), Vector2.zero, new Vector2(1920,1080));
            var backdrop = root.gameObject.AddComponent<Image>(); backdrop.color = new Color(.02f,.04f,.085f,.93f); backdrop.raycastTarget = false;
            teamIntroPanel = root.gameObject;
            teamCards = new TeamIntroCard[4];
            float[] x = {-600,-300,300,600};
            for (int i = 0; i < 4; i++)
            {
                var card = TeamRect("Player " + i,root,new Vector2(x[i],0),new Vector2(260,420));
                var background = card.gameObject.AddComponent<Image>(); background.raycastTarget = false;
                background.color = i < 2 ? new Color(.03f,.32f,.34f,.9f) : new Color(.4f,.07f,.15f,.9f);
                var view = new TeamIntroCard(); teamCards[i] = view;
                view.portrait = TeamRect("Face",card,new Vector2(0,55),new Vector2(240,260)).gameObject.AddComponent<RawImage>();
                view.portrait.raycastTarget = false;
                view.profile = TeamRect("Profile",card,new Vector2(-92,-106),new Vector2(42,42)).gameObject.AddComponent<Image>();
                view.profile.raycastTarget = false; view.profile.preserveAspect = true;
                view.playerName = TeamLabel("Name",card,new Vector2(22,-106),new Vector2(180,50),22,"");
                view.magicNames = TeamLabel("Magic",card,new Vector2(0,-165),new Vector2(240,65),22,"");
            }
            TeamLabel("OwnTeam",root,new Vector2(-450,265),new Vector2(500,60),34,"우리 팀");
            TeamLabel("EnemyTeam",root,new Vector2(450,265),new Vector2(500,60),34,"상대 팀");
            TeamLabel("VS",root,Vector2.zero,new Vector2(160,100),62,"VS");
            teamHeading = TeamLabel("Round",root,new Vector2(0,385),new Vector2(700,70),36,"");
        }
        if (teamIntroGroup == null)
        {
            teamIntroGroup = teamIntroPanel.GetComponent<CanvasGroup>();
            if (teamIntroGroup == null) teamIntroGroup = teamIntroPanel.AddComponent<CanvasGroup>();
            teamIntroGroup.blocksRaycasts = false; teamIntroGroup.interactable = false;
        }
        if (teamCountdownText == null)
            teamCountdownText = TeamLabel("Team Countdown",TeamCanvasRoot(),Vector2.zero,new Vector2(240,180),112,"");
    }
}
