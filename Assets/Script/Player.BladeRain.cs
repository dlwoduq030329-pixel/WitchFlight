using Fusion;
using UnityEngine;

public partial class Player
{
    public const int RainTargetCapacity = 4;
    [Networked] private bool RainCharging { get; set; }
    [Networked] private bool RainFiring { get; set; }
    [Networked] public float RainManaSpent { get; private set; }
    [Networked] public int RainTargetCount { get; private set; }
    [Networked] public int RainTargetRevision { get; private set; }
    [Networked, Capacity(RainTargetCapacity)] private NetworkArray<NetworkId> RainTargets => default;
    [Networked] private int RainVolleyCircles { get; set; }
    [Networked] public int RainVolleyStep { get; private set; }
    [Networked] private float RainCircleMana { get; set; }
    [Networked] private int RainCircleLimit { get; set; }
    private MagicStatEntry rainStats;
    private TickTimer rainNextWave;
    private bool rainNeedsRelease;
    private BladeRainVisual rainVisual;
    public bool IsRainCharging => networkSpawned && RainCharging;
    public int RainCircleCount => !networkSpawned ? 0 : RainFiring ? RainVolleyCircles : RainCharging
        ? Mathf.Clamp(Mathf.FloorToInt((RainManaSpent + .0001f) / Mathf.Max(1f, RainCircleMana)), 0, Mathf.Clamp(RainCircleLimit,2,16)) : 0;
    public NetworkId GetRainTarget(int index) => networkSpawned && index >= 0 && index < RainTargetCount ? RainTargets[index] : default;
    public bool HasRainTarget(NetworkId id)
    {
        for (int i=0;i<RainTargetCount;i++) if (RainTargets[i].Equals(id)) return true;
        return false;
    }
    private void ProcessBladeRain(NetworkInputData input, bool held, bool released)
    {
        if (!held) rainNeedsRelease = false;
        if (rainNeedsRelease) return;
        if (!RainCharging)
        {
            if (!held || TimerIsActive(MagicCooldowns[(int)MagicType.BladeRain])) return;
            SetInputLockTarget(input.lockTarget); UpdateLockCharge();
            if (!IsFullyLocked) return;
            rainStats = SelectedMagicStats;
            if (NowAp < rainStats.apCost) { NotifyManaFailure(); rainNeedsRelease=true; ClearLockTargetInternal(false); return; }
            RainCircleMana=rainStats.rainManaPerCircle; RainCircleLimit=rainStats.rainMaxCircles;
            RainCharging = true; RainManaSpent = 0; RainTargetCount = 1; RainVolleyStep = 0;
            RainTargets.Set(0,LockTargetId); RainTargetRevision++;
        }
        else PruneRainTargets(input);
        if (RainTargetCount == 0) { CancelBladeRain(); return; }
        if (released) { FireBladeRain(false); return; }
        if (!held) return;
        float reserve = Mathf.Max(0f,rainStats.apCost);
        float maximum = Mathf.Clamp(rainStats.rainMaxCircles,2,16) * Mathf.Max(1f,rainStats.rainManaPerCircle);
        float charge = Mathf.Min(Mathf.Max(0f,rainStats.rainManaPerSecond)*Runner.DeltaTime,
            Mathf.Max(0f,NowAp-reserve), Mathf.Max(0f,maximum-reserve-RainManaSpent));
        NowAp -= charge; RainManaSpent += charge;
        int slots = Mathf.Min(RainTargetCapacity,1+Mathf.FloorToInt((RainManaSpent+.0001f)/Mathf.Max(1f,rainStats.rainManaPerTarget)));
        if (RainTargetCount < slots && !HasRainTarget(input.rainCandidate) &&
            TryGetMagicTarget(input.rainCandidate,out _,out _,out _))
        {
            RainTargets.Set(RainTargetCount++,input.rainCandidate); RainTargetRevision++;
            RPC_PresentLockComplete(MagicType.BladeRain);
        }
        LockTargetId = RainTargets[0]; LockProgress = 1; IsFullyLocked = true;
        if (NowAp <= reserve+.0001f) FireBladeRain(true);
        else if (RainManaSpent+reserve >= maximum-.0001f) FireBladeRain(false);
    }
    private void PruneRainTargets(NetworkInputData input)
    {
        bool matching = input.rainTargetRevision == RainTargetRevision;
        bool changed = false;
        for (int i=RainTargetCount-1;i>=0;i--)
        {
            NetworkId id=RainTargets[i];
            bool visible = !matching || id.Equals(input.rainVisible0) || id.Equals(input.rainVisible1) ||
                id.Equals(input.rainVisible2) || id.Equals(input.rainVisible3);
            if (visible && TryGetMagicTarget(id,out _,out _,out _)) continue;
            for(int j=i;j<RainTargetCount-1;j++) RainTargets.Set(j,RainTargets[j+1]);
            RainTargets.Set(--RainTargetCount,default); changed=true;
        }
        if(changed) RainTargetRevision++;
    }
    private void FireBladeRain(bool lowMana)
    {
        if (!RainCharging || RainTargetCount <= 0 || !magicProjectilePrefab.IsValid) { CancelBladeRain(); return; }
        // Preserve a real-cost damage budget even if another action drained the reserved mana.
        float cost=Mathf.Min(NowAp,Mathf.Max(0f,rainStats.apCost));
        NowAp-=cost; RainManaSpent+=cost;
        if(lowMana) NotifyManaFailure();
        RainCharging=false; rainNeedsRelease=true;
        ClearLockTargetInternal(false);
        if(RainManaSpent<=0f) { CancelBladeRain(); return; }
        RainVolleyCircles=Mathf.Clamp(Mathf.FloorToInt((RainManaSpent+.0001f)/Mathf.Max(1f,rainStats.rainManaPerCircle)),1,16);
        RainFiring=true; RainVolleyStep=0;
        MagicCooldowns.Set((int)MagicType.BladeRain,TickTimer.CreateFromSeconds(Runner,Mathf.Max(0,rainStats.cooldownSeconds)));
        LastCastMagic=MagicType.BladeRain; CastSequence++;
        rainStats.perkCast=CastSequence; rainStats.perkAttacker=ParticipantRef;
        rainStats.titleCastId=PlayerData.FindPerkData(Runner,ParticipantRef)?.RecordTitleCast(MagicType.BladeRain,false)??0;
        rainNextWave=TickTimer.CreateFromSeconds(Runner,.12f);
        RPC_PresentCast(MagicType.BladeRain,MagicCastPosition,MagicCastPosition,0);
        TickBladeRainVolley();
    }
    private void TickBladeRainVolley()
    {
        if(!Object.HasStateAuthority || !RainFiring) return;
        if(!IsAlive || IsHitStunned || BattleManager.Instance==null || !BattleManager.Instance.IsGameplayActive)
        { CancelBladeRain(); return; }
        if(!rainNextWave.ExpiredOrNotRunning(Runner)) return;
        if(RainVolleyStep>=10) { CancelBladeRain(); return; }
        for(int circle=0;circle<RainVolleyCircles;circle++)
        {
            int targetIndex=circle%RainTargetCount;
            NetworkId id=RainTargets[targetIndex];
            if(!Runner.TryFindObject(id,out var obj) || obj==null || !obj.IsValid) continue;
            Player victim=obj.GetComponent<Player>(); MagicProjectile summon=obj.GetComponent<MagicProjectile>();
            if(victim!=null ? !victim.IsTargetableBy(this) : summon==null || !summon.IsSummonEnemyOf(this)) continue;
            int assigned=RainVolleyCircles/RainTargetCount+(targetIndex<RainVolleyCircles%RainTargetCount?1:0);
            MagicStatEntry shotStats=rainStats;
            shotStats.damage=(RainManaSpent*.5f/RainTargetCount)/assigned/10f;
            Vector3 origin=RainCirclePosition(circle);
            Vector3 aim=(victim!=null?victim.LockAimPoint:summon.transform.position)-origin;
            if (aim.sqrMagnitude < .001f) aim = transform.forward;
            var shot=Runner.Spawn(magicProjectilePrefab,origin,Quaternion.LookRotation(aim.normalized),ParticipantRef);
            if(shot!=null) shot.GetComponent<MagicProjectile>().Initialize(this,victim,shotStats,aim.normalized,0,victim==null?id:default);
        }
        RainVolleyStep++;
        rainNextWave=TickTimer.CreateFromSeconds(Runner,Mathf.Max(.01f,rainStats.rainBladeInterval));
    }
    public Vector3 RainCirclePosition(int index)
    {
        float spread=1f+index/4*.65f;
        Vector3 local=(index%4) switch
        {
            0=>new Vector3(-1.2f,.4f,-1f), 1=>new Vector3(1.2f,.4f,-1f),
            2=>new Vector3(-.85f,1.6f,-1.2f), _=>new Vector3(.85f,-.5f,-1.2f)
        };
        return LockAimPoint+transform.rotation*(local*spread);
    }
    private void CancelBladeRain()
    {
        if(Object==null || !Object.IsValid || !Object.HasStateAuthority) return;
        if (!RainCharging && !RainFiring && RainTargetCount==0 && RainManaSpent==0) return;
        ClearLockTargetInternal(false);
        RainCharging=RainFiring=false; RainTargetCount=0; RainManaSpent=0; RainVolleyCircles=0;
        RainTargetRevision++; rainNeedsRelease=previousButtons.IsSet(PlayerInputButton.Lock);
    }
    private void RenderBladeRain()
    {
        if(RainCircleCount>0 && rainVisual==null)
        {
            rainVisual=new GameObject("Blade Rain circles").AddComponent<BladeRainVisual>();
            rainVisual.transform.SetParent(transform,false); rainVisual.Initialize(this);
        }
        if(rainVisual!=null) rainVisual.Refresh();
    }
}
