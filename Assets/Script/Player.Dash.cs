using Fusion;
using UnityEngine;

public partial class Player
{
    public const int MaximumDashCharges = 3;
    [Networked] public int DashCharges { get; private set; }
    [Networked] private TickTimer DashRechargeTimer { get; set; }
    [Networked] private float DashRechargeDuration { get; set; }

    private float DashRechargeSeconds => Mathf.Max(.05f,
        (FlightCombatTable.Default != null ? FlightCombatTable.Default.dashRechargeSeconds : 4f) *
        (1f - Mathf.Clamp01(CommonAmount(PerkId.Tailwind))));

    public float DashRechargeProgress => Object != null && Object.IsValid && DashCharges < MaximumDashCharges &&
        DashRechargeTimer.IsRunning ? Mathf.Clamp01(1f -
        (DashRechargeTimer.RemainingTime(Runner) ?? 0f) / Mathf.Max(.05f, DashRechargeDuration)) : 0f;

    private void ResetDashCharges()
    {
        DashCharges = MaximumDashCharges;
        DashRechargeTimer = TickTimer.None;
        DashRechargeDuration = 0;
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
