using Fusion;
using UnityEngine;

public partial class PlayerData
{
    [Networked] public int EquippedTitleId { get; private set; }
    [Networked] public NetworkString<_32> TitleMatchId { get; private set; }
    [Networked, Capacity(13)] public NetworkArray<int> TitleMagicKills => default;
    [Networked] public int TitleEmotes { get; private set; }
    [Networked] public int TitleHealthPacks { get; private set; }
    [Networked] public int TitleAltitudeDeaths { get; private set; }
    [Networked] public int TitleBlackKills { get; private set; }
    [Networked] public NetworkBool TitleDoubleHit { get; private set; }
    [Networked] public NetworkBool TitleMatchCompleted { get; private set; }
    [Networked] public NetworkBool TitleBotMatch { get; private set; }
    private double previousTitleHit = double.NegativeInfinity;
    private float nextTitleSync;
    private bool titleFinalSaved;
    private bool CanRecordTitles => CanRecordBattleStats && !IsMatchBot && !BattleManager.Instance.IsTraining;

    public void SetEquippedTitle(int id)
    {
        if(Object!=null && Object.IsValid && Object.HasInputAuthority && TitleProgression.Owns(id)) RPC_SetEquippedTitle(id);
    }
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_SetEquippedTitle(int id) { if(TitleCatalog.Find(id)!=null) EquippedTitleId=id; }

    private void ResetTitleMatch()
    {
        bool eligible=BattleManager.Instance!=null && !BattleManager.Instance.IsTraining && !IsMatchBot;
        TitleMatchId=eligible?System.Guid.NewGuid().ToString("N"):string.Empty;
        TitleEmotes=TitleHealthPacks=TitleAltitudeDeaths=TitleBlackKills=0;
        TitleDoubleHit=TitleMatchCompleted=false;
        TitleBotMatch=NetworkGameManager.Instance!=null && BattleMatchRules.HasBots(NetworkGameManager.Instance.CurrentMatchMode);
        for(int i=0;i<13;i++)TitleMagicKills.Set(i,0);
        previousTitleHit=double.NegativeInfinity;
        titleFinalSaved=false;
    }
    public void CompleteTitleMatch()
    {
        if(Object!=null && Object.IsValid && Object.HasStateAuthority && !string.IsNullOrEmpty(TitleMatchId.ToString()))TitleMatchCompleted=true;
    }
    public void RecordTitleEmote() { if(CanRecordTitles)TitleEmotes++; }
    public void RecordTitleHealthPack() { if(CanRecordTitles)TitleHealthPacks++; }
    private void RecordTitleHit(PlayerData attacker)
    {
        if(!CanRecordTitles || !IsEnemyForStats(attacker))return;
        double now=Runner.SimulationTime;
        if(now-previousTitleHit<=1d)TitleDoubleHit=true;
        previousTitleHit=now;
    }
    private void RecordTitleDeath(PlayerData killer,MagicType magic,bool altitude)
    {
        if(CanRecordTitles && altitude)TitleAltitudeDeaths++;
        if(!IsEnemyForStats(killer) || !killer.CanRecordTitles)return;
        int index=(int)magic;
        if(index>0 && index<13)killer.TitleMagicKills.Set(index,killer.TitleMagicKills[index]+1);
        Color color=killer.clothColor;
        bool black=Mathf.Max(color.r,Mathf.Max(color.g,color.b))<=0.15f;
        bool dark=killer.magic1==MagicType.Dark || killer.magic2==MagicType.Dark || killer.magic1==MagicType.Curse || killer.magic2==MagicType.Curse;
        if(black && dark)killer.TitleBlackKills++;
    }
    public TitleMatchSnapshot GetTitleSnapshot()
    {
        var s=new TitleMatchSnapshot {id=TitleMatchId.ToString(),completed=TitleMatchCompleted,botMatch=TitleBotMatch,
            deaths=BattleDeaths,underwater=BattleUnderwaterSeconds,emotes=TitleEmotes,healthPacks=TitleHealthPacks,
            altitudeDeaths=TitleAltitudeDeaths,blackKills=TitleBlackKills,doubleHit=TitleDoubleHit};
        for(int i=0;i<13;i++)s.magicKills[i]=TitleMagicKills[i];
        return s;
    }
    public override void Render()
    {
        if(Object==null || !Object.IsValid || !Object.HasInputAuthority)return;
        if(TitleMatchCompleted && !titleFinalSaved || Time.unscaledTime>=nextTitleSync)
        {
            nextTitleSync=Time.unscaledTime+1f;
            TitleProgression.Observe(this);
            titleFinalSaved=TitleMatchCompleted && TitleProgression.Ready;
        }
    }
}
