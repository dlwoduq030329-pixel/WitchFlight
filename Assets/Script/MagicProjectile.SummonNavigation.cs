using Fusion;
using UnityEngine;

public sealed partial class MagicProjectile
{
    // Authority-only local routing, just like bot flight. Replicate the resulting transform.
    private RaycastHit[] summonNavigationHits = new RaycastHit[32];
    private Collider[] summonNavigationOverlaps = new Collider[32];
    private SphereCollider summonSeparationProbe;
    private Vector3 summonWaypoint, summonRouteDirection, summonProgressPosition;
    private float summonRouteRefresh, summonRouteHold, summonStuckSeconds;
    private bool summonAvoiding;
    private const float SummonWallSkin = .04f;
    private float SummonNavigationRadius => Mathf.Max(.12f, Mathf.Max(stats.projectileRadius, SummonHitRadius * .8f));

    private void ResetSummonNavigation()
    {
        summonAvoiding = false;
        summonRouteRefresh = summonRouteHold = summonStuckSeconds = 0f;
        summonRouteDirection = Vector3.zero;
        summonProgressPosition = transform.position;
    }

    private bool IsSummonScenery(Collider collider)
    {
        return collider != null && !collider.isTrigger &&
            !collider.transform.IsChildOf(transform) &&
            collider.GetComponentInParent<Player>() == null &&
            collider.GetComponentInParent<MagicProjectile>() == null &&
            collider.GetComponentInParent<BattleFlag>() == null &&
            !Physics.GetIgnoreLayerCollision(gameObject.layer, collider.gameObject.layer);
    }

    private void MoveSummonWithNavigation(Vector3 pursuitDelta, bool hasTarget)
    {
        float dt = Runner.DeltaTime;
        Vector3 start = transform.position;
        SeparateSummonFromScenery();
        pursuitDelta -= transform.position - start;
        Vector3 routeDelta = SummonRoute(pursuitDelta, hasTarget, dt);
        if (summonAvoiding) summonFlight.CancelLaunchAtObstacle();
        Vector3 movement = summonFlight.Step(routeDelta, hasTarget || summonAvoiding,
            summonAvoiding ? .5f : stats.summonFollowDistance, dt,
            summonAvoiding ? Mathf.Max(3f, stats.summonMoveSpeed * .45f) : float.PositiveInfinity);

        Vector3 position = transform.position;
        Vector3 travelStart = position;
        bool touched = false;
        // Sweep each slide separately, including at corners: never step through a wall.
        for (int pass = 0; pass < 3 && movement.sqrMagnitude > .000001f; pass++)
        {
            float length = movement.magnitude;
            Vector3 heading = movement / length;
            float clear = SummonClearance(position, heading, length + SummonWallSkin, out RaycastHit hit);
            float advance = Mathf.Min(length, Mathf.Max(0f, clear - SummonWallSkin));
            position += heading * advance;
            if (advance >= length - .00001f) break;
            touched = true;
            if (hit.collider == null || hit.normal.sqrMagnitude < .5f) break;
            movement = Vector3.ProjectOnPlane(heading * (length - advance), hit.normal);
        }
        transform.position = position;
        if (touched)
        {
            summonRouteRefresh = 0f;
            summonFlight.LimitSpeedAtObstacle((position - travelStart).magnitude / Mathf.Max(.0001f, dt));
        }
        SummonMoving = (position - start).sqrMagnitude > .00000001f;
    }

