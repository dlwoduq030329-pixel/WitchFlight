using Fusion;
using UnityEngine;

public partial class Player
{
    public const float WaterWarningSeconds = 5f, WaterDeathSeconds = 8f;
    [Networked] public float SubmergedSeconds { get; private set; }
    private Suimono.Core.SuimonoModule drowningWater;
    private float nextDrowningWaterLookup;

    public bool IsBelowWaterSurface()
    {
        if (drowningWater == null && Time.unscaledTime >= nextDrowningWaterLookup)
        {
            drowningWater = FindFirstObjectByType<Suimono.Core.SuimonoModule>();
            nextDrowningWaterLookup = Time.unscaledTime + 2f;
        }
        Vector3 point = LockAimPoint;
        return drowningWater != null && drowningWater.isActiveAndEnabled &&
            drowningWater.TryGetSurfaceHeight(point,out float surface) && point.y < surface;
    }

    private bool TickDrowning()
    {
        if (!Object.HasStateAuthority) return false;
        if (!IsBelowWaterSurface() || (testBot != null && testBot.Immortal))
        { SubmergedSeconds = 0f; return false; }
        SubmergedSeconds = Mathf.Min(WaterDeathSeconds,SubmergedSeconds + Runner.DeltaTime);
        if (SubmergedSeconds < WaterDeathSeconds) return false;
        // Environmental death uses normal health, flag-drop and respawn handling, with no stale kill credit.
        SpawnProtectionPending = false;
        SpawnProtectionTimer = TickTimer.None;
        TakeDamage(Mathf.Max(1f,NowHp),PlayerRef.None,MagicType.None,false,0f,0f);
        return !IsAlive;
    }

    private void TickRoundEndCoast()
    {
        // Keep Fusion's tick clock intact. Only the remaining flight motion eases to a stop.
        ResetControllerAfterRender();
        float decay = Mathf.Exp(-Mathf.Max(.1f,BattleResultsTable.Default.roundSlowdownResponse) * Runner.DeltaTime);
        Speed *= decay;
        KnockbackVelocity *= decay;
        Vector3 movement = ConstrainMapMovement((transform.forward * Speed + KnockbackVelocity) * Runner.DeltaTime);
        if (characterController != null && characterController.enabled) characterController.Move(movement);
        else transform.position += movement;
    }
}
