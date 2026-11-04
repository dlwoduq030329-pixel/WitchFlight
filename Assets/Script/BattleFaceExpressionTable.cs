using UnityEngine;

// Prebaked blend-shape curves, available in builds without animation-editor APIs.
public sealed class BattleFaceExpressionTable : ScriptableObject
{
    public BattleFacePortrait.FaceExpression[] expressions;
}
