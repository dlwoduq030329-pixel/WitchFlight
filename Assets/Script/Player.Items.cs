using Fusion;
using UnityEngine;
using System.Collections.Generic;

public partial class Player
{
    [Header("ManaBuff area")]
    [Tooltip("ManaBuff 레이어의 Trigger에 닿아 있는 동안 추가 회복하는 초당 마나. 영역 중첩은 중복 적용하지 않습니다.")]
    [SerializeField, Min(0f)] private float manaBuffRecoveryPerSecond = 10f;
    private readonly List<Collider> manaBuffContacts = new();
    private int manaBuffLayer = -1;

    private void OnTriggerEnter(Collider other) => RegisterManaBuffContact(other);
    // Also restores contact after a respawn inside a volume or a host migration.
    // No search/query: the physics engine supplies the touching collider.
    private void OnTriggerStay(Collider other) => RegisterManaBuffContact(other);
    private void OnTriggerExit(Collider other) => manaBuffContacts.Remove(other);

    private void RegisterManaBuffContact(Collider other)
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority || !IsAlive ||
            other == null || !other.isTrigger || other.gameObject.layer != manaBuffLayer) return;
        if (!manaBuffContacts.Contains(other)) manaBuffContacts.Add(other);
    }

    private void TickManaBuffArea()
    {
        if (!Object.HasStateAuthority || !IsAlive || IsReturningToMap || manaBuffContacts.Count == 0) return;
        bool touching = false;
        for (int i = manaBuffContacts.Count - 1; i >= 0; i--)
        {
            Collider area = manaBuffContacts[i];
            // Exit callbacks are not guaranteed when a collider is disabled/destroyed.
            // Bounds also reject stale contacts immediately after teleportation.
            if (area == null || !area.enabled || !area.gameObject.activeInHierarchy || !area.isTrigger ||
                area.gameObject.layer != manaBuffLayer || characterController == null ||
                !area.bounds.Intersects(characterController.bounds))
            { manaBuffContacts.RemoveAt(i); continue; }
            touching = true;
        }
        if (touching && NowAp < MaxAp && IsPositiveFiniteItemValue(manaBuffRecoveryPerSecond))
            RestoreAp(manaBuffRecoveryPerSecond * Runner.DeltaTime);
    }

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
            RPC_ManaPotionPickup();
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

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ManaPotionPickup()
    {
        presentation ??= GetComponent<CombatPresentation>();
        presentation?.ShowManaPotionPickup();
        BattleAudio.Play(BattleSound.ManaPotion, LockAimPoint, transform, Object.HasInputAuthority);
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
