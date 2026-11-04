using UnityEngine;

// Authority-side flight. Heading and speed follow the same rate-based rules as a broom.
public sealed class SummonFlightMotion
{
    public const float LaunchSeconds = 1f;
    public const float VisualScale = 2f;
    public Vector3 Heading { get; private set; }
    public float Speed { get; private set; }
    public float Age { get; private set; }
    public bool Launching => Age < LaunchSeconds - .00001f;
    private float cruiseSpeed, launchSpeed, turnRate, acceleration, braking, turnResponse, currentTurnRate;

    public void Initialize(Vector3 ownerVelocity, Vector3 fallback, float cruise, float turn, float response, float accel, float brake)
    {
        Heading = ownerVelocity.sqrMagnitude > .01f ? ownerVelocity.normalized : fallback.normalized;
        if (Heading.sqrMagnitude < .5f) Heading = Vector3.forward;
        cruiseSpeed = Mathf.Max(.1f, cruise);
        launchSpeed = Mathf.Max(cruiseSpeed, ownerVelocity.magnitude * 1.25f + 5f);
        Speed = launchSpeed; Age = 0; currentTurnRate = 0;
        // A familiar cannot instantly reverse after its target brakes underneath it.
        turnRate = Mathf.Clamp(turn,30f,100f); turnResponse = Mathf.Clamp(response,.01f,5f);
        acceleration = Mathf.Max(1f, accel); braking = Mathf.Clamp(brake,1f,Mathf.Max(1f,cruiseSpeed/.9f));
    }

    public Vector3 Step(Vector3 targetDelta, bool hasTarget, float followDistance, float dt, float speedLimit = float.PositiveInfinity)
    {
        if (dt <= 0) return Vector3.zero;
        Vector3 displacement = Vector3.zero;
        // Split the boundary tick so the initial straight leg lasts exactly one second.
        if (Launching)
        {
            float launchTime = Mathf.Min(dt, LaunchSeconds - Age);
            displacement = Heading * launchSpeed * launchTime;
            Age += launchTime; dt -= launchTime;
            if (dt <= .000001f) return displacement;
            targetDelta -= displacement;
        }
        Age += dt;
        float distance = targetDelta.magnitude;
        float desiredSpeed = 0f;
        if (hasTarget && distance > .001f)
        {
            Vector3 desired = targetDelta / distance;
            float angle = Vector3.Angle(Heading, desired);
            currentTurnRate = Mathf.Lerp(currentTurnRate, angle > .01f ? turnRate : 0f, 1f - Mathf.Exp(-turnResponse * dt));
            Heading = Vector3.RotateTowards(Heading, desired, currentTurnRate * Mathf.Deg2Rad * dt, 0f).normalized;
            float room = Mathf.Max(0f, distance - Mathf.Max(.5f, followDistance));
            // Keep forward momentum during a U-turn instead of stopping and rotating on the spot.
            float cornerSpeed = cruiseSpeed * Mathf.Lerp(.6f, 1f, Mathf.Clamp01(Vector3.Dot(Heading, desired)));
            desiredSpeed = Mathf.Min(cornerSpeed, Mathf.Sqrt(2f * braking * room));
            if (Vector3.Dot(Heading,desired) < 0f) desiredSpeed = Mathf.Max(desiredSpeed,cruiseSpeed*.6f);
        }
        desiredSpeed = Mathf.Min(desiredSpeed, Mathf.Max(0f, speedLimit));
        float before = Speed;
        Speed = Mathf.MoveTowards(Speed, desiredSpeed, (desiredSpeed < Speed ? braking : acceleration) * dt);
        return displacement + Heading * ((before + Speed) * .5f * dt);
    }

    public void CancelLaunchAtObstacle() => Age = Mathf.Max(Age, LaunchSeconds);

    public void LimitSpeedAtObstacle(float speed)
    {
        CancelLaunchAtObstacle();
        Speed = Mathf.Min(Speed, Mathf.Max(0f, speed));
    }

    public void StopAtObstacle() => LimitSpeedAtObstacle(0f);

    public static Quaternion SmoothLook(Quaternion current, Vector3 direction, float dt)
    {
        if (direction.sqrMagnitude < .000001f) return current;
        Vector3 up = Vector3.ProjectOnPlane(Vector3.up, direction.normalized);
        if (up.sqrMagnitude < .001f) up = Vector3.ProjectOnPlane(current * Vector3.up, direction.normalized);
        if (up.sqrMagnitude < .001f) up = Vector3.ProjectOnPlane(Vector3.forward, direction.normalized);
        Quaternion eased = Quaternion.Slerp(current,Quaternion.LookRotation(direction,up),1f-Mathf.Exp(-5f*Mathf.Max(0,dt)));
        return Quaternion.RotateTowards(current,eased,110f*Mathf.Max(0,dt));
    }
}
