using UnityEngine;

public sealed partial class GiantMeowBossView
{
    [SerializeField] private Transform[] wallWarnings, thunderResidue;
    private bool wasExposed;

    private void PresentMechanics()
    {
        bool playing = boss.IsTargetable;
        bool showWall = playing && boss.WallState == 1;
        Quaternion facing = boss.WallDirection.sqrMagnitude > .001f ? Quaternion.LookRotation(boss.WallDirection) : Quaternion.identity;
        for (int side = 0; side < wallWarnings.Length; side++)
        {
            int first = side == 0 ? 0 : boss.WallGapColumn + GiantMeowBoss.WallGapColumns;
            int columns = side == 0 ? boss.WallGapColumn : GiantMeowBoss.WallColumns - first;
            var volume = wallWarnings[side];
            volume.gameObject.SetActive(showWall && columns > 0);
            if (!showWall || columns == 0) continue;
            float center = (first + (columns - 1) * .5f - 4) * boss.Settings.wallSpacing;
            volume.SetPositionAndRotation(boss.WallOrigin + facing * new Vector3(center, 0, boss.Settings.wallRange * .5f), facing);
            volume.localScale = new Vector3((columns - 1) * boss.Settings.wallSpacing + boss.WallHitRadius * 2f,
                (GiantMeowBoss.WallRows - 1) * boss.Settings.wallSpacing + boss.WallHitRadius * 2f,
                boss.Settings.wallRange + boss.WallHitRadius * 2f);
        }
        for (int i = 0; i < thunderResidue.Length; i++)
        {
            bool active = boss.HazardsActive && i < boss.HazardCount;
            thunderResidue[i].gameObject.SetActive(active);
            if (!active) continue;
            thunderResidue[i].position = boss.ThunderHazards[i];
            thunderResidue[i].localScale = Vector3.one * boss.Settings.thunderHazardRadius * 2f;
        }
        if (boss.Exposed && !wasExposed && animator != null)
            animator.CrossFadeInFixedTime(idleState, .15f, 0);
        wasExposed = boss.Exposed;
    }
}
