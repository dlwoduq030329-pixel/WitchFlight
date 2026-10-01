using System.Collections.Generic;
using UnityEngine;

// Additive presentation only: the network root, controller and camera never bob.
[DisallowMultipleComponent]
[RequireComponent(typeof(Player))]
[DefaultExecutionOrder(25)]
public sealed class FlightVisualBob : MonoBehaviour
{
    [Tooltip("서로 겹치지 않는 외형 루트들입니다. Player 루트 자체는 지정하지 않습니다.")]
    [SerializeField] private Transform[] visualRoots;
    [Tooltip("최대 상하 흔들림 거리(m). 0이면 흔들림을 끕니다.")]
    [SerializeField, Min(0f)] private float amplitude = 0.08f;
    [Tooltip("초당 반복 횟수입니다. 0.6이면 한 주기가 약 1.7초입니다.")]
    [SerializeField, Min(0f)] private float frequency = 0.6f;
    [Tooltip("이 속도 이상에서 최대 진폭을 사용합니다. 후진에도 적용합니다.")]
    [SerializeField, Min(0.01f)] private float fullAmplitudeSpeed = 10f;
    [Tooltip("흔들림이 나타나고 사라지는 반응성입니다.")]
    [SerializeField, Min(0.01f)] private float blendSpeed = 4f;

    [Header("Turn banking")]
    [Tooltip("선회 시 최대 기울기(도). 0이면 기울임을 끕니다.")]
    [SerializeField, Range(0f, 60f)] private float maxBankAngle = 25f;
    [Tooltip("최대 기울기에 도달하는 초당 선회 각도입니다.")]
    [SerializeField, Min(1f)] private float fullBankTurnSpeed = 120f;
    [Tooltip("기울고 원래 자세로 돌아오는 반응성입니다.")]
    [SerializeField, Min(0.01f)] private float bankBlendSpeed = 5f;
    [Tooltip("Player 로컬 좌표 기준 기울임 중심입니다.")]
    [SerializeField] private Vector3 bankPivot = new(0f, 0.6f, 0f);

    private sealed class VisualPose
    {
        public Transform Root;
        public Vector3 BasePosition;
        public Vector3 AppliedPosition;
        public Quaternion BaseRotation;
        public Quaternion AppliedRotation;
    }

    private sealed class CapsulePose
    {
        public CapsuleCollider Collider;
        public Vector3 BaseCenter;
        public Vector3 AppliedCenter;
    }

    private readonly List<VisualPose> visuals = new();
    private readonly List<CapsulePose> capsules = new();
    private Player owner;
    private float phase;
    private float blend;
    private float bankAngle;
    private float previousYaw;
    private bool hasPreviousYaw;
    private bool hasAppliedOffset;

    private void Awake()
    {
        owner = GetComponent<Player>();
        if (visualRoots == null)
            return;
        var selected = new HashSet<Transform>();
        foreach (Transform root in visualRoots)
            if (root != null && root != transform && root.IsChildOf(transform))
                selected.Add(root);
        foreach (Transform root in selected)
        {
            bool hasSelectedParent = false;
            for (Transform parent = root.parent; parent != null && parent != transform; parent = parent.parent)
                if (selected.Contains(parent))
                {
                    hasSelectedParent = true;
                    break;
                }
            if (hasSelectedParent)
                continue;
            visuals.Add(new VisualPose { Root = root });
            // ChPrefab has two leg capsules under its animated rig. Counter-offset
            // their centers so this cosmetic bob does not change their hit geometry.
            foreach (CapsuleCollider capsule in root.GetComponentsInChildren<CapsuleCollider>(true))
                capsules.Add(new CapsulePose { Collider = capsule });
        }
    }

    private void Update()
    {
        // Undo only our last offset before Animator evaluates a fresh pose. Never
        // reparent bones or overwrite their authored animation with cached bind poses.
        RestorePose();
    }

