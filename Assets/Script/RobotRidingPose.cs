using System;
using UnityEngine;

// Cosmetic lower-body override only. Runs before flight bob and skirt simulation.
// Explicit prefab poses avoid capturing a seated animation as the standing pose.
[DefaultExecutionOrder(20)]
[DisallowMultipleComponent]
public sealed class RobotRidingPose : MonoBehaviour
{
    [Serializable]
    private struct BonePose
    {
        public Transform bone;
        public Vector3 position;
        public Quaternion rotation;
    }

    [SerializeField] private BonePose[] standingPose = Array.Empty<BonePose>();
    [Tooltip("서 있는 골반 위치 보정값입니다. 골반 부모의 로컬 좌표 기준이며 네트워크 이동 루트는 변경하지 않습니다.")]
    [SerializeField] private Vector3 pelvisOffset;
    [Header("Robot deck alignment")]
    [SerializeField] private Transform robotPlatform;
    [SerializeField] private Transform leftFoot, rightFoot, leftToes, rightToes;
    [Tooltip("발/발가락 본 아래 신발 밑창 두께를 포함한 간격입니다. 캐릭터 로컬 단위이며 캐릭터 배율에 맞춰 적용됩니다.")]
    [SerializeField, Min(0f)] private float soleClearance = .04f;
    [Tooltip("양발 중심을 기준으로 한 청소기의 앞뒤·좌우 위치 미세 조정입니다. 캐릭터 로컬 좌표입니다.")]
    [SerializeField] private Vector2 deckCenterOffset;
    private Vector3[] animatedPositions;
    private Quaternion[] animatedRotations;
    private Player player;
    private bool applied;
    private Bounds deckBounds;
    private bool hasDeckBounds, alignmentReady, platformApplied;
    private Vector3 platformOffset, platformPositionBeforeAlignment;

    private void Awake()
    {
        player = GetComponent<Player>();
        animatedPositions = new Vector3[standingPose.Length];
        animatedRotations = new Quaternion[standingPose.Length];
        CacheDeckBounds();
    }

    public void SetRiding(bool riding)
    {
        if (enabled != riding) alignmentReady = false;
        if (enabled != riding) enabled = riding;
    }

    private void Update() => RestoreAnimationPose();

    private void LateUpdate()
    {
        // Keep death/falling animations intact. An unspawned Main preview may also
        // use this component, without accessing Fusion's networked properties.
        if (player != null && player.Object != null && player.Object.IsValid && !player.IsAlive) return;
        for (int i = 0; i < standingPose.Length; i++)
        {
            var pose = standingPose[i];
            if (pose.bone == null) continue;
            animatedPositions[i] = pose.bone.localPosition;
            animatedRotations[i] = pose.bone.localRotation;
            pose.bone.SetLocalPositionAndRotation(pose.position + (i == 0 ? pelvisOffset : Vector3.zero), pose.rotation);
        }
        applied = true;
        AlignDeckUnderFeet();
    }

    // Mesh bounds are read once, even if the authored robot starts inactive.
    // No colliders, Physics queries, mesh baking or hierarchy searches per frame.
    private void CacheDeckBounds()
    {
        if (robotPlatform == null) return;
        foreach (var filter in robotPlatform.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;
            Bounds bounds = filter.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 point = robotPlatform.InverseTransformPoint(filter.transform.TransformPoint(Corner(bounds, i)));
                if (!hasDeckBounds) { deckBounds = new Bounds(point, Vector3.zero); hasDeckBounds = true; }
                else deckBounds.Encapsulate(point);
            }
        }
    }

    private static Vector3 Corner(Bounds bounds, int index) => bounds.center + Vector3.Scale(bounds.extents,
        new Vector3((index & 1) == 0 ? -1 : 1, (index & 2) == 0 ? -1 : 1, (index & 4) == 0 ? -1 : 1));

    private void AlignDeckUnderFeet()
    {
        if (!hasDeckBounds || robotPlatform == null || leftFoot == null || rightFoot == null ||
            leftToes == null || rightToes == null) return;
        if (!alignmentReady)
        {
            Vector3 lf = transform.InverseTransformPoint(leftFoot.position);
            Vector3 rf = transform.InverseTransformPoint(rightFoot.position);
            Vector3 lt = transform.InverseTransformPoint(leftToes.position);
            Vector3 rt = transform.InverseTransformPoint(rightToes.position);
            Vector3 feetCenter = (lf + rf + lt + rt) * .25f;
            float soleHeight = Mathf.Min(Mathf.Min(lf.y, rf.y), Mathf.Min(lt.y, rt.y)) - Mathf.Max(0f, soleClearance);
            Vector3 center = transform.InverseTransformPoint(robotPlatform.TransformPoint(deckBounds.center));
            float top = float.NegativeInfinity;
            for (int i = 0; i < 8; i++)
                top = Mathf.Max(top, transform.InverseTransformPoint(robotPlatform.TransformPoint(Corner(deckBounds, i))).y);
            Vector3 delta = new Vector3(feetCenter.x + deckCenterOffset.x - center.x, soleHeight - top,
                feetCenter.z + deckCenterOffset.y - center.z);
            Vector3 worldDelta = transform.TransformVector(delta);
            platformOffset = robotPlatform.parent != null ? robotPlatform.parent.InverseTransformVector(worldDelta) : worldDelta;
            alignmentReady = true;
        }
        platformPositionBeforeAlignment = robotPlatform.localPosition;
        robotPlatform.localPosition += platformOffset;
        platformApplied = true;
    }

    private void RestoreAnimationPose()
    {
        if (platformApplied)
        {
            if (robotPlatform != null) robotPlatform.localPosition = platformPositionBeforeAlignment;
            platformApplied = false;
        }
        if (!applied) return;
        applied = false;
        for (int i = 0; i < standingPose.Length; i++)
            if (standingPose[i].bone != null)
                standingPose[i].bone.SetLocalPositionAndRotation(animatedPositions[i], animatedRotations[i]);
    }

    private void OnDisable() => RestoreAnimationPose();
    private void OnValidate() => alignmentReady = false;
}
