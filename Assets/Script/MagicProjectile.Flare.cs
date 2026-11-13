using System.Collections.Generic;
using Fusion;
using UnityEngine;

// A pooled field emits discrete decoys. Only burst poses are replicated, never per-particle objects.
public partial class MagicProjectile
{
    private static readonly HashSet<MagicProjectile> flareFields = new();
    [Networked] private float FlareSpeed { get; set; }
    [Networked] private float FlareParticleLifetime { get; set; }
    [Networked] private bool FlareEmissionStopped { get; set; }
    [Networked] private float FlareSpread { get; set; }
    [Networked] private float FlareAttractionRadius { get; set; }
    [Networked] private bool FlareProtectsAllies { get; set; }
    [Networked] private float FlareDuration { get; set; }
    [Networked] private float FlareBurstInterval { get; set; }
    [Networked] private int FlareBurstSequence { get; set; }
    [Networked] private Vector3 FlareBurstPosition { get; set; }
    [Networked] private Quaternion FlareBurstRotation { get; set; }
    [Networked] private float FlareBurstAt { get; set; }
    private const int FlareHistoryCapacity = 32;
    private struct FlareSample { public Vector3 position; public Quaternion rotation; public float at; public int sequence; }
    private struct FlareSegment { public Vector3 start, end, origin; public int sequence, branch; }
    private FlareSample[] flareHistory;
    private FlareSegment[] flareSegments;
    private int flareHistoryHead, flareHistoryCount, flareSegmentCount, lastFlareSequence;
    private float nextFlareSample;
    private Player flareCaster;
    private FlareFieldVisual flareVisual;
    private MagicProjectile decoyField;
    private int decoySequence, decoyBranch;

