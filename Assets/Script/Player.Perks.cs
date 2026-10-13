using System.Collections.Generic;
using Fusion;
using UnityEngine;

public partial class Player
{
    [Networked] private float PerkBaseMaxHp { get; set; }
    [Networked] private float PerkBaseMaxAp { get; set; }
    [Networked] private float PerkBaseRecovery { get; set; }
    [Networked] private TickTimer CleanseProtection { get; set; }
    [Networked] private float CleanseDurationMultiplier { get; set; }
    private PlayerData perkOwner;
    private float lifeStartMaxHp, perkClock, nextManaRemnant;
    private MagicStatEntry pendingPerkStats, channelPerkStats, flarePerkStats;
    private float flarePulseAt = -1f, flareCastAt;
    private bool flareRefunded;
    private NetworkId beamVictim;
    private float beamContact, beamLastContact = -100f;
    private readonly float[] visionFirstHit = { -100f, -100f, -100f };
    private readonly NetworkId[] visionVictim = new NetworkId[3];
    private sealed class Burn { public PlayerRef attacker; public float remaining, damagePerSecond, nextTick, interval; }
    private sealed class IceMark
    {
        public int slot, cast; public bool used; public float expires, amount, channelSeconds, channelUntil;
    }
    private readonly Dictionary<NetworkId, IceMark> iceMarks = new();
    private readonly List<Burn> perkBurns = new();
    private PerkStatTable Perks => BattleManager.Instance != null ? BattleManager.Instance.Perks : PerkStatTable.Default;
    private PlayerData PerkOwner
    {
        get
        {
            if (IsTestBot || Object == null || !Object.IsValid) return null;
            if (perkOwner == null || perkOwner.Object == null || !perkOwner.Object.IsValid || perkOwner.Runner != Runner)
                perkOwner = PlayerData.FindPerkData(Runner, Object.InputAuthority);
            return perkOwner != null && perkOwner.PerksInitialized ? perkOwner : null;
        }
    }
    private float CommonAmount(PerkId id) => PerkOwner != null && PerkOwner.HasCommonPerk(id) ? Perks.Amount(id) : 0f;
    private void InitializePerkVitals(PlayerData data)
    {
        perkOwner = data;
        PerkBaseMaxHp = MaxHp; PerkBaseMaxAp = MaxAp; PerkBaseRecovery = ApRecoveryPerSecond;
        RefreshPerkVitals(true);
        lifeStartMaxHp = MaxHp;
    }
    public void RefreshPerkVitals(bool fill)
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority || PerkBaseMaxHp <= 0f) return;
        MaxHp = PerkBaseMaxHp + CommonAmount(PerkId.GuardianKnot);
        MaxAp = PerkBaseMaxAp * (1f + CommonAmount(PerkId.DeepMana));
        ApRecoveryPerSecond = PerkBaseRecovery * (1f + CommonAmount(PerkId.ManaBreath));
        NowHp = fill ? MaxHp : Mathf.Min(NowHp, MaxHp);
        NowAp = fill ? MaxAp : Mathf.Min(NowAp, MaxAp);
        SyncHealthToPlayerData(); SyncApToPlayerData();
    }
    private MagicStatEntry ApplyPerkStats(MagicStatEntry stats, int slot)
    {
        var data = PerkOwner;
        if (data == null) return stats;
        return PerkEffects.Apply(stats, slot, data.PerkLevel(slot), data.CommonPerksMask,
            PerkBaseMaxAp > 0f ? PerkBaseMaxAp : MaxAp, Perks);
    }
    private void CancelCombatForPerks()
    {
        ClearLockTargetInternal();
        if (!Object.HasStateAuthority) return;
        StopChannel(); pendingMagic = MagicType.None; pendingTargetId = default; castTimer = TickTimer.None;
    }
    private bool CanCleanse(MagicStatEntry s) => s.perkSecond != null && s.perkSecond.id == PerkId.HealingCleanse &&
        (IsBound || TimerIsActive(SlowTimer));
    private void ApplyPerkCleanse(MagicStatEntry s)
    {
        var p = s.perkSecond;
        if (p == null || p.id != PerkId.HealingCleanse) return;
        BindingTimer = SlowTimer = TickTimer.None; SlowMultiplier = 1f;
        CleanseDurationMultiplier = 1f - Mathf.Clamp01(p.amount);
        CleanseProtection = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0f,p.seconds));
    }
    private float StatusPerkDuration(float duration) => Mathf.Max(0f,duration) *
        (TimerIsActive(CleanseProtection) ? CleanseDurationMultiplier : 1f);
    private void TickPerkEffects()
    {
        if (!Object.HasStateAuthority || !IsAlive) return;
        perkClock += Runner.DeltaTime;
        for (int i = perkBurns.Count - 1; i >= 0; i--)
        {
            Burn burn = perkBurns[i];
            if (perkClock < burn.nextTick) continue;
            float dt = Mathf.Min(burn.interval, burn.remaining);
            burn.remaining -= dt; burn.nextTick += burn.interval;
            TakeDamage(burn.damagePerSecond * dt, burn.attacker, MagicType.Fire, false, 0f, 0f);
            if (burn.remaining <= .00001f) perkBurns.RemoveAt(i);
        }
        if (flarePulseAt >= 0f && perkClock >= flarePulseAt && IsAlive)
        {
            flarePulseAt = -1f; TriggerPerkFlare(flarePerkStats);
            RPC_PresentCast(MagicType.Flare, LockAimPoint, -transform.forward, 0f);
        }
    }
    private void StartPerkFlare(MagicStatEntry stats)
    {
        flarePerkStats = stats; flareRefunded = false; flareCastAt = perkClock;
        flarePulseAt = stats.perkFirst?.id == PerkId.FlarePulse ? perkClock + Mathf.Max(0f,stats.perkFirst.seconds) : -1f;
        TriggerPerkFlare(stats);
    }
    private void TriggerPerkFlare(MagicStatEntry stats)
    {
        int broken = MagicProjectile.BreakHomingFor(this, out bool rewardable);
        if (broken == 0) return;
        if (rewardable) PerkOwner?.AwardPerkFlare();
        var p = stats.perkSecond;
        if (flareRefunded || p == null || p.id != PerkId.FlareEfficiency) return;
        flareRefunded = true;
        RestoreAp(stats.apCost * Mathf.Clamp01(p.amount));
        // Total discount includes the common cooldown perk; discount this cast, not future casts.
        float raw = magicStatTable != null ? magicStatTable.GetStats(MagicType.Flare).cooldownSeconds : stats.cooldownSeconds;
        float duration = Mathf.Max(raw * (1f - Mathf.Clamp01(Perks.cooldownDiscountCap)),
            stats.cooldownSeconds * (1f - Mathf.Clamp01(p.secondaryAmount)));
        MagicCooldowns.Set((int)MagicType.Flare, TickTimer.CreateFromSeconds(Runner, Mathf.Max(0f,duration - (perkClock-flareCastAt))));
    }
    private void TrackPerkBeam(Player target, float time, MagicStatEntry stats)
    {
        if (stats.perkSecond?.id != PerkId.BeamFocus) return;
        if (target == null || !target.IsEnemyOf(this)) return;
        if (!beamVictim.Equals(target.Object.Id) || perkClock-beamLastContact > Mathf.Max(.001f,stats.perkSecond.condition)) beamContact = 0f;
        beamVictim = target.Object.Id; beamLastContact = perkClock; beamContact += time;
    }
    // Runs after parry rejection and before damage. No client can request these bonuses.
    private MagicStatEntry IncomingPerkDamage(MagicStatEntry s, Player source, NetworkId sourceId)
    {
        if (source == null || source == this || source.TeamIndex == TeamIndex || s.damage <= 0f) return s;
        if (s.perkSecond?.id == PerkId.VisionCombo && s.perkSlot >= 1 && s.perkSlot <= 2)
        {
            int slot = s.perkSlot;
            bool second = source.visionVictim[slot].Equals(Object.Id) && source.perkClock-source.visionFirstHit[slot] <= s.perkSecond.seconds;
            if (second) s.damage *= 1f + Mathf.Max(0f,s.perkSecond.amount);
            source.visionFirstHit[slot] = second ? -100f : source.perkClock;
            source.visionVictim[slot] = Object.Id;
        }
        if (s.perkSecond?.id == PerkId.BeamFocus && source.beamVictim.Equals(Object.Id) &&
            source.beamContact >= s.perkSecond.seconds) s.damage *= 1f + Mathf.Max(0f,s.perkSecond.amount);
        if (iceMarks.TryGetValue(sourceId,out IceMark mark))
        {
            if (!mark.used && perkClock <= mark.expires && mark.slot != s.perkSlot && s.perkSlot > 0)
            { mark.used = true; mark.cast = s.perkCast; mark.channelUntil = perkClock + mark.channelSeconds; }
            if (mark.used && mark.cast == s.perkCast && (s.IsChanneled ? perkClock < mark.channelUntil : perkClock <= mark.expires))
                s.damage *= 1f + mark.amount;
            if (!s.IsChanneled && mark.used || !mark.used && perkClock > mark.expires) iceMarks.Remove(sourceId);
        }
        return s;
    }
    private void OnAcceptedPerkHit(MagicStatEntry s, Player source, NetworkId sourceId)
    {
        if (source != null && source != this && source.TeamIndex != TeamIndex)
        {
            if (source.CommonAmount(PerkId.ManaRemnant) > 0f && source.perkClock >= source.nextManaRemnant)
            {
                source.nextManaRemnant = source.perkClock + Mathf.Max(0f,source.Perks.Get(PerkId.ManaRemnant).seconds);
                source.RestoreAp(source.MaxAp * source.CommonAmount(PerkId.ManaRemnant));
            }
            if (IsAlive && s.perkSecond?.id == PerkId.IceMark)
                iceMarks[sourceId] = new IceMark { slot=s.perkSlot, expires=perkClock+s.perkSecond.seconds,
                    amount=Mathf.Max(0f,s.perkSecond.amount), channelSeconds=Mathf.Max(0f,s.perkSecond.condition) };
        }
        if (IsAlive && s.perkFirst?.id == PerkId.FireBurn && s.perkFirst.seconds > 0f)
        {
            PlayerRef attacker = source != null ? source.Object.InputAuthority : s.perkAttacker;
            Burn burn = perkBurns.Find(x => x.attacker == attacker);
            if (burn == null) { burn = new Burn {attacker=attacker}; perkBurns.Add(burn); }
            burn.remaining = s.perkFirst.seconds; burn.damagePerSecond = Mathf.Max(0f,s.perkFirst.amount) / burn.remaining;
            burn.interval = Mathf.Max(.05f,Perks.burnTickSeconds); burn.nextTick = perkClock + Mathf.Min(burn.interval,burn.remaining);
        }
    }
    private void ResetVisionMiss(MagicStatEntry stats)
    {
        if (stats.magic == MagicType.Vision && stats.perkSlot >= 1 && stats.perkSlot <= 2) visionFirstHit[stats.perkSlot] = -100f;
    }
}
