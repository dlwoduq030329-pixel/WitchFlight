using UnityEngine;

// Presentation only: the host's existing player capsule remains the ram hitbox.
// Both bones and their weights are authored; no runtime mesh copies or physics rigs.
[DefaultExecutionOrder(35)]
[DisallowMultipleComponent]
public sealed class CleanerRidingMotion : MonoBehaviour
{
    [Header("Movement source (wand follows its authored left-hand parent)")]
    [SerializeField] private Transform characterRoot;

    [Header("Canister and flexible hose")]
    [SerializeField] private Transform bodyBone;
    [SerializeField] private SkinnedMeshRenderer bodyRenderer;
    [SerializeField] private Vector3 bodyPivot = new Vector3(0f, .65f, 0f);
    [SerializeField, Min(0f)] private float idleSwing = 6f;
    [SerializeField, Min(0f)] private float flightSwing = 48f;
    [SerializeField, Min(0f)] private float boostSwing = 96f;
    [SerializeField, Min(0f)] private float bobDistance = .36f;
    [SerializeField, Min(.1f)] private float frequency = 1.3f;
    [SerializeField, Min(.1f)] private float response = 6f;
    [Header("Movement inertia (body only)")]
    [SerializeField, Min(0f)] private float accelerationPull = .05f;
    [SerializeField, Min(0f)] private float accelerationTilt = 1.1f;
    [SerializeField, Min(.1f)] private float springFrequency = 1.4f;
    [SerializeField, Range(.05f, .95f)] private float springDamping = .18f;
    [SerializeField, Range(1f, 150f)] private float maximumTilt = 120f;
    [Tooltip("청소기 메시의 로컬 단위입니다. 몸체가 호스에서 너무 멀리 벗어나지 않도록 제한합니다.")]
    [SerializeField, Min(0f)] private float maximumBodyOffset = 2.5f;

    private Player player;
    private Vector3 lastPosition, lastForward;
    private float motion, bank, phase;
    private Vector3 filteredVelocity, bodyOffset, offsetVelocity, bodyAngles, angularVelocity;
    private bool sampled;

    private void Awake()
    {
        // Resolve once. Unspawned customization previews never read network properties.
        if (characterRoot != null) player = characterRoot.GetComponent<Player>();
    }

    private void OnEnable()
    {
        sampled = false;
        ResetInertia();
    }

    private void ResetInertia()
    {
        filteredVelocity = bodyOffset = offsetVelocity = bodyAngles = angularVelocity = Vector3.zero;
    }

    private void OnDisable()
    {
        if (bodyBone != null) bodyBone.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        motion = bank = phase = 0f;
        ResetInertia();
        sampled = false;
    }

    private void LateUpdate()
    {
        // Never reposition the equipment root: animation moves the left-hand mount.
        Transform frame = characterRoot != null ? characterRoot : transform;
        Vector3 position = frame.position, forward = frame.forward;
        float dt = Time.deltaTime;
        float speed = 0f, turn = 0f;
        Vector3 acceleration = Vector3.zero;
        float blend = 1f - Mathf.Exp(-response * dt);
        if (sampled && dt > .0001f)
        {
            Vector3 delta = position - lastPosition;
            // Teleports/respawns must not fling the canister away from the hose.
            if (delta.sqrMagnitude < 100f && dt <= .1f)
            {
                Vector3 velocity = delta / dt;
                Vector3 nextVelocity = Vector3.Lerp(filteredVelocity, velocity, blend);
                acceleration = Vector3.ClampMagnitude((nextVelocity - filteredVelocity) / dt, 120f);
                filteredVelocity = nextVelocity;
                speed = velocity.magnitude;
                turn = Mathf.Clamp(Vector3.SignedAngle(lastForward, forward, frame.up) / dt / 135f, -1f, 1f);
            }
            else ResetInertia();
        }
        lastPosition = position;
        lastForward = forward;
        sampled = true;
        if (bodyBone == null || bodyRenderer == null || !bodyRenderer.isVisible || dt <= 0f) return;

        motion = Mathf.Lerp(motion, Mathf.Clamp01(speed / 32f), blend);
        bank = Mathf.Lerp(bank, turn, blend);
        bool boosting = player != null && player.IsVisualBoostActive;
        float amplitude = Mathf.Lerp(idleSwing,
            boosting ? boostSwing : flightSwing, motion);
        phase = Mathf.Repeat(phase + dt * frequency * Mathf.PI * 2f, Mathf.PI * 20f);
        // World acceleration also captures the outward pull when the pilot changes course.
        // Convert to mesh space: the rear-facing wand must not reverse the inertia direction.
        Vector3 localAcceleration = transform.InverseTransformDirection(acceleration);
        Vector3 targetOffset = -localAcceleration * accelerationPull;
        targetOffset.y += Mathf.Sin(phase * 1.1f) * bobDistance * (.3f + motion) * (boosting ? 2f : 1f);
        targetOffset = Vector3.ClampMagnitude(targetOffset, maximumBodyOffset);
        Vector3 targetAngles = new Vector3(
            Mathf.Sin(phase) * amplitude * .7f - localAcceleration.z * accelerationTilt,
            Mathf.Sin(phase * .7f) * amplitude * .55f - bank * amplitude,
            Mathf.Sin(phase * .8f) * amplitude + localAcceleration.x * accelerationTilt);
        targetAngles = Vector3.ClampMagnitude(targetAngles, maximumTilt);
        float step = Mathf.Min(dt, .1f);
        Spring(ref bodyOffset, ref offsetVelocity, targetOffset, step);
        Spring(ref bodyAngles, ref angularVelocity, targetAngles, step);
        Limit(ref bodyOffset, ref offsetVelocity, maximumBodyOffset);
        Limit(ref bodyAngles, ref angularVelocity, maximumTilt);
        Quaternion rotation = Quaternion.Euler(bodyAngles);
        bodyBone.SetLocalPositionAndRotation(bodyPivot - rotation * bodyPivot + bodyOffset, rotation);
    }

    // Closed-form damped spring: frame-rate independent, no physics objects or substep loop.
    private void Spring(ref Vector3 value, ref Vector3 velocity, Vector3 target, float dt)
    {
        float omega = Mathf.Max(.1f, springFrequency) * Mathf.PI * 2f;
        float damping = Mathf.Clamp(springDamping, .05f, .95f);
        float decay = damping * omega;
        float frequency = omega * Mathf.Sqrt(1f - damping * damping);
        float sin = Mathf.Sin(frequency * dt), cos = Mathf.Cos(frequency * dt);
        float envelope = Mathf.Exp(-decay * dt);
        Vector3 offset = value - target;
        Vector3 tangent = (velocity + decay * offset) / frequency;
        Vector3 next = offset * cos + tangent * sin;
        velocity = (frequency * (-offset * sin + tangent * cos) - decay * next) * envelope;
        value = target + next * envelope;
    }

    private static void Limit(ref Vector3 value, ref Vector3 velocity, float maximum)
    {
        maximum = Mathf.Max(0f, maximum);
        if (value.sqrMagnitude <= maximum * maximum) return;
        Vector3 direction = value.normalized;
        value = direction * maximum;
        velocity -= direction * Mathf.Max(0f, Vector3.Dot(velocity, direction));
    }
}
