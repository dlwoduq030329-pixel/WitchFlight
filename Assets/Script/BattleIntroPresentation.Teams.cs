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
    public TMP_Text playerTitle;
    public TMP_Text magicNames;
    public Image magic1Icon;
    public Image magic2Icon;
}

public sealed partial class BattleIntroPresentation
{
    [Header("2v2: existing VSUI profiles")]
    [Tooltip("비우면 VSUI의 PlayerProfile (1), EnemyProfile (1)을 자동 연결합니다.")]
    [SerializeField] private GameObject teammateProfileRoot;
    [SerializeField] private GameObject secondEnemyProfileRoot;
    [Tooltip("나, 우리 팀원, 상대 1, 상대 2. 비우면 기존 VSUI에서 자동 연결합니다.")]
    [SerializeField] private TeamIntroCard[] teamCards = new TeamIntroCard[4];
    [SerializeField] private ProfileImageTable teamProfileTable;
    [Header("2v2: two characters per camera (presentation meshes only)")]
    [SerializeField, Min(.1f)] private float teamCharacterSpacing = 1.6f;
    [SerializeField, Min(.1f)] private float teamPortraitMinimumHalfHeight = 1.5f;
    [SerializeField, Min(1f)] private float teamPortraitPadding = 1.1f;
    private bool teamProfilesBound, soloProfilesBound, restoreIntroUserInfo;
    private readonly PortraitSnapshot[] teamSnapshots = new PortraitSnapshot[2];
    private readonly List<PlayerData> teamRoster = new();
    private Player[] teamPlayers = new Player[4];
    private Transform[] expandedProfileRoots;
    private readonly List<ProfileMotion> teamProfileMotions = new();
    private sealed class ProfileMotion
    {
        public RectTransform source, copy;
        public Vector2 sourcePosition, copyPosition;
    }

    private void PrepareTeamProfileMotion()
    {
        if (introPanel == null || teamProfileMotions.Count > 0) return;
        foreach (string name in new[] { "PlayerProfile", "EnemyProfile" })
        {
            var source = FindVSChild(introPanel.transform, name) as RectTransform;
            var copy = FindVSChild(introPanel.transform, name + " (1)") as RectTransform;
            if (source == null || copy == null) continue;
            teamProfileMotions.Add(new ProfileMotion { source = source, copy = copy,
                sourcePosition = source.anchoredPosition, copyPosition = copy.anchoredPosition });
        }
    }

    private void UpdateTeamProfileMotion()
    {
        if (activeLayout != null) return;
        if (!IsTeamBattle) return;
        foreach (var motion in teamProfileMotions)
            if (motion.source != null && motion.copy != null && motion.copy.gameObject.activeInHierarchy)
                motion.copy.anchoredPosition = motion.copyPosition + motion.source.anchoredPosition - motion.sourcePosition;
    }
    private bool IsTeamBattle => BattleManager.Instance != null && !BattleManager.Instance.IsRace && BattleManager.Instance.UsesVersusPresentation && BattleManager.Instance.ExpectedPlayerCount > 2;

    private void PrepareTeamPresentation(BattleStartPhase phase)
    {
        NetworkGameManager network = NetworkGameManager.Instance;
        if (network == null || !network.IsBattleSceneLoaded) return;
        BattleMatchRules.CollectRoster(network.Runner, teamRoster);
        int required = network.RequiredPlayerCount;
        if (!BattleMatchRules.IsFullBalanced(teamRoster, required) || !teamRoster[0].Object.HasInputAuthority)
        { ReleasePortraits(); return; }
        EnsureTeamIntroUI();
        if (teamProfileTable == null) teamProfileTable = ProfileImageTable.Default;
        if (teamPlayers.Length != required) Array.Resize(ref teamPlayers, required);
        for (int i = 0; i < required; i++)
        {
            PlayerData data = teamRoster[i];
            if (!BattleParticipants.TryGetPlayerObject(network.Runner, data.ParticipantRef, out var obj) || obj == null || !obj.IsValid)
                return;
            Player player = obj.GetComponent<Player>();
            if (player == null || !player.IsPresentationReady) return;
            teamPlayers[i] = player;
            TeamIntroCard view = teamCards != null && i < teamCards.Length ? teamCards[i] : null;
            if (view == null) continue;
            SetTeamMagicIcon(view.profile, teamProfileTable != null ? teamProfileTable.GetSprite(data.playerprofile) : null);
            ApplyIntroProfileLabels(view, data);
            if (view.magicNames != null) { view.magicNames.text = string.Empty; view.magicNames.enabled = false; }
            MagicStatTable table = player.MagicTable != null ? player.MagicTable : MagicStatTable.Default;
            SetTeamMagicIcon(view.magic1Icon, table != null ? table.GetIcon(data.magic1) : null);
            SetTeamMagicIcon(view.magic2Icon, table != null ? table.GetIcon(data.magic2) : null);
        }
        int perTeam = required / 2;
        bool ownReady = EnsureTeamSnapshot(ref teamSnapshots[0], teamPlayers, 0, perTeam, 0, localPortrait);
        bool enemyReady = EnsureTeamSnapshot(ref teamSnapshots[1], teamPlayers, perTeam, perTeam, 1, opponentPortrait);
        if (!ownReady || !enemyReady) return;
        if (localPortrait != null) localPortrait.texture = teamSnapshots[0].Texture;
        if (opponentPortrait != null) opponentPortrait.texture = teamSnapshots[1].Texture;
        UpdatePortraitVisibility(phase);
        if (phase == BattleStartPhase.WaitingForPlayers && teamSnapshots[0].IsReady && teamSnapshots[1].IsReady)
            teamPlayers[0].ReportBattleSceneReady();
    }

