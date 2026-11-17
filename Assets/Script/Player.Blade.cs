using Fusion;
using UnityEngine;

public partial class Player
{
    [SerializeField, Tooltip("칼날 시작 위치. 비우면 지팡이 MagicCastAnchor를 사용합니다. 파란 축이 칼날 방향입니다.")]
    private Transform bladeSlashAnchor;
    [SerializeField] private Vector3 bladeSlashLocalEuler;
    public Transform BladeSlashAnchor => bladeSlashAnchor != null ? bladeSlashAnchor : MagicCastAnchor;
    public Vector3 BladeSlashDirection => BladeSlashAnchor.rotation * Quaternion.Euler(bladeSlashLocalEuler) * Vector3.forward;
    public int BladeSurfaceMask => lockObstructionMask;
    [Networked] public int BladeCharges { get; private set; }
    [Networked] private TickTimer BladeShotCooldown { get; set; }
    [Networked] private TickTimer WoundTimer { get; set; }
    [Networked] private float WoundHealingMultiplier { get; set; }
    [Networked] public int BladeSwingSequence { get; private set; }
    [Networked] public int BladeSwingStep { get; private set; }
    [Networked] private TickTimer BladeAbilityTimer { get; set; }
    private MagicStatEntry bladeStats;
    [Networked] public bool BladeCharging { get; private set; }
    [Networked] public float BladeChargeProgress { get; private set; }
    private float bladeManaSpent;
    private bool bladeFullCue;
    private bool bladeNeedsRelease;
    private LineRenderer bladeChargeArc;
    private Material bladeChargeMaterial;
    public bool IsBladeArmed => networkSpawned && Object != null && Object.IsValid && IsAlive && BladeCharges > 0 &&
        (CurrentMagicSlot == 0 || GetSelectedMagic() == MagicType.Blade) && TimerIsActive(BladeAbilityTimer);