    public void InitializeFlareField(Player owner, MagicStatEntry entry)
    {
        Initialize(owner, null, entry, -owner.transform.forward);
        ConstructKind = MagicConstructKind.FlareField;
        Settled = true;
        ConstructRadius = Mathf.Max(.1f, entry.flareInterceptRadius);
        FlareSpeed = Mathf.Max(0f, entry.flareVfxSpeed);
        FlareParticleLifetime = 1.1f + Mathf.Clamp(entry.flareParticleExtraLifetime, 0f, 5f);
        FlareEmissionStopped = false;
        flareCaster = owner;
        EnsureFlareHistory();
        flareHistoryHead = flareHistoryCount = flareSegmentCount = lastFlareSequence = 0;
        FlareBurstSequence = 0;
        nextFlareSample = 0f;
        FlareSpread = Mathf.Clamp(entry.flareSpreadAngle, 0f, 80f);
        FlareProtectsAllies = entry.perkFirst?.id == PerkId.FlarePulse;
        FlareAttractionRadius = Mathf.Max(0f, entry.flareAttractionRadius);
        if (FlareProtectsAllies) FlareAttractionRadius = Mathf.Max(FlareAttractionRadius, entry.perkFirst.amount);
        FlareBurstInterval = Mathf.Max(.25f, entry.flareBurstInterval);
        FlareDuration = Mathf.Max(.1f, entry.effectDuration);
        Lifetime = TickTimer.CreateFromSeconds(Runner, FlareDuration + FlareParticleLifetime + FlareFieldVisual.SmokeLifetime);
        flareFields.Add(this);
    }
    private void EnsureFlareHistory()
    {
        flareHistory ??= new FlareSample[FlareHistoryCapacity];
        flareSegments ??= new FlareSegment[FlareHistoryCapacity * 3];
    }
    private bool IsLiveFlare(NetworkRunner runner) => ConstructKind == MagicConstructKind.FlareField &&
        Runner == runner && Object != null && Object.IsValid && !Finished && !Lifetime.ExpiredOrNotRunning(Runner);
    private float FlareElapsed => FlareDuration + FlareParticleLifetime + FlareFieldVisual.SmokeLifetime - (Lifetime.RemainingTime(Runner) ?? 0f);
    private bool FlareEmitting => !FlareEmissionStopped && FlareElapsed < FlareDuration;
    private void FollowFlareCaster()
    {
        if (flareCaster == null && Runner.TryFindObject(ShooterId, out var obj) && obj != null)
            flareCaster = obj.GetComponent<Player>();
        if (flareCaster == null || flareCaster.Object == null || !flareCaster.Object.IsValid || !flareCaster.IsAlive) return;
        Vector3 rear = -flareCaster.transform.forward;
        transform.SetPositionAndRotation(flareCaster.LockAimPoint + rear * Mathf.Max(0f, PresentationStats.flareVfxBackOffset), Quaternion.LookRotation(rear));
    }
    private void RecordFlareBurst(Vector3 position, Quaternion rotation, float at, int sequence)
    {
        if (sequence <= lastFlareSequence) return;
        EnsureFlareHistory();
        flareHistory[flareHistoryHead] = new FlareSample { position=position,rotation=rotation,at=at,sequence=sequence };
        flareHistoryHead=(flareHistoryHead+1)%FlareHistoryCapacity;
        flareHistoryCount=Mathf.Min(flareHistoryCount+1,FlareHistoryCapacity);
        lastFlareSequence=sequence;
    }
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_FlareBurst(Vector3 position, Quaternion rotation, float at, int sequence)
    {
        if (!Object.HasStateAuthority) RecordFlareBurst(position,rotation,at,sequence);
    }
    private void TickFlareField()
    {
        if (Lifetime.Expired(Runner)) { Runner.Despawn(Object); return; }
        float now=FlareElapsed;
        if (FlareEmitting)
        {
            FollowFlareCaster();
            if (flareCaster == null || flareCaster.Object == null || !flareCaster.Object.IsValid || !flareCaster.IsAlive) FlareEmissionStopped=true;
            else if (now >= nextFlareSample)
            {
                FlareBurstPosition=transform.position; FlareBurstRotation=transform.rotation; FlareBurstAt=now;
                FlareBurstSequence++;
                RecordFlareBurst(FlareBurstPosition,FlareBurstRotation,now,FlareBurstSequence);
                RPC_FlareBurst(FlareBurstPosition,FlareBurstRotation,now,FlareBurstSequence);
                nextFlareSample=now+FlareBurstInterval;
            }
        }
        flareSegmentCount=0;
        for(int i=0;i<flareHistoryCount;i++)
        {
            var sample=flareHistory[(flareHistoryHead-1-i+FlareHistoryCapacity)%FlareHistoryCapacity];
            float age=now-sample.at;
            if(age>FlareParticleLifetime) break;
            for(int branch=0;branch<3;branch++)
            {
                Vector3 heading=sample.rotation*Quaternion.Euler(0,(branch-1)*FlareSpread,0)*Vector3.forward;
                flareSegments[flareSegmentCount++]=new FlareSegment {
                    start=FlareTrailPoint(sample.position,heading,FlareSpeed,Mathf.Max(0,age-Runner.DeltaTime)),
                    end=FlareTrailPoint(sample.position,heading,FlareSpeed,age),origin=sample.position,sequence=sample.sequence,branch=branch };
            }
        }
    }
    // Decelerate horizontally, then drift down. Shared by authority collision and rendered cores.
    internal static Vector3 FlareTrailPoint(Vector3 origin, Vector3 heading, float speed, float age)
    {
        age=Mathf.Max(0,age);
        return origin+heading*(Mathf.Max(0,speed)*(1f-Mathf.Exp(-.65f*age))/.65f)+Vector3.down*(.8f*age*age);
    }
    internal static bool CanLureFlare(bool guided, bool targetsOwner, bool targetsAlly, bool telekinesis)
        => guided && (targetsOwner || (telekinesis && targetsAlly));

