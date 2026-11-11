using Fusion;
using UnityEngine;

public partial class Player
{
    [Networked] private int DashChargeCapacity { get; set; }
    [Networked] private int DashBonusCharges { get; set; }
    [Networked] private TickTimer DashGuardTimer { get; set; }
    public int MaximumDashCharges => Mathf.Clamp(DashChargeCapacity, 1, 5) + DashBonusCharges;
    public bool IsDashGuardActive => Object != null && Object.IsValid && IsAlive && TimerIsActive(DashGuardTimer);
    [Networked] public int DashCharges { get; private set; }
    [Networked] private TickTimer DashRechargeTimer { get; set; }
    [Networked] private float DashRechargeDuration { get; set; }

    private float DashRechargeSeconds => Mathf.Max(.05f,
        (FlightCombatTable.Default != null ? FlightCombatTable.Default.dashRechargeSeconds : 4f) *
        (1f - Mathf.Clamp01(CommonAmount(PerkId.Tailwind))));

    public float DashRechargeProgress => Object != null && Object.IsValid && DashCharges < MaximumDashCharges &&
        DashRechargeTimer.IsRunning ? Mathf.Clamp01(1f -
        (DashRechargeTimer.RemainingTime(Runner) ?? 0f) / Mathf.Max(.05f, DashRechargeDuration)) : 0f;

    private void ResetDashCharges(int maximumCharges = 3)
    {
        DashChargeCapacity = Mathf.Clamp(maximumCharges, 1, 5);
        DashBonusCharges = 0;
        DashGuardTimer = TickTimer.None;
        DashCharges = MaximumDashCharges;
        DashRechargeTimer = TickTimer.None;
        DashRechargeDuration = 0;
    }

    // Called on initial spawn and confirmed perk changes, not every simulation tick.
    private void RefreshDashPerks(bool fill)
    {
        int previousBonus = DashBonusCharges;
        DashBonusCharges = Mathf.Clamp(Mathf.RoundToInt(CommonAmount(PerkId.FirstWind)), 0, 1);
        DashCharges = fill ? MaximumDashCharges : Mathf.Min(MaximumDashCharges,
            DashCharges + Mathf.Max(0, DashBonusCharges - previousBonus));
        if (DashCharges >= MaximumDashCharges) DashRechargeTimer = TickTimer.None;
        else if (!DashRechargeTimer.IsRunning) StartDashRecharge();
    }

    private void StartDashGuard()
    {
        float seconds = CommonAmount(PerkId.BrakingRune);
        if (seconds > 0f) DashGuardTimer = TickTimer.CreateFromSeconds(Runner, seconds);
    }

    private void StartDashRecharge()
    {
        DashRechargeDuration = DashRechargeSeconds;
        DashRechargeTimer = TickTimer.CreateFromSeconds(Runner, DashRechargeDuration);
    }

    private void TickDashRecharge()
    {
        if (DashCharges >= MaximumDashCharges) return;
        if (!DashRechargeTimer.IsRunning) StartDashRecharge();
        if (!DashRechargeTimer.Expired(Runner)) return;
        DashCharges = Mathf.Min(MaximumDashCharges, DashCharges + 1);
        if (DashCharges < MaximumDashCharges) StartDashRecharge();
        else DashRechargeTimer = TickTimer.None;
    }

    private bool TryConsumeDashCharge()
    {
        if (DashCharges <= 0) return false;
        DashCharges--;
        // Spending another charge must not restart the recharge already in progress.
        if (!DashRechargeTimer.IsRunning) StartDashRecharge();
        return true;
    }
}
