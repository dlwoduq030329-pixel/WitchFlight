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
    public Vector3 aimUp;
    public NetworkBool steerToAim;
    public NetworkId lockTarget;
    public NetworkBool suppressActions;
}
