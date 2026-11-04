using UnityEngine;

public class UITransitionAnimation : MonoBehaviour
{
    // 애니메이션이 끝날 때 호출할 함수
    public void DisableObject()
    {
        gameObject.SetActive(false);
    }
}