    private void LateUpdate()
    {
        RestorePose();
        BattleManager battle = BattleManager.Instance;
        if (owner == null || owner.Object == null || !owner.Object.IsValid ||
            !owner.IsAlive || battle == null || !battle.IsGameplayActive)
        {
            blend = 0f;
            phase = 0f;
            bankAngle = 0f;
            hasPreviousYaw = false;
            return;
        }

        float targetBlend = Mathf.Clamp01(Mathf.Abs(owner.CurrentSpeed) / Mathf.Max(0.01f, fullAmplitudeSpeed));
        float smoothing = 1f - Mathf.Exp(-Mathf.Max(0.01f, blendSpeed) * Time.deltaTime);
        blend = Mathf.Lerp(blend, targetBlend, smoothing);
        if (targetBlend == 0f && blend < 0.001f)
        {
            blend = 0f;
            phase = 0f;
        }
        phase = Mathf.Repeat(phase + Time.deltaTime * frequency * Mathf.PI * 2f, Mathf.PI * 2f);
        Vector3 worldOffset = frequency > 0f
            ? Vector3.up * (Mathf.Sin(phase) * amplitude * blend) : Vector3.zero;

        // Sample the rendered network root, not local keyboard input: opponents bank
        // too, without adding a second network movement/rotation system.
        float yaw = transform.eulerAngles.y;
        float yawDelta = hasPreviousYaw ? Mathf.DeltaAngle(previousYaw, yaw) : 0f;
        previousYaw = yaw;
        hasPreviousYaw = true;
        // Ignore pose snaps (e.g. a teleport), rather than banking for one frame.
        float yawSpeed = Time.deltaTime > 0f && Mathf.Abs(yawDelta) < 45f
            ? yawDelta / Time.deltaTime : 0f;
        float targetBank = -Mathf.Clamp(yawSpeed / Mathf.Max(1f, fullBankTurnSpeed), -1f, 1f) * maxBankAngle;
        bankAngle = Mathf.Lerp(bankAngle, targetBank,
            1f - Mathf.Exp(-Mathf.Max(0.01f, bankBlendSpeed) * Time.deltaTime));
        Quaternion worldBank = Quaternion.AngleAxis(bankAngle, transform.forward);
        Vector3 pivot = transform.TransformPoint(bankPivot);
        foreach (VisualPose visual in visuals)
        {
            if (visual.Root == null)
                continue;
            visual.BasePosition = visual.Root.localPosition;
            visual.BaseRotation = visual.Root.localRotation;
            // Rotate all selected roots around one shared pivot to keep rig, hair
            // and equipment aligned. No hierarchy or animation binding changes.
            Vector3 position = pivot + worldBank * (visual.Root.position - pivot) + worldOffset;
            visual.AppliedPosition = visual.Root.parent.InverseTransformPoint(position);
            visual.AppliedRotation = Quaternion.Inverse(visual.Root.parent.rotation) * worldBank * visual.Root.rotation;
            visual.Root.localPosition = visual.AppliedPosition;
            visual.Root.localRotation = visual.AppliedRotation;
        }
        foreach (CapsulePose capsule in capsules)
        {
            if (capsule.Collider == null)
                continue;
            capsule.BaseCenter = capsule.Collider.center;
            capsule.AppliedCenter = capsule.BaseCenter - capsule.Collider.transform.InverseTransformVector(worldOffset);
            capsule.Collider.center = capsule.AppliedCenter;
        }
        hasAppliedOffset = true;
    }

    private void RestorePose()
    {
        if (!hasAppliedOffset)
            return;
        foreach (VisualPose visual in visuals)
        {
            if (visual.Root == null)
                continue;
            if (visual.Root.localPosition == visual.AppliedPosition)
                visual.Root.localPosition = visual.BasePosition;
            if (visual.Root.localRotation == visual.AppliedRotation)
                visual.Root.localRotation = visual.BaseRotation;
        }
        foreach (CapsulePose capsule in capsules)
            if (capsule.Collider != null && capsule.Collider.center == capsule.AppliedCenter)
                capsule.Collider.center = capsule.BaseCenter;
        hasAppliedOffset = false;
    }

    private void OnDisable()
    {
        RestorePose();
        blend = 0f;
        phase = 0f;
        bankAngle = 0f;
        hasPreviousYaw = false;
    }
}
