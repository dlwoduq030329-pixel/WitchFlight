using Fusion;
using UnityEngine;

// Reusable visual-only cloth. Initially connected only to ChPrefabForRobby.
[DisallowMultipleComponent, DefaultExecutionOrder(340)]
public sealed class CharacterSkirtCloth : MonoBehaviour
{
    [SerializeField] private SkinnedMeshRenderer skirt;
    [SerializeField] private Animator bodyAnimator;
    [SerializeField] private Transform motionRoot;
    [SerializeField] private bool localPlayerOnly = true;
    [Header("Fabric")]
    [SerializeField, Range(.05f, .5f)] private float pinnedTopFraction = .18f;
    [SerializeField, Min(.01f)] private float hemTravel = .25f;
    [SerializeField, Range(30f, 120f)] private float solverFrequency = 60f;
    [SerializeField, Range(0f, 1f)] private float damping = .65f;
    [SerializeField, Range(0f, 1f)] private float bendingStiffness = .5f;
    [Header("Body clearance (model units)")]
    [SerializeField] private float thighRadius = .105f;
    [SerializeField] private float shinRadius = .065f;
    [SerializeField] private float armRadius = .045f;
    [SerializeField] private float handRadius = .065f;
    [SerializeField] private float clearance = .012f;
    [Header("Wind")]
    [SerializeField] private float windStrength = .4f;
    [SerializeField] private float maxWind = 8f;

    private struct Segment
    {
        public Transform start, end;
        public CapsuleCollider collider;
        public float radius;
    }
    private Segment[] segments;
    private Cloth cloth;
    private NetworkObject owner;
    private Vector3 lastPosition;
    private bool sampled, initialized, failed;
    private float nextOwnerCheck;

    private void Awake()
    {
        if (skirt == null) skirt = GetComponent<SkinnedMeshRenderer>();
        if (motionRoot == null) motionRoot = transform.root;
        if (bodyAnimator == null) bodyAnimator = motionRoot.GetComponentInChildren<Animator>();
        owner = motionRoot.GetComponent<NetworkObject>();
        if (skirt == null || bodyAnimator == null || !bodyAnimator.isHuman)
        {
            Debug.LogWarning("Skirt Cloth requires a skirt renderer and Humanoid Animator.", this);
            enabled = false;
        }
    }

    private void LateUpdate()
    {
        if (failed) return;
        if (Time.unscaledTime >= nextOwnerCheck)
        {
            nextOwnerCheck = Time.unscaledTime + .5f;
            bool allowed = !localPlayerOnly || (owner != null && owner.IsValid && owner.HasInputAuthority);
            if (allowed && !initialized) InitializeCloth();
            if (cloth != null && cloth.enabled != allowed) { cloth.enabled = allowed; sampled = false; }
        }
        if (cloth == null || !cloth.enabled) return;
        UpdateColliders();
        Vector3 position = motionRoot.position;
        float dt = Time.deltaTime;
        if (!sampled || dt <= 0f || dt > .15f || (position - lastPosition).sqrMagnitude > 25f)
        {
            cloth.ClearTransformMotion();
            cloth.externalAcceleration = Vector3.zero;
        }
        else
            cloth.externalAcceleration = Vector3.ClampMagnitude(-(position - lastPosition) / dt * windStrength, maxWind);
        sampled = true; lastPosition = position;
    }

