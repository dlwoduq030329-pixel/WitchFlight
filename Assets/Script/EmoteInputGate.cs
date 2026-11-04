using System;

// Frame-rate input policy, independent of UI animation and Fusion ticks.
// Stable slot order: Up=0, Right=1, Down=2, Left=3. Centre means cancel.
public sealed class EmoteInputGate
{
    public bool Showing { get; private set; }
    public bool CapturesMouse { get; private set; }
    public bool BlocksCombat { get; private set; }
    public int SelectedSlot { get; private set; } = -1;
    public int Choice { get; private set; } = -1;
    public float X { get; private set; }
    public float Y { get; private set; }
    private bool previousHeld, requireMouseRelease;

    public void Reset(bool held = false)
    {
        Showing = CapturesMouse = BlocksCombat = requireMouseRelease = false;
        previousHeld = held;
        SelectedSlot = Choice = -1;
        X = Y = 0f;
    }

    public void Sample(bool allowed, bool held, bool cancel, float dx, float dy,
        bool left, bool right, float deadZone, float radius)
    {
        Choice = -1;
        bool wasShowing = Showing;
        if (!allowed || cancel) Showing = false;
        else if (held && !previousHeld)
        {
            Showing = true;
            SelectedSlot = -1;
            X = Y = 0f;
        }

        if (Showing)
        {
            radius = Math.Max(1f, radius);
            deadZone = Math.Max(0f, Math.Min(deadZone, radius - .01f));
            if (!float.IsNaN(dx) && !float.IsInfinity(dx)) X += dx;
            if (!float.IsNaN(dy) && !float.IsInfinity(dy)) Y += dy;
            float length = (float)Math.Sqrt(X * X + Y * Y);
            if (length > radius) { X *= radius / length; Y *= radius / length; }
            SelectedSlot = length <= deadZone ? -1 : Math.Abs(Y) >= Math.Abs(X)
                ? (Y >= 0 ? 0 : 2) : (X >= 0 ? 1 : 3);
            if (!held) { Choice = SelectedSlot; Showing = false; }
        }

        // Include the closing frame so its mouse delta cannot rotate the camera.
        CapturesMouse = Showing || wasShowing;
        if (CapturesMouse) requireMouseRelease = true;
        BlocksCombat = CapturesMouse || requireMouseRelease;
        if (!Showing && !left && !right) requireMouseRelease = false;
        if (!Showing) SelectedSlot = -1;
        previousHeld = held; // A held T cannot reopen after focus/phase/menu changes.
    }
}
