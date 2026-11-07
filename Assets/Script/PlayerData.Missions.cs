using System.Collections.Generic;
using Fusion;
using UnityEngine;

public partial class PlayerData
{
    [Networked, Capacity(28)] public NetworkArray<float> MissionCounters => default;
    [Networked] public NetworkString<_32> MissionMatchId { get; private set; }
    [Networked] public NetworkBool MissionCompleted { get; private set; }
    [Networked] public NetworkBool MissionWon { get; private set; }
    private readonly Dictionary<PlayerRef,double> missionContributors = new();
    private bool CanRecordMissions => Object != null && Object.IsValid && Object.HasStateAuthority && BattleManager.Instance != null && BattleManager.Instance.IsGameplayActive && !MissionCompleted && NetworkGameManager.Instance != null &&
        !NetworkGameManager.Instance.IsTraining && !NetworkGameManager.Instance.IsDeathMatch;
    private void ResetMissions()
    {
        for(int i=0;i<28;i++) MissionCounters.Set(i,0);
        MissionMatchId=System.Guid.NewGuid().ToString("N"); MissionCompleted=false; MissionWon=false; missionContributors.Clear();
    }
    public void CompleteMissions(bool won)
    {
        if(Object==null || !Object.IsValid || !Object.HasStateAuthority || IsMatchBot || NetworkGameManager.Instance==null || NetworkGameManager.Instance.IsTraining || NetworkGameManager.Instance.IsDeathMatch)return;
        MissionWon=won; MissionCompleted=true;
    }
    public void RecordMission(MissionObjective objective,float amount=1)
    {
        if(CanRecordMissions && !IsMatchBot && amount>0 && !float.IsNaN(amount) && !float.IsInfinity(amount))
            MissionCounters.Set((int)objective,Mathf.Min(100000000, MissionCounters[(int)objective]+amount));
    }
    public void RecordMissionMagicHit(MagicType magic)
    {
        int index=(int)magic;
        if(CanRecordMissions && !IsMatchBot && index>0 && index<18 && IsTitleAttackMagic(magic))
            MissionCounters.Set(10+index,MissionCounters[10+index]+1);
    }
    private void RecordMissionDamage(PlayerData source,float amount)
    {
        if(!CanRecordMissions || source==null)return;
        source.RecordMission(MissionObjective.DamageDealt,amount);
        missionContributors[source.ParticipantRef]=Runner.SimulationTime;
    }
    private void RecordMissionDeath(PlayerData killer)
    {
        if(!CanRecordMissions)return;
        if(IsEnemyForStats(killer))killer.RecordMission(MissionObjective.KillOrAssist);
        foreach(var entry in missionContributors)
        {
            if(killer!=null && entry.Key==killer.ParticipantRef || Runner.SimulationTime-entry.Value>10)continue;
            var assist=FindPerkData(Runner,entry.Key);
            if(IsEnemyForStats(assist))assist.RecordMission(MissionObjective.KillOrAssist);
        }
        missionContributors.Clear();
    }
}
