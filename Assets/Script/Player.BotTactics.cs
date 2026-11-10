using System.Collections.Generic;
using Fusion;
using UnityEngine;

public partial class Player
{
    // Host-only decisions reuse the combatant registry; no scene searches or per-tick roster allocation.
    private MatchBotRole botRole;
    private int botTeamOrder;
    private Player botCarrier, botCastAlly;
    private PlayerRef botObservedCarrier;
    private bool botEscortDuty;
    private float botEscortSpacing;
    private Player botFormationTarget;
    private Vector3 botAttackSlotOffset, botAttackRouteOffset;
    private float botAttackArrivalDistance;
    private readonly List<Player> botTacticalRoster = new(10);

    private bool IsBotCombatant(Player player) => player != null && player.Object != null &&
        player.Object.IsValid && player.Runner == Runner && player.IsAlive;

    private void RefreshBotTactics(MatchBotSettings settings)
    {
        var flag = BattleFlag.Instance;
        botObservedCarrier = flag != null ? flag.Carrier : PlayerRef.None;
        botCarrier = null;
        botTacticalRoster.Clear();
        int livingSupports = 0, totalSupports = 0;
        foreach (var data in PlayerData.PerkPlayers)
            if (data != null && data.Object != null && data.Object.IsValid && data.Runner == Runner &&
                data.teamIndex == TeamIndex && data.IsMatchBot && data.BotRole == MatchBotRole.Support) totalSupports++;
        foreach (var player in ActiveCombatants)
        {
            if (!IsBotCombatant(player)) continue;
            botTacticalRoster.Add(player);
            if (player.ParticipantRef == botObservedCarrier) botCarrier = player;
            if (player.TeamIndex == TeamIndex && player.IsMatchBot && player.botRole == MatchBotRole.Support) livingSupports++;
        }
        int fighterOrder = botTeamOrder <= 1 ? 0 : 1;
        botEscortDuty = botRole != MatchBotRole.Fighter || MatchBotRoles.CoversSupport(botRole,
            livingSupports, Mathf.Max(0, totalSupports - livingSupports), fighterOrder);
        botEscortSpacing = Mathf.Max(5f, settings.escortDistance);
        if (botRole == MatchBotRole.Support)
        {
            var first = GetMagicStats(Magic1); var second = GetMagicStats(Magic2);
            float range = first.TargetsAllies ? first.range : second.TargetsAllies ? second.range : botEscortSpacing * 2f;
            botEscortSpacing = Mathf.Min(botEscortSpacing, Mathf.Max(5f, range * .35f));
        }
        bool alliedCarrier = botCarrier != null && botCarrier.TeamIndex == TeamIndex;
        Vector3 anchor = botCarrier != null ? botCarrier.LockAimPoint : flag != null ? flag.transform.position : LockAimPoint;
        botTarget = null;

        // Objective/support wings focus the carrier regardless of nearer distractions.
        if (botEscortDuty && botCarrier != null && !alliedCarrier) { botTarget = botCarrier; return; }

        int bestPriority = int.MaxValue;
        float bestScore = float.PositiveInfinity;
        foreach (var enemy in botTacticalRoster)
        {
            if (enemy.TeamIndex == TeamIndex) continue;
            float fromAnchor = (enemy.LockAimPoint - anchor).sqrMagnitude;
            if (alliedCarrier && botCarrier != this && botEscortDuty &&
                fromAnchor > settings.escortThreatRange * settings.escortThreatRange) continue;
            if (!botEscortDuty && fromAnchor > settings.fighterLeashDistance * settings.fighterLeashDistance) continue;
            bool threatening = IsBotEscortThreat(enemy, anchor, settings);
            int neighbours = 0;
            if (!botEscortDuty)
                foreach (var other in botTacticalRoster)
                    if (other != enemy && other.TeamIndex == enemy.TeamIndex &&
                        (other.LockAimPoint - enemy.LockAimPoint).sqrMagnitude <= settings.isolationRadius * settings.isolationRadius) neighbours++;
            int priority = botEscortDuty ? (threatening ? 0 : 1) :
                threatening ? 0 : enemy == botCarrier ? 3 : neighbours == 0 ? 1 : 2;
            float score = Vector3.Distance(LockAimPoint, enemy.LockAimPoint) + neighbours * settings.isolationRadius +
                (enemy.MaxHp > 0f ? enemy.NowHp / enemy.MaxHp : 1f) * 20f;
            if (priority < bestPriority || (priority == bestPriority && (score < bestScore ||
                (Mathf.Approximately(score, bestScore) && botTarget != null && enemy.ParticipantRef.RawEncoded < botTarget.ParticipantRef.RawEncoded))))
            { bestPriority = priority; bestScore = score; botTarget = enemy; }
        }
    }

