using Fusion;
using Fusion.Photon.Realtime;
using UnityEngine;

public partial class NetworkGameManager
{
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
