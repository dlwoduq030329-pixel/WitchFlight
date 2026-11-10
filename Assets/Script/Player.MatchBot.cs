using Fusion;
using UnityEngine;

public partial class Player
{
    [Networked] public int MatchBotSlot { get; private set; }
    public bool IsMatchBot => MatchBotSlot > 0;
    public PlayerRef ParticipantRef => IsMatchBot ? BattleParticipants.BotRef(MatchBotSlot) : Object.InputAuthority;
    private float botDecisionTime, botAttackTime, botHeldTime;
    private bool botHolding, botReleaseFrame, botSpeedPulse;
    private Player botTarget;
    private bool botRecovering, botAimMiss;
    private float botAimRefresh;
    private Vector2 botAimOffset;
    private Player botAimTarget;
    private MagicType botAimMagic;
    private BattleItemPickup botHealthPickup;

    private bool ReadCombatInput(out NetworkInputData input)
    {
        if (IsMatchBot && RaceCourse.Instance != null) return BuildRaceInput(out input);
        bool valid = BuildBotCombatInput(out input);
        if (valid && IsMatchBot && Object.HasStateAuthority)
        {
            ApplyBotAimError(ref input);
            UpdateBotParry(ref input);
        }
        return valid;
    }

    private bool BuildBotCombatInput(out NetworkInputData input)
    {
        if (!IsMatchBot) return GetInput(out input);
        input = default;
        if (!Object.HasStateAuthority) return false;
        var settings = MatchBotSettings.Default;
        float dt = Runner.DeltaTime;
        TickBotMobilityPlan(dt);
        botDecisionTime -= dt; botAttackTime -= dt;
        if (BattleFlag.Instance != null && botObservedCarrier != BattleFlag.Instance.Carrier) botDecisionTime = 0f;
        if (botDecisionTime <= 0)
        {
            botDecisionTime = Mathf.Max(.1f, settings.reactionSeconds);
            PlayerData.FindPerkData(Runner, ParticipantRef)?.ChooseBotPerk();
            float health = MaxHp > 0f ? NowHp / MaxHp : 1f;
            if (health <= settings.seekHealthBelow) botRecovering = true;
            else if (health >= Mathf.Max(settings.seekHealthBelow, settings.resumeCombatAbove)) botRecovering = false;
            RefreshBotTactics(settings);
            botHealthPickup = botRecovering ? FindBotHealthPickup(settings) : null;
            RefreshBotEncirclement(settings);
        }
        bool hasEnemy = botTarget != null && botTarget.Object != null && botTarget.Object.IsValid && botTarget.IsAlive;
        var flag = BattleFlag.Instance;
        bool carrying = flag != null && flag.Carrier == ParticipantRef;
        bool retreating = botRecovering || carrying;
        bool pickupGoal = false;
        var botBoundary = BattleManager.Instance != null ? BattleManager.Instance.MapBoundary : null;
        float goalRadius = .85f;
        bool encircling = hasEnemy && botFormationTarget == botTarget;
        Vector3 destination = encircling ? botTarget.LockAimPoint + botAttackRouteOffset
            : hasEnemy ? botTarget.LockAimPoint
            : transform.position + transform.forward * 30;
        bool escorting = IsBotCombatant(botCarrier) && botCarrier != this && botCarrier.TeamIndex == TeamIndex;
        if (escorting && (botEscortDuty || !hasEnemy)) { destination = BotEscortPosition(botCarrier, settings); encircling = false; }
        else if (!hasEnemy && flag != null) destination = IsBotCombatant(botCarrier) ? botCarrier.transform.position : flag.transform.position;
        if (retreating && hasEnemy)
        {
            encircling = false;
            Vector3 away = (LockAimPoint - botTarget.LockAimPoint).normalized;
            if (away.sqrMagnitude < .01f) away = -transform.forward;
            // Keep possession rather than deliberately turning into a pursuer.
            destination = transform.position + away * settings.carrierSafeDistance;
            if (botBoundary != null)
                destination = botBoundary.BotRetreatDestination(transform.position, botTarget.transform.position,
                    transform.forward, settings.carrierSafeDistance);
        }
        if (TryGetBotFlagPickupGoal(flag, out Vector3 flagDestination, out float flagRadius))
        { destination = flagDestination; pickupGoal = true; goalRadius = flagRadius; encircling = false; }
        if (botHealthPickup != null && botHealthPickup.IsAvailableHealth(Runner))
        { destination = botHealthPickup.BotPickupPoint; pickupGoal = true; goalRadius = botHealthPickup.BotPickupRadius; encircling = false; }
        bool ramAllowed = !retreating && !pickupGoal && hasEnemy &&
            (!escorting || (botTarget.LockAimPoint - botCarrier.LockAimPoint).sqrMagnitude <=
                settings.escortDistance * settings.escortDistance * 4f);
        bool evading = TryBotEvasionDestination(settings, out Vector3 evadeDestination, out bool evasionDash);
        if (evading) { CancelBotRam(); destination = evadeDestination; encircling = false; }
        else if (TryBotRamDestination(settings, ramAllowed, out Vector3 ramDestination))
        { destination = ramDestination; encircling = false; }
        if (botBoundary != null) destination = botBoundary.ClampBotPosition(destination);
        if (!evading && botRamTarget == null && TryBotPortalRoute(destination, settings, out Vector3 portalEntry))
        { destination = portalEntry; pickupGoal = true; goalRadius = .1f; encircling = false; }
        Vector3 direction = destination - transform.position;
        if (direction.sqrMagnitude < .001f) direction = transform.forward;
        direction.Normalize();
        direction = BotRouteDirection(destination, direction, settings, out bool avoidingWall, pickupGoal, goalRadius);
        if (botBoundary != null && botBoundary.TryGetBotFlightBounds(out var flightBounds))
        {
            float lookAhead = Mathf.Max(8f, Mathf.Abs(Speed) * Mathf.Max(1.5f, settings.obstacleLookAheadSeconds));
            Vector3 projected = transform.position + direction * lookAhead;
            if (!flightBounds.Contains(projected))
            {
                Vector3 inward = flightBounds.ClosestPoint(projected) - transform.position;
                if (inward.sqrMagnitude < 4f) inward = flightBounds.center - transform.position;
                direction = inward.normalized;
                avoidingWall = true;
            }
        }
        bool firingFromSector = encircling && !avoidingWall &&
            (LockAimPoint - (botTarget.LockAimPoint + botAttackSlotOffset)).sqrMagnitude <= botAttackArrivalDistance * botAttackArrivalDistance;
        // Face inward and brake at the assigned sector so lock-on spells can charge without chasing the tail.
        if (firingFromSector) direction = (botTarget.LockAimPoint - LockAimPoint).normalized;
        input.aimDirection = direction; input.aimUp = Vector3.up; input.steerToAim = true;
        input.portalExitSequence = PortalExitSequence;
        int desiredStage = avoidingWall || Vector3.Dot(transform.forward, direction) < .4f ||
            Vector3.Distance(transform.position, destination) < settings.pickupApproachDistance ? 1 : 3;
        if (pickupGoal && Vector3.Distance(LockAimPoint, destination) < goalRadius * .75f) desiredStage = 0;
        if (firingFromSector) desiredStage = 0;
        if (evading && !avoidingWall) desiredStage = 3;
        if (botRamTarget != null && !avoidingWall && Vector3.Dot(transform.forward, direction) >= .85f)
            desiredStage = 3;
        botSpeedPulse = !botSpeedPulse;
        if (botSpeedPulse && SpeedStage < desiredStage) input.buttons.Set(PlayerInputButton.Accelerate, true);
        if (botSpeedPulse && SpeedStage > desiredStage) input.buttons.Set(PlayerInputButton.Decelerate, true);
        ApplyBotCombatDash(settings, avoidingWall, pickupGoal, evading, evasionDash, desiredStage, destination, direction, ref input);
        input.magicAimDirection = hasEnemy ? (botTarget.LockAimPoint - MagicCastPosition).normalized : transform.forward;
        input.areaAimPoint = hasEnemy ? botTarget.LockAimPoint : destination;
        input.hasAreaAimPoint = hasEnemy;
        if (hasEnemy) input.lockTarget = botTarget.Object.Id;
        var stats = SelectedMagicStats;
        if (stats.TargetsAllies) AimBotSupport(ref input);
        if (IsMagicStormActive) return true;
        if (botReleaseFrame) { botReleaseFrame = false; return true; }
        if (botHolding)
        {
            botHeldTime += dt;
            bool castTargetAlive = stats.TargetsAllies ? IsBotCombatant(botCastAlly) : hasEnemy;
            bool release = !castTargetAlive || (stats.requiresTarget ? IsFullyLocked || botHeldTime > 3f :
                botHeldTime >= (stats.IsChanneled ? 1.5f : .35f));
            if (!release) input.buttons.Set(PlayerInputButton.Lock, true);
            else { botHolding = false; botReleaseFrame = true; botAttackTime = settings.attackPauseSeconds; }
            return true;
        }
        if (botAttackTime > 0 || pendingMagic != MagicType.None) return true;
        if (BladeCharges > 0)
        {
            if (hasEnemy)
            {
                input.buttons.Set(PlayerInputButton.Lock, true);
                botReleaseFrame = true;
                botAttackTime = Mathf.Max(.2f, bladeStats.bladeShotInterval);
            }
            return true;
        }
        // Alternate slots after a complete release, preserving normal cast/cooldown validation.
        SelectMagicSlot(CurrentMagicSlot == 1 ? 2 : 1);
        stats = SelectedMagicStats;
        botAttackTime = settings.attackPauseSeconds;
        bool support = stats.magic == MagicType.Healing || stats.magic == MagicType.Flare || stats.magic == MagicType.Smoke || stats.magic == MagicType.MeowFestival || stats.magic == MagicType.TimeStop;
        if (stats.TargetsAllies)
        {
            botCastAlly = ChooseBotSupportTarget(stats);
            if (botCastAlly == null) return true;
            AimBotSupport(ref input);
        }
        else
        {
            if (!hasEnemy || (!support && Vector3.Distance(LockAimPoint, botTarget.LockAimPoint) > stats.range)) return true;
            // The previous slot may have been a heal: reset its ally aim after switching to an attack.
            input.lockTarget = botTarget.Object.Id;
            input.magicAimDirection = (botTarget.LockAimPoint - MagicCastPosition).normalized;
        }
        if (stats.magic == MagicType.Flare && !HasIncomingBotThreat()) return true;
        if (TimerIsActive(MagicCooldowns[(int)stats.magic]) || NowAp < stats.apCost) return true;
        input.buttons.Set(PlayerInputButton.Lock, true);
        botHolding = stats.requiresTarget || stats.IsChanneled || stats.effect == MagicEffectKind.DelayedArea;
        botHeldTime = 0;
        if (!botHolding) botReleaseFrame = true;
        return true;
    }