    private void EnsureTeamIntroUI()
    {
        if (introPanel == null) return;
        if (introUserInfo == null) introUserInfo = introPanel.GetComponentInChildren<linkuserinfo>(true);
        if (introUserInfo != null && introUserInfo.enabled)
        {
            restoreIntroUserInfo = true;
            introUserInfo.enabled = false; // This controller owns all four cards during the team intro.
        }
        if (teamProfilesBound) return;
        Transform own = FindVSChild(introPanel.transform, "PlayerProfile");
        Transform enemy = FindVSChild(introPanel.transform, "EnemyProfile");
        if (teammateProfileRoot == null) teammateProfileRoot = FindVSChild(introPanel.transform, "PlayerProfile (1)")?.gameObject;
        if (secondEnemyProfileRoot == null) secondEnemyProfileRoot = FindVSChild(introPanel.transform, "EnemyProfile (1)")?.gameObject;
        if (teammateProfileRoot == null && own != null) teammateProfileRoot = ParticipantUiSlots.Clone(own, "PlayerProfile (1)").gameObject;
        if (secondEnemyProfileRoot == null && enemy != null) secondEnemyProfileRoot = ParticipantUiSlots.Clone(enemy, "EnemyProfile (1)").gameObject;
        if (teamCards == null || teamCards.Length != 4) Array.Resize(ref teamCards, 4);
        BindTeamProfile(ref teamCards[0], own);
        BindTeamProfile(ref teamCards[1], teammateProfileRoot != null ? teammateProfileRoot.transform : null);
        BindTeamProfile(ref teamCards[2], enemy);
        BindTeamProfile(ref teamCards[3], secondEnemyProfileRoot != null ? secondEnemyProfileRoot.transform : null);
        teamProfilesBound = own != null && enemy != null && teammateProfileRoot != null && secondEnemyProfileRoot != null;
        if (teamProfilesBound && BattleManager.Instance.ExpectedPlayerCount > 4)
        {
            int count = BattleManager.Instance.ExpectedPlayerCount / 2;
            teamCards = new TeamIntroCard[count * 2];
            expandedProfileRoots = new Transform[count * 2];
            teamProfileMotions.Clear();
            for (int side = 0; side < 2; side++)
            {
                Transform first = side == 0 ? own : enemy;
                Transform second = side == 0 ? teammateProfileRoot.transform : secondEnemyProfileRoot.transform;
                var rows = new List<RectTransform>();
                for (int i = 0; i < count; i++)
                {
                    Transform row = i == 0 ? first : i == 1 ? second :
                        FindVSChild(introPanel.transform, first.name.Trim() + " (" + i + ")");
                    if (row == null) row = ParticipantUiSlots.Clone(second, first.name.Trim() + " (" + i + ")");
                    row.gameObject.SetActive(false);
                    expandedProfileRoots[side * count + i] = row;
                    BindTeamProfile(ref teamCards[side * count + i], row);
                    rows.Add(row as RectTransform);
                }
                ParticipantUiSlots.FitStack(rows, first as RectTransform, second as RectTransform);
                for (int i = 1; i < count; i++)
                {
                    foreach (var tween in rows[i].GetComponentsInChildren<DG.Tweening.DOTweenAnimation>(true))
                    { tween.DOPause(); tween.enabled = false; }
                    teamProfileMotions.Add(new ProfileMotion { source = rows[0], copy = rows[i],
                        sourcePosition = rows[0].anchoredPosition, copyPosition = rows[i].anchoredPosition });
                }
            }
        }
    }

