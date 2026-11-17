using Fusion;
using UnityEngine;

public partial class Player
{
    [Networked] private TickTimer FestivalTimer { get; set; }
    [Networked] public float FestivalRadius { get; private set; }
    [Networked] private NetworkBool FestivalClimax { get; set; }
    [Networked] private float FestivalAllySpeed { get; set; }
    [Networked, Capacity(20)] private NetworkArray<NetworkId> FestivalHealed => default;
    [Networked] private int FestivalHealedCount { get; set; }
    [Networked] private float FestivalSpeedBonus { get; set; }
    [Networked] private int FestivalGuardCharges { get; set; }
    [Networked] public Vector3 FestivalOrigin { get; private set; }
    [Networked] public int FestivalSequence { get; private set; }
    private float festivalHealRate, festivalBudget, festivalHealElapsed;
    private PlayerRef festivalHealer;
    private MeowFestivalVisual festivalVisual;
    public bool HasMeowFestival => networkSpawned && Object != null && Object.IsValid && IsAlive && TimerIsActive(FestivalTimer);
    public bool HasFestivalGuard => HasMeowFestival && FestivalGuardCharges > 0;
    private float FestivalRecoveryBonus => 1f;
    private bool ContainsFestivalAlly(Player ally) => ally != null && ally.Object != null && ally.Object.IsValid &&
        ally.IsAlive && ally.Runner == Runner && ally.TeamIndex == TeamIndex &&
        (ally.LockAimPoint - LockAimPoint).sqrMagnitude <= FestivalRadius * FestivalRadius;
    private float FestivalMovementMultiplier
    {
        get
        {
            float bonus = HasMeowFestival ? FestivalSpeedBonus : 0f;
            foreach (var zone in combatants)
            {
                if (zone == null || !zone.HasMeowFestival || zone.FestivalAllySpeed <= bonus || !zone.ContainsFestivalAlly(this)) continue;
                for (int i = 0; i < zone.FestivalHealedCount; i++)
                    if (zone.FestivalHealed[i] == Object.Id) { bonus = zone.FestivalAllySpeed; break; }
            }
            return 1f + bonus;
        }
    }
    private bool HasFestivalClimaxProtection()
    {
        foreach (var zone in combatants)
            if (zone != null && zone.HasMeowFestival && zone.FestivalClimax && zone.ContainsFestivalAlly(this)) return true;
        return false;
    }


    private void ApplyMeowFestival(Player caster, MagicStatEntry entry)
    {
        if (!Object.HasStateAuthority || !IsAlive) return;
        festivalHealElapsed = 0f;
        FestivalHealedCount = 0;
        festivalBudget = Mathf.Max(0f, entry.effectDuration);
        FestivalTimer = TickTimer.CreateFromSeconds(Runner, festivalBudget);
        FestivalRadius = Mathf.Max(0f, entry.radius);
        FestivalClimax = entry.perkSecond?.id == PerkId.FestivalGuard;
        FestivalAllySpeed = entry.perkFirst?.id == PerkId.FestivalSpeed ? Mathf.Max(0f, entry.perkFirst.amount) : 0f;
        festivalHealRate = Mathf.Max(0f, entry.festivalHealPerSecond);
        FestivalSpeedBonus = Mathf.Max(0f, entry.festivalTargetSpeedBonus);
        FestivalGuardCharges = 0;
        festivalHealer = caster.ParticipantRef;
        FestivalOrigin = caster.MagicCastPosition;
        FestivalSequence++;
    }
    private void TickMeowFestival()
    {
        if (!Object.HasStateAuthority || !IsAlive || festivalBudget <= 0f) return;
        float seconds = Mathf.Min(Runner.DeltaTime, festivalBudget);
        festivalBudget = Mathf.Max(0f, festivalBudget - seconds);
        festivalHealElapsed += seconds;
        if (festivalHealElapsed >= 1f || festivalBudget <= .0001f)
        {
            float healingSeconds = festivalBudget <= .0001f ? festivalHealElapsed : Mathf.Floor(festivalHealElapsed);
            foreach (var ally in combatants)
            {
                if (!ContainsFestivalAlly(ally)) continue;
                float before = ally.NowHp;
                ally.RestoreHealth(festivalHealRate * healingSeconds, festivalHealer, false);
                if (ally.NowHp <= before || FestivalAllySpeed <= 0f) continue;
                bool recorded = false;
                for (int i = 0; i < FestivalHealedCount; i++)
                    if (FestivalHealed[i] == ally.Object.Id) { recorded = true; break; }
                if (!recorded && FestivalHealedCount < FestivalHealed.Length)
                    FestivalHealed.Set(FestivalHealedCount++, ally.Object.Id);
            }
            festivalHealElapsed = Mathf.Max(0f, festivalHealElapsed - healingSeconds);
        }
    }
    private bool TryBlockFestivalDamage()
    {
        if (!Object.HasStateAuthority || !HasMeowFestival || FestivalGuardCharges <= 0) return false;
        FestivalGuardCharges--;
        RPC_PlayBattleSound(BattleSound.ParrySuccess);
        return true;
    }

    private void ResetMeowFestival()
    {
        FestivalTimer = TickTimer.None;
        FestivalHealedCount = 0;
        FestivalClimax = false;
        FestivalAllySpeed = 0f;
        FestivalSpeedBonus = 0f;
        FestivalGuardCharges = 0;
        festivalBudget = festivalHealElapsed = 0f;
    }
    private void RenderMeowFestival()
    {
        bool active = HasMeowFestival && BattleManager.Instance != null && BattleManager.Instance.IsGameplayActive;
        if (active && festivalVisual == null)
        {
            var root = new GameObject("Meow Festival visuals");
            root.transform.SetParent(transform, false);
            festivalVisual = root.AddComponent<MeowFestivalVisual>();
            festivalVisual.Initialize(this, GetMagicStats(MagicType.MeowFestival));
        }
        if (festivalVisual != null) festivalVisual.Show(active);
    }
}
