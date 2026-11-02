using Fusion;
using UnityEngine;

public partial class Player
{
    [Networked] public int BladeCharges { get; private set; }
    [Networked] private TickTimer BladeShotCooldown { get; set; }
    [Networked] private TickTimer WoundTimer { get; set; }
    [Networked] private float WoundHealingMultiplier { get; set; }
    [Networked] public int BladeSwingSequence { get; private set; }
    [Networked] public int BladeSwingStep { get; private set; }
    [Networked] private TickTimer BladeAbilityTimer { get; set; }
    private MagicStatEntry bladeStats;
    public bool IsBladeArmed => Object != null && Object.IsValid && IsAlive && BladeCharges > 0 &&
        GetSelectedMagic() == MagicType.Blade && TimerIsActive(BladeAbilityTimer);

    public bool ShowBladeWeapon => Object != null && Object.IsValid && IsAlive &&
        GetSelectedMagic() == MagicType.Blade && TimerIsActive(BladeAbilityTimer) &&
        (BladeCharges > 0 || bladeSwing.IsActive);

    public bool TryGetBladeWeaponRotation(out Quaternion rotation)
    {
        rotation = MagicCastAnchor.rotation;
        if (!bladeSwing.IsActive || !bladeSwing.TryGetArmDirection(out Vector3 direction)) return false;
        Vector3 up = Vector3.ProjectOnPlane(transform.up, direction);
        if (up.sqrMagnitude < .001f) up = Vector3.ProjectOnPlane(transform.forward, direction);
        rotation = Quaternion.LookRotation(direction, up.normalized);
        return true;
    }

    private void ClearBladeAbility()
    {
        BladeCharges = 0;
        BladeAbilityTimer = TickTimer.None;
    }

    private void TickBladeAbility()
    {
        if (Object.HasStateAuthority && BladeCharges > 0 && !IsBladeArmed) ClearBladeAbility();
    }

    private void ResetBladeState()
    {
        ClearBladeAbility();
        BladeShotCooldown = WoundTimer = TickTimer.None;
        WoundHealingMultiplier = 1f;
    }

    private void FireBlade()
    {
        if (!Object.HasStateAuthority || !IsBladeArmed || TimerIsActive(BladeShotCooldown) ||
            !magicProjectilePrefab.IsValid) return;
        Vector3 direction = IsFiniteDirection(castAimDirection) && castAimDirection.sqrMagnitude > .001f
            ? castAimDirection.normalized : transform.forward;
        var shot = Runner.Spawn(magicProjectilePrefab, MagicCastPosition, Quaternion.LookRotation(direction), ParticipantRef);
        if (shot == null) return;
        MagicStatEntry entry = bladeStats;
        CastSequence++;
        entry.perkCast = CastSequence;
        entry.titleCastId = PlayerData.FindPerkData(Runner, ParticipantRef)?.RecordTitleCast(MagicType.Blade, false) ?? 0;
        shot.GetComponent<MagicProjectile>().Initialize(this, null, entry, direction, castInputTick);
        BladeSwingStep = Mathf.Max(0, bladeStats.bladeCharges - BladeCharges);
        BladeSwingSequence++;
        BladeCharges--;
        BladeShotCooldown = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0f, entry.bladeShotInterval));
        LastCastMagic = MagicType.Blade;
        RPC_PresentCast(MagicType.Blade, MagicCastPosition, MagicCastPosition, 0f);
    }

    private void ApplyWound(MagicStatEntry entry)
    {
        WoundTimer = TickTimer.CreateFromSeconds(Runner, StatusPerkDuration(entry.effectDuration));
        WoundHealingMultiplier = Mathf.Clamp01(entry.woundHealingMultiplier);
    }
}
