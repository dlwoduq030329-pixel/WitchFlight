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
        botDecisionTime -= dt; botAttackTime -= dt;
        if (botDecisionTime <= 0)
        {
            botDecisionTime = Mathf.Max(.1f, settings.reactionSeconds);
            botTarget = null;
            float best = float.PositiveInfinity;
            foreach (var enemy in ActiveCombatants)
            {
                if (enemy == null || enemy.Object == null || !enemy.Object.IsValid || enemy.Runner != Runner ||
                    !enemy.IsAlive || enemy.TeamIndex == TeamIndex) continue;
                float score = (enemy.transform.position - transform.position).sqrMagnitude;
                if (BattleFlag.Instance != null && BattleFlag.Instance.Carrier == enemy.ParticipantRef) score *= .1f;
                if (score < best) { best = score; botTarget = enemy; }
            }
            PlayerData.FindPerkData(Runner, ParticipantRef)?.ChooseBotPerk();
            float health = MaxHp > 0f ? NowHp / MaxHp : 1f;
            if (health <= settings.seekHealthBelow) botRecovering = true;
            else if (health >= Mathf.Max(settings.seekHealthBelow, settings.resumeCombatAbove)) botRecovering = false;
            botHealthPickup = botRecovering ? FindBotHealthPickup(settings) : null;
        }
        bool hasEnemy = botTarget != null && botTarget.Object != null && botTarget.Object.IsValid && botTarget.IsAlive;
        var flag = BattleFlag.Instance;
        bool carrying = flag != null && flag.Carrier == ParticipantRef;
        bool retreating = botRecovering || carrying;
        bool pickupGoal = false;
        var botBoundary = BattleManager.Instance != null ? BattleManager.Instance.MapBoundary : null;
        float goalRadius = .85f;
        Vector3 destination = hasEnemy ? botTarget.transform.position - botTarget.transform.forward * settings.tailDistance
            : transform.position + transform.forward * 30;
        if (retreating && hasEnemy)
        {
            Vector3 away = (LockAimPoint - botTarget.LockAimPoint).normalized;
            if (away.sqrMagnitude < .01f) away = -transform.forward;
            // Keep possession rather than deliberately turning into a pursuer.
            destination = transform.position + away * settings.carrierSafeDistance;
            if (botBoundary != null)
                destination = botBoundary.BotRetreatDestination(transform.position, botTarget.transform.position,
                    transform.forward, settings.carrierSafeDistance);
        }
        if (flag != null && flag.Carrier == PlayerRef.None && !botRecovering)
        { destination = flag.transform.position; pickupGoal = true; goalRadius = flag.PickupRadius; }
        if (botHealthPickup != null && botHealthPickup.IsAvailableHealth(Runner))
        { destination = botHealthPickup.BotPickupPoint; pickupGoal = true; goalRadius = botHealthPickup.BotPickupRadius; }
        if (botBoundary != null) destination = botBoundary.ClampBotPosition(destination);
        if (TryBotPortalRoute(destination, settings, out Vector3 portalEntry))
        { destination = portalEntry; pickupGoal = true; goalRadius = .1f; }
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
        input.aimDirection = direction; input.aimUp = Vector3.up; input.steerToAim = true;
        input.portalExitSequence = PortalExitSequence;
        int desiredStage = avoidingWall || Vector3.Dot(transform.forward, direction) < .4f ||
            Vector3.Distance(transform.position, destination) < settings.pickupApproachDistance ? 1 : 3;
        if (pickupGoal && Vector3.Distance(LockAimPoint, destination) < goalRadius * .75f) desiredStage = 0;
        botSpeedPulse = !botSpeedPulse;
        if (botSpeedPulse && SpeedStage < desiredStage) input.buttons.Set(PlayerInputButton.Accelerate, true);
        if (botSpeedPulse && SpeedStage > desiredStage) input.buttons.Set(PlayerInputButton.Decelerate, true);
        if (desiredStage == 3 && NowAp > MaxAp * .65f && hasEnemy &&
            Vector3.Distance(transform.position, destination) > 50 && !pickupGoal)
            input.buttons.Set(PlayerInputButton.Boost, true);
        input.magicAimDirection = hasEnemy ? (botTarget.LockAimPoint - MagicCastPosition).normalized : transform.forward;
        input.areaAimPoint = hasEnemy ? botTarget.LockAimPoint : destination;
        input.hasAreaAimPoint = hasEnemy;
        if (hasEnemy) input.lockTarget = botTarget.Object.Id;
        if (botReleaseFrame) { botReleaseFrame = false; return true; }
        var stats = SelectedMagicStats;
        if (botHolding)
        {
            botHeldTime += dt;
            bool release = !hasEnemy || (stats.requiresTarget ? IsFullyLocked || botHeldTime > 3f :
                botHeldTime >= (stats.IsChanneled ? 1.5f : .35f));
            if (!release) input.buttons.Set(PlayerInputButton.Lock, true);
            else { botHolding = false; botReleaseFrame = true; botAttackTime = settings.attackPauseSeconds; }
            return true;
        }
        if (botAttackTime > 0 || pendingMagic != MagicType.None) return true;
        // Alternate slots after a complete release, preserving normal cast/cooldown validation.
        SelectMagicSlot(CurrentMagicSlot == 1 ? 2 : 1);
        stats = SelectedMagicStats;
        botAttackTime = settings.attackPauseSeconds;
        bool support = stats.magic == MagicType.Healing || stats.magic == MagicType.Flare || stats.magic == MagicType.Smoke;
        if (!hasEnemy || (!support && Vector3.Distance(LockAimPoint, botTarget.LockAimPoint) > stats.range)) return true;
        if (stats.magic == MagicType.Healing)
        {
            input.lockTarget = default;
            if (NowHp >= MaxHp * .75f) return true;
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