    private void InitializeCloth()
    {
        // Do not run a second bone solver on the same fabric.
        var boneMotion = GetComponent<SkirtWindMotion>();
        if (boneMotion != null) boneMotion.enabled = false;
        cloth = GetComponent<Cloth>();
        if (cloth == null) cloth = gameObject.AddComponent<Cloth>();
        Vector3[] vertices = cloth.vertices;
        ClothSkinningCoefficient[] limits = cloth.coefficients;
        if (vertices.Length == 0 || vertices.Length != limits.Length)
        {
            Debug.LogError("Skirt Cloth could not read simulation vertices; cloth has been disabled.", this);
            cloth.enabled = false; failed = true; return;
        }
        float low = float.PositiveInfinity, high = float.NegativeInfinity;
        Vector3 up = motionRoot.up;
        // Cloth particles use the renderer's root-bone rotation and position,
        // with scale already applied. TransformPoint on the mesh object rotates
        // the imported (-90 degree) mesh a second time and pins the wrong edge.
        // Matches Unity's ClothInspector.InitClothParticlesInWorldSpace.
        Transform particleRoot = skirt.rootBone != null ? skirt.rootBone : skirt.transform;
        Quaternion particleRotation = particleRoot.rotation;
        Vector3 particlePosition = particleRoot.position;
        for (int i = 0; i < vertices.Length; i++)
        {
            float h = Vector3.Dot(particleRotation * vertices[i] + particlePosition, up);
            low = Mathf.Min(low, h); high = Mathf.Max(high, h);
        }
        float height = Mathf.Max(.001f, high - low);
        int pinnedCount = 0;
        for (int i = 0; i < vertices.Length; i++)
        {
            float belowTop = (high - Vector3.Dot(particleRotation * vertices[i] + particlePosition, up)) / height;
            float free = Mathf.Clamp01((belowTop - pinnedTopFraction) / (1f - pinnedTopFraction));
            limits[i].maxDistance = hemTravel * Mathf.SmoothStep(0f, 1f, free);
            // float.MaxValue is Unity's disabled backstop value. A zero backstop
            // forces each particle onto one side of its animated surface normal;
            // folded/double-sided trim then fights the body capsules.
            limits[i].collisionSphereDistance = float.MaxValue;
            if (free == 0f) pinnedCount++;
        }
        cloth.coefficients = limits;
        cloth.useGravity = true;
        cloth.stretchingStiffness = 1f;
        cloth.bendingStiffness = bendingStiffness;
        cloth.damping = damping;
        cloth.friction = .45f;
        cloth.clothSolverFrequency = solverFrequency;
        cloth.worldVelocityScale = .15f;
        cloth.worldAccelerationScale = .1f;
        cloth.enableContinuousCollision = true;
        cloth.useVirtualParticles = 1f;
        cloth.selfCollisionDistance = 0f;
        cloth.randomAcceleration = new Vector3(.15f, .05f, .15f);
        segments = new Segment[8];
        MakeSegment(0, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, thighRadius);
        MakeSegment(1, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, thighRadius);
        MakeSegment(2, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, shinRadius);
        MakeSegment(3, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, shinRadius);
        MakeSegment(4, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, armRadius);
        MakeSegment(5, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, armRadius);
        MakeSegment(6, HumanBodyBones.LeftHand, HumanBodyBones.LeftMiddleDistal, handRadius);
        MakeSegment(7, HumanBodyBones.RightHand, HumanBodyBones.RightMiddleDistal, handRadius);
        var colliders = new CapsuleCollider[segments.Length];
        for (int i = 0; i < segments.Length; i++) colliders[i] = segments[i].collider;
        UpdateColliders();
        cloth.capsuleColliders = colliders;
        initialized = true;
        Debug.Log($"Skirt Cloth initialized: {vertices.Length} particles, {pinnedCount} waist pins, height={height:F3}, root={particleRoot.name}, 8 body capsules (local visual only).", this);
    }

    private void MakeSegment(int index, HumanBodyBones from, HumanBodyBones to, float radius)
    {
        Transform start = bodyAnimator.GetBoneTransform(from), end = bodyAnimator.GetBoneTransform(to);
        if (start == null) return;
        if (end == null) end = start;
        var go = new GameObject("ClothOnly_" + from);
        go.layer = 2; // Ignore Raycast. These colliders are never enabled in the physics world.
        go.transform.SetParent(motionRoot, false);
        var capsule = go.AddComponent<CapsuleCollider>();
        capsule.enabled = false;
        capsule.direction = 1;
        segments[index] = new Segment { start = start, end = end, collider = capsule, radius = radius };
    }

    private void UpdateColliders()
    {
        if (segments == null) return;
        float scale = Mathf.Max(.001f, Mathf.Abs(motionRoot.lossyScale.y));
        for (int i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            if (segment.collider == null || segment.start == null || segment.end == null) continue;
            Vector3 delta = segment.end.position - segment.start.position;
            Transform target = segment.collider.transform;
            target.position = (segment.start.position + segment.end.position) * .5f;
            target.rotation = delta.sqrMagnitude > .000001f ? Quaternion.FromToRotation(Vector3.up, delta.normalized) : segment.start.rotation;
            segment.collider.radius = segment.radius + clearance;
            segment.collider.height = delta.magnitude / scale + segment.collider.radius * 2f;
        }
    }

    private void OnDisable() { if (cloth != null) cloth.enabled = false; sampled = false; }
    private void OnEnable() { nextOwnerCheck = 0f; }
    private void OnDestroy()
    {
        if (segments == null) return;
        foreach (var segment in segments)
            if (segment.collider != null) Destroy(segment.collider.gameObject);
    }
}