    private Vector3 SummonRoute(Vector3 pursuitDelta, bool hasTarget, float dt)
    {
        summonRouteRefresh -= dt;
        summonRouteHold -= dt;
        float probe = Mathf.Clamp(Mathf.Max(12f, summonFlight.Speed * 1.1f), 12f, 60f);
        Vector3 desiredDelta = summonFlight.Launching ? summonFlight.Heading * probe : pursuitDelta;
        bool shouldMove = summonFlight.Launching || (hasTarget &&
            pursuitDelta.magnitude > Mathf.Max(.5f, stats.summonFollowDistance) + 1f);
        if ((transform.position - summonProgressPosition).sqrMagnitude > .25f || !shouldMove && !summonAvoiding)
        { summonProgressPosition = transform.position; summonStuckSeconds = 0f; }
        else summonStuckSeconds += dt;
        if (!hasTarget && !summonFlight.Launching)
        { summonAvoiding = false; return pursuitDelta; }

        Vector3 waypointDelta = summonWaypoint - transform.position;
        // Give a rate-limited U-turn time to finish before replacing its escape route.
        float stuckDelay = summonAvoiding && Vector3.Dot(summonFlight.Heading, waypointDelta.normalized) < .25f ? 2.5f : .9f;
        bool stuck = summonStuckSeconds > stuckDelay;
        if (summonRouteRefresh > 0f && !stuck)
            return summonAvoiding ? waypointDelta : pursuitDelta;
        summonRouteRefresh = .15f;
        Vector3 desired = desiredDelta.sqrMagnitude > .001f ? desiredDelta.normalized : summonFlight.Heading;
        float directLength = Mathf.Min(probe, desiredDelta.magnitude);
        float aheadLength = Mathf.Clamp(summonFlight.Speed * .8f, 2f, probe);
        bool blocked = SummonClearance(transform.position, desired, directLength, out _) < directLength - .1f ||
            SummonClearance(transform.position, summonFlight.Heading, aheadLength, out _) < aheadLength - .1f;

        // Keep the waypoint beyond the corner rather than alternating left/right each tick.
        if (summonAvoiding && !stuck && summonRouteHold > 0f && waypointDelta.magnitude > 1.5f &&
            SummonClearance(transform.position, waypointDelta.normalized, waypointDelta.magnitude, out _) >= waypointDelta.magnitude - .1f)
            return waypointDelta;
        if (!blocked && !stuck)
        { summonAvoiding = false; return pursuitDelta; }

        Vector3 flat = Vector3.ProjectOnPlane(desired, Vector3.up).normalized;
        if (flat.sqrMagnitude < .1f) flat = Vector3.forward;
        float bestScore = float.NegativeInfinity;
        Vector3 bestPoint = transform.position, bestDirection = summonRouteDirection;
        for (int yaw = -180; yaw < 180; yaw += 45)
            for (int pitch = -1; pitch <= 1; pitch++)
                ScoreSummonRoute((Quaternion.AngleAxis(yaw, Vector3.up) * flat + Vector3.up * pitch).normalized,
                    desired, desiredDelta, probe, stuck, ref bestScore, ref bestPoint, ref bestDirection);
        ScoreSummonRoute(Vector3.up, desired, desiredDelta, probe, stuck, ref bestScore, ref bestPoint, ref bestDirection);
        ScoreSummonRoute(Vector3.down, desired, desiredDelta, probe, stuck, ref bestScore, ref bestPoint, ref bestDirection);
        summonAvoiding = true;
        summonWaypoint = bestPoint;
        summonRouteDirection = bestDirection;
        summonRouteHold = 1.2f;
        if (stuck) { summonStuckSeconds = 0f; summonProgressPosition = transform.position; }
        return summonWaypoint - transform.position;
    }

