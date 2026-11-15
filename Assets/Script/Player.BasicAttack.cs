using System.Collections.Generic;
using Fusion;
using UnityEngine;

public partial class Player
{
    [Networked] public int BasicAttackCharges { get; private set; }
    [Networked] private float BasicAttackRechargeElapsed { get; set; }
    [Networked] private TickTimer BasicAttackShotCooldown { get; set; }
    [Networked] private int lastMagicSelectionRevision { get; set; }
    [Networked] private NetworkId previousBasicAttackTarget { get; set; }
    private readonly List<Player> basicAttackCandidates = new List<Player>(20);
    public int BasicAttackCapacity => Mathf.Max(1, BasicAttackSettings.Default != null ? BasicAttackSettings.Default.capacity : 3);
    public float BasicAttackRechargeSeconds => Mathf.Max(.05f, BasicAttackSettings.Default != null ? BasicAttackSettings.Default.rechargeSeconds : 4);
    public float BasicAttackRechargeProgress => Object != null && Object.IsValid && BasicAttackCharges < BasicAttackCapacity
        ? Mathf.Clamp01(BasicAttackRechargeElapsed / BasicAttackRechargeSeconds) : 0;

    private void ResetBasicAttack()
    {
        previousBasicAttackTarget = default;
        BasicAttackCharges = BasicAttackCapacity;
        BasicAttackRechargeElapsed = 0;
        BasicAttackShotCooldown = TickTimer.None;
    }

    // Sequential recharge continues while using a spell or the independent blade charges.
    private void TickBasicAttackRecharge()
    {
        if (!Object.HasStateAuthority) return;
        BasicAttackCharges = Mathf.Min(BasicAttackCharges, BasicAttackCapacity);
        if (BasicAttackCharges >= BasicAttackCapacity) { BasicAttackRechargeElapsed = 0; return; }
        BasicAttackRechargeElapsed += Runner.DeltaTime;
        while (BasicAttackRechargeElapsed >= BasicAttackRechargeSeconds && BasicAttackCharges < BasicAttackCapacity)
        {
            BasicAttackRechargeElapsed -= BasicAttackRechargeSeconds;
            BasicAttackCharges++;
        }
        if (BasicAttackCharges >= BasicAttackCapacity) BasicAttackRechargeElapsed = 0;
    }

    private void FireBasicAttack()
    {
        if (!Object.HasStateAuthority || !IsAlive || IsHitStunned || IsReturningToMap || BladeCharges > 0 ||
            BasicAttackCharges <= 0 || TimerIsActive(BasicAttackShotCooldown) || !magicProjectilePrefab.IsValid) return;
        MagicStatEntry stats = BasicAttackSettings.Stats;
        Vector3 aim = IsFiniteDirection(castAimDirection) && castAimDirection.sqrMagnitude > .001f
            ? castAimDirection.normalized : transform.forward;
        Player target = FindBasicAttackTarget(MagicCastPosition, aim, stats.range);
        if (target == null) return; // No target: no projectile, charge consumption or firing feedback.
        var shot = Runner.Spawn(magicProjectilePrefab, MagicCastPosition, Quaternion.LookRotation(aim), ParticipantRef);
        if (shot == null) return;
        shot.GetComponent<MagicProjectile>().Initialize(this, target, stats, aim, castInputTick);
        previousBasicAttackTarget = target.Object.Id;
        if (BasicAttackCharges == BasicAttackCapacity) BasicAttackRechargeElapsed = 0;
        BasicAttackCharges--;
        BasicAttackShotCooldown = TickTimer.CreateFromSeconds(Runner,
            Mathf.Max(.05f, BasicAttackSettings.Default != null ? BasicAttackSettings.Default.shotInterval : .2f));
        LastCastMagic = MagicType.BasicAttack;
        CastSequence++;
        RPC_PresentCast(MagicType.BasicAttack, MagicCastPosition, MagicCastPosition + aim, 0);
    }

    private Player FindBasicAttackTarget(Vector3 origin, Vector3 aim, float range)
    {
        var settings = BasicAttackSettings.Default;
        float cone = Mathf.Clamp(settings != null ? settings.aimAssistAngle : 12, 0, 90);
        float tolerance = Mathf.Max(0, settings != null ? settings.similarAimAngle : 2);
        float minimumDot = Mathf.Cos(cone * Mathf.Deg2Rad);
        float nearestAngle = cone;
        basicAttackCandidates.Clear();
        // Shot-time only. Keep the last attacked enemy only while still inside the aim cone,
        // in range, alive/hostile and visible. Never steer towards an off-screen stale target.
        foreach (Player candidate in ActiveCombatants)
        {
            if (candidate == null || candidate.Object == null || !candidate.Object.IsValid ||
                candidate.Runner != Runner || !candidate.IsTargetableBy(this)) continue;
            Vector3 delta = candidate.LockAimPoint - origin;
            if (delta.sqrMagnitude > range * range || delta.sqrMagnitude < .001f) continue;
            float dot = Vector3.Dot(aim, delta.normalized);
            if (dot < minimumDot || !MagicProjectile.HasSummonSight(origin, candidate.LockAimPoint)) continue;
            if (candidate.Object.Id == previousBasicAttackTarget) return candidate;
            nearestAngle = Mathf.Min(nearestAngle, Mathf.Acos(Mathf.Clamp(dot, -1, 1)) * Mathf.Rad2Deg);
            basicAttackCandidates.Add(candidate);
        }
        Player best = null;
        float bestAngle = float.PositiveInfinity;
        // Compare against the true closest aim angle, not the previous iteration, so a
        // chain of similar angles cannot pull selection to the far edge of the cone.
        foreach (Player candidate in basicAttackCandidates)
        {
            float angle = Vector3.Angle(aim, candidate.LockAimPoint - origin);
            if (angle > nearestAngle + tolerance) continue;
            if (best == null || candidate.NowHp < best.NowHp ||
                (candidate.NowHp == best.NowHp && angle < bestAngle))
            { best = candidate; bestAngle = angle; }
        }
        return best;
    }
}
