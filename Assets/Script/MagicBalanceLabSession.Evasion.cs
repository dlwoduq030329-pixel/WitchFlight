using UnityEngine;

public sealed partial class MagicBalanceLabSession
{
    private bool useReactiveBotEvasion = true;
    private MagicBalanceLabShot evasionShot;
    private int evasionLaunch;
    private float evasionScan, evasionRetry, evasionDelay, evasionHold, evasionDashRetry, evasionDashDelay;
    private bool evasionNeedsDash, evasionDashPending;
    private Vector3 evasionHeading;
    private string botEvasionStatus = "공격 모드: 유도탄을 감지하면 회피합니다.";
    private readonly RaycastHit[] evasionHits = new RaycastHit[24];

    private void ResetReactiveBot()
    {
        evasionShot = null;
        evasionScan = evasionRetry = evasionDelay = evasionHold = evasionDashRetry = evasionDashDelay = 0f;
        evasionDashPending = false;
        botEvasionStatus = "공격 모드: 유도탄을 감지하면 회피합니다.";
    }

    private void UpdateReactiveBot(float dt, ref Vector3 heading, out bool dash)
    {
        dash = false;
        var settings = MatchBotSettings.Default;
        evasionScan -= dt; evasionRetry -= dt; evasionDelay -= dt; evasionHold -= dt;
        evasionDashRetry -= dt; evasionDashDelay -= dt;
        if (!settings.useHomingEvasion || !bot.CanManeuver)
        { evasionShot = null; evasionDashPending = false; return; }
        if (evasionShot != null && (evasionHold <= 0 || evasionShot.LaunchSequence != evasionLaunch ||
            !evasionShot.IsLiveHomingThreat(bot)))
        { evasionShot = null; evasionDashPending = false; }
        if (evasionShot == null && evasionScan <= 0 && evasionRetry <= 0)
        {
            evasionScan = Mathf.Max(.1f, settings.evasionScanSeconds);
            MagicBalanceLabShot nearest = null;
            float arrival = float.PositiveInfinity, speed = 0f, rate = 0f, initialRate = 0f;
            foreach (var shot in shots)
            {
                if (shot == null || (shot.transform.position - bot.LockAimPoint).sqrMagnitude >
                    settings.evasionDetectionRange * settings.evasionDetectionRange ||
                    !shot.TryGetHomingThreat(bot, out float eta, out float candidateSpeed, out float turn, out float initial) ||
                    eta >= arrival || !LabEvasionSight(shot.transform.position, bot.LockAimPoint)) continue;
                nearest = shot; arrival = eta; speed = candidateSpeed; rate = turn; initialRate = initial;
            }
            if (nearest != null && arrival <= settings.evasionLookAheadSeconds)
            {
                evasionRetry = Mathf.Max(.1f, settings.evasionRetrySeconds);
                botEvasionStatus = $"접근탄 속도 {speed:0.0} / 선회 {rate:0}°/초";
                if (BotHomingEvasion.TryPlan(rate, initialRate, arrival, bot.DashCharges > 0 || bot.IsBoosting,
                    settings, out float angle, out float delay, out bool useDash))
                {
                    Vector3 preferred = BotHomingEvasion.ChooseSide(bot.transform.forward, nearest.transform.forward, angle, out var alternative);
                    float probe = Mathf.Clamp(Mathf.Max(Equipment.GetBroomStats(bot.Broom).maxSpeed, bot.Speed) * settings.evasionHoldSeconds, 12f, 100f);
                    bool clear = LabEvasionRouteClear(preferred, probe);
                    if (!clear && LabEvasionRouteClear(alternative, probe)) { preferred = alternative; clear = true; }
                    if (clear)
                    {
                        evasionShot = nearest; evasionLaunch = nearest.LaunchSequence;
                        evasionHeading = preferred.normalized; evasionDelay = delay;
                        evasionHold = Mathf.Max(.1f, settings.evasionHoldSeconds) + delay;
                        evasionNeedsDash = useDash; evasionDashPending = false; evasionDashRetry = 0;
                        botEvasionStatus += delay > 0 ? " · 판단 실수: 지연/얕은 선회" : useDash ? " · 대쉬 보조 회피" : " · 감속한 유도탄: 급선회";
                    }
                    else botEvasionStatus += " · 장애물로 회피 취소";
                }
                else botEvasionStatus += " · 이번 회피 건너뜀";
            }
        }
        if (evasionShot == null || evasionDelay > 0) return;
        heading = evasionHeading;
        if (!evasionNeedsDash || !settings.useCombatDash || bot.IsBoosting || bot.DashCharges <= 0 ||
            Vector3.Dot(bot.transform.forward, heading) < .85f) { evasionDashPending = false; return; }
        if (!evasionDashPending)
        {
            if (evasionDashRetry > 0) return;
            evasionDashRetry = Mathf.Max(.1f, settings.dashDecisionSeconds);
            if (!BotHomingEvasion.TryDashDelay(settings, out evasionDashDelay)) return;
            evasionDashPending = true;
        }
        if (evasionDashDelay > 0) return;
        evasionDashPending = false;
        var broom = Equipment.GetBroomStats(bot.Broom);
        dash = LabEvasionRouteClear(bot.transform.forward, Mathf.Clamp(broom.maxSpeed * broom.boostMultiplier * .65f, 12f, 100f));
    }

    private bool LabEvasionRouteClear(Vector3 heading, float length)
    {
        if ((bot.transform.position + heading * length - arenaCenter).sqrMagnitude > 230f * 230f) return false;
        bot.GetCollisionCapsule(out var center, out var half, out float radius);
        int count = Physics.CapsuleCastNonAlloc(center - half, center + half, radius, heading,
            evasionHits, length, ~0, QueryTriggerInteraction.Ignore);
        return LabEvasionHitsClear(count);
    }

    private bool LabEvasionSight(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        int count = Physics.RaycastNonAlloc(from, delta.normalized, evasionHits, delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
        return LabEvasionHitsClear(count);
    }

    private bool LabEvasionHitsClear(int count)
    {
        if (count == evasionHits.Length) return false;
        for (int i = 0; i < count; i++)
        {
            var collider = evasionHits[i].collider;
            if (collider != null && collider.GetComponentInParent<MagicBalanceLabActor>() == null &&
                collider.GetComponentInParent<MagicBalanceLabShot>() == null) return false;
        }
        return true;
    }
}
