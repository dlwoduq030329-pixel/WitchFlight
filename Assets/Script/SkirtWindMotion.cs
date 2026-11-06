using System.Collections.Generic;
using UnityEngine;

// Cosmetic bone motion only. No network state or gameplay collision is modified.
[DefaultExecutionOrder(320)]
public sealed class SkirtWindMotion : MonoBehaviour
{
    [SerializeField] private SkinnedMeshRenderer skirt;
    [SerializeField] private Transform motionRoot;
    [SerializeField, Min(1f)] private float fullWindSpeed = 18f;
    [SerializeField, Range(0f, 8f)] private float windAngle = 2.5f;
    [SerializeField, Range(0f, 4f)] private float flutterAngle = 1f;
    [SerializeField, Range(0f, 8f)] private float turnAngle = 2f;
    [SerializeField, Min(.1f)] private float response = 6f;
    [Header("Body pose and thigh clearance")]
    [SerializeField] private Animator bodyAnimator;
    [SerializeField, Range(0f, 1f)] private float thighFollow = .85f;
    [SerializeField, Min(.01f)] private float thighRadius = .105f;
    [SerializeField, Min(0f)] private float clothClearance = .025f;
    [SerializeField, Range(0f, 30f)] private float maxClearanceAngle = 18f;

    private struct Bone
    {
        public Transform transform;
        public Quaternion pose;
        public float weight, phase;
        public Transform tip;
        public bool attachment;
        public int depth;
    }
    private Bone[] bones;
    private Vector3 previousPosition, air;
    private Quaternion previousRotation;
    private float turn;
    private bool sampled, applied;
    private Transform hips, leftThigh, leftKnee, rightThigh, rightKnee;
    private Vector3 hipsUp, hipsForward;

    private void Awake()
    {
        if (skirt == null || motionRoot == null) { enabled = false; return; }
        if (bodyAnimator != null && bodyAnimator.isHuman)
        {
            hips = bodyAnimator.GetBoneTransform(HumanBodyBones.Hips);
            leftThigh = bodyAnimator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            leftKnee = bodyAnimator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            rightThigh = bodyAnimator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            rightKnee = bodyAnimator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            if (hips != null)
            {
                hipsUp = hips.InverseTransformDirection(motionRoot.up);
                hipsForward = hips.InverseTransformDirection(motionRoot.forward);
            }
        }
        var selected = new List<Bone>();
        // Read the renderer's own rig once; never search the scene per frame.
        foreach (var bone in skirt.bones)
        {
            if (bone == null || !bone.name.StartsWith("Skirt_")) continue;
            string[] parts = bone.name.Split('_');
            int level = 0;
            foreach (string part in parts)
                if (part.Length == 3 && int.TryParse(part, out int n)) level = n;
            if (level < 1 || level > 5) continue;
            int depth = 0;
            for (Transform parent = bone.parent; parent != null; parent = parent.parent) depth++;
            selected.Add(new Bone { transform = bone, weight = (level - 1) / 4f,
                phase = selected.Count * 1.37f, attachment = level == 1, depth = depth,
                tip = bone.childCount > 0 ? bone.GetChild(0) : null });
        }
        selected.Sort((a, b) => a.depth.CompareTo(b.depth));
        bones = selected.ToArray();
    }

    private void OnEnable() { sampled = false; }
    private void Update() { RestorePose(); }
    private void OnDisable() { RestorePose(); sampled = false; }

    private void RestorePose()
    {
        if (!applied || bones == null) return;
        for (int i = 0; i < bones.Length; i++)
            if (bones[i].transform != null) bones[i].transform.localRotation = bones[i].pose;
        applied = false;
    }

