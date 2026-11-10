using UnityEngine;

public partial class Player
{
    // Host-only intent. Charge spending, movement, contact damage and replication stay
    // on the ordinary player path. No extra combatant scans or per-tick random rolls.
    private Player botRamTarget;
    private Vector3 botRamPoint, botRamHeading;
    private float botRamRemaining, botRamRetry, botDashDecision, botDashDelay;
    private bool botDashPending, botDashForRam;
    private int botMobilityRound = -1, botMobilityDeath = -1, botMobilityPortal = -1;

    private void TickBotMobilityPlan(float dt)
    {
        int round = BattleManager.Instance != null ? BattleManager.Instance.RoundNumber : 0;
        if (round != botMobilityRound || DeathSequence != botMobilityDeath || PortalExitSequence != botMobilityPortal)
        {
            CancelBotRam();
            botDashPending = false;
            botRamRetry = botDashDecision = botDashDelay = 0f;
            ResetBotEvasion();
            botMobilityRound = round; botMobilityDeath = DeathSequence; botMobilityPortal = PortalExitSequence;
        }
        botRamRemaining -= dt; botRamRetry -= dt; botDashDecision -= dt; botDashDelay -= dt;
    }

    private void CancelBotRam()
    {
        botRamTarget = null;
        botRamRemaining = 0f;
        if (botDashForRam) botDashPending = false;
        botDashForRam = false;
    }

    private bool TryBotRamDestination(MatchBotSettings settings, bool allowed, out Vector3 destination)
    {
        destination = default;
        var combat = FlightCombatTable.Default;
        if (!allowed || !settings.useBodyRam || !settings.useCombatDash || combat == null || !combat.enableRamming ||
            !IsBotCombatant(botTarget) || botTarget.TeamIndex == TeamIndex || botTarget.IsSpawnProtected ||
            IsBound || IsHitStunned || IsReturningToMap || hasPendingPortalTeleport ||
            TimerIsActive(RamCooldown) || botTarget.TimerIsActive(botTarget.RamCooldown))
        { CancelBotRam(); return false; }

        if (botRamTarget != null)
        {
            if (botRamTarget != botTarget || botRamRemaining <= 0f ||
                Vector3.Dot(botRamPoint - LockAimPoint, botRamHeading) < -4f)
            { CancelBotRam(); return false; }
            // Commit to the sampled intercept. A target can turn away; the bot does
            // not perfectly re-aim the charge every tick or orbit after a missed pass.
            destination = botRamPoint - (LockAimPoint - transform.position) + botRamHeading * 12f;
            return true;
        }
        if (botRamRetry > 0f || (DashCharges <= 0 && !TimerIsActive(BurstBoostTimer))) return false;
        Vector3 delta = botTarget.LockAimPoint - LockAimPoint;
        float reach = Mathf.Max(5f, settings.ramApproachDistance);
        if (delta.sqrMagnitude < 25f || delta.sqrMagnitude > reach * reach ||
            Vector3.Dot(transform.forward, delta.normalized) < -.1f) return false;

        // A rejected attempt also waits before retrying; increasing FPS cannot make
        // the bot eventually "win" a probability roll within the same opportunity.
        botRamRetry = Mathf.Max(.1f, settings.ramRetrySeconds);
        if (Random.value >= Mathf.Clamp01(settings.ramAttemptChance) || !HasLineOfSight(botTarget)) return false;
        bool mistake = Random.value < Mathf.Clamp01(settings.ramMistakeChance);
        Vector3 aim = MagicProjectile.HomingAimDelta(delta, botTarget.ParryHintVelocity,
            Mathf.Max(1f, flightMaxSpeed * boostMultiplier), mistake ? 0f : Mathf.Max(0f, settings.ramLeadSeconds));
        if (mistake)
        {
            Vector3 side = Vector3.Cross(Vector3.up, delta).normalized;
            if (side.sqrMagnitude < .1f) side = transform.right;
            aim += side * Mathf.Max(0f, settings.ramMissDistance) *
                (Random.value < .5f ? -1f : 1f) * Random.Range(.8f, 1.2f);
        }
        botRamTarget = botTarget;
        botRamPoint = LockAimPoint + aim;
        botRamHeading = aim.normalized;
        botRamRemaining = Mathf.Max(.1f, settings.ramCommitSeconds);
        botDashPending = false;
        botDashDecision = 0f;
        destination = botRamPoint - (LockAimPoint - transform.position) + botRamHeading * 12f;
        return true;
    }

    private void ApplyBotCombatDash(MatchBotSettings settings, bool avoidingWall, bool pickupGoal,
        bool evading, bool evasionDash,
        int desiredStage, Vector3 destination, Vector3 direction, ref NetworkInputData input)
    {
        if (avoidingWall) CancelBotRam();
        float distance = Vector3.Distance(transform.position, destination);
        float minimum = pickupGoal ? Mathf.Max(settings.dashTravelDistance, settings.pickupApproachDistance * 2f)
            : settings.dashTravelDistance;
        bool ram = botRamTarget != null;
        bool eligible = settings.useCombatDash && (!evading || evasionDash) && !avoidingWall && desiredStage == 3 && DashCharges > 0 &&
            !TimerIsActive(BurstBoostTimer) && !IsBound && !IsHitStunned && !IsReturningToMap &&
            !hasPendingPortalTeleport && Vector3.Dot(transform.forward, direction) >= .85f &&
            (ram || evasionDash || distance >= minimum);
        if (!eligible || (botDashPending && botDashForRam != ram))
        { botDashPending = false; return; }
        if (!botDashPending)
        {
            if (botDashDecision > 0f) return;
            botDashDecision = Mathf.Max(.1f, settings.dashDecisionSeconds);
            if (!BotHomingEvasion.TryDashDelay(settings, out botDashDelay)) return;
            botDashPending = true; botDashForRam = ram;
        }
        if (botDashDelay > 0f || previousButtons.IsSet(PlayerInputButton.Boost)) return;
        botDashPending = false;
        // Check the faster trajectory only when actually about to press Shift.
        // Mistakes affect intent, never disable obstacle avoidance or give free charges.
        float probe = Mathf.Clamp(flightMaxSpeed * boostMultiplier * .65f, 12f, 100f);
        Vector3 origin = characterController != null ? characterController.bounds.center : LockAimPoint;
        if (BotClearance(origin, transform.forward, probe) < probe - .1f ||
            BotClearance(origin, direction, probe) < probe - .1f)
        { CancelBotRam(); return; }
        var boundary = BattleManager.Instance != null ? BattleManager.Instance.MapBoundary : null;
        if (boundary != null && boundary.TryGetBotFlightBounds(out var bounds) &&
            !bounds.Contains(transform.position + transform.forward * probe))
        { CancelBotRam(); return; }
        input.buttons.Set(PlayerInputButton.Boost, true); // Released on the next tick.
    }
}
