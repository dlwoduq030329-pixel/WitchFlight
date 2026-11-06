using EZhex1991.EZSoftBone;
using Fusion;
using UnityEngine;

// Runs after the lobby pose/IK, before EZSoftBone (execution order 15100).
// This is a solver-only collider: it creates no Physics colliders or gameplay contacts.
[DefaultExecutionOrder(14000)]
public sealed class LobbySkirtSoftBone : EZSoftBoneColliderBase
{
    [SerializeField] private EZSoftBone solver;
    [SerializeField] private Animator bodyAnimator;
    [SerializeField] private Transform motionRoot;
    [SerializeField] private SkinnedMeshRenderer skirt;
    [SerializeField] private float thighRadius = .09f;
    [SerializeField] private float armRadius = .04f;
    [Header("Pelvis / seat clearance (model units)")]
    [SerializeField, Min(0f)] private float pelvisRadius = .10f;
    [SerializeField, Min(0f)] private float pelvisHalfWidth = .055f;
    [Tooltip("Relative to the initial character axes: X right, Y up, Z forward. Follows the animated hips.")]
    [SerializeField] private Vector3 pelvisOffset = new Vector3(0f, -.025f, -.025f);
    [SerializeField] private float clearance = .008f;
    [SerializeField] private float windStrength = .004f;
    [SerializeField] private float maxWind = .03f;
    [Header("Flight (enabled only on battle prefab)")]
    [SerializeField] private bool flightMode;
    [SerializeField, Min(1f)] private float fullFlutterSpeed = 60f;
    [SerializeField, Range(0f, .03f)] private float flutterStrength = .012f;
    public float FlightFlutterAmount { get; private set; }
    public float FlightBoostAmount { get; private set; }
    private Player flightPlayer;
    [Header("Flight broom contact")]
    [SerializeField] private bool broomContact = true;
    [SerializeField, Min(0f)] private float broomRadius = .045f;
    [SerializeField] private Vector3 broomContactOffset;
    private PlayerEquipment equipment;
    private GameObject currentBroom;
    private struct BroomShape { public Vector3 start, end; public bool valid; }
    private readonly System.Collections.Generic.Dictionary<GameObject, BroomShape> broomShapes = new();
    private BroomShape broomShape;
    private Vector3 broomStart, broomEnd;
    private float broomWorldRadius;
    private NetworkObject owner;
    private bool running;
    private Vector3 lastPosition, wind;
    private Quaternion lastRotation;
    private Transform hips;
    private Quaternion pelvisAxes;
    private struct Capsule
    {
        public Transform from, to;
        public Vector3 a, b;
        public float modelRadius, radius;
    }
    private Capsule[] capsules;

