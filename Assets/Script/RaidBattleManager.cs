using Fusion;
using UnityEngine;

public sealed class RaidBattleManager : BattleManager
{
    [SerializeField] private GiantMeowRaidDirector director;
    [SerializeField] private NetworkPrefabRef raidPlayerPrefab;
    public override bool UsesVersusPresentation => false;
    public override bool UsesStandardResults => false;
    public override bool RecordsBattleStats => false;
    public override string ObjectiveLabel => "거대 먀오녀를 처치하세요";
    public override string ModeLabel => "거대 먀오녀 토벌";
    public override BattleStartPhase Phase => director.Phase;
    public override float PhaseRemainingSeconds => director.CountdownRemaining;
    public override bool IsBattleEnded => director.Finished;
    public override int WinningTeam => director.Cleared ? 1 : 0;
    protected override void SpawnFlag() => director.SpawnBoss(runner, playerDatas);
    protected override void SpawnBattlePlayer(PlayerRef participant, PlayerData data, NetworkPrefabRef prefab, bool isRespawn = false)
    {
        if (isRespawn || data == null || !data.IsLoadoutInitialized || data.IsMatchBot) return;
        director.SpawnPose(participant, playerDatas, out var position, out var rotation);
        var obj = runner.Spawn(raidPlayerPrefab, position, rotation, participant);
        if (obj == null) return;
        spawnedPlayers[participant] = obj;
        runner.SetPlayerObject(participant, obj);
        obj.GetComponent<RaidPlayer>().InitPlayer(data, isRespawn);
    }
}
