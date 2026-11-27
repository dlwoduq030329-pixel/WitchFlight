using System.Collections.Generic;
using Fusion;
using UnityEngine;

public partial class Player
{
    private readonly Dictionary<NetworkId,MagicProjectile> botParryHandled = new();
    private readonly List<NetworkId> botParryExpired = new();
    private TickTimer botParryPressAt, botParryScanAt, botParryLateExpiry;
    private MagicProjectile botParryThreat;
    private NetworkId botParryThreatId;
    private bool botParryPending, botParryLate;
    private int botParryRound = -1, botParryPortal = -1, botParryDeath = -1;
    private bool botParryAwaitingOutcome;
    private float botParryProbabilityOffset;
    private int botParryPendingTrial, botParryActiveTrial;
    private float BotParryCurrentProbability => Mathf.Clamp01
        (MatchBotSettings.Default.parryGoodTimingChance + botParryProbabilityOffset);
    private void CancelBotParryTelemetry(string reason)
    {
        if (botParryPendingTrial == 0 && botParryActiveTrial == 0) return;
        BotParryTelemetry.Finish(botParryPendingTrial, reason, BotParryCurrentProbability);
        BotParryTelemetry.Finish(botParryActiveTrial, reason, BotParryCurrentProbability);
        botParryPendingTrial = botParryActiveTrial = 0;
    }

    private void ResolveBotParryOutcome(bool success)
    {
        if (!IsMatchBot || !Object.HasStateAuthority || !botParryAwaitingOutcome) return;
        botParryAwaitingOutcome = false;
        AdjustBotParryProbability(success);
        BotParryTelemetry.Finish(botParryActiveTrial, success ? "blocked" : "missed", BotParryCurrentProbability);
        botParryActiveTrial = 0;
    }

    private void AdjustBotParryProbability(bool success)
    {
        var settings = MatchBotSettings.Default;
        float baseline = Mathf.Clamp01(settings.parryGoodTimingChance); // baseline == settings.parryGood == 0.5f
        botParryProbabilityOffset = Mathf.Clamp(botParryProbabilityOffset +
            (baseline - (success ? 1f : 0f)) * settings.parryOutcomeCorrection, -baseline, 1f - baseline);
        // settings.parryOutcomeCorrection == 0.2f
    }

