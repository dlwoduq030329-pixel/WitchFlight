using System.Collections.Generic;
using Fusion;
using UnityEngine;

public partial class Player
{
    [Networked] private float PerkBaseMaxHp { get; set; }
    [Networked] private float PerkBaseMaxAp { get; set; }
    [Networked] private float PerkBaseRecovery { get; set; }
    [Networked] private float BindingDamageBonus { get; set; }
    [Networked] private TickTimer CurseSlowTimer { get; set; }
    [Networked] private float CurseSpeedMultiplier { get; set; }
    [Networked] private TickTimer CleanseProtection { get; set; }
    [Networked] private float CleanseDurationMultiplier { get; set; }
    [Networked] protected bool HasBurnStatus { get; set; }
    [Networked] private TickTimer FlareProtection { get; set; }
    public bool IsFlareActive => Object != null && Object.IsValid && IsAlive && TimerIsActive(FlareProtection);
    public float LockRangeMultiplier => CombatSmoke.ContainsEnemy(Runner, LockAimPoint, TeamIndex) ? .7f : 1f;
    public float LockDurationMultiplierAgainst(Player target) => CombatSmoke.LockDurationMultiplier(Runner, LockAimPoint, TeamIndex);
    private PlayerData perkOwner;
    private float lifeStartMaxHp, nextManaRemnant;
    private float timeFieldHealing, timeFieldHealElapsed;
    private PlayerRef timeFieldHealer;

