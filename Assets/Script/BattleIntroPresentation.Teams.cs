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
    private bool teamProfilesBound, restoreIntroUserInfo;
    private readonly PortraitSnapshot[] teamSnapshots = new PortraitSnapshot[2];
    private readonly List<PlayerData> teamRoster = new();
    private readonly Player[] teamPlayers = new Player[4];
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
        if (!IsTeamBattle) return;
        foreach (var motion in teamProfileMotions)
            if (motion.source != null && motion.copy != null && motion.copy.gameObject.activeInHierarchy)
                motion.copy.anchoredPosition = motion.copyPosition + motion.source.anchoredPosition - motion.sourcePosition;
    }
    private bool IsTeamBattle => BattleManager.Instance != null && BattleManager.Instance.ExpectedPlayerCount > 2;

    private void PrepareTeamPresentation(BattleStartPhase phase)
    {
        NetworkGameManager network = NetworkGameManager.Instance;
        if (network == null || !network.IsBattleSceneLoaded) return;
        BattleMatchRules.CollectRoster(network.Runner, teamRoster);
        if (!BattleMatchRules.IsFullBalanced(teamRoster, 4) || !teamRoster[0].Object.HasInputAuthority)
        { ReleasePortraits(); return; }
        EnsureTeamIntroUI();
        if (teamProfileTable == null) teamProfileTable = ProfileImageTable.Default;
        for (int i = 0; i < 4; i++)
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
            if (view.playerName != null) { view.playerName.richText = false; view.playerName.text = data.playerName.ToString(); }
            if (view.magicNames != null) { view.magicNames.text = string.Empty; view.magicNames.enabled = false; }
            MagicStatTable table = player.MagicTable != null ? player.MagicTable : MagicStatTable.Default;
            SetTeamMagicIcon(view.magic1Icon, table != null ? table.GetIcon(data.magic1) : null);
            SetTeamMagicIcon(view.magic2Icon, table != null ? table.GetIcon(data.magic2) : null);
        }
        bool ownReady = EnsureTeamSnapshot(ref teamSnapshots[0], teamPlayers[0], teamPlayers[1], 0, localPortrait);
        bool enemyReady = EnsureTeamSnapshot(ref teamSnapshots[1], teamPlayers[2], teamPlayers[3], 1, opponentPortrait);
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
        if (teamCards == null || teamCards.Length != 4) Array.Resize(ref teamCards, 4);
        BindTeamProfile(ref teamCards[0], own);
        BindTeamProfile(ref teamCards[1], teammateProfileRoot != null ? teammateProfileRoot.transform : null);
        BindTeamProfile(ref teamCards[2], enemy);
        BindTeamProfile(ref teamCards[3], secondEnemyProfileRoot != null ? secondEnemyProfileRoot.transform : null);
        teamProfilesBound = own != null && enemy != null && teammateProfileRoot != null && secondEnemyProfileRoot != null;
    }

    private void SetTeamUIPhase(BattleStartPhase phase)
    {
        bool visible = IsTeamBattle && (phase == BattleStartPhase.WaitingForPlayers || phase == BattleStartPhase.Intro);
        if (visible) EnsureTeamIntroUI();
        if (teammateProfileRoot != null) teammateProfileRoot.SetActive(visible);
        if (secondEnemyProfileRoot != null) secondEnemyProfileRoot.SetActive(visible);
        if (!visible && restoreIntroUserInfo)
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
        if (card.playerName == null) card.playerName = FindVSChild(root,"Nickname")?.GetComponent<TMP_Text>();
        if (card.magic1Icon == null) card.magic1Icon = FindVSChild(root,"MagicIcon01")?.GetComponent<Image>();
        if (card.magic2Icon == null) card.magic2Icon = FindVSChild(root,"MagicIcon02")?.GetComponent<Image>();
    }

    private static void SetTeamMagicIcon(Image image, Sprite sprite)
    {
        if (image == null) return;
        image.sprite = sprite;
        image.enabled = sprite != null;
        image.preserveAspect = true;
        image.raycastTarget = false;
    }

    private bool EnsureTeamSnapshot(ref PortraitSnapshot snapshot, Player first, Player second, int slot, RawImage display)
    {
        if (snapshot != null && snapshot.Player == first && snapshot.Teammate == second &&
            snapshot.Texture != null && snapshot.Texture.IsCreated()) return true;
        ReleaseSnapshot(ref snapshot);
        PortraitSnapshot companion = null;
        try
        {
            snapshot = new PortraitSnapshot { Player = first, Teammate = second };
            companion = new PortraitSnapshot { Player = second };
            BuildSnapshot(snapshot, slot * 2);
            BuildSnapshot(companion, slot * 2 + 1);
            snapshot.Camera.enabled = false;
            companion.Camera.enabled = false;
            Transform firstVisual = snapshot.Stage.transform.Find("Visual meshes only");
            Transform secondVisual = companion.Stage.transform.Find("Visual meshes only");
            secondVisual.SetParent(snapshot.Stage.transform, false);
            firstVisual.localRotation = secondVisual.localRotation = Quaternion.identity;
            firstVisual.localPosition = Vector3.left * teamCharacterSpacing * .5f;
            secondVisual.localPosition = Vector3.right * teamCharacterSpacing * .5f;
            snapshot.OwnedMeshes.AddRange(companion.OwnedMeshes);
            snapshot.AnimatedCat |= companion.AnimatedCat;
            companion.OwnedMeshes.Clear(); // Ownership follows the reparented visual, never double-dispose baked meshes.
            ReleaseSnapshot(ref companion);

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
            foreach (var catFloat in firstVisual.GetComponentsInChildren<CatIntroFloat>())
                catFloat.PlaceBeside(camera, renderers, 1, CatCompanionTable.Default.vsCharacterGap);
            foreach (var catFloat in secondVisual.GetComponentsInChildren<CatIntroFloat>())
                catFloat.PlaceBeside(camera, renderers, -1, CatCompanionTable.Default.vsCharacterGap);
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
