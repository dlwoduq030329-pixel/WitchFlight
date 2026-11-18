using Fusion;
using Fusion.Photon.Realtime;
using UnityEngine;

public partial class NetworkGameManager
{
    private NetworkRunner deathMatchEntryRunner;
    private bool deathMatchEntryComplete;
    private float nextDeathMatchEntryAck;
    private bool deathMatchPrefabReady;

    private void TickDeathMatchEntry()
    {
        if (!IsDeathMatch || _runner == null || !_runner.IsRunning || stopInProgress || cancelRequested) return;
        if (deathMatchEntryRunner != _runner)
        {
            deathMatchEntryRunner = _runner;
            deathMatchEntryComplete = false;
            nextDeathMatchEntryAck = 0;
            deathMatchPrefabReady = false;
        }
        if (deathMatchEntryComplete) return;
        BattleLoadingUI.Show("로딩중");
        if (IsMigratingDeathMatch || !IsBattleSceneLoaded || !BattleLoadingUI.AssetsReady ||
            BattleManager.Instance == null || DeathMatchState.Instance == null ||
            DeathMatchState.Instance.Runner != _runner ||
            DeathMatchState.Instance.Object == null || !DeathMatchState.Instance.Object.IsValid) return;
        if (!deathMatchPrefabReady)
        {
            var prefabs = _runner.Config.PrefabTable;
            if (!playerPrefab.IsValid ||
                prefabs.Load(prefabs.GetId((NetworkObjectGuid)playerPrefab), isSynchronous: false) == null) return;
            deathMatchPrefabReady = true;
        }
        // Require participant data from the current session snapshot, not merely a scene-load callback.
        foreach (var participant in _runner.ActivePlayers)
        {
            var data = PlayerData.FindPerkData(_runner, participant);
            if (data == null || data.Object == null || !data.Object.IsValid || !data.IsLoadoutInitialized) return;
        }
        var local = PlayerData.FindPerkData(_runner, _runner.LocalPlayer);
        if (local == null || !local.Object.HasInputAuthority) return;
        BattleLoadingUI.SetProgress(.85f);
        if (!local.DeathMatchEntryReady && Time.unscaledTime >= nextDeathMatchEntryAck)
        {
            nextDeathMatchEntryAck = Time.unscaledTime + 1f;
            local.RPC_ConfirmDeathMatchEntry();
        }
        var avatar = Player.LocalPlayer;
        if (avatar == null || avatar.Runner != _runner || !avatar.IsPresentationReady) return;
        deathMatchEntryComplete = true;
        BattleLoadingUI.SetProgress(1f);
        BattleLoadingUI.Hide();
    }

    [SerializeField] private NetworkPrefabRef deathMatchStatePrefab;
    public void SpawnDeathMatchState()
    {
        if (IsDeathMatch && IsRoomHost && DeathMatchState.Instance == null) _runner.Spawn(deathMatchStatePrefab);
    }

    private int deathMatchJoinSequence;

    public const int DeathMatchCapacity = 20;
    private const string DeathMatchSession = "WitchFlight-DeathMatch-v1";
    [Header("Deathmatch shared session")]
    [SerializeField] private string deathMatchRegion = "kr";

    public void StartDeathMatch()
    {
        // Scene buttons can reference the new scene's duplicate manager after returning to Main.
        if (instance != null && instance != this) { instance.StartDeathMatch(); return; }
        if (!IsMatching) StartGame(MatchRequest.DeathMatch, DeathMatchSession);
    }

    public void JoinDeathMatch(System.Action<string> onFailed)
    {
        if (IsMatching) { onFailed?.Invoke("이미 접속 중입니다."); return; }
        StartGame(MatchRequest.DeathMatch, DeathMatchSession, null, onFailed);
    }

    private FusionAppSettings DeathMatchPhotonSettings()
    {
        var settings = PhotonAppSettings.Global.AppSettings.GetCopy();
        // A session name alone is NOT global: Photon rooms are scoped by region/version.
        settings.FixedRegion = string.IsNullOrWhiteSpace(deathMatchRegion) ? "kr" : deathMatchRegion.Trim();
        return settings;
    }

    private int NextDeathMatchTeam()
    {
        // Each participant is their own team, so existing hostile/friendly spell checks work.
        for (int team = 1; team <= DeathMatchCapacity; team++)
            {
                bool reserved = assignedTeamIndexes.ContainsValue(team);
                if (!reserved)
                    foreach (var obj in migrationData.Values)
                        if (obj != null && obj.IsValid && obj.GetComponent<PlayerData>().teamIndex == team) { reserved = true; break; }
                if (!reserved) return team;
            }
        return 0;
    }
}