    // Register only the locally owned active solver, not every remote avatar.
    private new void OnEnable() { }
    private new void OnDisable() { StopSimulation(); }
    private void Awake()
    {
        if (solver == null || bodyAnimator == null || motionRoot == null || skirt == null || !bodyAnimator.isHuman)
        { enabled = false; return; }
        owner = motionRoot.GetComponent<NetworkObject>();
        if (flightMode) flightPlayer = motionRoot.GetComponent<Player>();
        if (flightMode) equipment = motionRoot.GetComponentInChildren<PlayerEquipment>(true);
        solver.enabled = false;
        if (flightMode) solver.sleepThreshold = Mathf.Min(solver.sleepThreshold, 1e-8f);
        solver.customForce = GetWind;
        hips = bodyAnimator.GetBoneTransform(HumanBodyBones.Hips);
        if (hips != null) pelvisAxes = Quaternion.Inverse(hips.rotation) * motionRoot.rotation;
        capsules = new Capsule[7];
        Cache(0, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, thighRadius);
        Cache(1, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, thighRadius);
        Cache(2, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, .055f);
        Cache(3, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, .055f);
        Cache(4, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, armRadius);
        Cache(5, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, armRadius);
    }
    private void Cache(int i, HumanBodyBones from, HumanBodyBones to, float radius)
    {
        Transform a = bodyAnimator.GetBoneTransform(from), b = bodyAnimator.GetBoneTransform(to);
        capsules[i] = new Capsule { from = a, to = b != null ? b : a, modelRadius = radius };
    }
    private void LateUpdate()
    {
        bool active = owner != null && owner.IsValid && owner.HasInputAuthority && skirt.enabled && skirt.isVisible;
        if (!active) { StopSimulation(); return; }
        FlightBoostAmount = Mathf.MoveTowards(FlightBoostAmount,
            flightPlayer != null && flightPlayer.IsVisualBoostActive ? 1f : 0f, Time.deltaTime * 5f);
        float scale = Mathf.Max(.001f, Mathf.Abs((flightMode ? bodyAnimator.transform : motionRoot).lossyScale.y));
        UpdateBroomContact(scale);
        for (int i = 0; i < capsules.Length; i++)
        {
            if (capsules[i].from == null || capsules[i].to == null) continue;
            capsules[i].a = capsules[i].from.position;
            capsules[i].b = capsules[i].to.position;
            capsules[i].radius = (capsules[i].modelRadius + clearance) * scale;
        }
        if (hips != null)
        {
            // Bone axes vary between models. Calibrate to the character once,
            // then follow the final animated/seat-adjusted pelvis, including tilt.
            Quaternion axes = hips.rotation * pelvisAxes;
            Vector3 center = hips.position + axes * (pelvisOffset * scale);
            Vector3 side = axes * Vector3.right * (pelvisHalfWidth * scale);
            capsules[6].a = center - side;
            capsules[6].b = center + side;
            capsules[6].radius = pelvisRadius > 0f ? (pelvisRadius + clearance) * scale : 0f;
        }
        Vector3 position = motionRoot.position;
        Quaternion rotation = motionRoot.rotation;
        float teleportDistance = flightMode ? Mathf.Max(5f, fullFlutterSpeed * Time.deltaTime * 3f) : 2f;
        bool reset = !running || Time.deltaTime > .1f ||
            (position - lastPosition).sqrMagnitude > teleportDistance * teleportDistance || Quaternion.Angle(rotation, lastRotation) > 90f;
        if (!running)
        {
            base.OnEnable();
            solver.enabled = true;
            running = true;
        }
        if (reset) { solver.SetRestState(); wind = Vector3.zero; FlightFlutterAmount = 0f; }
        else if (Time.deltaTime > .00001f)
        {
            Vector3 velocity = (position - lastPosition) / Time.deltaTime;
            if (flightMode)
            {
                float speed = Mathf.Clamp01(velocity.magnitude / Mathf.Max(1f, fullFlutterSpeed));
                FlightFlutterAmount = Mathf.Lerp(FlightFlutterAmount, speed, 1f - Mathf.Exp(-6f * Time.deltaTime));
                // Remove vertical wind so climbing cannot turn the skirt inside out.
                Vector3 back = Vector3.ProjectOnPlane(-velocity, motionRoot.up).normalized;
                float pulse = Mathf.Sin(Time.time * 11f) * .65f + Mathf.Sin(Time.time * 17f) * .35f;
                Vector3 target = back * (maxWind * speed * .7f) + motionRoot.right * (pulse * flutterStrength * speed);
                wind = Vector3.Lerp(wind, Vector3.ClampMagnitude(target, maxWind), 1f - Mathf.Exp(-8f * Time.deltaTime));
            }
            else wind = Vector3.ClampMagnitude(-velocity * windStrength, maxWind);
        }
        lastPosition = position; lastRotation = rotation;
    }
    private Vector3 GetWind(float length) { return wind * length; }
    private void UpdateBroomContact(float scale)
    {
        broomWorldRadius = 0f;
        if (!flightMode || !broomContact || equipment == null) return;
        GameObject selected = equipment.CurrentBroomObject;
        if (selected != currentBroom)
        {
            currentBroom = selected;
            broomShape = default;
            if (selected != null && !broomShapes.TryGetValue(selected, out broomShape))
            {
                // Build a shaft proxy only on equipment change, never read meshes each frame.
                Bounds bounds = default; bool found = false;
                foreach (var renderer in selected.GetComponentsInChildren<Renderer>(true))
                {
                    if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
                    Bounds local = renderer.localBounds;
                    for (int corner = 0; corner < 8; corner++)
                    {
                        Vector3 sign = new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1);
                        Vector3 p = selected.transform.InverseTransformPoint(renderer.transform.TransformPoint(local.center + Vector3.Scale(local.extents, sign)));
                        if (!found) { bounds = new Bounds(p, Vector3.zero); found = true; } else bounds.Encapsulate(p);
                    }
                }
                Vector3 e = bounds.extents;
                int axis = e.x >= e.y && e.x >= e.z ? 0 : e.y >= e.z ? 1 : 2;
                Vector3 half = Vector3.zero; half[axis] = e[axis] * .9f;
                broomShape = new BroomShape { start = bounds.center - half, end = bounds.center + half, valid = found };
                broomShapes[selected] = broomShape;
            }
        }
        if (selected == null || !selected.activeInHierarchy || !broomShape.valid) return;
        broomStart = selected.transform.TransformPoint(broomShape.start + broomContactOffset);
        broomEnd = selected.transform.TransformPoint(broomShape.end + broomContactOffset);
        broomWorldRadius = (broomRadius + clearance) * scale;
    }
    public void ResolveBroomContact(ref Vector3 position, float spacing)
    {
        if (!running || broomWorldRadius <= 0f) return;
        Vector3 axis = broomEnd - broomStart;
        float length = axis.sqrMagnitude;
        Vector3 nearest = broomStart + axis * (length > .000001f ? Mathf.Clamp01(Vector3.Dot(position - broomStart, axis) / length) : 0f);
        Vector3 away = position - nearest;
        float r = broomWorldRadius + spacing;
        if (away.sqrMagnitude < r * r)
            position = nearest + (away.sqrMagnitude > .000001f ? away.normalized : motionRoot.up) * r;
    }
    private void StopSimulation()
    {
        base.OnDisable();
        if (solver != null) solver.enabled = false;
        running = false;
        FlightFlutterAmount = 0f;
        FlightBoostAmount = 0f;
    }
    public override void Collide(ref Vector3 position, float spacing)
    {
        if (!running || capsules == null) return;
        for (int i = 0; i < capsules.Length; i++)
        {
            Capsule c = capsules[i];
            if (c.radius <= 0f) continue;
            Vector3 axis = c.b - c.a;
            float length = axis.sqrMagnitude;
            Vector3 closest = c.a + axis * (length > .000001f ? Mathf.Clamp01(Vector3.Dot(position - c.a, axis) / length) : 0f);
            Vector3 away = position - closest;
            float r = c.radius + spacing;
            if (away.sqrMagnitude >= r * r) continue;
            position = closest + (away.sqrMagnitude > .000001f ? away.normalized : motionRoot.forward) * r;
        }
        ResolveBroomContact(ref position, spacing);
    }
    private void OnDestroy()
    {
        StopSimulation();
        if (solver != null) solver.customForce = null;
    }
}
