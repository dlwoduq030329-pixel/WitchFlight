using Fusion;
using UnityEngine;

// Local mode selection. A selection change while holding fire requires a fresh click.
public struct MagicSelectionInput
{
    public int Slot { get; private set; }
    public int Revision { get; private set; }
    private bool waitForRelease;
    public void Toggle(int slot, bool fireHeld)
    {
        if (slot < 1 || slot > 2) return;
        Slot = Slot == slot ? 0 : slot;
        Revision++;
        waitForRelease |= fireHeld;
    }
    public bool SampleCast(bool fireHeld)
    {
        if (!fireHeld) waitForRelease = false;
        return fireHeld && !waitForRelease;
    }
    public bool ReturnToBasic(int completedRevision, bool fireHeld)
    {
        // A late completion must never deselect a newer Q/E choice.
        if (Slot == 0 || Revision != completedRevision) return false;
        Slot = 0;
        Revision++;
        waitForRelease |= fireHeld;
        return true;
    }
}

// 버튼은 NetworkButtons로 전송해 Host가 각 입력의 눌림/해제를 한 번만 판정합니다.
public enum PlayerInputButton
{
    Accelerate,
    Decelerate,
    TurnLeft,
    TurnRight,
    Lock,
    Parry,
    Boost,
    MagicSlot1,
    MagicSlot2,
    MagicSlot3,
    Menu,
    QuickBrake
}

public struct NetworkInputData : INetworkInput
{
    public NetworkButtons buttons;
    // Bots keep their separate slot/Lock protocol; zero is the human basic-attack mode.
    public NetworkBool usesToggleSelection;
    public int selectedMagicSlot;
    public int magicSelectionRevision;
    public Vector2 look;
    public Vector3 aimDirection;
    // Dot-targeted hitscan aim is independent of the smoothed flight steering aim.
    public Vector3 magicAimDirection;
    public Vector3 areaAimPoint;
    public NetworkBool hasAreaAimPoint;
    public Vector3 aimUp;
    public NetworkBool steerToAim;
    // Acknowledges the exit-facing camera reset; stale pre-portal steering is ignored.
    public int portalExitSequence;
    public NetworkId lockTarget;
    public int rainTargetRevision;
    public NetworkId rainVisible0, rainVisible1, rainVisible2, rainVisible3, rainCandidate;
    public NetworkBool suppressActions;
    // Perk selection consumes only mouse combat; flight and camera remain responsive.
    public NetworkBool suppressCombat;
}
