using UnityEngine;

// Presentation-only immortal target. Never registers with battle damage/score systems.
[DefaultExecutionOrder(10000)]
public sealed class RobbyPracticeDummy : MonoBehaviour
{
    [SerializeField] private Transform lockOnPos;
    [SerializeField] private Transform springBone;
    [SerializeField, Min(.05f)] private float hitRadius = .7f;
    [SerializeField, Min(1f)] private float springFrequency = 12f;
    [SerializeField, Min(.1f)] private float damping = 3f;
    [SerializeField, Min(1f)] private float hitImpulse = 480f;
    [SerializeField, Range(1f, 65f)] private float maxAngle = 38f;
    [SerializeField, Range(0f, .4f)] private float squashAmount = .22f;
    private Quaternion restRotation;
    private Vector3 restScale;
    private float displacement, velocity;
    private Vector3 axis = Vector3.forward;
    public Vector3 AimPoint => lockOnPos != null ? lockOnPos.position : transform.position + Vector3.up;
    public float Radius => hitRadius;
    private void Awake()
    {
        if (springBone != null) { restRotation = springBone.localRotation; restScale = springBone.localScale; }
        enabled = false;
    }
    public void Hit(Vector3 direction)
    {
        if (springBone == null) return;
        Vector3 local = springBone.parent != null ? springBone.parent.InverseTransformDirection(direction) : direction;
        axis = Vector3.Cross(Vector3.up, local.normalized);
        if (axis.sqrMagnitude < .001f) axis = Vector3.forward;
        axis.Normalize();
        velocity = Mathf.Clamp(velocity + hitImpulse, -hitImpulse * 1.5f, hitImpulse * 1.5f);
        enabled = true;
    }
    private void LateUpdate()
    {
        if (springBone == null) { enabled = false; return; }
        // Substeps keep the damped spring stable after a slow frame.
        float remaining = Mathf.Min(Time.deltaTime, .1f);
        while (remaining > 0f)
        {
            float dt = Mathf.Min(remaining, .008f); remaining -= dt;
            velocity += (-springFrequency * springFrequency * displacement - damping * velocity) * dt;
            displacement += velocity * dt;
        }
        displacement = Mathf.Clamp(displacement, -maxAngle, maxAngle);
        springBone.localRotation = restRotation * Quaternion.AngleAxis(displacement, axis);
        float squash = Mathf.Clamp(displacement / Mathf.Max(1f, maxAngle), -1f, 1f) * squashAmount;
        springBone.localScale = Vector3.Scale(restScale, new Vector3(1f + squash, 1f - squash, 1f + squash));
        if (Mathf.Abs(displacement) < .01f && Mathf.Abs(velocity) < .05f)
        { Restore(); enabled = false; }
    }
    private void Restore()
    {
        if (springBone != null) { springBone.localRotation = restRotation; springBone.localScale = restScale; }
        displacement = velocity = 0f;
    }
    private void OnDisable() => Restore();
}
