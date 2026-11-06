using UnityEngine;

// Presentation-only immortal target. Never registers with battle damage/score systems.
[DefaultExecutionOrder(10000)]
public sealed class RobbyPracticeDummy : MonoBehaviour
{
    [SerializeField] private Transform lockOnPos;
    [SerializeField] private Transform[] springRoots;
    [SerializeField, HideInInspector] private Transform springBone; // Legacy scene reference only.
    [SerializeField, Min(.05f)] private float hitRadius = .7f;
    [SerializeField, Min(1f)] private float springFrequency = 12f;
    [SerializeField, Min(.1f)] private float damping = 3f;
    [SerializeField, Min(1f)] private float hitImpulse = 480f;
    [SerializeField, Range(1f, 65f)] private float maxAngle = 38f;
    [SerializeField, Range(0f, .4f)] private float squashAmount = .22f;
    private Quaternion[] restRotations;
    private Vector3[] restScales, axes, verticalAxes;
    private float displacement, velocity;
    public Vector3 AimPoint => lockOnPos != null ? lockOnPos.position : transform.position + Vector3.up;
    public float Radius => hitRadius;
    private void Awake()
    {
        if ((springRoots == null || springRoots.Length == 0) && springBone != null)
            springRoots = new[] { springBone };
        int count = springRoots != null ? springRoots.Length : 0;
        restRotations = new Quaternion[count]; restScales = new Vector3[count];
        axes = new Vector3[count]; verticalAxes = new Vector3[count];
        for (int i = 0; i < count; i++) if (springRoots[i] != null)
        {
            restRotations[i] = springRoots[i].localRotation;
            restScales[i] = springRoots[i].localScale;
            Vector3 up = springRoots[i].InverseTransformDirection(transform.up).normalized;
            verticalAxes[i] = new Vector3(Mathf.Abs(up.x), Mathf.Abs(up.y), Mathf.Abs(up.z));
        }
        enabled = false;
    }
    public void Hit(Vector3 direction)
    {
        if (springRoots == null || springRoots.Length == 0) return;
        Vector3 worldAxis = Vector3.Cross(transform.up, direction.normalized);
        if (worldAxis.sqrMagnitude < .001f) worldAxis = transform.forward;
        for (int i = 0; i < springRoots.Length; i++) if (springRoots[i] != null)
            axes[i] = (springRoots[i].parent != null
                ? springRoots[i].parent.InverseTransformDirection(worldAxis) : worldAxis).normalized;
        velocity = Mathf.Clamp(velocity + hitImpulse, -hitImpulse * 1.5f, hitImpulse * 1.5f);
        enabled = true;
    }
    private void LateUpdate()
    {
        if (springRoots == null || springRoots.Length == 0) { enabled = false; return; }
        // Substeps keep the damped spring stable after a slow frame.
        float remaining = Mathf.Min(Time.deltaTime, .1f);
        while (remaining > 0f)
        {
            float dt = Mathf.Min(remaining, .008f); remaining -= dt;
            velocity += (-springFrequency * springFrequency * displacement - damping * velocity) * dt;
            displacement += velocity * dt;
        }
        displacement = Mathf.Clamp(displacement, -maxAngle, maxAngle);
        float squash = Mathf.Clamp(displacement / Mathf.Max(1f, maxAngle), -1f, 1f) * squashAmount;
        for (int i = 0; i < springRoots.Length; i++) if (springRoots[i] != null)
        {
            // Parent-space rotation keeps both imported armatures moving in the same world direction.
            springRoots[i].localRotation = Quaternion.AngleAxis(displacement, axes[i]) * restRotations[i];
            Vector3 scale = Vector3.one * (1 + squash) - verticalAxes[i] * (2 * squash);
            springRoots[i].localScale = Vector3.Scale(restScales[i], scale);
        }
        if (Mathf.Abs(displacement) < .01f && Mathf.Abs(velocity) < .05f)
        { Restore(); enabled = false; }
    }
    private void Restore()
    {
        if (restRotations != null && springRoots != null)
            for (int i = 0; i < restRotations.Length; i++) if (springRoots[i] != null)
            { springRoots[i].localRotation = restRotations[i]; springRoots[i].localScale = restScales[i]; }
        displacement = velocity = 0f;
    }
    private void OnDisable() => Restore();
}
