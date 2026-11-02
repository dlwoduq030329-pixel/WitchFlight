using System;
using UnityEngine;

[Serializable]
public sealed class PlayerBladeSwing
{
    [SerializeField, Min(.2f)] private float duration = .65f;
    [SerializeField, Range(0f, 90f)] private float armSweep = 65f;
    [SerializeField, Range(0f, 35f)] private float torsoTwist = 20f;
    private Animator cachedAnimator;
    private readonly Transform[] bones = new Transform[4];
    private readonly Quaternion[] baseRotations = new Quaternion[4];
    private bool applied, initialized;
    private int sequence, step;
    private float elapsed = 10f;
    public bool IsActive => initialized && elapsed < duration;

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

    public void Apply(Transform body, Animator animator, int currentSequence, int swingStep, bool active, float deltaTime, Transform weaponAnchor)
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
        if (elapsed >= duration) return;
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
        float t = Mathf.Clamp01(elapsed / Mathf.Max(.2f, duration));
        elapsed += Mathf.Max(0f, deltaTime);
        float side = step % 2 == 0 ? -1f : 1f;
        // Short draw-back, fast cross-body sweep, then a long weighted recovery.
        float sweep = t < .18f ? Mathf.Lerp(0f, -.35f, Mathf.SmoothStep(0f, 1f, t / .18f)) :
            t < .4f ? Mathf.Lerp(-.35f, 1f, Mathf.SmoothStep(0f, 1f, (t - .18f) / .22f)) :
            Mathf.Lerp(1f, 0f, Mathf.SmoothStep(0f, 1f, (t - .4f) / .6f));
        float lift = Mathf.Sin(t * Mathf.PI);
        // Store all local poses before applying parent rotations. Bone lengths stay authored.
        for (int i = 0; i < bones.Length; i++)
            if (bones[i] != null) baseRotations[i] = bones[i].localRotation;
        Rotate(body, bones[0], new Vector3(6f * lift, side * torsoTwist * sweep, -side * 7f * lift));
        Rotate(body, bones[1], new Vector3(-18f * lift, side * armSweep * sweep, side * 15f * sweep));
        Rotate(body, bones[2], new Vector3(-10f * lift, side * 18f * sweep, 0f));
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
