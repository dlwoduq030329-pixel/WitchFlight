using Fusion;
using UnityEngine;

// 로컬 입력 권한을 가진 플레이어만 화면 안의 다른 플레이어를 선택합니다.
public class enemyLockOn : MonoBehaviour
{
    [SerializeField] private float targetRefreshInterval = 0.1f;

    private Player owner;
    private Camera targetCamera;
    private Player currentTarget;
    private float nextRefreshTime;

    private void Awake()
    {
        owner = GetComponent<Player>();
    }

    private void Update()
    {
        if (owner == null || !owner.Object || !owner.Object.HasInputAuthority)
            return;

        if (!Input.GetMouseButton(0))
        {
            if (currentTarget != null)
            {
                owner.ClearLockTarget();
                currentTarget = null;
            }
            return;
        }

        if (Time.unscaledTime < nextRefreshTime)
            return;

        nextRefreshTime = Time.unscaledTime + targetRefreshInterval;
        Player target = FindClosestVisibleOtherPlayer();
        if (target == currentTarget)
            return;

        currentTarget = target;
        if (currentTarget != null)
            owner.SetLockTarget(currentTarget.Object);
        else
            owner.ClearLockTarget();
    }

    private Player FindClosestVisibleOtherPlayer()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        if (targetCamera == null)
            return null;

        Player bestTarget = null;
        float bestDistanceFromCenter = float.PositiveInfinity;

        foreach (Player candidate in FindObjectsByType<Player>(FindObjectsSortMode.None))
        {
            if (candidate == owner || !candidate.Object)
                continue;

            Vector3 viewportPoint = targetCamera.WorldToViewportPoint(candidate.transform.position);
            if (viewportPoint.z <= 0f || viewportPoint.x < 0f || viewportPoint.x > 1f ||
                viewportPoint.y < 0f || viewportPoint.y > 1f)
                continue;

            float distanceFromCenter = (new Vector2(viewportPoint.x, viewportPoint.y) - new Vector2(0.5f, 0.5f)).sqrMagnitude;
            if (distanceFromCenter < bestDistanceFromCenter)
            {
                bestDistanceFromCenter = distanceFromCenter;
                bestTarget = candidate;
            }
        }

        return bestTarget;
    }
}
