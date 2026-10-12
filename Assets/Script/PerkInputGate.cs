// Pure input policy, separate from UI animation and from the network tick rate.
public sealed class PerkInputGate
{
    public bool Showing { get; private set; }
    public bool BlocksCombat { get; private set; }
    public int Choice { get; private set; } = -1;
    private bool previousSpace, previousLeft, previousRight, requireRelease;
    public void Sample(bool allowed, bool space, bool left, bool right)
    {
        Showing = allowed && space;
        bool leftEdge = left && !previousLeft, rightEdge = right && !previousRight;
        Choice = Showing && !(left && right) && leftEdge != rightEdge ? (rightEdge ? 1 : 0) : -1;
        if (Showing || previousSpace) requireRelease = true;
        BlocksCombat = Showing || previousSpace || requireRelease;
        // Still block this release frame; a new combat press is required next frame.
        if (!Showing && !left && !right) requireRelease = false;
        previousSpace = Showing; previousLeft = left; previousRight = right;
    }
}
