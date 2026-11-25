using Fusion;
using UnityEngine;

public partial class Player
{
    [Networked] public int VisionOpenBooks { get; private set; }
    public bool HasVisionEquipped => Magic1 == MagicType.Vision || Magic2 == MagicType.Vision;
    private bool PrepareVisionBooks(ref MagicStatEntry stats)
    {
        if (stats.magic != MagicType.Vision || !HasVisionEquipped) return false;
        var settings = VisionBookSettings.Default;
        if (VisionOpenBooks < Mathf.Clamp(settings.bookCount, 1, 12)) return false;
        stats.damage = Mathf.Max(0, settings.empoweredDamage);
        stats.hitscanRadius *= Mathf.Max(1, settings.empoweredRadiusMultiplier);
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