    private void SetTeamUIPhase(BattleStartPhase phase)
    {
        if (activeLayout != null) return;
        bool preparing = IsTeamBattle && (phase == BattleStartPhase.WaitingForPlayers || phase == BattleStartPhase.Intro);
        bool visible = preparing && phase == BattleStartPhase.Intro;
        if (preparing) EnsureTeamIntroUI();
        if (teammateProfileRoot != null) teammateProfileRoot.SetActive(visible);
        if (secondEnemyProfileRoot != null) secondEnemyProfileRoot.SetActive(visible);
        if (expandedProfileRoots != null)
            foreach (var row in expandedProfileRoots) if (row != null) row.gameObject.SetActive(visible);
        if (!preparing && restoreIntroUserInfo)
        {
            restoreIntroUserInfo = false;
            if (introUserInfo != null) introUserInfo.enabled = true;
        }
    }

    private static Transform FindVSChild(Transform root, string name)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (string.Equals(child.name.Trim(), name, StringComparison.OrdinalIgnoreCase)) return child;
        return null;
    }

    private static void BindTeamProfile(ref TeamIntroCard card, Transform root)
    {
        if (root == null) return;
        card ??= new TeamIntroCard();
        if (card.profile == null) card.profile = FindVSChild(root,"ProfileImage")?.GetComponent<Image>();
        // Authored names take priority over references saved before the labels were swapped.
        TMP_Text nickname = FindVSChild(root, "Nickname")?.GetComponent<TMP_Text>();
        TMP_Text title = FindVSChild(root, "Chingho")?.GetComponent<TMP_Text>();
        if (title == null)
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
                if (PlayerTitleLabel.IsTitleObject(text.name)) { title = text; break; }
        if (nickname != null) card.playerName = nickname;
        if (title != null) card.playerTitle = title;
        if (card.playerName == card.playerTitle) card.playerName = null;
        if (card.magic1Icon == null) card.magic1Icon = FindVSChild(root,"MagicIcon01")?.GetComponent<Image>();
        if (card.magic2Icon == null) card.magic2Icon = FindVSChild(root,"MagicIcon02")?.GetComponent<Image>();
    }

    private void ApplySoloProfileLabels(Player local, Player opponent)
    {
        if (introPanel == null) return;
        if (!soloProfilesBound)
        {
            if (teamCards == null || teamCards.Length != 4) Array.Resize(ref teamCards, 4);
            Transform own = FindVSChild(introPanel.transform, "PlayerProfile");
            Transform enemy = FindVSChild(introPanel.transform, "EnemyProfile");
            BindTeamProfile(ref teamCards[0], own);
            BindTeamProfile(ref teamCards[2], enemy);
            if (introUserInfo != null)
                introUserInfo.SetIntroNameTexts(teamCards[0]?.playerName, teamCards[2]?.playerName);
            soloProfilesBound = own != null && enemy != null;
        }
        ApplyIntroProfileLabels(teamCards[0], PlayerData.FindPerkData(local.Runner, local.ParticipantRef));
        ApplyIntroProfileLabels(teamCards[2], opponent != null
            ? PlayerData.FindPerkData(opponent.Runner, opponent.ParticipantRef) : null);
    }

    private static void ApplyIntroProfileLabels(TeamIntroCard view, PlayerData data)
    {
        if (view == null) return;
        bool ready = data != null && data.Object != null && data.Object.IsValid && data.IsLoadoutInitialized;
        if (view.playerName != null)
        {
            // A renamed object can still carry a title component from an older binding.
            if (view.playerName.TryGetComponent(out PlayerTitleLabel oldTitle)) oldTitle.enabled = false;
            view.playerName.richText = false;
            view.playerName.text = ready ? data.playerName.ToString() : string.Empty;
            view.playerName.enabled = true;
            view.playerName.gameObject.SetActive(true);
        }
        if (view.playerTitle != null)
        {
            var title = view.playerTitle.GetComponent<PlayerTitleLabel>() ?? view.playerTitle.gameObject.AddComponent<PlayerTitleLabel>();
            title.SetPlayer(ready ? data : null);
            title.enabled = true;
            view.playerTitle.enabled = true;
            view.playerTitle.gameObject.SetActive(true);
        }
    }

    private static void SetTeamMagicIcon(Image image, Sprite sprite)
    {
        if (image == null) return;
        image.sprite = sprite;
        image.enabled = sprite != null;
        image.preserveAspect = true;
        image.raycastTarget = false;
    }

    private bool EnsureTeamSnapshot(ref PortraitSnapshot snapshot, Player[] players, int offset, int count, int slot, RawImage display)
    {
        Player first = players[offset], second = players[offset + 1];
        bool sameRoster = snapshot != null && snapshot.TeamMembers != null && snapshot.TeamMembers.Length == count;
        if (sameRoster)
            for (int i = 0; i < count; i++) sameRoster &= snapshot.TeamMembers[i] == players[offset + i];
        if (sameRoster && snapshot.Texture != null && snapshot.Texture.IsCreated()) return true;
        ReleaseSnapshot(ref snapshot);
        PortraitSnapshot companion = null;
        try
        {
            snapshot = new PortraitSnapshot { Player = first, Teammate = second };
            snapshot.TeamMembers = new Player[count];
            Array.Copy(players, offset, snapshot.TeamMembers, 0, count);
            BuildSnapshot(snapshot, slot * 2);
            snapshot.Camera.enabled = false;
            Transform firstVisual = snapshot.Stage.transform.Find("Visual meshes only");
            var visuals = new List<Transform> { firstVisual };
            for (int i = 1; i < count; i++)
            {
                companion = new PortraitSnapshot { Player = players[offset + i] };
                BuildSnapshot(companion, slot * 2 + 1);
                companion.Camera.enabled = false;
                Transform visual = companion.Stage.transform.Find("Visual meshes only");
                visual.SetParent(snapshot.Stage.transform, false);
                visuals.Add(visual);
                snapshot.OwnedMeshes.AddRange(companion.OwnedMeshes);
                snapshot.AnimatedCat |= companion.AnimatedCat;
                companion.OwnedMeshes.Clear();
                ReleaseSnapshot(ref companion);
            }
            for (int i = 0; i < count; i++)
            {
                visuals[i].localRotation = Quaternion.identity;
                visuals[i].localPosition = Vector3.right * ((i - (count - 1) * .5f) * teamCharacterSpacing * .84f);
            }

            Renderer[] renderers = snapshot.Stage.GetComponentsInChildren<Renderer>();
            Bounds bounds = new Bounds();
            bool hasBounds = false;
            foreach (var renderer in renderers)
            {
                if (renderer.GetComponentInParent<CatAvatar>() != null) continue;
                if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            float aspect = display != null && display.rectTransform.rect.height > 0f
                ? Mathf.Clamp(display.rectTransform.rect.width / display.rectTransform.rect.height, .25f, 4f) : 1f;
            Camera camera = snapshot.Camera;
            camera.targetTexture = null;
            snapshot.Texture.Release();
            Destroy(snapshot.Texture);
            int width = Mathf.Clamp(portraitResolution, 128, 2048);
            snapshot.Texture = new RenderTexture(width, Mathf.Clamp(Mathf.RoundToInt(width / aspect), 128, 2048), 24, RenderTextureFormat.ARGB32)
            { name = slot == 0 ? "Own team portrait" : "Enemy team portrait", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            if (!snapshot.Texture.Create()) throw new InvalidOperationException("Could not allocate team portrait.");
            camera.targetTexture = snapshot.Texture;
            camera.aspect = aspect;
            camera.orthographicSize = Mathf.Max(teamPortraitMinimumHalfHeight,
                Mathf.Max(bounds.extents.y, bounds.extents.x / aspect) * teamPortraitPadding);
            float distance = bounds.extents.z + Mathf.Max(1f, headDistance);
            camera.transform.SetPositionAndRotation(bounds.center + Vector3.forward * distance, Quaternion.LookRotation(Vector3.back, Vector3.up));
            camera.farClipPlane = distance + bounds.extents.z + 5f;
            // Put both companions outside the pair, not between the two witches.
            if (count == 2)
                for (int i = 0; i < count; i++)
                    foreach (var catFloat in visuals[i].GetComponentsInChildren<CatIntroFloat>())
                        catFloat.PlaceBeside(camera, renderers, i == 0 ? 1 : -1, CatCompanionTable.Default.vsCharacterGap);
            CatIntroFloat.FrameWholeGroup(snapshot.Stage.transform,camera);
            Light light = snapshot.Stage.GetComponentInChildren<Light>();
            if (light != null)
            {
                light.transform.position = camera.transform.position + Vector3.up;
                light.range = Mathf.Max(6f, bounds.size.magnitude * 2f);
            }
            snapshot.RenderedFrame = -1;
            camera.enabled = true;
            reportedSnapshotFailure = false;
            return true;
        }
        catch (Exception exception)
        {
            ReleaseSnapshot(ref companion);
            ReleaseSnapshot(ref snapshot);
            if (!reportedSnapshotFailure) Debug.LogWarning($"Team portrait is not ready; retrying. {exception.Message}",this);
            reportedSnapshotFailure = true;
            return false;
        }
    }
}
