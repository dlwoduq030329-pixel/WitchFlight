using UnityEngine;

// Pure formation geometry. Assign sectors on the bot decision tick, not every simulation frame.
public static class BotEncirclement
{
    public static Vector3 SlotOffset(int slot, int count, float radius, float height, int targetKey)
    {
        float phase = (targetKey & 7) * 45f;
        float angle = (phase + 360f * slot / Mathf.Max(1, count)) * Mathf.Deg2Rad;
        float vertical = count > 1 ? (slot % 2 == 0 ? 1f : -1f) * Mathf.Min(height, radius * .35f) : 0f;
        return new Vector3(Mathf.Sin(angle) * radius, vertical, Mathf.Cos(angle) * radius);
    }

    public static Vector3 ApproachOffset(Vector3 fromCenter, Vector3 slotOffset, float radius)
    {
        Vector3 from = new Vector3(fromCenter.x, 0f, fromCenter.z);
        Vector3 to = new Vector3(slotOffset.x, 0f, slotOffset.z);
        float clearance = radius * .65f;
        if (from.sqrMagnitude < clearance * clearance)
        {
            // Already too close: get out on the current side, never cut through the enemy.
            Vector3 outward = from.sqrMagnitude > .01f ? from.normalized : to.normalized;
            return outward * radius + new Vector3(0f, slotOffset.y, 0f);
        }
        Vector3 segment = to - from;
        float t = segment.sqrMagnitude > .001f ? Mathf.Clamp01(-Vector3.Dot(from, segment) / segment.sqrMagnitude) : 0f;
        if ((from + segment * t).sqrMagnitude >= clearance * clearance) return slotOffset;
        float angle = Mathf.Atan2(from.x, from.z) * Mathf.Rad2Deg;
        float targetAngle = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
        angle = (angle + Mathf.Clamp(Mathf.DeltaAngle(angle, targetAngle), -60f, 60f)) * Mathf.Deg2Rad;
        float arcRadius = Mathf.Max(radius, Mathf.Min(from.magnitude, radius * 1.5f));
        return new Vector3(Mathf.Sin(angle) * arcRadius, slotOffset.y, Mathf.Cos(angle) * arcRadius);
    }
}
