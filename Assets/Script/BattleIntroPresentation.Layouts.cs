using System;
using Fusion;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class BattleIntroPresentation
{
    [Header("Authored VSUI_1vs1 … VSUI_5vs5")]
    [Tooltip("개별 캐릭터 RenderTexture의 긴 변 해상도 상한. 최대 10명의 VS 연출에만 사용합니다.")]
    [SerializeField, Range(128, 1024)] private int authoredPortraitResolution = 512;

    private sealed class AuthoredLayout
    {
        public GameObject root;
        public Transform ownGroup, enemyGroup;
        public TeamIntroCard[] cards;
    }

    private readonly AuthoredLayout[] authoredLayouts = new AuthoredLayout[5];
    private PortraitSnapshot[] authoredSnapshots = Array.Empty<PortraitSnapshot>();
    private AuthoredLayout activeLayout;
    private GameObject legacyIntroPanel;
    private bool layoutsDiscovered;

    // Once per scene, including inactive roots. No global/per-frame hierarchy search.
    private void DiscoverAuthoredLayouts()
    {
        if (layoutsDiscovered) return;
        layoutsDiscovered = true;
        legacyIntroPanel = introPanel;
        foreach (var root in gameObject.scene.GetRootGameObjects())
        foreach (var candidate in root.GetComponentsInChildren<RectTransform>(true))
        {
            int size = 0;
            for (int n = 1; n <= 5; n++)
                if (string.Equals(candidate.name.Trim(), $"VSUI_{n}vs{n}", StringComparison.OrdinalIgnoreCase))
                { size = n; break; }
            if (size == 0) continue;
            var own = FindVSChild(candidate, "TeamAProfileGroups");
            var enemy = FindVSChild(candidate, "TeamBProfileGroups");
            var layout = new AuthoredLayout { root = candidate.gameObject, ownGroup = own, enemyGroup = enemy,
                cards = new TeamIntroCard[size * 2] };
            bool valid = own != null && enemy != null;
            if (valid)
            {
                for (int side = 0; side < 2; side++)
                for (int i = 0; i < size; i++)
                {
                    var group = side == 0 ? own : enemy;
                    // Numbered authored slots, independent of decorative siblings or hierarchy order.
                    var row = FindVSChild(group, $"PlayerProfile{i + 1:00}");
                    int index = side * size + i;
                    BindTeamProfile(ref layout.cards[index], row);
                    var card = layout.cards[index];
                    if (card != null) card.portrait = FindVSChild(row, "Rawimage")?.GetComponent<RawImage>();
                    valid &= card != null && card.portrait != null && card.playerName != null &&
                        card.playerTitle != null && card.magic1Icon != null && card.magic2Icon != null;
                    if (card?.portrait != null) { card.portrait.texture = null; card.portrait.enabled = false; }
                    if (row != null) row.gameObject.SetActive(true);
                }
                own.gameObject.SetActive(false);
                enemy.gameObject.SetActive(false);
            }
            // Cache/pause authored tweens before enabling any selected layout.
            introPanel = candidate.gameObject;
            PrepareIntroTweens();
            foreach (var info in candidate.GetComponentsInChildren<linkuserinfo>(true)) info.enabled = false;
            candidate.gameObject.SetActive(false);
            if (valid) authoredLayouts[size - 1] = layout;
            else Debug.LogError($"{candidate.name}: VS 카드의 Rawimage, Chingho, Nickname, MagicIcon01/02 연결을 확인하세요.", candidate);
        }
        introPanel = legacyIntroPanel;
    }

    private bool SelectAuthoredLayout()
    {
        if (!layoutsDiscovered) return false;
        var network = NetworkGameManager.Instance;
        var battle = BattleManager.Instance;
        int count = battle != null ? battle.ExpectedPlayerCount : network != null ? network.RequiredPlayerCount : 0;
        bool versus = (network == null || (!network.IsTraining && !network.IsDeathMatch && !network.IsRaidMode && !network.IsRaceMode)) &&
            (battle == null || (!battle.IsTraining && !battle.IsRace && battle.UsesVersusPresentation));
        var selected = versus && count >= 2 && count <= 10 && count % 2 == 0 ? authoredLayouts[count / 2 - 1] : null;
        if (activeLayout == selected) return false;
        ReleasePortraits();
        PauseIntroTweens();
        if (activeLayout != null) activeLayout.root.SetActive(false);
        activeLayout = selected;
        introPanel = selected != null ? selected.root : legacyIntroPanel;
        if (selected != null)
        {
            if (legacyIntroPanel != null && legacyIntroPanel != selected.root) legacyIntroPanel.SetActive(false);
            if (introUserInfo != null) introUserInfo.enabled = false;
            authoredSnapshots = new PortraitSnapshot[count];
            selected.ownGroup.gameObject.SetActive(false);
            selected.enemyGroup.gameObject.SetActive(false);
        }
        nextPreparationTime = 0f;
        return true;
    }

    private void PrepareAuthoredPresentation(BattleStartPhase phase)
    {
        var network = NetworkGameManager.Instance;
        if (network == null || !network.IsBattleSceneLoaded) return;
        BattleMatchRules.CollectRoster(network.Runner, teamRoster);
        int count = activeLayout.cards.Length;
        if (!BattleMatchRules.IsFullBalanced(teamRoster, count) ||
            teamRoster[0].ParticipantRef != network.Runner.LocalPlayer)
        { ReleaseAuthoredPortraits(); return; }

        if (teamPlayers.Length != count) Array.Resize(ref teamPlayers, count);
        // Resolve the entire roster before touching any snapshot: never show mixed/partial teams.
        for (int i = 0; i < count; i++)
        {
            if (!BattleParticipants.TryGetPlayerObject(network.Runner, teamRoster[i].ParticipantRef, out NetworkObject obj) ||
                obj == null || !obj.IsValid || !obj.TryGetComponent(out Player player) || !player.IsPresentationReady) return;
            teamPlayers[i] = player;
        }
        if (teamProfileTable == null) teamProfileTable = ProfileImageTable.Default;
        bool ready = true;
        for (int i = 0; i < count; i++)
        {
            var data = teamRoster[i];
            var card = activeLayout.cards[i];
            ApplyIntroProfileLabels(card, data);
            SetTeamMagicIcon(card.profile, teamProfileTable != null ? teamProfileTable.GetSprite(data.playerprofile) : null);
            var magicTable = teamPlayers[i].MagicTable != null ? teamPlayers[i].MagicTable : MagicStatTable.Default;
            SetTeamMagicIcon(card.magic1Icon, magicTable != null ? magicTable.GetIcon(data.magic1) : null);
            SetTeamMagicIcon(card.magic2Icon, magicTable != null ? magicTable.GetIcon(data.magic2) : null);
            // Slot spacing isolates each rendering stage; reuse the actual player's customized visuals.
            if (!EnsureSnapshot(ref authoredSnapshots[i], teamPlayers[i], i, card.portrait)) { ready = false; continue; }
            card.portrait.texture = authoredSnapshots[i].Texture;
            ready &= authoredSnapshots[i].IsReady;
        }
        UpdatePortraitVisibility(phase);
        if (ready && phase == BattleStartPhase.WaitingForPlayers) teamPlayers[0].ReportBattleSceneReady();
    }

    private void UpdateAuthoredPortraitCameras(BattleStartPhase phase)
    {
        foreach (var snapshot in authoredSnapshots)
        {
            if (snapshot?.Camera == null) continue;
            // Bake once while waiting; animated companions render only during the short VS presentation.
            snapshot.Camera.enabled = !snapshot.IsReady || (snapshot.AnimatedCat && phase == BattleStartPhase.Intro);
        }
    }

    private void ReleaseAuthoredPortraits()
    {
        for (int i = 0; i < authoredSnapshots.Length; i++)
        {
            if (activeLayout != null && i < activeLayout.cards.Length && activeLayout.cards[i].portrait != null)
                activeLayout.cards[i].portrait.texture = null;
            ReleaseSnapshot(ref authoredSnapshots[i]);
        }
    }
}