    private void ScoreSummonRoute(Vector3 candidate, Vector3 desired, Vector3 goalDelta, float probe, bool stuck,
        ref float bestScore, ref Vector3 bestPoint, ref Vector3 bestDirection)
    {
        Vector3 origin = transform.position;
        float clear = SummonClearance(origin, candidate, probe, out _);
        float distance = Mathf.Min(probe * .85f, clear - SummonWallSkin * 2f);
        var boundary = BattleManager.Instance != null ? BattleManager.Instance.MapBoundary : null;
        // Shorten a detour to fit the same safe flight box used by bots.
        if (boundary != null && boundary.TryGetBotFlightBounds(out var box))
        {
            if (box.Contains(origin))
            {
                while (distance > 1f && !box.Contains(origin + candidate * distance)) distance *= .5f;
                if (!box.Contains(origin + candidate * distance)) return;
            }
            else if (box.SqrDistance(origin + candidate * distance) >= box.SqrDistance(origin)) return;
        }
        if (distance < .75f) return;
        Vector3 point = origin + candidate * distance;
        float score = distance / probe * 4f + Vector3.Dot(candidate, desired) * .9f;
        // Prefer lateral detours over repeatedly climbing. A clear second leg helps choose a corner.
        score -= Mathf.Abs(candidate.y) * .3f;
        Vector3 next = origin + goalDelta - point;
        float nextLength = Mathf.Min(probe, next.magnitude);
        if (nextLength > .1f)
            score += SummonClearance(point, next.normalized, nextLength, out _) / nextLength * .8f;
        if (summonAvoiding) score += Vector3.Dot(candidate, summonRouteDirection) * (stuck ? -1.2f : .8f);
        if (score <= bestScore) return;
        bestScore = score; bestPoint = point; bestDirection = candidate;
    }

    private float SummonClearance(Vector3 origin, Vector3 heading, float distance, out RaycastHit nearestHit)
    {
        nearestHit = default;
        if (distance <= .00001f) return 0f;
        var physics = Runner.GetPhysicsScene();
        int count;
        // Grow instead of silently losing the nearest wall in a dense pile of colliders.
        while (true)
        {
            count = physics.SphereCast(origin, SummonNavigationRadius, heading, summonNavigationHits,
                distance, ~0, QueryTriggerInteraction.Ignore);
            if (count < summonNavigationHits.Length) break;
            if (summonNavigationHits.Length >= 256) return 0f;
            System.Array.Resize(ref summonNavigationHits, summonNavigationHits.Length * 2);
        }
        float nearest = distance;
        for (int i = 0; i < count; i++)
            if (IsSummonScenery(summonNavigationHits[i].collider) && summonNavigationHits[i].distance < nearest)
            { nearestHit = summonNavigationHits[i]; nearest = nearestHit.distance; }
        return nearest;
    }

    private void SeparateSummonFromScenery()
    {
        var physics = Runner.GetPhysicsScene();
        for (int pass = 0; pass < 4; pass++)
        {
            int count;
            while (true)
            {
                count = physics.OverlapSphere(transform.position, SummonNavigationRadius,
                    summonNavigationOverlaps, ~0, QueryTriggerInteraction.Ignore);
                if (count < summonNavigationOverlaps.Length || summonNavigationOverlaps.Length >= 256) break;
                System.Array.Resize(ref summonNavigationOverlaps, summonNavigationOverlaps.Length * 2);
            }
            bool separated = false;
            for (int i = 0; i < count; i++)
            {
                Collider obstacle = summonNavigationOverlaps[i];
                if (!IsSummonScenery(obstacle)) continue;
                if (summonSeparationProbe == null)
                {
                    var probe = new GameObject("Summon separation probe");
                    probe.SetActive(false);
                    probe.transform.SetParent(transform, false);
                    summonSeparationProbe = probe.AddComponent<SphereCollider>();
                    summonSeparationProbe.enabled = false;
                }
                // Compensate for prefab scale: ComputePenetration takes world position/rotation,
                // but retains the collider transform's scale. This collider never joins physics.
                Vector3 scale = summonSeparationProbe.transform.lossyScale;
                summonSeparationProbe.radius = SummonNavigationRadius /
                    Mathf.Max(.0001f, Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z))));
                if (!Physics.ComputePenetration(summonSeparationProbe, transform.position, Quaternion.identity,
                    obstacle, obstacle.transform.position, obstacle.transform.rotation, out Vector3 outward, out float depth)) continue;
                transform.position += outward * (depth + SummonWallSkin);
                separated = true;
            }
            if (!separated) break;
            summonRouteRefresh = 0f;
            summonFlight.StopAtObstacle();
        }
    }
}
