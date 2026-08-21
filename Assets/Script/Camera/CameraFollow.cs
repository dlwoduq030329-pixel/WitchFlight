using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    bool stopChase;
    Vector3 offset;
    Vector3 shoulderOffset;
    GameObject target;
    [SerializeField]
    private float followSpeed = 20f;

    private bool isAim = false;

    private Vector3 lookOffset = new Vector3(0, 2, 0);
    

    public void SetTarget(GameObject temp)
    {
        
        this.transform.position = temp.transform.position - (temp.transform.forward*5f);
        offset = new Vector3(0, 2, -5f);
        shoulderOffset = new Vector3(1, 2, -1f);

        target = temp;  


    }

    public void setAim(bool temp)
    {
        isAim = temp;

        if(isAim)
        {
            followSpeed = 20f;
        }else
        {
            followSpeed = 20f;
        }
    }

    private void LateUpdate()
    {
        if (target == null || stopChase)
            return;

        Vector3 targetPos =
            target.transform.TransformPoint(isAim ? shoulderOffset : offset);

        transform.position =
            Vector3.Lerp(
                transform.position,
                targetPos,
                followSpeed * Time.deltaTime
            );

        if (isAim)
        {
            this.transform.rotation = target.transform.rotation;
            return;
        }

        transform.LookAt(target.transform.position + lookOffset);
    }
}
