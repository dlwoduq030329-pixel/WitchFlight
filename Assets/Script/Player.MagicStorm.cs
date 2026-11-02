using Fusion;
using UnityEngine;

public partial class Player
{
    [Networked] public int StormShotsRemaining { get; private set; }
    [Networked] private TickTimer StormTimer { get; set; }
    [Networked] private Vector3 StormAimDirection { get; set; }
    private MagicStatEntry stormStats;
    private int stormShotCount;
    public bool IsMagicStormActive => networkSpawned && Object != null && Object.IsValid && IsAlive && TimerIsActive(StormTimer);
    public Vector3 MagicStormAimPoint => MagicCastPosition + StormAimDirection * Mathf.Max(1f, stormStats.range);

    private void StartMagicStorm(MagicStatEntry entry)
    {
        stormStats = entry;
        stormShotCount = Mathf.Clamp(entry.stormShotCount, 1, 120);
        StormShotsRemaining = stormShotCount;
        StormTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(.01f, entry.stormDuration));
        StormAimDirection = transform.forward;
        UpdateMagicStormAim(castAimDirection);
        TickMagicStorm();
    }

    private void UpdateMagicStormAim(Vector3 aim)
    {
        if (Object.HasStateAuthority && IsMagicStormActive && IsFiniteDirection(aim) && aim.sqrMagnitude > .001f)
            StormAimDirection = aim.normalized;
    }

    private void StopMagicStorm()
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority) return;
        StormShotsRemaining = 0;
        StormTimer = TickTimer.None;
    }

    private void TickMagicStorm()
    {
        if (!Object.HasStateAuthority || StormShotsRemaining <= 0) return;
        if (!IsAlive || IsHitStunned || IsReturningToMap || BattleManager.Instance == null || !BattleManager.Instance.IsGameplayActive)
        { StopMagicStorm(); return; }
        float duration = Mathf.Max(.01f, stormStats.stormDuration);
        float elapsed = duration - Mathf.Max(0f, StormTimer.RemainingTime(Runner) ?? 0f);
        int due = Mathf.Min(stormShotCount, 1 + Mathf.FloorToInt(elapsed * stormShotCount / duration + .0001f));
        while (stormShotCount - StormShotsRemaining < due)
        {
            Vector2 cone = Random.insideUnitCircle * Mathf.Tan(stormStats.stormLaunchAngle * Mathf.Deg2Rad);
            Quaternion aimRotation = Quaternion.LookRotation(StormAimDirection);
            Vector3 direction = aimRotation * new Vector3(cone.x, cone.y, 1f).normalized;
            var shot = Runner.Spawn(magicProjectilePrefab, MagicCastPosition, Quaternion.LookRotation(direction), ParticipantRef);
            if (shot == null) { StopMagicStorm(); return; }
            shot.GetComponent<MagicProjectile>().Initialize(this, null, stormStats, direction, castInputTick);
            StormShotsRemaining--;
            RPC_PresentCast(MagicType.MagicStorm, MagicCastPosition, MagicCastPosition, 0f);
        }
    }
}