    private void ApplyFlareAttraction()
    {
        if (IsConstruct || IsMine || Finished || FlightExhausted || decoyField != null || !stats.requiresTarget) return;
        var victim=FindTarget();
        if(victim==null) return;
        MagicProjectile nearest=null;int index=-1;float closest=float.PositiveInfinity;
        foreach(var field in flareFields)
        {
            if(field==null || !field.IsLiveFlare(Runner) || field.ShooterTeam==ShooterTeam ||
                !CanLureFlare(stats.requiresTarget,TargetId==field.ShooterId,victim.TeamIndex==field.ShooterTeam,field.FlareProtectsAllies))continue;
            for(int i=0;i<field.flareSegmentCount;i++)
            {
                var head=field.flareSegments[i];
                float sqr=(head.end-transform.position).sqrMagnitude;
                if(sqr>=closest || sqr>field.FlareAttractionRadius*field.FlareAttractionRadius || !HasSummonSight(transform.position,head.end))continue;
                nearest=field;index=i;closest=sqr;
            }
        }
        if(nearest==null)return;
        decoyField=nearest;decoySequence=nearest.flareSegments[index].sequence;decoyBranch=nearest.flareSegments[index].branch;
        TargetId=nearest.Object.Id; // Threat UI no longer treats this as aimed at the original player.
        sweptTarget=null;
    }
    private bool TryGetFlareAim(out Vector3 point)
    {
        point=default;
        if(decoyField==null)return false;
        if(decoyField.IsLiveFlare(Runner))
            for(int i=0;i<decoyField.flareSegmentCount;i++)
            {
                var head=decoyField.flareSegments[i];
                if(head.sequence!=decoySequence || head.branch!=decoyBranch)continue;
                point=head.end;return true;
            }
        decoyField=null;TargetId=default; // Spent decoy: keep flying, never snap back to the player.
        return false;
    }
    private bool InterceptedByFlare(Vector3 start, Vector3 end)
    {
        if(IsConstruct || IsMine || Finished)return false;
        MagicProjectile nearest=null;Vector3 contact=end;float closest=float.PositiveInfinity;
        foreach(var field in flareFields)
        {
            if(field==null || !field.IsLiveFlare(Runner) || field.ShooterTeam==ShooterTeam)continue;
            float radius=field.ConstructRadius+Mathf.Max(0,stats.projectileRadius);
            for(int i=0;i<field.flareSegmentCount;i++)
            {
                var head=field.flareSegments[i];
                float sqr=BattleFlag.SegmentDistanceSquared(start,end,head.start,head.end,out Vector3 point);
                if(sqr>radius*radius || !HasSummonSight(head.origin,point))continue;
                float along=(point-start).sqrMagnitude;if(along>=closest)continue;
                nearest=field;contact=point;closest=along;
            }
        }
        if(nearest==null)return false;
        Finished=true;TargetId=default;decoyField=null;transform.position=contact;
        RPC_Impact(contact,Magic);
        Lifetime=TickTimer.CreateFromSeconds(Runner,.1f);
        PlayerData.FindPerkData(Runner,nearest.Shooter)?.AwardPerkFlare();
        return true;
    }
    private void RenderFlareField()
    {
        if(Finished || Lifetime.ExpiredOrNotRunning(Runner)){if(flareVisual!=null)flareVisual.gameObject.SetActive(false);return;}
        // Snapshot fallback also gives late joiners the latest burst, RPCs retain consecutive bursts.
        RecordFlareBurst(FlareBurstPosition,FlareBurstRotation,FlareBurstAt,FlareBurstSequence);
        if(flareVisual==null)flareVisual=FlareFieldVisual.Create(transform,PresentationStats);
        if(!flareVisual.gameObject.activeSelf)
        {
            flareVisual.Configure(PresentationStats);
            flareVisual.gameObject.SetActive(true);
        }
        flareVisual.BeginFrame();
        float now=FlareElapsed;
        for(int i=0;i<flareHistoryCount;i++)
        {
            var sample=flareHistory[(flareHistoryHead-1-i+FlareHistoryCapacity)%FlareHistoryCapacity];
            float age=now-sample.at;if(age>FlareParticleLifetime)break;if(age<0)continue;
            for(int branch=0;branch<3;branch++)
            {
                Vector3 heading=sample.rotation*Quaternion.Euler(0,(branch-1)*FlareSpread,0)*Vector3.forward;
                flareVisual.SetHead((sample.sequence%FlareHistoryCapacity)*3+branch,
                    FlareTrailPoint(sample.position,heading,FlareSpeed,age),
                    heading*(FlareSpeed*Mathf.Exp(-.65f*age))+Vector3.down*(1.6f*age),age,FlareParticleLifetime);
            }
        }
        flareVisual.EndFrame(FlareBurstSequence);
    }
    private void ClearFlareField()
    {
        flareFields.Remove(this);flareCaster=null;decoyField=null;
        flareHistoryHead=flareHistoryCount=flareSegmentCount=lastFlareSequence=0;
        if(flareVisual!=null)flareVisual.gameObject.SetActive(false);
    }
}
