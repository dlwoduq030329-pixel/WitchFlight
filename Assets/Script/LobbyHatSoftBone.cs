using EZhex1991.EZSoftBone;
using Fusion;
using UnityEngine;

[DefaultExecutionOrder(15000)]
public sealed class LobbyHatSoftBone : EZSoftBoneColliderBase
{
    public EZSoftBone solver;
    public Renderer hatRenderer;
    public Animator bodyAnimator;
    public Transform motionRoot;
    public bool softMovement;
    [Tooltip("Movement-driven wobble strength. Only applies when Soft Movement is enabled.")]
    [Range(0f, .03f)] public float movementStrength = .003f;
    [Range(.01f, .12f)] public float armRadius = .045f;
    public bool flightMode;
    [Min(1f)] public float fullFlutterSpeed = 60f;
    [Range(0f, .03f)] public float flightFlutterStrength = .008f;
    [Range(0f, 30f)] public float flightFlutterDegrees = 18f;
    [Range(0f, 16f)] public float flightBaseFlutterDegrees = 5f;
    [Range(0f, 1f)] public float flightCruiseFlutterFloor = .65f;
    [Range(1f, 2f)] public float flightBoostFlutterMultiplier = 1.5f;
    public float FlightFlutterAmount { get; private set; }
    public float FlightBoostAmount { get; private set; }
    private Player flightPlayer;
    private NetworkObject owner;
    private Transform[] joints;
    private readonly Vector3[] points = new Vector3[6];
    private readonly Vector3[] segmentAxis = new Vector3[4], segmentMin = new Vector3[4], segmentMax = new Vector3[4];
    private readonly float[] inverseLengthSquared = new float[4];
    private Vector3 previous, wind;
    private bool running;
    private float radius;
    public float ContactScale => Mathf.Abs((flightMode && bodyAnimator != null ? bodyAnimator.transform : motionRoot).lossyScale.y);
    private new void OnEnable() { }
    private new void OnDisable() { Stop(); }
    private void Awake()
    {
        if (solver == null || hatRenderer == null || bodyAnimator == null || motionRoot == null || !bodyAnimator.isHuman)
        { enabled = false; return; }
        owner = motionRoot.GetComponent<NetworkObject>();
        if (flightMode) flightPlayer = motionRoot.GetComponent<Player>();
        joints = new[] {
            bodyAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm),
            bodyAnimator.GetBoneTransform(HumanBodyBones.LeftLowerArm),
            bodyAnimator.GetBoneTransform(HumanBodyBones.LeftHand),
            bodyAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm),
            bodyAnimator.GetBoneTransform(HumanBodyBones.RightLowerArm),
            bodyAnimator.GetBoneTransform(HumanBodyBones.RightHand) };
        solver.enabled = false;
        if (flightMode) solver.sleepThreshold = Mathf.Min(solver.sleepThreshold, 1e-8f);
        solver.customForce = GetForce;
    }
    private Vector3 GetForce(float length) => wind * length;
    private void LateUpdate()
    {
        if (owner == null || !owner.IsValid || !owner.HasInputAuthority || !hatRenderer.enabled || !hatRenderer.gameObject.activeInHierarchy || !hatRenderer.isVisible)
        { Stop(); return; }
        FlightBoostAmount = Mathf.MoveTowards(FlightBoostAmount,
            flightPlayer != null && flightPlayer.IsVisualBoostActive ? 1f : 0f, Time.deltaTime * 5f);
        for (int i = 0; i < joints.Length; i++) if (joints[i] != null) points[i] = joints[i].position;
        radius = armRadius * ContactScale;
        for (int segment = 0; segment < 4; segment++)
        {
            int i = segment / 2 * 3 + segment % 2;
            segmentAxis[segment] = points[i + 1] - points[i];
            float length = segmentAxis[segment].sqrMagnitude;
            inverseLengthSquared[segment] = length > .000001f ? 1f / length : 0f;
            segmentMin[segment] = Vector3.Min(points[i], points[i + 1]);
            segmentMax[segment] = Vector3.Max(points[i], points[i + 1]);
        }
        Vector3 position = motionRoot.position;
        float teleportDistance = flightMode ? Mathf.Max(5f, fullFlutterSpeed * Time.deltaTime * 3f) : 2f;
        bool reset = !running || Time.deltaTime > .1f || (position - previous).sqrMagnitude > teleportDistance * teleportDistance;
        if (!running) { base.OnEnable(); solver.enabled = true; running = true; }
        if (reset) { solver.SetRestState(); wind = Vector3.zero; FlightFlutterAmount = 0f; }
        else if (Time.deltaTime > .00001f)
        {
            Vector3 velocity = (position - previous) / Time.deltaTime;
            float speed = Mathf.Clamp01(velocity.magnitude / (flightMode ? Mathf.Max(1f, fullFlutterSpeed) : 5f));
            if (flightMode)
            {
                FlightFlutterAmount = Mathf.Lerp(FlightFlutterAmount, speed, 1f - Mathf.Exp(-6f * Time.deltaTime));
                Vector3 back = Vector3.ProjectOnPlane(-velocity, motionRoot.up).normalized;
                Vector3 target = back * (.012f * speed) + motionRoot.up * (Mathf.Sin(Time.time * 9f) * flightFlutterStrength * speed);
                wind = Vector3.Lerp(wind, Vector3.ClampMagnitude(target, .018f), 1f - Mathf.Exp(-8f * Time.deltaTime));
            }
            else wind = softMovement ? Vector3.ClampMagnitude(-velocity * movementStrength, .018f) +
                motionRoot.up * (Mathf.Sin(Time.time * 5f) * speed * movementStrength) : Vector3.zero;
        }
        previous = position;
    }
    public override void Collide(ref Vector3 position, float spacing)
    {
        if (!running) return;
        for (int arm = 0; arm < 2; arm++)
            for (int part = 0; part < 2; part++)
            {
                int i = arm * 3 + part;
                if (joints[i] == null || joints[i + 1] == null) continue;
                int segment = arm * 2 + part;
                float r = radius + spacing;
                Vector3 min = segmentMin[segment], max = segmentMax[segment];
                if (position.x < min.x - r || position.x > max.x + r ||
                    position.y < min.y - r || position.y > max.y + r ||
                    position.z < min.z - r || position.z > max.z + r) continue;
                Vector3 axis = segmentAxis[segment];
                Vector3 nearest = points[i] + axis * Mathf.Clamp01(Vector3.Dot(position - points[i], axis) * inverseLengthSquared[segment]);
                Vector3 away = position - nearest;
                if (away.sqrMagnitude < r * r)
                    position = nearest + (away.sqrMagnitude > .000001f ? away.normalized : motionRoot.up) * r;
            }
    }
    private void Stop() { base.OnDisable(); if (solver != null) solver.enabled = false; running = false; FlightFlutterAmount = 0f; FlightBoostAmount = 0f; }
    private void OnDestroy() { Stop(); if (solver != null) solver.customForce = null; }
}