    public bool ShowBladeWeapon => networkSpawned && Object != null && Object.IsValid && IsAlive &&
        (CurrentMagicSlot == 0 || GetSelectedMagic() == MagicType.Blade) && TimerIsActive(BladeAbilityTimer) &&
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
        if (BladeCharging && Object != null && Object.IsValid && Object.HasStateAuthority)
            RPC_PresentLockComplete(MagicType.None);
        BladeCharging = false;
        BladeChargeProgress = 0f;
        bladeManaSpent = 0f;
        BladeCharges = 0;
        BladeAbilityTimer = TickTimer.None;
    }

    private void TickBladeAbility()
    {
        if (Object.HasStateAuthority && BladeCharges > 0 && !IsBladeArmed) ClearBladeAbility();
    }

    private void ProcessBladeCharge(bool held, bool released)
    {
        if (!held) bladeNeedsRelease = false;
        if (bladeNeedsRelease) return;
        if (!BladeCharging)
        {
            if (!held || TimerIsActive(MagicCooldowns[(int)MagicType.Blade])) return;
            if (NowAp <= .0001f) { NotifyManaFailure(); bladeNeedsRelease = true; return; }
            bladeStats = SelectedMagicStats;
            BladeCharging = true;
            bladeManaSpent = 0f;
            BladeChargeProgress = 0f;
            bladeFullCue = false;
        }
        if (released) { ReleaseBladeSlash(); return; }
        if (!held) return;
        float budget = Mathf.Max(.01f, bladeStats.bladeChargeMaxMana);
        float spend = Mathf.Min(Mathf.Max(.01f, bladeStats.bladeChargeManaPerSecond) * Runner.DeltaTime,
            Mathf.Max(0f, NowAp), Mathf.Max(0f, budget - bladeManaSpent));
        NowAp -= spend;
        bladeManaSpent += spend;
        BladeChargeProgress = Mathf.Clamp01(bladeManaSpent / budget);
        SyncApToPlayerData();
        if (!bladeFullCue && BladeChargeProgress >= .9999f)
        {
            bladeFullCue = true;
            RPC_PresentLockComplete(MagicType.Blade);
        }
    }

    private void ReleaseBladeSlash()
    {
        if (!BladeCharging) return;
        float charge = BladeChargeProgress;
        MagicStatEntry hit = bladeStats;
        hit.damage = Mathf.Lerp(hit.bladeMinDamage, Mathf.Max(hit.bladeMinDamage, hit.bladeMaxDamage), charge);
        float reach = Mathf.Lerp(hit.bladeMinRange, Mathf.Max(hit.bladeMinRange, hit.bladeMaxRange), charge);
        Vector3 aim = IsFiniteDirection(castAimDirection) && castAimDirection.sqrMagnitude > .001f
            ? castAimDirection.normalized : transform.forward;
        Vector3 origin = LockAimPoint;
        Quaternion frame = Quaternion.LookRotation(aim);
        CastSequence++;
        LastCastMagic = MagicType.Blade;
        hit.perkCast = CastSequence;
        hit.perkAttacker = ParticipantRef;
        hit.titleCastId = PlayerData.FindPerkData(Runner, ParticipantRef)?.RecordTitleCast(MagicType.Blade, false) ?? 0;
        bool contacted = false;
        foreach (Player enemy in ActiveCombatants)
        {
            if (enemy == null || enemy.Runner != Runner || !enemy.IsAlive || enemy.TeamIndex == TeamIndex) continue;
            Vector3 local = Quaternion.Inverse(frame) * (enemy.LockAimPoint - origin);
            if (Mathf.Abs(local.y) > hit.bladeSlashHalfHeight || new Vector2(local.x, local.z).sqrMagnitude > reach * reach ||
                Mathf.Abs(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg) > hit.bladeSlashAngle * .5f ||
                !MagicProjectile.HasBlastSight(origin, enemy)) continue;
            int before = enemy.HitSequence;
            enemy.ReceiveMagicHit(hit, ParticipantRef, Object.Id, enemy.LockAimPoint - origin, enemy.LockAimPoint);
            contacted |= enemy.HitSequence != before;
        }
        contacted |= MagicProjectile.DamageSummonsInBladeSlash(Runner, TeamIndex, origin, frame, reach, hit, Object.Id);
        if (!contacted) NotifyFaceMiss();
        BladeSwingStep = 0; // Always sweep the staff to the left.
        BladeSwingSequence++;
        MagicCooldowns.Set((int)MagicType.Blade, TickTimer.CreateFromSeconds(Runner, Mathf.Max(0f, hit.cooldownSeconds)));
        ClearBladeAbility();
        ClearLockTargetInternal(false);
        RPC_BladeSlash(origin, aim, reach, hit.bladeSlashAngle, hit.bladeSlashHalfHeight);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_BladeSlash(Vector3 origin, Vector3 aim, float reach, float angle, float height)
    {
        BladeSlashVisual.Play(this, reach, bladeSwing.Duration, GetMagicStats(MagicType.Blade).bladeSlashTexture);
        presentation ??= GetComponent<CombatPresentation>();
        presentation.ShowCast(MagicType.Blade, origin, origin);
    }

    private void ResetBladeState()
    {
        ClearBladeAbility();
        bladeNeedsRelease = false;
        BladeShotCooldown = WoundTimer = TickTimer.None;
        WoundHealingMultiplier = 1f;
    }

    private void FireBlade() => ReleaseBladeSlash();

    private void ApplyWound(MagicStatEntry entry)
    {
        float remaining = WoundTimer.RemainingTime(Runner) ?? 0f;
        float multiplier = Mathf.Clamp01(entry.woundHealingMultiplier);
        if (remaining > 0f) multiplier = Mathf.Min(multiplier, WoundHealingMultiplier);
        WoundTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(remaining, StatusPerkDuration(entry.effectDuration)));
        WoundHealingMultiplier = multiplier;
    }

    private void RenderBladeChargePreview()
    {
        bool show = networkSpawned && Object != null && Object.IsValid && Object.HasInputAuthority && BladeCharging && IsAlive;
        if (!show) { if (bladeChargeArc != null) bladeChargeArc.enabled = false; return; }
        if (bladeChargeArc == null)
        {
            var root = new GameObject("Blade charge range");
            root.transform.SetParent(transform, false);
            bladeChargeArc = root.AddComponent<LineRenderer>();
            bladeChargeArc.useWorldSpace = true;
            bladeChargeArc.positionCount = 49;
            bladeChargeArc.startWidth = bladeChargeArc.endWidth = .12f;
            bladeChargeMaterial = CombatPresentation.CreateEffectMaterial(new Color(.7f,.3f,1f,.35f));
            bladeChargeArc.sharedMaterial = bladeChargeMaterial;
            bladeChargeArc.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        bladeChargeArc.enabled = true;
        MagicStatEntry settings = GetMagicStats(MagicType.Blade);
        float radius = Mathf.Lerp(settings.bladeMinRange, Mathf.Max(settings.bladeMinRange, settings.bladeMaxRange), BladeChargeProgress);
        Vector3 aim = localCameraFollow != null && localCameraFollow.TryGetSteeringInput(out Vector3 direction, out _)
            ? direction : transform.forward;
        Quaternion frame = Quaternion.LookRotation(aim.sqrMagnitude > .001f ? aim : transform.forward);
        for (int i=0;i<49;i++)
        {
            float a = Mathf.Lerp(-settings.bladeSlashAngle*.5f,settings.bladeSlashAngle*.5f,i/48f)*Mathf.Deg2Rad;
            Vector3 endpoint = LockAimPoint+frame*new Vector3(Mathf.Sin(a),0,Mathf.Cos(a))*radius;
            bladeChargeArc.SetPosition(i, BladeSlashVisual.ProjectToSurface(this, LockAimPoint, endpoint));
        }
    }
}
