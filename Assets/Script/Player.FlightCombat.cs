using Fusion;
using UnityEngine;

public partial class Player
{
    [Networked] private TickTimer RamCooldown { get; set; }
    [Networked] public bool CobraActive { get; private set; }
    [Networked] private TickTimer CobraTimer { get; set; }
    [Networked] private TickTimer CobraCooldown { get; set; }
    [Networked] private Quaternion CobraRotation { get; set; }
    [Networked] private float CobraInitialSpeed { get; set; }
    [Networked] public Vector3 PortalEntryPosition { get; private set; }

    private void TryRam(Vector3 start, Vector3 end, Vector3 velocity)
    {
        FlightCombatTable table = FlightCombatTable.Default;
        if (!Object.HasStateAuthority || !IsAlive || IsReturningToMap || table == null || !table.enableRamming ||
            TimerIsActive(RamCooldown) || velocity.magnitude < table.minimumRamSpeed ||
            BattleManager.Instance == null || !BattleManager.Instance.IsGameplayActive) return;
        BattleFlag.GetCapsule(this, out Vector3 center, out Vector3 half, out float radius);
        Vector3 startCenter = center + start - end;
        foreach (Player other in ActiveCombatants)
        {
            if (other == null || other == this || other.Object == null || !other.Object.IsValid ||
                !other.Object.HasStateAuthority || other.Runner != Runner || !other.IsAlive ||
                other.TeamIndex == TeamIndex || other.TeamIndex <= 0 || other.IsReturningToMap ||
                other.TimerIsActive(other.RamCooldown)) continue;
            BattleFlag.GetCapsule(other, out Vector3 target, out Vector3 targetHalf, out float targetRadius);
            Vector3 towards = (target - startCenter).normalized;
            float attackSpeed = Vector3.Dot(velocity, towards);
            float counterSpeed = Vector3.Dot(other.transform.forward * other.Speed, -towards);
            if (attackSpeed < table.minimumRamSpeed) continue;
            float combined = radius + targetRadius;
            int steps = Mathf.Clamp(Mathf.CeilToInt((end - start).magnitude / Mathf.Max(.1f, combined * .4f)), 1, 24);
            bool contact = false;
            for (int step = 0; step <= steps; step++)
            {
                Vector3 at = Vector3.Lerp(startCenter, center, (float)step / steps);
                if (BattleFlag.SegmentDistanceSquared(at - half, at + half, target - targetHalf, target + targetHalf, out _) <= combined * combined)
                { contact = true; break; }
            }
            if (!contact || !HasLineOfSight(other)) continue;
            RamCooldown = other.RamCooldown = TickTimer.CreateFromSeconds(Runner, Mathf.Max(.1f, table.ramCooldown));
            bool counter = counterSpeed >= table.minimumRamSpeed;
            // Capture both outcomes before either pilot can die. Contacts never parry.
            other.DropCarriedFlag();
            if (counter) DropCarriedFlag();
            other.TakeDamage(table.ramDamage, ParticipantRef, MagicType.None, false,
                table.ramKnockback, table.ramStun, Object.Id);
            if (counter) TakeDamage(table.ramDamage, other.ParticipantRef, MagicType.None, false,
                table.ramKnockback, table.ramStun, other.Object.Id);
            RPC_PlayBattleSound(BattleSound.BodyRam);
            break;
        }
    }

    private void DropCarriedFlag()
    {
        BattleFlag flag = BattleFlag.Instance;
        if (Object.HasStateAuthority && flag != null && flag.Runner == Runner)
            flag.DropCarrier(ParticipantRef, transform.position, true);
    }

    private bool TryStartCobra()
    {
        FlightCombatTable table = FlightCombatTable.Default;
        float cruiseSpeed = Mathf.Max(1f, flightMaxSpeed * GetMovementMultiplier() *
            GetFlagSpeedMultiplier() * GetItemSpeedMultiplier());
        if (table == null || !table.enableCobra || CobraActive || TimerIsActive(CobraCooldown) ||
            SpeedStage != 3 || Speed < cruiseSpeed * table.cobraMinimumSpeedRatio ||
            IsBound || IsHitStunned || IsReturningToMap) return false;
        CobraRotation = transform.rotation;
        CobraInitialSpeed = Speed;
        CobraActive = true;
        CobraTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(.2f, table.cobraSeconds));
        CobraCooldown = TickTimer.CreateFromSeconds(Runner, Mathf.Max(table.cobraSeconds, table.cobraCooldown));
        SpeedStage = 0;
        IsBoosting = false;
        boostNeedsRelease = true;
        areaAimArmed = false;
        pendingMagic = MagicType.None;
        ClearLockTargetInternal();
        if (Object.HasStateAuthority) { StopChannel(); RPC_PlayBattleSound(BattleSound.Cobra); }
        return true;
    }

    internal static float CobraArc(float progress) => Mathf.Sin(Mathf.Clamp01(progress) * Mathf.PI);
    internal static float CobraSpeedFactor(float progress) => 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress));
    internal static float CobraTravelSpeed(float progress, float initialSpeed, float backwardSpeed)
    {
        float retreat = Mathf.Sin(Mathf.PI * Mathf.Clamp01((progress - .35f) / .55f));
        return initialSpeed * CobraSpeedFactor(progress / .45f) - Mathf.Max(0f, backwardSpeed) * retreat * retreat;
    }

    private bool TickCobra()
    {
        if (!CobraActive) return false;
        FlightCombatTable table = FlightCombatTable.Default;
        float duration = Mathf.Max(.2f, table != null ? table.cobraSeconds : 1.4f);
        float remaining = CobraTimer.RemainingTime(Runner) ?? 0f;
        float t = Mathf.Clamp01(1f - remaining / duration);
        if (remaining <= 0f || IsBound || IsHitStunned || !IsAlive)
        {
            CobraActive = false;
            CobraTimer = TickTimer.None;
            transform.rotation = CobraRotation;
            currentPitch = NormalizePitch(CobraRotation.eulerAngles.x);
            currentYaw = CobraRotation.eulerAngles.y;
            currentAimTurnSpeed = currentTurnSpeed = Speed = 0f;
            return true;
        }
        transform.rotation = CobraRotation * Quaternion.Euler(-CobraArc(t) * (table != null ? table.cobraPitch : 80f), 0f, 0f);
        // Keep moving throughout the pitch arc. Zero slope at both ends avoids a braking snap.
        Speed = CobraTravelSpeed(t, CobraInitialSpeed, table != null ? table.cobraBackwardSpeed : 6f);
        float next = Mathf.Clamp01(t + Runner.DeltaTime / duration);
        Vector3 movement = CobraRotation * Vector3.forward * Speed * Runner.DeltaTime +
            Vector3.up * ((CobraArc(next) - CobraArc(t)) * (table != null ? table.cobraLift : 2f));
        movement = ConstrainMapMovement(movement);
        if (characterController != null && characterController.enabled) characterController.Move(movement);
        else transform.position += movement;
        return true;
    }
}
