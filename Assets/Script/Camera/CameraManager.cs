using UnityEngine;

public class CameraManager : MonoBehaviour
{
    public static CameraManager Instance;

    CameraFollow follow;

    private void Awake()
    {
        Instance = this;
        follow = GetComponent<CameraFollow>();
    }

    public void SetTarget(GameObject target)
    {
        follow.SetTarget(target);
    }

    public void SetCam(bool temp)
    {
        follow.setAim(temp);
    }
}