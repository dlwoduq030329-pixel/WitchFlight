using UnityEngine;

// Preparation only. BattleIntroPresentation still renders snapshots after player animation.
[DefaultExecutionOrder(-10000)]
[DisallowMultipleComponent]
[RequireComponent(typeof(BattleIntroPresentation))]
public sealed class BattleIntroInitializer : MonoBehaviour
{
    private void Awake()
    {
        GetComponent<BattleIntroPresentation>().PrepareBeforeDisplay();
    }
}
