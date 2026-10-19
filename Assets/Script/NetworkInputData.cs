using Fusion;
using UnityEngine;

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
    Menu
}

public struct NetworkInputData : INetworkInput
{
    public NetworkButtons buttons;
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
    public NetworkBool suppressActions;
    // Perk selection consumes only mouse combat; flight and camera remain responsive.
    public NetworkBool suppressCombat;
}
