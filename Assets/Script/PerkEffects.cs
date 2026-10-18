using UnityEngine;

// Never mutate the shared MagicStatTable. All changes are on a cast-local struct copy.
public static class PerkEffects
{
    public static MagicStatEntry Apply(MagicStatEntry s, int slot, int level, int commonMask, float baseMp, PerkStatTable table)
    {
        s.perkSlot = slot;
        s.perkFirst = level >= 1 ? table.Get(PerkRules.Spell(s.magic, 1)) : null;
        s.perkSecond = level >= 2 ? table.Get(PerkRules.Spell(s.magic, 2)) : null;
        float mana = 1f, cooldown = 1f;
        if ((commonMask & PerkRules.Bit(PerkId.FrugalRune)) != 0) mana *= 1f - Mathf.Clamp01(table.Amount(PerkId.FrugalRune));
        if ((commonMask & PerkRules.Bit(PerkId.EchoSpell)) != 0) cooldown *= 1f - Mathf.Clamp01(table.Amount(PerkId.EchoSpell));
        ApplyStage(ref s, s.perkFirst, ref mana);
        ApplyStage(ref s, s.perkSecond, ref mana);
        mana = Mathf.Max(1f - Mathf.Clamp01(table.manaDiscountCap), mana);
        cooldown = Mathf.Max(1f - Mathf.Clamp01(table.cooldownDiscountCap), cooldown);
        s.apCost *= mana;
        s.apPerSecond = s.ChannelManaPerSecond(baseMp) * mana;
        s.maxApFractionPerSecond = 0f; // Extra maximum mana must extend channel duration.
        s.cooldownSeconds *= cooldown;
        return s;
    }
    private static void ApplyStage(ref MagicStatEntry s, PerkDefinition p, ref float mana)
    {
        if (p == null) return;
        float a = Mathf.Max(0f, p.amount);
        switch (p.id)
        {
            case PerkId.FireIgnition: s.lockChargeSeconds *= 1f - Mathf.Clamp01(a); mana *= 1f - Mathf.Clamp01(p.secondaryAmount); break;
            case PerkId.IceSlow: s.movementMultiplier = Mathf.Clamp(1f - (1f - s.movementMultiplier) * (1f + a), .05f, 1f); break;
            case PerkId.VisionAim: case PerkId.BeamWidth: s.hitscanRadius *= 1f + a; break;
            case PerkId.ThunderRadius: case PerkId.SmokeRadius: s.radius *= 1f + a; break;
            case PerkId.MineRadius:
                if (s.effect == MagicEffectKind.PursuitSummon) { s.range *= 1f + a; s.summonAttackRange *= 1f + a; }
                else s.radius *= 1f + a;
                break;
            case PerkId.HealingPower: s.healing *= 1f + a; break;
            case PerkId.BindingLock: s.lockChargeSeconds *= 1f - Mathf.Clamp01(a); break;
            case PerkId.BindingCost: mana *= 1f - Mathf.Clamp01(a); break;
            case PerkId.CurseEfficiency: s.damagePerSecond *= 1f + a; break;
            case PerkId.DarkCost: s.healthCost *= 1f - Mathf.Clamp01(a); break;
            case PerkId.DarkHarvest: s.healOnHit *= 1f + a; break;
            case PerkId.CurseRange: s.range *= 1f + a; break;
            case PerkId.MineArming:
                if (s.effect == MagicEffectKind.PursuitSummon) {
                    s.lockChargeSeconds *= 1f - Mathf.Clamp01(a);
                    s.summonAttackInterval = Mathf.Max(.1f, s.summonAttackInterval * (1f - Mathf.Clamp01(p.secondaryAmount)));
                }
                else s.activationDelay *= 1f - Mathf.Clamp01(a);
                break;
            case PerkId.SmokeDuration: s.effectDuration += a; break;
        }
    }
    // Cheap sustained hits must not generate more MP than their channel costs.
    public static float HitManaRefund(MagicStatEntry s, float maximumMp, float fraction)
        => s.IsChanneled ? 0f : Mathf.Max(0f, maximumMp) * Mathf.Max(0f, fraction);

    public static MagicStatEntry AreaHit(MagicStatEntry s, float distance)
    {
        var p = s.perkSecond;
        if (p != null && p.id == PerkId.ThunderCore && distance <= s.radius * Mathf.Clamp01(p.condition))
            s.damage *= 1f + Mathf.Max(0f, p.amount);
        return s;
    }
}
