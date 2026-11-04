using System;
using UnityEngine;

// Cosmetic bone offset only. Never writes the player root, hips, aim or flight state.
[Serializable]
public sealed class PlayerHeadLook
{
    [SerializeField] private bool enabled = true;
    [Tooltip("실제 머리 본. 비워 두면 Humanoid Head 본을 찾습니다.")]
    [SerializeField] private Transform headBone;
    [SerializeField, Range(0f, 90f)] private float maxYaw = 70f;
    [SerializeField, Range(0f, 60f)] private float maxPitch = 40f;
    [SerializeField, Min(0.1f)] private float responseSpeed = 12f;
    [SerializeField, Range(0f, 1f)] private float weight = 1f;

    private bool searched, applied;
    private Transform appliedBone;
    private Quaternion animatedLocalRotation;
    private Vector2 smoothedAngles;

    public Vector2 GetAngles(Transform body, Vector3 direction)
    {
        if (!enabled || !IsFinite(direction) || direction.sqrMagnitude < 0.0001f)
            return Vector2.zero;
        Vector3 local = body.InverseTransformDirection(direction.normalized);
        // Never turn through the back of the neck. Keep at the yaw/pitch limit instead.
        float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
        float pitch = -Mathf.Atan2(local.y, Mathf.Sqrt(local.x * local.x + local.z * local.z)) * Mathf.Rad2Deg;
        // Half-degree steps suppress tiny network changes. Presentation smooths these steps.
        return new Vector2(Mathf.Round(Mathf.Clamp(yaw, -maxYaw, maxYaw) * 2f) * 0.5f,
            Mathf.Round(Mathf.Clamp(pitch, -maxPitch, maxPitch) * 2f) * 0.5f);
    }

    private static bool IsFinite(Vector3 value) =>
        !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
        !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
        !float.IsNaN(value.z) && !float.IsInfinity(value.z) && value.sqrMagnitude <= 4f;

    public void RestorePose()
    {
        if (applied && appliedBone != null) appliedBone.localRotation = animatedLocalRotation;
        applied = false;
    }

    public void Reset()
    {
        RestorePose();
        smoothedAngles = Vector2.zero;
    }

    public void Apply(Transform body, Animator animator, Vector2 angles, bool active, float deltaTime)
    {
        if (!enabled || !active) { Reset(); return; }
        if (headBone == null && !searched)
        {
            searched = true;
            if (animator != null && animator.isHuman && animator.avatar != null && animator.avatar.isValid)
                headBone = animator.GetBoneTransform(HumanBodyBones.Head);
            if (headBone == null)
            {
                // Ignore end/leaf bones named Head when a real parent Head exists.
                foreach (Transform bone in body.GetComponentsInChildren<Transform>(true))
                    if (bone.name == "Head" && (headBone == null || bone.childCount > headBone.childCount))
                        headBone = bone;
            }
        }
        if (headBone == null || headBone == body || !headBone.IsChildOf(body)) return;
        smoothedAngles = Vector2.Lerp(smoothedAngles, angles,
            1f - Mathf.Exp(-Mathf.Max(0.1f, responseSpeed) * Mathf.Max(0f, deltaTime)));
        animatedLocalRotation = headBone.localRotation;
        appliedBone = headBone;
        // Conjugate by body rotation so imported bone axes and rolled flight both work.
        Quaternion offset = Quaternion.Euler(smoothedAngles.y * weight, smoothedAngles.x * weight, 0f);
        headBone.rotation = body.rotation * offset * Quaternion.Inverse(body.rotation) * headBone.rotation;
        applied = true;
    }
}
