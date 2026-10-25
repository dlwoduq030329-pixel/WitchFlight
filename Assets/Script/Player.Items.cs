using Fusion;
using UnityEngine;

public partial class Player
{
    // Replicated so input-authority movement prediction uses the same temporary bonus.
    [Networked] private TickTimer ItemBoostTimer { get; set; }
    [Networked] private float ItemBoostMultiplier { get; set; }

    public bool HasItemSpeedBoost => IsAlive && TimerIsActive(ItemBoostTimer);
    public float ItemSpeedBoostRemaining => HasItemSpeedBoost
        ? Mathf.Max(0f, ItemBoostTimer.RemainingTime(Runner) ?? 0f) : 0f;

    internal bool CanReceiveBattleItem(BattleItemType type)
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority || !IsAlive ||
            IsTestBot || IsReturningToMap || Runner == null || BattleManager.Instance == null ||
            !BattleManager.Instance.IsGameplayActive) return false;
        return type == BattleItemType.Booster ||
            (type == BattleItemType.HealthPotion && NowHp < MaxHp) ||
            (type == BattleItemType.ManaPotion && NowAp < MaxAp);
    }

    // Called only by the server's contact check; clients cannot request arbitrary heals/boosts.
    internal bool TryApplyBattleItem(BattleItemType type, float healing, float multiplier, float seconds,
        float manaRecovery = 0f)
    {
        if (!CanReceiveBattleItem(type)) return false;
        if (type == BattleItemType.HealthPotion)
        {
            if (!IsPositiveFiniteItemValue(healing)) return false;
            RestoreHealth(healing); // Existing health/HUD/PlayerData notification path.
            RPC_PlayBattleSound(BattleSound.HealthPotion);
            return true;
        }
        if (type == BattleItemType.ManaPotion)
        {
            if (!IsPositiveFiniteItemValue(manaRecovery)) return false;
            RestoreAp(manaRecovery); // Existing replicated MP/PlayerData notification path.
            RPC_PlayBattleSound(BattleSound.ManaPotion);
            return true;
        }
        if (!IsPositiveFiniteItemValue(multiplier) || multiplier <= 1f ||
            !IsPositiveFiniteItemValue(seconds)) return false;

        // Refresh duration, never multiply bonuses on repeated pickup. A weaker pickup
        // cannot shorten or reduce an already-active stronger boost.
        float remaining = ItemSpeedBoostRemaining;
        ItemBoostMultiplier = Mathf.Max(HasItemSpeedBoost ? ItemBoostMultiplier : 1f, multiplier);
        ItemBoostTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(remaining, seconds));
        RPC_PlayBattleSound(BattleSound.BoosterPickup);
        return true;
    }

    private static bool IsPositiveFiniteItemValue(float value)
        => value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);

    private float GetItemSpeedMultiplier()
    {
        return HasItemSpeedBoost ? Mathf.Max(1f, ItemBoostMultiplier) : 1f;
    }

    private void ResetItemBoost()
    {
        ItemBoostTimer = TickTimer.None;
        ItemBoostMultiplier = 1f;
    }
}
