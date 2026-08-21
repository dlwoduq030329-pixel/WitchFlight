using Fusion;
using UnityEngine;

public struct NetworkInputData : INetworkInput
{
    public bool accelerate;
    public bool decelerate;

    public bool turnLeft;
    public bool turnRight;

    public float mouseY;
}