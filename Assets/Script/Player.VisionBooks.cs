using Fusion;
using UnityEngine;

public partial class Player
{
    [Networked] public int VisionOpenBooks { get; private set; }
    public bool HasVisionEquipped => Magic1 == MagicType.Vision || Magic2 == MagicType.Vision;
    private bool PrepareVisionBooks(ref MagicStatEntry stats)
    {
        if (stats.visionBooksEmpowered) return true;
        if (!HasVisionEquipped || stats.TargetsAllies || (stats.damage <= 0f && stats.damagePerSecond <= 0f)) return false;
        var settings = VisionBookSettings.Default;
        if (VisionOpenBooks < Mathf.Clamp(settings.bookCount, 1, 12)) return false;
        if (stats.damage > 0f) stats.damage += Mathf.Max(stats.damage * settings.empoweredDamageBonus, settings.minimumDamageBonus);
        if (stats.damagePerSecond > 0f) stats.damagePerSecond += Mathf.Max(stats.damagePerSecond * settings.empoweredDamageBonus, settings.minimumDamageBonus);
        if (stats.magic == MagicType.Vision) stats.hitscanRadius *= Mathf.Max(1, settings.empoweredRadiusMultiplier);
        // Consume on firing, including misses and parries. The charged shot cannot
        // immediately reopen a book or inherit the old consecutive-hit perk bonus.
        stats.visionBooksEmpowered = true;
        VisionOpenBooks = 0;
        return true;
    }
    private void CompleteVisionBooks(MagicStatEntry stats, bool empowered, bool accepted)
    {
        if (stats.magic != MagicType.Vision || empowered || !HasVisionEquipped) return;
        VisionOpenBooks = Mathf.Clamp(VisionOpenBooks + (accepted ? 1 : -1), 0,
            Mathf.Clamp(VisionBookSettings.Default.bookCount, 1, 12));
    }
}
