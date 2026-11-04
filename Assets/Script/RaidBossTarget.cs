using Fusion;
using UnityEngine;

public sealed class RaidBossTarget : MagicProjectile
{
    private GiantMeowBoss boss;
    public void Configure(GiantMeowBoss owner)
    {
        boss = owner;
        if (!Object.HasStateAuthority) return;
        ConstructKind = MagicConstructKind.Summon;
        Magic = MagicType.Vision;
        Shooter = PlayerRef.None;
        ShooterTeam = 99;
        ShooterId = Object.Id;
        Finished = false;
    }
    public override bool IsLiveSummon => base.IsLiveSummon && boss != null && boss.IsTargetable;
    public override float SummonMaxHealth => boss != null ? boss.MaxHealth : 1f;
    public override float SummonHealthFraction => boss != null ? boss.Health / Mathf.Max(1f, boss.MaxHealth) : 0f;
    public override float SummonHitRadius => boss != null ? boss.Settings.hitRadius : 0f;
    public override bool TraceBody(Vector3 origin, Vector3 direction, float radius, float limit, out float distance)
    {
        distance = limit;
        return boss != null && boss.TraceBody(origin, direction, radius, limit, out distance);
    }
    public override float DistanceToBody(Vector3 point) => boss != null ? boss.DistanceToBody(point) : float.PositiveInfinity;
    public override float AreaContactRadius(float radius) => radius;
    public override bool ReceiveSummonHit(MagicStatEntry attack, int team, NetworkId attacker) =>
        IsLiveSummon && Object.HasStateAuthority && team != ShooterTeam && boss.ReceiveHit(attack, attacker);
    public override void FixedUpdateNetwork() { }
    public override void Render() { }
}
