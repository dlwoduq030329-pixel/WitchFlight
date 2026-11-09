using UnityEngine;
using EZhex1991.EZSoftBone;

// Visual-only capsules. No Unity Physics colliders, network state or per-frame searches.
[DefaultExecutionOrder(14000)]
public sealed class LongHairSoftBone : EZSoftBoneColliderBase
{
    [SerializeField] private EZSoftBone solver;
    [SerializeField] private Animator bodyAnimator;
    [SerializeField] private Transform motionRoot;
    [SerializeField] private Renderer[] hairRenderers;
    [Header("Sway")]
    [SerializeField, Min(1f)] private float fullWindSpeed = 80f;
    [SerializeField, Range(0f, .6f)] private float windStrength = .3f;
    [SerializeField, Range(0f, .3f)] private float flutterStrength = .14f;
    [Tooltip("Movement speed at which the visible travelling hair wave reaches full strength.")]
    [SerializeField, Min(1f)] private float movingFlutterSpeed = 12f;
    [SerializeField, Range(0f, .6f)] private float liftStrength = .18f;
    public float MovingFlutterAmount { get; private set; }
    public float FlutterTime { get; private set; }
    [Header("Body clearance (model metres)")]
    [SerializeField, Min(0f)] private float torsoRadius = .13f;
    [SerializeField, Min(0f)] private float shoulderRadius = .065f;
    [SerializeField, Min(0f)] private float neckRadius = .055f;
    [SerializeField, Min(0f)] private float armRadius = .055f;
    [SerializeField, Min(0f)] private float clearance = .008f;
    [Header("Performance")]
    [SerializeField, Min(1f)] private float simulationDistance = 35f;
    private struct Capsule { public Transform from, to; public Vector3 a, b; public float size, radius; }
    private Capsule[] capsules;
    private Camera viewCamera;
    private Vector3 lastPosition, force;
    private Quaternion lastRotation;
    private bool running;
    private Bounds contactBounds;
    private float swayTime;
    private float windSeed;