    private void RefreshBotEncirclement(MatchBotSettings settings)
    {
        botFormationTarget = null;
        if (!IsBotCombatant(botTarget)) return;
        int count = 1, slot = 0;
        foreach (var ally in botTacticalRoster)
        {
            if (ally == this || !ally.IsMatchBot || ally.TeamIndex != TeamIndex || ally.botTarget != botTarget ||
                ally.botRecovering || ally == botCarrier) continue;
            // Close escorts stay with their carrier; do not reserve an attack sector they won't use.
            if (botCarrier != null && botCarrier.TeamIndex == TeamIndex && ally.botEscortDuty) continue;
            count++;
            if (ally.ParticipantRef.RawEncoded < ParticipantRef.RawEncoded) slot++;
        }
        float radius = Mathf.Max(5f, settings.encirclementRadius);
        var first = GetMagicStats(Magic1); var second = GetMagicStats(Magic2);
        if (MatchBotRoles.IsAttack(Magic1) && first.range > 0f)
            radius = Mathf.Min(radius, (first.requiresTarget ? Mathf.Min(maxLockDistance, first.range) * LockRangeMultiplier : first.range) * .65f);
        if (MatchBotRoles.IsAttack(Magic2) && second.range > 0f)
            radius = Mathf.Min(radius, (second.requiresTarget ? Mathf.Min(maxLockDistance, second.range) * LockRangeMultiplier : second.range) * .65f);
        radius = Mathf.Max(.1f, radius);
        // World-relative sectors don't rotate when the target spins. Sorted IDs keep peers in distinct sectors.
        botAttackSlotOffset = BotEncirclement.SlotOffset(slot, count, radius, settings.encirclementHeight, botTarget.ParticipantRef.RawEncoded);
        botAttackRouteOffset = BotEncirclement.ApproachOffset(LockAimPoint - botTarget.LockAimPoint, botAttackSlotOffset, radius);
        Vector3 lead = botTarget.transform.forward * Mathf.Clamp(botTarget.Speed * settings.encirclementLeadSeconds, -radius * .2f, radius * .2f);
        botAttackSlotOffset += lead;
        botAttackRouteOffset += lead;
        botAttackArrivalDistance = Mathf.Min(settings.encirclementArrivalDistance, radius * .25f);
        botFormationTarget = botTarget;
    }

    private bool IsBotEscortThreat(Player enemy, Vector3 anchor, MatchBotSettings settings)
    {
        foreach (var ally in botTacticalRoster)
        {
            if (ally.TeamIndex != TeamIndex || (ally.LockAimPoint - anchor).sqrMagnitude >
                settings.escortThreatRange * settings.escortThreatRange) continue;
            if (enemy.LockTargetId == ally.Object.Id) return true;
            Vector3 aim = ally.LockAimPoint - enemy.LockAimPoint;
            bool attacking = enemy.previousButtons.IsSet(PlayerInputButton.Lock) || enemy.pendingMagic != MagicType.None || enemy.IsMagicStormActive;
            if (attacking && aim.sqrMagnitude < settings.escortThreatRange * settings.escortThreatRange &&
                Vector3.Dot(enemy.castAimDirection.normalized, aim.normalized) > .94f) return true;
        }
        return false;
    }

    private Vector3 BotEscortPosition(Player carrier, MatchBotSettings settings)
    {
        // Spread escorts beside/behind the carrier instead of stacking on one point.
        float side = botTeamOrder % 2 == 0 ? -1f : 1f;
        float row = 1f + botTeamOrder / 2 * .4f;
        float distance = botEscortSpacing > 0f ? botEscortSpacing : Mathf.Max(5f, settings.escortDistance);
        return carrier.transform.position - carrier.transform.forward * distance * row +
            carrier.transform.right * distance * .6f * side;
    }

    private bool NeedsBotSupport(Player ally, MagicStatEntry stats)
    {
        if (!IsBotCombatant(ally) || ally.TeamIndex != TeamIndex) return false;
        float range = Mathf.Min(maxLockDistance, stats.range) * LockRangeMultiplier;
        if ((ally.transform.position - transform.position).sqrMagnitude > range * range) return false;
        if (stats.magic == MagicType.Healing && ally.NowHp >= ally.MaxHp * .85f) return false;
        if (stats.magic == MagicType.MeowFestival && ally.HasMeowFestival) return false;
        return ally == this || HasLineOfSight(ally);
    }

    private Player ChooseBotSupportTarget(MagicStatEntry stats)
    {
        if (NeedsBotSupport(botCarrier, stats)) return botCarrier;
        Player recipient = null;
        float best = float.PositiveInfinity;
        foreach (var ally in botTacticalRoster)
        {
            if (!NeedsBotSupport(ally, stats)) continue;
            float score = (ally.MaxHp > 0f ? ally.NowHp / ally.MaxHp : 1f) * 100f +
                Vector3.Distance(LockAimPoint, ally.LockAimPoint) * .05f;
            if (score < best) { best = score; recipient = ally; }
        }
        return recipient;
    }

    private void AimBotSupport(ref NetworkInputData input)
    {
        input.lockTarget = botCastAlly != this && IsBotCombatant(botCastAlly) ? botCastAlly.Object.Id : default;
        input.magicAimDirection = botCastAlly != this && IsBotCombatant(botCastAlly)
            ? (botCastAlly.LockAimPoint - MagicCastPosition).normalized : transform.forward;
    }
}
