using UnityEngine;

public partial class Player
{
    internal bool TryApplyUpdraft(float launchSpeed)
    {
        if (!CanReceiveBattleItem(BattleItemType.Booster) || IsBound || IsHitStunned ||
            !IsPositiveFiniteItemValue(launchSpeed)) return false;
        // Compensate downward flight so an inverted/diving player still launches upward.
        // Preserve lateral impulses; repeated pads never add unlimited vertical velocity.
        KnockbackVelocity = ResolveUpdraftVelocity(KnockbackVelocity, transform.forward * Speed, launchSpeed);
        RPC_PlayBattleSound(BattleSound.Updraft);
        return true;
    }

    internal static Vector3 ResolveUpdraftVelocity(Vector3 impulse, Vector3 flight, float launchSpeed)
    {
        impulse.y = Mathf.Max(impulse.y, launchSpeed - flight.y);
        return impulse;
    }
}
