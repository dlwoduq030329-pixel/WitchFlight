using System;
using UnityEngine;

[Serializable]
public sealed class PlayerBladeSwing
{
    [SerializeField, Min(.2f)] private float duration = .65f;
    [SerializeField, Range(0f, 90f)] private float armSweep = 65f;
    [SerializeField, Range(0f, 35f)] private float torsoTwist = 20f;
    [SerializeField, Range(0f, 140f), Tooltip("곧게 편 시작 자세 기준, 팔꿈치를 굽히기 시작하는 베기 각도입니다.")]
    private float elbowBendStartAngle = 130f;
    private Animator cachedAnimator;
    private readonly Transform[] bones = new Transform[4];
    private readonly Quaternion[] baseRotations = new Quaternion[4];
    private bool applied, initialized;
    private int sequence, step;
    private float elapsed = 10f;
    public bool IsActive => initialized && elapsed < duration;
    public float Duration => Mathf.Max(.2f, duration);
    public static float CutProgress(float time) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.1f, .38f, time));

    public void RestorePose()
    {
        if (!applied) return;
        for (int i = 0; i < bones.Length; i++)
            if (bones[i] != null) bones[i].localRotation = baseRotations[i];
        applied = false;
    }

    public void Reset()
    {
        RestorePose();
        elapsed = 10f;
        initialized = false;
    }

    public void Apply(Transform body, Animator animator, int currentSequence, int swingStep, bool active, float deltaTime, Transform weaponAnchor, bool charging = false)
    {
        RestorePose();
        if (!initialized)
        {
            initialized = true;
            sequence = currentSequence;
        }
        if (sequence != currentSequence)
        {
            sequence = currentSequence;
            step = swingStep;
            elapsed = 0f;
        }
        if (!active) { elapsed = 10f; return; }
        if (elapsed >= duration && !charging) return;
        if (animator != cachedAnimator)
        {
            cachedAnimator = animator;
            Array.Clear(bones, 0, bones.Length);
            if (animator != null && animator.isHuman)
            {
                bones[0] = animator.GetBoneTransform(HumanBodyBones.Chest);
                bones[1] = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                bones[2] = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
                bones[3] = animator.GetBoneTransform(HumanBodyBones.RightHand);
            }
            else if (animator != null)
            {
                foreach (Transform bone in animator.GetComponentsInChildren<Transform>(true))
                {
                    if (bone.name == "Chest") bones[0] = bone;
                    else if (bone.name == "Upperarm_R") bones[1] = bone;
                    else if (bone.name == "Lowerarm_R") bones[2] = bone;
                    else if (bone.name == "Right Hand" || bone.name == "Hand_R") bones[3] = bone;
                }
            }
        }
        float t = charging ? .1f : Mathf.Clamp01(elapsed / Mathf.Max(.2f, duration));
        elapsed += Mathf.Max(0f, deltaTime);
        float side = step % 2 == 0 ? -1f : 1f;
        // Short draw-back, fast cross-body sweep, then a long weighted recovery.
        float sweep = t < .1f ? Mathf.Lerp(0f, -.7f, Mathf.SmoothStep(0f, 1f, t / .1f)) :
            t < .38f ? Mathf.Lerp(-.7f, 1f, CutProgress(t)) :
            Mathf.Lerp(1f, 0f, Mathf.SmoothStep(0f, 1f, (t - .38f) / .62f));
        float lift = Mathf.Sin(t * Mathf.PI);
        // Store all local poses before applying parent rotations. Bone lengths stay authored.
        for (int i = 0; i < bones.Length; i++)
            if (bones[i] != null) baseRotations[i] = bones[i].localRotation;
        Rotate(body, bones[0], new Vector3(6f * lift, side * torsoTwist * sweep, -side * 7f * lift));
        Rotate(body, bones[1], new Vector3(-18f * lift, side * armSweep * sweep, side * 15f * sweep));
        Rotate(body, bones[2], new Vector3(-10f * lift, side * 18f * sweep, 0f));
        // Aim the whole arm across the chest instead of merely twisting its local axes.
        // Keep authored bone lengths and the broom-holding left arm untouched.
        if (bones[1] != null && bones[3] != null)
        {
            float reachWeight = t < .1f ? Mathf.SmoothStep(0,1,t/.1f) :
                1f - Mathf.SmoothStep(0,1,Mathf.InverseLerp(.48f,1f,t));
            float sweptAngle = 140f * CutProgress(t);
            float yaw = 70f - sweptAngle;
            float shoulderYaw = 70f - Mathf.Min(sweptAngle, elbowBendStartAngle);
            Vector3 forward = Vector3.ProjectOnPlane(body.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .001f) forward = Vector3.forward;
            Vector3 desired = Quaternion.AngleAxis(yaw, Vector3.up) * forward;
            Vector3 shoulderDirection = Quaternion.AngleAxis(shoulderYaw, Vector3.up) * forward;
            Vector3 current = (bones[2] != null ? bones[2].position : bones[3].position) - bones[1].position;
            if (current.sqrMagnitude > .001f)
                bones[1].rotation = Quaternion.Slerp(Quaternion.identity,
                    Quaternion.FromToRotation(current,shoulderDirection),reachWeight) * bones[1].rotation;
            // The upper arm stops at the threshold; the forearm continues left, bending the elbow.
            if (bones[2] != null)
            {
                Vector3 forearm = bones[3].position - bones[2].position;
                if (forearm.sqrMagnitude > .001f)
                    bones[2].rotation = Quaternion.Slerp(Quaternion.identity,
                        Quaternion.FromToRotation(forearm, desired), reachWeight) * bones[2].rotation;
            }
        }
        if (TryGetArmDirection(out Vector3 armDirection) && weaponAnchor != null && weaponAnchor.IsChildOf(bones[3]))
        {
            Vector3 wandDirection = weaponAnchor.position - bones[3].position;
            if (wandDirection.sqrMagnitude > .0001f)
                bones[3].rotation = Quaternion.FromToRotation(wandDirection, armDirection) * bones[3].rotation;
        }
        applied = true;
    }

    public bool TryGetArmDirection(out Vector3 direction)
    {
        direction = Vector3.zero;
        if (bones[2] == null || bones[3] == null) return false;
        direction = bones[3].position - bones[2].position;
        if (direction.sqrMagnitude <= .0001f) return false;
        direction.Normalize();
        return true;
    }

    private static void Rotate(Transform body, Transform bone, Vector3 angles)
    {
        if (bone == null) return;
        bone.rotation = body.rotation * Quaternion.Euler(angles) * Quaternion.Inverse(body.rotation) * bone.rotation;
    }
}