    private BattleItemPickup FindBotHealthPickup(MatchBotSettings settings)
    {
        BattleItemPickup best = null;
        float bestScore = float.PositiveInfinity;
        foreach (var item in BattleItemPickup.ActiveItems)
        {
            if (item == null || !item.IsAvailableHealth(Runner)) continue;
            float distance = Vector3.Distance(LockAimPoint, item.BotPickupPoint);
            if (distance > settings.healthSearchDistance) continue;
            float danger = 0f;
            foreach (var enemy in ActiveCombatants)
            {
                if (enemy == null || enemy.Object == null || !enemy.Object.IsValid || enemy.Runner != Runner ||
                    !enemy.IsAlive || enemy.TeamIndex == TeamIndex) continue;
                danger += Mathf.Max(0f, settings.carrierSafeDistance - Vector3.Distance(enemy.LockAimPoint, item.BotPickupPoint));
            }
            float score = distance + danger * 2f;
            if (score < bestScore) { bestScore = score; best = item; }
        }
        return best;
    }

    private void ApplyBotAimError(ref NetworkInputData input)
    {
        var stats = SelectedMagicStats;
        if (botTarget == null || botTarget.Object == null || !botTarget.Object.IsValid ||
            botTarget.Runner != Runner || !botTarget.IsAlive || stats.requiresTarget || stats.TargetsAllies) return;
        var settings = MatchBotSettings.Default;
        botAimRefresh -= Runner.DeltaTime;
        bool pressed = input.buttons.IsSet(PlayerInputButton.Lock) && !previousButtons.IsSet(PlayerInputButton.Lock);
        if (pressed || botAimRefresh <= 0f || botAimTarget != botTarget || botAimMagic != stats.magic)
        {
            botAimRefresh = Mathf.Max(.1f, settings.aimCorrectionSeconds);
            botAimTarget = botTarget; botAimMagic = stats.magic;
            botAimMiss = Random.value < settings.nonTargetMissChance;
            Vector2 sample = Random.insideUnitCircle;
            if (sample.sqrMagnitude < .001f) sample = Vector2.right;
            botAimOffset = botAimMiss ? sample.normalized * Random.Range(.75f, 1.25f) : sample;
        }
        Vector3 forward = (botTarget.LockAimPoint - MagicCastPosition).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        if (right.sqrMagnitude < .01f) right = transform.right;
        Vector3 up = Vector3.Cross(forward, right).normalized;
        float degrees = botAimMiss ? settings.missAngleDegrees : settings.aimSpreadDegrees;
        input.magicAimDirection = (forward + (right * botAimOffset.x + up * botAimOffset.y) *
            Mathf.Tan(Mathf.Clamp(degrees, 0f, 60f) * Mathf.Deg2Rad)).normalized;
        if (stats.effect == MagicEffectKind.DelayedArea)
        {
            float radius = botAimMiss ? Mathf.Max(3f, stats.radius * settings.areaMissRadiusMultiplier) : 1f;
            input.areaAimPoint = botTarget.LockAimPoint + (right * botAimOffset.x + up * botAimOffset.y) * radius;
        }
        // Guided channels normally auto-correct within a generous cone. A missed
        // tracking interval must actually lose its target, not silently snap back.
        if (stats.effect == MagicEffectKind.GuidedChannel && botAimMiss) input.lockTarget = default;
    }

    private bool HasIncomingBotThreat()
    {
        foreach (var shot in MagicProjectile.ActiveProjectiles)
            if (shot != null && shot.Object != null && shot.Object.IsValid && shot.Runner == Runner &&
                !shot.Finished && !shot.Settled && shot.ShooterTeam != TeamIndex && shot.TargetId == Object.Id &&
                (shot.transform.position - LockAimPoint).sqrMagnitude < 10000f) return true;
        return false;
    }
}