    private void TickTimeFieldHealing()
    {
        if (!Object.HasStateAuthority) return;
        float rate = MagicProjectile.TimeHealingRate(Runner, transform.position, TeamIndex, out PlayerRef healer);
        if (timeFieldHealing > 0f && (rate <= 0f || healer != timeFieldHealer))
        {
            RestoreHealth(timeFieldHealing, timeFieldHealer);
            timeFieldHealing = timeFieldHealElapsed = 0f;
        }
        if (rate <= 0f) return;
        timeFieldHealer = healer;
        timeFieldHealing += rate * Runner.DeltaTime;
        timeFieldHealElapsed += Runner.DeltaTime;
        if (timeFieldHealElapsed < .5f) return;
        RestoreHealth(timeFieldHealing, timeFieldHealer);
        timeFieldHealing = timeFieldHealElapsed = 0f;
    }
    protected float perkClock;
    private MagicStatEntry pendingPerkStats, channelPerkStats;
    private NetworkId beamVictim;
    private float beamContact, beamLastContact = -100f;
    private readonly float[] visionFirstHit = { -100f, -100f, -100f };
    private readonly NetworkId[] visionVictim = new NetworkId[3];
    protected sealed class Burn { public PlayerRef attacker; public float remaining, damagePerSecond, nextTick, interval; }
    private sealed class IceMark
    {
        public int slot, cast; public bool used; public float expires, amount, channelSeconds, channelUntil;
    }
    private readonly Dictionary<NetworkId, IceMark> iceMarks = new();
    protected readonly List<Burn> perkBurns = new();
    private PerkStatTable Perks => BattleManager.Instance != null ? BattleManager.Instance.Perks : PerkStatTable.Default;
    private PlayerData PerkOwner
    {
        get
        {
            if (IsTestBot || Object == null || !Object.IsValid) return null;
            if (perkOwner == null || perkOwner.Object == null || !perkOwner.Object.IsValid || perkOwner.Runner != Runner)
                perkOwner = PlayerData.FindPerkData(Runner, ParticipantRef);
            return perkOwner != null && perkOwner.PerksEnabled && perkOwner.PerksInitialized ? perkOwner : null;
        }
    }
    private float CommonAmount(PerkId id) => PerkOwner != null && PerkOwner.HasCommonPerk(id) ? Perks.Amount(id) : 0f;
    private void InitializePerkVitals(PlayerData data)
    {
        FlareProtection = TickTimer.None;
        BindingDamageBonus = 0f;
        CurseSlowTimer = TickTimer.None;
        CurseSpeedMultiplier = 1f;
        perkBurns.Clear();
        timeFieldHealing = timeFieldHealElapsed = 0f;
        HasBurnStatus = false;
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
        RefreshDashPerks(fill);
        NowHp = fill ? MaxHp : Mathf.Min(NowHp, MaxHp);
        NowAp = fill ? MaxAp : Mathf.Min(NowAp, MaxAp);
        SyncHealthToPlayerData(); SyncApToPlayerData();
    }
    private MagicStatEntry ApplyPerkStats(MagicStatEntry stats, int slot)
    {
        var data = PerkOwner;
        if (data == null || !data.PerksEnabled) return stats;
        return PerkEffects.Apply(stats, slot, data.PerkLevel(slot), data.CommonPerksMask,
            PerkBaseMaxAp > 0f ? PerkBaseMaxAp : MaxAp, Perks);
    }
    private void CancelCombatForPerks()
    {
        areaAimArmed = false;
        ClearLockTargetInternal();
        if (!Object.HasStateAuthority) return;
        StopMagicStorm();
        CancelBladeRain();
        StopChannel(); pendingMagic = MagicType.None; pendingTargetId = default; castTimer = TickTimer.None;
    }
    private bool CanCleanse(MagicStatEntry s) => s.perkSecond != null && s.perkSecond.id == PerkId.HealingCleanse &&
        (IsBound || TimerIsActive(SlowTimer) || TimerIsActive(CurseSlowTimer) || TimerIsActive(WoundTimer));
    private void ApplyPerkCleanse(MagicStatEntry s)
    {
        var p = s.perkSecond;
        if (p == null || p.id != PerkId.HealingCleanse) return;
        BindingTimer = SlowTimer = WoundTimer = CurseSlowTimer = TickTimer.None; SlowMultiplier = CurseSpeedMultiplier = 1f;
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
        HasBurnStatus = perkBurns.Count > 0;
    }
    private void StartPerkFlare(MagicStatEntry stats)
    {
        if (!Object.HasStateAuthority || !magicProjectilePrefab.IsValid) return;
        FlareProtection = TickTimer.CreateFromSeconds(Runner, Mathf.Max(.1f, stats.effectDuration));
        Vector3 rear = -transform.forward;
        Vector3 origin = LockAimPoint + rear * Mathf.Max(0f, stats.flareVfxBackOffset);
        Runner.Spawn(magicProjectilePrefab, origin, Quaternion.LookRotation(rear), ParticipantRef,
            onBeforeSpawned: (runner, obj) => obj.GetComponent<MagicProjectile>().InitializeFlareField(this, stats));
    }
    private void TrackPerkBeam(Player target, float time, MagicStatEntry stats)
    {
        if (stats.perkSecond?.id != PerkId.BeamFocus) return;
        if (target == null || !target.IsEnemyOf(this)) return;
        if (!beamVictim.Equals(target.Object.Id) || perkClock-beamLastContact > Mathf.Max(.05f, stats.channelTickSeconds + Runner.DeltaTime * 2)) beamContact = 0f;
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
            float manaRefund = PerkEffects.HitManaRefund(s, source.MaxAp, source.CommonAmount(PerkId.ManaRemnant));
            if (manaRefund > 0f && source.perkClock >= source.nextManaRemnant)
            {
                source.nextManaRemnant = source.perkClock + Mathf.Max(0f,source.Perks.Get(PerkId.ManaRemnant).seconds);
                source.RestoreAp(manaRefund);
            }
            if (IsAlive && s.perkSecond?.id == PerkId.IceMark)
                iceMarks[sourceId] = new IceMark { slot=s.perkSlot, expires=perkClock+s.perkSecond.seconds,
                    amount=Mathf.Max(0f,s.perkSecond.amount), channelSeconds=Mathf.Max(0f,s.perkSecond.condition) };
        }
        if (IsAlive && s.perkSecond?.id == PerkId.CurseEfficiency)
        {
            CurseSpeedMultiplier = Mathf.Clamp(1f - s.perkSecond.amount, .05f, 1f);
            CurseSlowTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(.05f, s.channelTickSeconds + Runner.DeltaTime * 2));
        }
        PerkDefinition burnPerk = s.perkFirst?.id == PerkId.FireBurn ? s.perkFirst : null;
        if (IsAlive && source != null && s.perkSecond?.id == PerkId.BeamFocus)
        {
            source.TrackPerkBeam(this, s.channelContactSeconds, s);
            if (source.beamContact + .0001f >= s.perkSecond.condition) burnPerk = s.perkSecond;
        }
        if (IsAlive && burnPerk != null && burnPerk.seconds > 0f)
        {
            PlayerRef attacker = source != null ? source.ParticipantRef : s.perkAttacker;
            Burn burn = perkBurns.Find(x => x.attacker == attacker);
            if (burn == null) { burn = new Burn {attacker=attacker}; perkBurns.Add(burn); }
            bool starting = burn.remaining <= 0f;
            burn.remaining = burnPerk.seconds; burn.damagePerSecond = Mathf.Max(0f,burnPerk.amount) / burn.remaining;
            burn.interval = Mathf.Max(.05f,Perks.burnTickSeconds);
            if (starting) burn.nextTick = perkClock + Mathf.Min(burn.interval,burn.remaining);
            HasBurnStatus = true;
        }
    }
    private void ResetVisionMiss(MagicStatEntry stats)
    {
        if (stats.magic == MagicType.Vision && stats.perkSlot >= 1 && stats.perkSlot <= 2) visionFirstHit[stats.perkSlot] = -100f;
    }
}