    private new void OnEnable() { }
    private new void OnDisable() { StopSimulation(); }
    private void Awake()
    {
        if (solver == null || bodyAnimator == null || !bodyAnimator.isHuman || motionRoot == null)
        { enabled = false; return; }
        solver.enabled = false;
        windSeed = Mathf.Abs(GetInstanceID() % 991) * .173f;
        solver.customForce = GetWind;
        // Existing skirt/hat colliders are on Ignore Raycast (2); hair uses Default (0).
        // Keeping the masks separate prevents the new torso clearance changing those rigs.
        solver.collisionLayers = 1 << gameObject.layer;
        capsules = new Capsule[6];
        Cache(0, HumanBodyBones.Hips, HumanBodyBones.Chest, torsoRadius);
        Cache(1, HumanBodyBones.Chest, HumanBodyBones.Neck, torsoRadius * .75f);
        Cache(2, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, shoulderRadius);
        Cache(3, HumanBodyBones.Neck, HumanBodyBones.Head, neckRadius);
        Cache(4, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, armRadius);
        Cache(5, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, armRadius);
    }
    private void Start() { viewCamera = Camera.main; }
    private void Cache(int index, HumanBodyBones from, HumanBodyBones to, float radius)
    {
        Transform a = bodyAnimator.GetBoneTransform(from), b = bodyAnimator.GetBoneTransform(to);
        capsules[index] = new Capsule { from = a, to = b != null ? b : a, size = radius };
    }
    private void LateUpdate()
    {
        bool visible = false;
        if (hairRenderers != null)
            foreach (var r in hairRenderers)
                if (r != null && r.enabled && r.gameObject.activeInHierarchy && r.isVisible) { visible = true; break; }
        if (!visible || (viewCamera != null &&
            (viewCamera.transform.position - motionRoot.position).sqrMagnitude > simulationDistance * simulationDistance))
        { StopSimulation(); return; }
        AdvanceSimulation(Time.deltaTime);
    }
    private void AdvanceSimulation(float dt)
    {
        float scale = Mathf.Max(.001f, Mathf.Abs(bodyAnimator.transform.lossyScale.y));
        contactBounds = new Bounds(motionRoot.position, Vector3.zero);
        for (int i = 0; i < capsules.Length; i++)
        {
            if (capsules[i].from == null) continue;
            capsules[i].a = capsules[i].from.position;
            capsules[i].b = capsules[i].to.position;
            capsules[i].radius = (capsules[i].size + clearance) * scale;
            contactBounds.Encapsulate(capsules[i].a);
            contactBounds.Encapsulate(capsules[i].b);
        }
        float largestRadius = Mathf.Max(Mathf.Max(torsoRadius, shoulderRadius), Mathf.Max(neckRadius, armRadius));
        contactBounds.Expand((largestRadius + clearance + .05f) * scale * 2f);
        Vector3 position = motionRoot.position;
        bool reset = !running || dt > .1f ||
            (position - lastPosition).sqrMagnitude > Mathf.Pow(Mathf.Max(5f, fullWindSpeed * dt * 3f), 2f) ||
            Quaternion.Angle(lastRotation, motionRoot.rotation) > 90f;
        if (!running) { base.OnEnable(); solver.enabled = true; running = true; }
        if (reset) { solver.SetRestState(); force = Vector3.zero; MovingFlutterAmount = 0f; }
        else if (dt > .00001f)
        {
            Vector3 velocity = (position - lastPosition) / dt;
            float speed = Mathf.Clamp01(velocity.magnitude / fullWindSpeed);
            float moving = Mathf.Clamp01((velocity.magnitude - .5f) / Mathf.Max(1f, movingFlutterSpeed - .5f));
            MovingFlutterAmount = Mathf.Lerp(MovingFlutterAmount, moving, 1f - Mathf.Exp(-6f * dt));
            FlutterTime += dt * Mathf.Lerp(1f, 1.4f, speed);
            swayTime += dt;
            float wave = Mathf.Sin(swayTime * 5.7f) * .65f +
                (Mathf.PerlinNoise(windSeed, swayTime * 1.2f) * 2f - 1f) * .35f;
            float sideInertia = Mathf.Clamp(-Vector3.Dot(velocity, motionRoot.right) / fullWindSpeed, -1f, 1f);
            // Backward wind plus lift: moving hair floats up instead of only sagging.
            // Never blow forward or apply downward gusts.
            float gust = .65f + .7f * Mathf.PerlinNoise(windSeed + 19f, swayTime * .9f);
            Vector3 target = -motionRoot.forward * (windStrength * speed * gust) +
                motionRoot.right * ((wave + sideInertia * .5f) * flutterStrength * Mathf.Lerp(.3f, 1f, speed)) +
                motionRoot.up * (liftStrength * MovingFlutterAmount * gust);
            force = Vector3.Lerp(force, target * scale, 1f - Mathf.Exp(-7f * dt));
            // Reproject the smoothed force after yaw/pitch changes as well.
            force = motionRoot.right * Vector3.Dot(force, motionRoot.right) +
                motionRoot.forward * Mathf.Min(0f, Vector3.Dot(force, motionRoot.forward)) +
                motionRoot.up * Mathf.Max(0f, Vector3.Dot(force, motionRoot.up));
        }
        lastPosition = position;
        lastRotation = motionRoot.rotation;
    }
    private Vector3 GetWind(float length) => force * Mathf.Lerp(.2f, 1f, length);
    public override void Collide(ref Vector3 position, float spacing)
    {
        if (!running || !contactBounds.Contains(position)) return;
        // Two cheap passes settle overlapping shoulder/neck capsules without sticking.
        for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < capsules.Length; i++)
            {
                Capsule c = capsules[i];
                if (c.radius <= 0f) continue;
                Vector3 axis = c.b - c.a;
                float length = axis.sqrMagnitude;
                Vector3 nearest = c.a + axis * (length > .000001f ? Mathf.Clamp01(Vector3.Dot(position - c.a, axis) / length) : 0f);
                Vector3 away = position - nearest;
                float radius = c.radius + spacing;
                if (away.sqrMagnitude < radius * radius)
                    position = nearest + (away.sqrMagnitude > .000001f ? away.normalized : -motionRoot.forward) * radius;
            }
    }
    private void StopSimulation()
    {
        base.OnDisable();
        if (solver != null && solver.enabled) solver.enabled = false;
        running = false;
        force = Vector3.zero;
        MovingFlutterAmount = 0f;
    }
    private void OnDestroy()
    {
        StopSimulation();
        if (solver != null) solver.customForce = null;
    }
}
