using UnityEngine;

[CreateAssetMenu(menuName = "WitchFlight/Battle Results Table")]
public sealed class BattleResultsTable : ScriptableObject
{
    [Min(.5f)] public float roundResultSeconds = 4f;
    [Min(.05f)] public float roundFadeSeconds = 1.2f;
    [Min(.1f)] public float roundSlowdownResponse = 2.2f;
    [Min(.1f)] public float statRevealSeconds = .85f;
    [Min(0f)] public float statPauseSeconds = .2f;
    [Min(1f)] public float statStartScale = 2.2f;
    [Range(0f, 1f)] public float statStartAlpha = .08f;
    [Min(.1f)] public float praiseSeconds = 2f;
    public AnimationClip standingClip;
    public AnimationClip happyClip;
    [UnityEngine.Serialization.FormerlySerializedAs("shoulderCatScale")]
    [Min(.1f)] public float floatingCatScale = .7f;
    [Min(0f)] public float floatingCatGap = .04f;
    public float floatingCatHeight = .8f;
    [Range(128, 1024)] public int portraitResolution = 512;
    private static BattleResultsTable cached;
    public static BattleResultsTable Default
    {
        get
        {
            if (cached != null) return cached;
            cached = Resources.Load<BattleResultsTable>("BattleResultsTable");
            if (cached == null) cached = CreateInstance<BattleResultsTable>();
            return cached;
        }
    }
}
