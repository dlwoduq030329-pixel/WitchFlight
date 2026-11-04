using UnityEngine;

public partial class Player
{
    // Host-only route memory. Keep a chosen detour long enough to pass a corner.
    private readonly RaycastHit[] botSceneryHits = new RaycastHit[32];
    private Vector3 botRoute, botProgressPosition;
    private float botRouteRefresh, botRouteRemaining, botStuckTime;
    private bool botAvoiding, botProgressSampled;
    private Portal botPortal;
    private Vector3 botPortalGoal;
    private float botPortalRefresh;
    private int botPortalSequence;
    private readonly System.Collections.Generic.List<Vector3> botPortalEntries = new(), botPortalExits = new();
    private readonly System.Collections.Generic.List<Portal> botPortals = new();
    private float[] botPortalCosts = System.Array.Empty<float>();

    private bool TryBotPortalRoute(Vector3 goal, MatchBotSettings settings, out Vector3 entry)
    {
        entry = default;
        if (!settings.usePortals || hasPendingPortalTeleport || !portalReentryTimer.ExpiredOrNotRunning(Runner))
        { botPortal = null; botPortalRefresh = 0; return false; }
        botPortalRefresh -= Runner.DeltaTime;
        if (botPortalSequence != PortalExitSequence)
        { botPortalSequence = PortalExitSequence; botPortalRefresh = 0; botAvoiding = false; botRouteRemaining = 0; }
        if (botPortalRefresh <= 0 || (goal - botPortalGoal).sqrMagnitude > 100f)
        {
            botPortalRefresh = .5f; botPortalGoal = goal; botPortal = null;
            botPortals.Clear(); botPortalEntries.Clear(); botPortalExits.Clear();
            foreach (var portal in Portal.ActivePortals)
            {
                if (portal == null || portal.gameObject.scene != gameObject.scene ||
                    !portal.TryGetBotRoute(Runner, out var start, out var end, out var rotation) ||
                    !CanOccupyPortalExit(end, rotation)) continue;
                var boundary = BattleManager.Instance != null ? BattleManager.Instance.MapBoundary : null;
                if (boundary != null && boundary.TryGetBotFlightBounds(out var box) &&
                    (!box.Contains(start) || !box.Contains(end))) continue;
                botPortals.Add(portal); botPortalEntries.Add(start); botPortalExits.Add(end);
            }
            int n = botPortals.Count;
            // Distance-to-goal relaxation also permits useful chains of portals.
            // Positive traversal costs prevent zero-cost loops between linked pairs.
            if (botPortalCosts.Length < n) System.Array.Resize(ref botPortalCosts, Mathf.NextPowerOfTwo(n));
            float[] costs = botPortalCosts;
            for (int i = 0; i < n; i++) costs[i] = Vector3.Distance(botPortalExits[i], goal);
            float transit = Mathf.Max(.1f, settings.portalTraversalCost);
            for (int pass = 0; pass < n; pass++)
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++)
                        if (i != j) costs[i] = Mathf.Min(costs[i], Vector3.Distance(botPortalExits[i], botPortalEntries[j]) + transit + costs[j]);
            float best = Vector3.Distance(transform.position, goal) - Mathf.Max(0f, settings.portalMinimumSaving);
            for (int i = 0; i < n; i++)
            {
                float cost = Vector3.Distance(transform.position, botPortalEntries[i]) + transit + costs[i];
                if (cost < best) { best = cost; botPortal = botPortals[i]; }
            }
        }
        if (botPortal == null || !botPortal.TryGetBotRoute(Runner, out entry, out _, out _))
        { botPortalRefresh = 0; return false; }
        return true;
    }

    private Vector3 BotRouteDirection(Vector3 destination, Vector3 desired, MatchBotSettings settings, out bool avoiding,
        bool pickupGoal = false, float pickupRadius = .85f)
    {
        float dt = Runner.DeltaTime;
        botRouteRefresh -= dt;
        botRouteRemaining -= dt;
        if (!botProgressSampled) { botProgressSampled = true; botProgressPosition = transform.position; }
        if ((transform.position - botProgressPosition).sqrMagnitude >= 1f)
        { botStuckTime = 0; botProgressPosition = transform.position; }
        else botStuckTime += dt;

        Vector3 origin = characterController != null ? characterController.bounds.center : LockAimPoint;
        float toGoal = Vector3.Distance(origin, destination);
        if (pickupGoal && toGoal <= settings.pickupApproachDistance)
        {
            Vector3 approach = destination - origin;
            float stopShort = Mathf.Max(.1f, toGoal - Mathf.Max(.1f, pickupRadius) * .65f);
            // Probe only as far as needed to touch the pickup. The wall BEHIND a
            // reachable flag must not cause a long, repeated detour.
            if (BotClearance(origin, approach.normalized, stopShort) >= stopShort - .05f)
            {
                botAvoiding = false; botRouteRemaining = 0f; botRouteRefresh = 0f;
                botRoute = approach.sqrMagnitude > .001f ? approach.normalized : desired;
                avoiding = false; return botRoute;
            }
        }
        if (botRouteRefresh > 0 && botRoute.sqrMagnitude > .5f)
        { avoiding = botAvoiding; return botAvoiding ? botRoute : desired; }
        botRouteRefresh = .15f;
        float distance = Mathf.Max(settings.obstacleProbeDistance, Mathf.Abs(Speed) * settings.obstacleLookAheadSeconds);
        distance = Mathf.Clamp(distance, 4f, 100f);
        float directProbe = Mathf.Min(distance, Mathf.Max(2f, toGoal));
        float directClear = BotClearance(origin, desired, directProbe);
        float forwardClear = BotClearance(origin, transform.forward, distance);
        bool stuck = botStuckTime >= settings.stuckSeconds && SpeedStage > 0 && toGoal > 4f;
        bool blocked = directClear < directProbe - .1f || forwardClear < Mathf.Max(4f, Mathf.Abs(Speed) * .7f);

        if (!blocked && !stuck && (!botAvoiding || botRouteRemaining <= 0))
        { botAvoiding = false; botRoute = desired; avoiding = false; return desired; }

        if (botAvoiding && !stuck && botRouteRemaining > 0 && BotClearance(origin, botRoute, distance) > distance * .65f)
        { avoiding = true; return botRoute; }

        Vector3 best = -transform.forward;
        float bestScore = float.NegativeInfinity;
        // Search a 3D fan: left/right, above/below and retreat, rather than always climbing.
        Vector3 flat = Vector3.ProjectOnPlane(desired, Vector3.up).normalized;
        if (flat.sqrMagnitude < .1f) flat = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        if (flat.sqrMagnitude < .1f) flat = Vector3.forward;
        for (int yaw = -180; yaw <= 135; yaw += 45)
            for (int pitch = -1; pitch <= 1; pitch++)
            {
                Vector3 candidate = (Quaternion.AngleAxis(yaw, Vector3.up) * flat + Vector3.up * pitch).normalized;
                ScoreBotRoute(origin, candidate, desired, distance, stuck, ref best, ref bestScore);
            }
        ScoreBotRoute(origin, Vector3.up, desired, distance, stuck, ref best, ref bestScore);
        ScoreBotRoute(origin, Vector3.down, desired, distance, stuck, ref best, ref bestScore);
        botRoute = best; botAvoiding = true;
        botRouteRemaining = Mathf.Max(.2f, settings.routeHoldSeconds);
        if (stuck) { botStuckTime = 0; botProgressPosition = transform.position; }
        avoiding = true;
        return botRoute;
    }

    private void ScoreBotRoute(Vector3 origin, Vector3 candidate, Vector3 desired, float distance,
        bool stuck, ref Vector3 best, ref float bestScore)
    {
        float clearance = BotClearance(origin, candidate, distance);
        var boundary = BattleManager.Instance != null ? BattleManager.Instance.MapBoundary : null;
        if (boundary != null && boundary.TryGetBotFlightBounds(out var flightBox) &&
            !flightBox.Contains(transform.position + candidate * distance)) return;
        Vector3 end = origin + candidate * Mathf.Min(clearance, 8f);
        if (boundary != null && ((boundary.HasValidBounds &&
            (end.x < boundary.xmin || end.x > boundary.xmax || end.z < boundary.zmin || end.z > boundary.zmax)) ||
            (boundary.lowerLimitEnabled && boundary.HasValidLowerLimit && end.y < boundary.ymin + 1f) ||
            (boundary.altitudeLimitEnabled && boundary.HasValidAltitudeLimit && end.y > boundary.altitudeFadeStartY - 2f))) return;
        float score = clearance / distance * 4f + Vector3.Dot(candidate, desired) * .8f;
        if (botAvoiding) score += Vector3.Dot(candidate, botRoute) * (stuck ? -.8f : .5f);
        if (clearance < 2f) score -= 5f;
        if (score > bestScore) { bestScore = score; best = candidate; }
    }

    private float BotClearance(Vector3 origin, Vector3 direction, float distance)
    {
        var physics = Runner.GetPhysicsScene();
        float radius = characterController != null ? Mathf.Max(.4f, characterController.bounds.extents.x,
            characterController.bounds.extents.z) : .6f;
        int count = physics.SphereCast(origin, radius, direction, botSceneryHits, distance, ~0, QueryTriggerInteraction.Ignore);
        float nearest = count == botSceneryHits.Length ? 0f : distance;
        nearest = BotNearestScenery(count, nearest);
        // A sweep starting in contact can miss that surface. The central ray still sees it.
        count = physics.Raycast(origin, direction, botSceneryHits, distance, ~0, QueryTriggerInteraction.Ignore);
        return BotNearestScenery(count, count == botSceneryHits.Length ? 0f : nearest);
    }

    private float BotNearestScenery(int count, float nearest)
    {
        for (int i = 0; i < count; i++)
        {
            var hit = botSceneryHits[i];
            if (hit.collider == null || hit.collider.GetComponentInParent<Player>() != null ||
                hit.collider.GetComponentInParent<MagicProjectile>() != null ||
                hit.collider.GetComponentInParent<BattleFlag>() != null ||
                Physics.GetIgnoreLayerCollision(gameObject.layer, hit.collider.gameObject.layer)) continue;
            nearest = Mathf.Min(nearest, hit.distance);
        }
        return nearest;
    }
}
