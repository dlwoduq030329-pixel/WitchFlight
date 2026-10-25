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

    private bool ReadCombatInput(out NetworkInputData input)
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
        }
        bool hasEnemy = botTarget != null && botTarget.Object != null && botTarget.Object.IsValid && botTarget.IsAlive;
        var flag = BattleFlag.Instance;
        Vector3 destination = hasEnemy ? botTarget.transform.position - botTarget.transform.forward * settings.tailDistance
            : transform.position + transform.forward * 30;
        if (flag != null && flag.Carrier == PlayerRef.None) destination = flag.transform.position;
        Vector3 direction = destination - transform.position;
        if (direction.sqrMagnitude < 4) direction = transform.forward;
        direction.Normalize();
        // Probe only solid scenery, not triggers, players or the flag. Climb around a blocking wall.
        if (Runner.GetPhysicsScene().SphereCast(LockAimPoint, .8f, direction, out var obstacle,
                settings.obstacleProbeDistance, ~0, QueryTriggerInteraction.Ignore) &&
            obstacle.collider.GetComponentInParent<Player>() == null && obstacle.collider.GetComponentInParent<BattleFlag>() == null)
            direction = (Vector3.ProjectOnPlane(direction, obstacle.normal) + Vector3.up + obstacle.normal * .6f).normalized;
        input.aimDirection = direction; input.aimUp = Vector3.up; input.steerToAim = true;
        input.portalExitSequence = PortalExitSequence;
        int desiredStage = Vector3.Dot(transform.forward, direction) < .4f ||
            Vector3.Distance(transform.position, destination) < 12 ? 1 : 3;
        botSpeedPulse = !botSpeedPulse;
        if (botSpeedPulse && SpeedStage < desiredStage) input.buttons.Set(PlayerInputButton.Accelerate, true);
        if (botSpeedPulse && SpeedStage > desiredStage) input.buttons.Set(PlayerInputButton.Decelerate, true);
        if (desiredStage == 3 && NowAp > MaxAp * .65f && hasEnemy &&
            Vector3.Distance(transform.position, destination) > 50)
            input.buttons.Set(PlayerInputButton.Boost, true);
        input.magicAimDirection = hasEnemy ? (botTarget.LockAimPoint - LockAimPoint).normalized : transform.forward;
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

    private bool HasIncomingBotThreat()
    {
        foreach (var shot in MagicProjectile.ActiveProjectiles)
            if (shot != null && shot.Object != null && shot.Object.IsValid && shot.Runner == Runner &&
                !shot.Finished && !shot.Settled && shot.ShooterTeam != TeamIndex && shot.TargetId == Object.Id &&
                (shot.transform.position - LockAimPoint).sqrMagnitude < 10000f) return true;
        return false;
    }
}