    private void LateUpdate()
    {
        if (skirt == null || motionRoot == null || bones == null) return;
        Vector3 position = motionRoot.position;
        Quaternion rotation = motionRoot.rotation;
        float dt = Time.deltaTime;
        bool visible = skirt.enabled && skirt.isVisible;
        if (!sampled || !visible || dt <= 0f || dt > .15f ||
            (position - previousPosition).sqrMagnitude > 100f)
        {
            previousPosition = position; previousRotation = rotation;
            air = Vector3.zero; turn = 0f; sampled = visible;
            return;
        }
        float scale = Mathf.Max(.01f, Mathf.Abs(motionRoot.lossyScale.y));
        Vector3 velocity = (position - previousPosition) / (dt * scale);
        Vector3 localAir = Quaternion.Inverse(rotation) * -velocity / fullWindSpeed;
        float yawRate = Mathf.DeltaAngle(previousRotation.eulerAngles.y, rotation.eulerAngles.y) / dt;
        float blend = 1f - Mathf.Exp(-response * dt);
        air = Vector3.Lerp(air, Vector3.ClampMagnitude(localAir, 1f), blend);
        turn = Mathf.Lerp(turn, Mathf.Clamp(yawRate / 180f, -1f, 1f), blend);
        previousPosition = position; previousRotation = rotation;
        float strength = air.magnitude;
        Vector3 windAxis = motionRoot.TransformDirection(new Vector3(air.z, 0f, -air.x));
        for (int i = 0; i < bones.Length; i++)
        {
            Transform bone = bones[i].transform;
            if (bone == null) continue;
            bones[i].pose = bone.localRotation;
            float flutter = Mathf.Sin(Time.time * (5f + strength * 5f) + bones[i].phase) *
                flutterAngle * strength;
            Vector3 worldAngles = windAxis * windAngle + motionRoot.forward * (turn * turnAngle + flutter);
            Vector3 localAngles = bone.parent != null ? bone.parent.InverseTransformDirection(worldAngles) : worldAngles;
            bone.localRotation = Quaternion.Euler(localAngles * bones[i].weight) * bones[i].pose;
        }
        ApplyBodyClearance(scale);
        applied = true;
    }

    private void ApplyBodyClearance(float scale)
    {
        if (hips == null || leftThigh == null || leftKnee == null || rightThigh == null || rightKnee == null) return;
        Vector3 up = hips.TransformDirection(hipsUp), forward = hips.TransformDirection(hipsForward);
        Vector3 right = Vector3.Cross(up, forward).normalized;
        // Move the front panels over the raised thighs before resolving intersections.
        // Only rotations change: waist anchors and bone lengths remain intact.
        for (int i = 0; i < bones.Length; i++)
        {
            Transform bone = bones[i].transform;
            if (!bones[i].attachment || bone == null) continue;
            Vector3 offset = bone.position - hips.position;
            Vector3 radial = Vector3.ProjectOnPlane(offset, up);
            float front = Mathf.Clamp01(Vector3.Dot(radial.normalized, forward) * 2f);
            bool left = Vector3.Dot(offset, right) < 0f;
            Vector3 leg = ((left ? leftKnee.position : rightKnee.position) -
                (left ? leftThigh.position : rightThigh.position)).normalized;
            float bend = Mathf.Clamp(Mathf.Atan2(Vector3.Dot(leg, forward),
                -Vector3.Dot(leg, up)) * Mathf.Rad2Deg, 0f, 110f);
            bone.rotation = Quaternion.AngleAxis(bend * front * thighFollow, -right) * bone.rotation;
        }
        float radius = (thighRadius + clothClearance) * scale;
        // Two small parent-first passes, no Physics queries or Collider allocations.
        for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < bones.Length; i++)
            {
                Transform bone = bones[i].transform, tip = bones[i].tip;
                if (bone == null || tip == null) continue;
                AvoidThigh(bone, tip, leftThigh.position, leftKnee.position, radius, up);
                AvoidThigh(bone, tip, rightThigh.position, rightKnee.position, radius, up);
            }
    }

    private void AvoidThigh(Transform bone, Transform tip, Vector3 start, Vector3 end, float radius, Vector3 up)
    {
        Vector3 segment = end - start;
        float lengthSquared = segment.sqrMagnitude;
        if (lengthSquared < .000001f) return;
        // Test both the segment's middle and end to reduce long-bone tunnelling.
        for (int sample = 1; sample <= 2; sample++)
        {
            Vector3 point = Vector3.Lerp(bone.position, tip.position, sample * .5f);
            Vector3 closest = start + segment * Mathf.Clamp01(Vector3.Dot(point - start, segment) / lengthSquared);
            Vector3 outward = point - closest;
            float distance = outward.magnitude;
            if (distance >= radius) continue;
            if (distance < .0001f) outward = up;
            else outward /= distance;
            // On a raised thigh prefer the upper surface, rather than folding underneath it.
            if (Vector3.Dot(segment.normalized, up) > -.6f && Vector3.Dot(outward, up) < .25f)
                outward = (Vector3.ProjectOnPlane(outward, up) + up).normalized;
            Vector3 from = point - bone.position;
            Vector3 to = closest + outward * radius - bone.position;
            if (from.sqrMagnitude < .000001f || to.sqrMagnitude < .000001f) continue;
            Quaternion correction = Quaternion.FromToRotation(from, to);
            bone.rotation = Quaternion.RotateTowards(Quaternion.identity, correction, maxClearanceAngle * .5f) * bone.rotation;
        }
    }
}
