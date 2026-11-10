using UnityEngine;

public partial class Player
{
    private float botEvasionScan, botEvasionRetry, botEvasionDelay, botEvasionRemaining;
    private Vector3 botEvasionDirection;
    private MagicProjectile botEvasionThreat;
    private Fusion.NetworkId botEvasionThreatId;
    private bool botEvasionDash;

    private void ResetBotEvasion()
    {
        botEvasionScan = botEvasionRetry = botEvasionDelay = botEvasionRemaining = 0f;
        botEvasionThreat = null;
        botEvasionDash = false;
    }

    private bool TryBotEvasionDestination(MatchBotSettings settings, out Vector3 destination, out bool dash)
    {
        destination = default; dash = false;
        float dt = Runner.DeltaTime;
        botEvasionScan -= dt; botEvasionRetry -= dt; botEvasionDelay -= dt; botEvasionRemaining -= dt;
        if (!settings.useHomingEvasion || IsHitStunned || IsBound || IsReturningToMap || hasPendingPortalTeleport)
        { botEvasionThreat = null; return false; }
        if (botEvasionThreat != null)
        {
            if (botEvasionRemaining <= 0f || botEvasionThreat.Object == null || !botEvasionThreat.Object.IsValid ||
                !botEvasionThreat.Object.Id.Equals(botEvasionThreatId) || !botEvasionThreat.IsThreatTo(this))
                botEvasionThreat = null;
            else
            {
                if (botEvasionDelay > 0f) return false;
                destination = transform.position + botEvasionDirection * Mathf.Max(30f, Mathf.Abs(Speed) * 2f);
                dash = botEvasionDash;
                return true;
            }
        }
        if (botEvasionScan > 0f || botEvasionRetry > 0f) return false;
        botEvasionScan = Mathf.Max(.1f, settings.evasionScanSeconds);
        if (!MagicProjectile.TryGetBotHomingThreat(this, settings.evasionDetectionRange, out var threat,
            out float arrival, out float turnRate, out float initialTurnRate) || arrival > settings.evasionLookAheadSeconds) return false;
        botEvasionRetry = Mathf.Max(.1f, settings.evasionRetrySeconds);
        if (!BotHomingEvasion.TryPlan(turnRate, initialTurnRate, arrival,
            DashCharges > 0 || TimerIsActive(BurstBoostTimer), settings, out float angle, out float delay, out bool useDash)) return false;
        Vector3 preferred = BotHomingEvasion.ChooseSide(transform.forward, threat.transform.forward, angle, out Vector3 alternative);
        float probe = Mathf.Clamp(Mathf.Max(flightMaxSpeed, Mathf.Abs(Speed)) * settings.evasionHoldSeconds, 12f, 100f);
        if (!BotEvasionRouteClear(preferred, probe))
        {
            if (!BotEvasionRouteClear(alternative, probe)) return false;
            preferred = alternative;
        }
        botEvasionThreat = threat; botEvasionThreatId = threat.Object.Id;
        botEvasionDirection = preferred.normalized;
        botEvasionDash = useDash;
        botEvasionDelay = delay;
        botEvasionRemaining = Mathf.Max(.1f, settings.evasionHoldSeconds) + botEvasionDelay;
        // Give the chosen evasion a fresh dash decision, without consuming a charge here.
        botDashPending = false; botDashDecision = 0f;
        if (botEvasionDelay > 0f) return false;
        destination = transform.position + botEvasionDirection * Mathf.Max(30f, Mathf.Abs(Speed) * 2f);
        dash = botEvasionDash;
        return true;
    }

    public static bool CanExploitBotHomingTurn(float currentRate, float initialRate, float arrival, float weakRatio)
    {
        return BotHomingEvasion.CanExploit(currentRate, initialRate, arrival, weakRatio);
    }

    private bool BotEvasionRouteClear(Vector3 direction, float distance)
    {
        var boundary = BattleManager.Instance != null ? BattleManager.Instance.MapBoundary : null;
        if (boundary != null && boundary.TryGetBotFlightBounds(out var box) &&
            !box.Contains(transform.position + direction * distance)) return false;
        Vector3 origin = characterController != null ? characterController.bounds.center : LockAimPoint;
        return BotClearance(origin, direction, distance) >= distance - .1f;
    }
}
