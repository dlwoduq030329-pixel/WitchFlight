using System.Collections.Generic;
using Fusion;
using UnityEngine;

public partial class PlayerData
{
    [Networked] public int TitleChallengeFlags {get;private set;}
    private int titleAttackCasts,titleSuccessfulCasts,titleBlocks,titleCatKills,titleSummons,titleRound;
    private bool titleUnblockedHit,titleChangedMagic,titleEverySwap,titlePeaceful,titleEvasive,titleAttackedCat;
    private bool titleLatePickup,titleFinalLateFlag,titleFinalLowFlag;
    private MagicType titlePreviousFirst,titlePreviousSecond;
    private float titleHealing;
    private double titleCrashOlder=double.NegativeInfinity,titleCrashLast=double.NegativeInfinity;
    private readonly HashSet<int> titleHitCasts=new();
    private bool CanTrackTitleMatch=>Object!=null&&Object.IsValid&&Object.HasStateAuthority&&
        !IsMatchBot&&BattleManager.Instance!=null&&!BattleManager.Instance.IsTraining&&!TitleMatchCompleted&&
        !string.IsNullOrEmpty(TitleMatchId.ToString());

    public static bool IsTitleAttackMagic(MagicType magic)=>magic!=MagicType.None&&magic!=MagicType.Healing&&magic!=MagicType.Flare&&magic!=MagicType.Smoke&&magic!=MagicType.TimeStop&&magic!=MagicType.MeowFestival;
    private void ResetTitleChallenges()
    {
        TitleChallengeFlags=0;titleAttackCasts=titleSuccessfulCasts=titleBlocks=titleCatKills=titleSummons=titleRound=0;
        titleUnblockedHit=titleChangedMagic=titleAttackedCat=titleLatePickup=titleFinalLateFlag=titleFinalLowFlag=false;
        titleEverySwap=titlePeaceful=titleEvasive=true;titleHealing=0;titleHitCasts.Clear();
        titleCrashOlder=titleCrashLast=double.NegativeInfinity;
    }
    public int RecordTitleCast(MagicType magic,bool targetsCat)
    {
        if(!CanRecordTitles||!IsTitleAttackMagic(magic))return 0;
        if(targetsCat)titleAttackedCat=true;
        return ++titleAttackCasts;
    }
    public void RecordTitleCastHit(int cast)
    {
        if(CanRecordTitles&&cast>0&&cast<=titleAttackCasts&&titleHitCasts.Add(cast))titleSuccessfulCasts++;
    }
    public void RecordTitleBlock(PlayerRef source)
    {if(CanRecordTitles&&IsEnemyForStats(FindPerkData(Runner,source)))titleBlocks++;}
    public void RecordTitleUnblockedHit(PlayerRef source)
    {if(CanRecordTitles&&IsEnemyForStats(FindPerkData(Runner,source)))titleUnblockedHit=true;}
    public void RecordTitleHealing(float actual)
    {if(CanRecordTitles&&actual>0&&!float.IsNaN(actual)&&!float.IsInfinity(actual))titleHealing=Mathf.Min(100000000,titleHealing+actual);}
    public void RecordTitleLoadoutChange(MagicType first,MagicType second)
    {if(CanTrackTitleMatch&&(magic1!=first||magic2!=second))titleChangedMagic=true;}
    public void RecordTitleRoundStart(int round)
    {
        if(!CanTrackTitleMatch||round<=titleRound)return;
        if(titleRound>0&&magic1==titlePreviousFirst&&magic2==titlePreviousSecond)titleEverySwap=false;
        titlePreviousFirst=magic1;titlePreviousSecond=magic2;titleRound=round;
        titlePeaceful&=magic1!=MagicType.None&&magic2!=MagicType.None&&!IsTitleAttackMagic(magic1)&&!IsTitleAttackMagic(magic2);
        titleEvasive&=(magic1==MagicType.Flare&&magic2==MagicType.Smoke)||(magic1==MagicType.Smoke&&magic2==MagicType.Flare);
        titleLatePickup=false;titleFinalLateFlag=titleFinalLowFlag=false;
    }
    public void RecordTitleFlagPickup(float seconds,bool overtime)
    {if(CanRecordTitles)titleLatePickup=!overtime&&seconds>0&&seconds<=5;}
    public void RecordTitleFlagRoundEnd(bool carriedByMe,int winningTeam,float hp)
    {
        if(!CanTrackTitleMatch)return;
        titleFinalLateFlag=carriedByMe&&teamIndex==winningTeam&&titleLatePickup;
        titleFinalLowFlag=carriedByMe&&teamIndex==winningTeam&&hp>0&&hp<=10;
    }
    public void RecordTitleSummonSpawn(){if(CanRecordTitles)titleSummons++;}
    public void RecordTitleCatTarget(){if(CanRecordTitles)titleAttackedCat=true;}
    public void RecordTitleSummonHit(int cast,bool killed)
    {
        if(!CanRecordTitles)return;
        titleAttackedCat=true;RecordTitleCastHit(cast);if(killed)titleCatKills++;
    }
    public void RecordTitleObstacleContact()
    {
        if(!CanRecordTitles)return;
        double now=Runner.SimulationTime;
        if(now-titleCrashOlder<=5)TitleChallengeFlags|=(int)TitleChallenge.Bulldozer;
        titleCrashOlder=titleCrashLast;titleCrashLast=now;
    }
    private void CompleteTitleChallenges(int winner)
    {
        if(!CanTrackTitleMatch||titleRound==0)return;
        var flags=(TitleChallenge)TitleChallengeFlags;
        if(titleAttackCasts>0&&titleAttackCasts==titleSuccessfulCasts)flags|=TitleChallenge.PerfectAttack;
        if(titleBlocks>0&&!titleUnblockedHit)flags|=TitleChallenge.PerfectDefense;
        if(!titleChangedMagic)flags|=TitleChallenge.NoSwap;
        if(titleRound>1&&titleEverySwap)flags|=TitleChallenge.EverySwap;
        if(winner==teamIndex&&titleFinalLateFlag)flags|=TitleChallenge.LastSecond;
        if(winner==teamIndex&&titleFinalLowFlag)flags|=TitleChallenge.Thriller;
        if(titleCatKills>=10)flags|=TitleChallenge.CatSlayer;
        if(titleSummons>=10&&!titleAttackedCat)flags|=TitleChallenge.Polite;
        if(titlePeaceful)flags|=TitleChallenge.Pacifist;
        if(titleEvasive)flags|=TitleChallenge.Evasive;
        if(BattleDamageDealt<=0&&titleHealing>=10000)flags|=TitleChallenge.Saint;
        TitleChallengeFlags=(int)flags;
    }
}
