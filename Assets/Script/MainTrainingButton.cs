using UnityEngine;

// Scene buttons outlive their serialized manager target when Main is reloaded.
// Resolve the persistent manager only on a click, without polling or scene searches.
public sealed class MainTrainingButton : MonoBehaviour
{
    public void StartTraining()
    {
        var manager = NetworkGameManager.Instance;
        if (manager == null)
        {
            Debug.LogWarning("훈련장에 입장할 네트워크 매니저가 준비되지 않았습니다.", this);
            return;
        }
        manager.StartTraining();
    }
}