    private void UpdateBotParry(ref NetworkInputData input)
    {
        var settings = MatchBotSettings.Default;
        // Count an actual started parry once, even if it blocks several projectiles.
        if (botParryAwaitingOutcome && (!IsParrying || botParryDeath != DeathSequence))
            ResolveBotParryOutcome(false);
        int round = BattleManager.Instance != null ? BattleManager.Instance.RoundNumber : 0;
        if (!settings.useParry || botParryRound != round || botParryPortal != PortalExitSequence || botParryDeath != DeathSequence)
        {
            CancelBotParryTelemetry("context_reset");
            botParryPending = false; botParryThreat = null; botParryHandled.Clear();
            botParryPressAt = botParryScanAt = botParryLateExpiry = TickTimer.None;
            botParryRound = round; botParryPortal = PortalExitSequence; botParryDeath = DeathSequence;
            if (!settings.useParry) return;
        }
        if (botParryPending)
        {
            // A late reaction must survive the projectile's impact/despawn, otherwise
            // the bot would never visibly press too late. Stun/death still prevent input normally.
            bool live = botParryThreat != null && botParryThreat.Object != null && botParryThreat.Object.IsValid &&
                botParryThreat.Object.Id == botParryThreatId && !botParryThreat.Finished;
            if (!botParryLate && !live)
            {
                BotParryTelemetry.Finish(botParryPendingTrial, "threat_gone", BotParryCurrentProbability);
                botParryPendingTrial = 0; botParryPending = false; return;
            }
            if (!botParryPressAt.Expired(Runner)) return;
            if (previousButtons.IsSet(PlayerInputButton.Parry)) return; // One-frame input edge.
            // Preserve a delayed press through a short hit stun, but never wait indefinitely.
            if (botParryLate && IsHitStunned && !botParryLateExpiry.Expired(Runner)) return;
            botParryPending = false;
            if (CanStartParryNow && !IsParrying) input.buttons.Set(PlayerInputButton.Parry,true);
            else
            {
                BotParryTelemetry.Finish(botParryPendingTrial, "unavailable", BotParryCurrentProbability);
                botParryPendingTrial = 0;
            }
            return;
        }
        // A requested input was not accepted by ProcessInput on the previous tick.
        if (botParryPendingTrial != 0)
        {
            BotParryTelemetry.Finish(botParryPendingTrial, "input_rejected", BotParryCurrentProbability);
            botParryPendingTrial = 0;
        }
        if (!CanStartParryNow || IsParrying || !botParryScanAt.ExpiredOrNotRunning(Runner)) return;
        botParryScanAt = TickTimer.CreateFromSeconds(Runner,.08f);
        botParryExpired.Clear();
        foreach(var entry in botParryHandled)
            if(entry.Value == null || entry.Value.Object == null || !entry.Value.Object.IsValid ||
                entry.Value.Object.Id != entry.Key || entry.Value.Finished) botParryExpired.Add(entry.Key);
        foreach(var id in botParryExpired) botParryHandled.Remove(id);
        float horizon = Mathf.Max(.1f,settings.parryLookAheadSeconds);
        if (!MagicProjectile.TryGetIncomingThreat(this,settings.parryDetectionRange,out _,out var threat,horizon,
            shot => !botParryHandled.ContainsKey(shot.Object.Id) && shot.TryGetImpactTime(this,out float eta) && eta <= horizon)) return;
        if (!threat.TryGetImpactTime(this,out float impact)) return;
        float reaction = Mathf.Max(Runner.DeltaTime,settings.parryReactionSeconds);
        float minError = Mathf.Max(Runner.DeltaTime*2,settings.parryMistakeSeconds.x);
        float error = Random.Range(minError,Mathf.Max(minError,settings.parryMistakeSeconds.y));
        float probability = BotParryCurrentProbability;
        float roll = Random.value;
        bool good = roll < probability;
        if (!good && Random.value < settings.parrySkipShareOfMistakes)
        {
            // This threat is intentionally ignored: do not reroll it on the next scan.
            botParryHandled[threat.Object.Id] = threat;
            int skippedTrial = BotParryTelemetry.Begin(ParticipantRef.ToString() + "/" + Object.Id,
                threat.Object.Id.ToString(), settings.parryGoodTimingChance, settings.parryOutcomeCorrection,
                botParryProbabilityOffset, probability, roll, good, "skip", impact);
            // Skipping is a failed decision, not a started parry (no input, MP or cooldown).
            AdjustBotParryProbability(false);
            BotParryTelemetry.Finish(skippedTrial, "skipped", BotParryCurrentProbability);
            return;
        }
        bool early = !good && Random.value < settings.parryEarlyShareOfMistakes;
        float lead = good ? ParryWindowSeconds*Random.Range(.35f,.65f) : early ? ParryWindowSeconds+error : -error;
        // Already too close to press early: make this a late mistake instead of silently succeeding.
        if (early && impact-lead < reaction) { early = false; lead = -error; }
        botParryPendingTrial = BotParryTelemetry.Begin(ParticipantRef.ToString() + "/" + Object.Id,
            threat.Object.Id.ToString(), settings.parryGoodTimingChance, settings.parryOutcomeCorrection,
            botParryProbabilityOffset, probability, roll, good, good ? "good" : early ? "early" : "late", impact);
        botParryLate = !good && !early;
        botParryThreat = threat; botParryThreatId = threat.Object.Id;
        botParryHandled[botParryThreatId] = threat;
        botParryPressAt = TickTimer.CreateFromSeconds(Runner,Mathf.Max(reaction,impact-lead));
        botParryLateExpiry = TickTimer.CreateFromSeconds(Runner,Mathf.Max(reaction,impact-lead)+.65f);
        botParryPending = true;
    }
}
