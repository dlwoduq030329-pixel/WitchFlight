using Fusion;
using UnityEngine;

public partial class Player
{
    [Networked] private bool areaAimArmed { get; set; }
    private Vector3 confirmedAreaPoint;
    private bool hasConfirmedAreaPoint;
    private float areaAimDistance;
    private MagicAreaVisual areaPreview;
    private bool previewNeedsRelease;
    private bool localAreaAimStarted;
    private Player areaSnapTarget;
    private Camera areaAimCamera;

    // Selection happens once per press, not every frame/tick. Only the selected
    // enemy's position is followed until release; scrolling switches to manual aim.
    private void BeginLocalAreaAim(Camera camera, Ray ray)
    {
        if (localAreaAimStarted) return;
        localAreaAimStarted = true;
        areaAimDistance = Mathf.Max(1f, SelectedMagicStats.areaAimDistance);
        areaSnapTarget = FindAreaSnapTarget(camera, ray, SelectedMagicStats);
    }

    private Player FindAreaSnapTarget(Camera camera, Ray ray, MagicStatEntry stats)
    {
        if (camera == null) return null;
        Vector2 cursor = camera.WorldToScreenPoint(ray.GetPoint(1f));
        Player best = null;
        float bestScore = float.PositiveInfinity;
        foreach (Player candidate in ActiveCombatants)
        {
            if (!IsAreaSnapTarget(candidate)) continue;
            Vector3 position = candidate.LockAimPoint;
            Vector3 viewport = camera.WorldToViewportPoint(position);
            if (viewport.z <= 0f || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f) continue;
            float score = ((Vector2)camera.WorldToScreenPoint(position) - cursor).sqrMagnitude;
            if (score >= bestScore || !ValidateAreaPoint(position, stats, out _) ||
                !MagicProjectile.HasSummonSight(ray.origin, position)) continue;
            best = candidate;
            bestScore = score;
        }
        return best;
    }

    private bool IsAreaSnapTarget(Player candidate)
    {
        return candidate != null && candidate.Object != null && candidate.Object.IsValid &&
            candidate.Runner == Runner && candidate.IsTargetableBy(this);
    }

    public bool SampleAreaAimInput(Camera camera, bool held, out Vector3 point)
    {
        point = default;
        if (camera == null || !CanAimAreaMagic())
        { ResetLocalAreaAim(); return false; }
        Ray ray = BattleHud.GetMagicAimRay(camera);
        if (held) BeginLocalAreaAim(camera, ray);
        if (!localAreaAimStarted) return false;
        bool valid = TryGetAreaAimPoint(ray, out point);
        // Sample the final enemy position BEFORE clearing the release state.
        if (!held) ResetLocalAreaAim();
        return valid;
    }

    private void ResetLocalAreaAim()
    {
        localAreaAimStarted = false;
        areaSnapTarget = null;
        areaAimDistance = 0f;
    }

    private bool CanAimAreaMagic()
    {
        return Object != null && Object.IsValid && IsAlive && !IsReturningToMap && !IsHitStunned &&
            BattleManager.Instance != null && BattleManager.Instance.IsGameplayActive &&
            SelectedMagicStats.effect == MagicEffectKind.DelayedArea && pendingMagic == MagicType.None &&
            magicProjectilePrefab.IsValid && GetMagicCooldownRemaining(CurrentMagicSlot) <= 0f &&
            NowAp >= Mathf.Max(0f, SelectedMagicStats.apCost);
    }

    private void ProcessAreaAim(NetworkInputData data, bool held, bool pressed, bool released)
    {
        if (!CanAimAreaMagic()) { areaAimArmed = false; return; }
        if (pressed) areaAimArmed = true;
        if (!released || !areaAimArmed) return;
        hasConfirmedAreaPoint = data.hasAreaAimPoint &&
            ValidateAreaPoint(data.areaAimPoint, SelectedMagicStats, out confirmedAreaPoint);
        if (hasConfirmedAreaPoint) TryCastSelectedMagic();
        hasConfirmedAreaPoint = false;
        areaAimArmed = false;
    }

    // Both preview and host trace from the character as well as the camera: a third-person
    // camera looking over cover cannot place the spell through the wall next to the caster.
    public bool TryGetAreaAimPoint(Ray cameraRay, out Vector3 point)
    {
        point = default;
        if (Object == null || !Object.IsValid || SelectedMagicStats.effect != MagicEffectKind.DelayedArea ||
            !IsFiniteDirection(cameraRay.direction) || cameraRay.direction.sqrMagnitude < .001f) return false;
        MagicStatEntry s = SelectedMagicStats;
        if (IsAreaSnapTarget(areaSnapTarget) && ValidateAreaPoint(areaSnapTarget.LockAimPoint, s, out point) &&
            MagicProjectile.HasSummonSight(cameraRay.origin, point))
        {
            areaAimDistance = Mathf.Clamp(Vector3.Distance(cameraRay.origin, point), 1f, Mathf.Max(1f, s.range));
            return true;
        }
        areaSnapTarget = null;
        float distance = Mathf.Clamp(areaAimDistance > 0f ? areaAimDistance : Mathf.Max(1f, s.areaAimDistance), 1f, Mathf.Max(1f,s.range));
        Vector3 candidate = TraceMagic(new MagicStatEntry { range = distance },
            cameraRay.origin, cameraRay.direction.normalized, out _);
        Vector3 delta = candidate - MagicCastPosition;
        float length = Mathf.Min(delta.magnitude, Mathf.Max(1f,s.range));
        if (length < .05f) return false;
        point = TraceMagic(new MagicStatEntry { range = length }, MagicCastPosition, delta.normalized, out _);
        return true;
    }

    private bool ValidateAreaPoint(Vector3 requested, MagicStatEntry s, out Vector3 point)
    {
        point = default;
        if (!IsFiniteAreaPoint(requested)) return false;
        Vector3 delta = requested - MagicCastPosition;
        float distance = delta.magnitude;
        if (distance < .05f || distance > Mathf.Max(1f,s.range) + .5f) return false;
        // Players do not obstruct an area center: snapping must reach the enemy's
        // aim point, rather than stopping on the surface of its collider. Walls do.
        if (!MagicProjectile.HasSummonSight(MagicCastPosition, requested)) return false;
        point = requested;
        return true;
    }

    private static bool IsFiniteAreaPoint(Vector3 value)
    {
        // World coordinates are not unit directions (Player.IsFiniteDirection caps length at 2).
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }

    private bool CanCreateSpecialMagic(MagicStatEntry s)
    {
        if (s.effect == MagicEffectKind.DelayedArea)
            return magicProjectilePrefab.IsValid && areaAimArmed && hasConfirmedAreaPoint &&
                ValidateAreaPoint(confirmedAreaPoint, s, out _);
        if (s.effect == MagicEffectKind.PursuitSummon)
            return magicProjectilePrefab.IsValid && s.effectDuration > 0f && s.range > 0f && s.projectileSpeed > 0f;
        return true;
    }

    private void CreateSpecialMagic(MagicStatEntry s)
    {
        if (!Object.HasStateAuthority) return;
        bool area = s.effect == MagicEffectKind.DelayedArea;
        Vector3 origin = area ? confirmedAreaPoint : MagicCastPosition;
        if (!area)
        {
            Vector3 end = TraceMagic(new MagicStatEntry { range = Mathf.Max(.1f,s.placementDistance) },
                origin,transform.forward,out _);
            Vector3 delta = end-origin;
            origin += delta.normalized * Mathf.Max(0f,delta.magnitude-Mathf.Max(.25f,s.projectileRadius+.05f));
        }
        int count = !area && s.perkSecond?.id == PerkId.MineRadius ? 2 : 1;
        bool created = false;
        for (int i = 0; i < count; i++)
        {
            Vector3 position = origin;
            if (count > 1)
            {
                Vector3 side = transform.right * (i == 0 ? -1f : 1f);
                position = TraceMagic(new MagicStatEntry { range = 1.5f }, origin, side, out _);
                position = Vector3.MoveTowards(position, origin, .1f);
            }
            NetworkObject spawned = Runner.Spawn(magicProjectilePrefab, position, Quaternion.identity, ParticipantRef,
                onBeforeSpawned: (runner, obj) => obj.GetComponent<MagicProjectile>().InitializeConstruct(this, s, magicProjectilePrefab));
            if (spawned == null) continue;
            created = true;
            if (!area) MagicProjectile.TrimSummons(this, Mathf.Clamp(Mathf.Max(count, s.summonMaxCount), 1, 16), spawned);
        }
        if (created) RPC_PresentCast(s.magic, MagicCastPosition, MagicCastPosition, 0f);
    }

    private void UpdateAreaAimPreview()
    {
        bool held = ControlSettings.Held(ControlAction.MagicUse);
        if (!held) previewNeedsRelease = false;
        bool eligible = Object != null && Object.IsValid && Object.HasInputAuthority && CanAimAreaMagic() &&
            !CombatPresentation.MenuOpen && !BattlePerkUI.BlocksCombatInput && !BattleEmoteUI.BlocksCombatInput &&
            Cursor.lockState == CursorLockMode.Locked;
        if (!eligible)
        {
            if (held) previewNeedsRelease = true;
            if (areaPreview != null) areaPreview.gameObject.SetActive(false);
            ResetLocalAreaAim();
            return;
        }
        if (!held || previewNeedsRelease)
        { if (areaPreview != null) areaPreview.gameObject.SetActive(false); return; }
        MagicStatEntry s = SelectedMagicStats;
        if (areaAimCamera == null || !areaAimCamera.isActiveAndEnabled) areaAimCamera = Camera.main;
        Camera camera = areaAimCamera;
        if (camera != null) BeginLocalAreaAim(camera, BattleHud.GetMagicAimRay(camera));
        float scroll = Input.mouseScrollDelta.y;
        if (scroll != 0f)
        {
            if (IsAreaSnapTarget(areaSnapTarget) && camera != null)
                areaAimDistance = Vector3.Distance(camera.transform.position, areaSnapTarget.LockAimPoint);
            areaSnapTarget = null;
            areaAimDistance = Mathf.Clamp(areaAimDistance + scroll * Mathf.Max(.1f,s.areaAimScrollSpeed),1f,Mathf.Max(1f,s.range));
        }
        if (camera == null || !TryGetAreaAimPoint(BattleHud.GetMagicAimRay(camera),out Vector3 point))
        { if (areaPreview != null) areaPreview.gameObject.SetActive(false); return; }
        if (areaPreview == null) areaPreview = MagicAreaVisual.Create(s.areaPreviewVfxPrefab, "Thunder aiming (local only)");
        areaPreview.gameObject.SetActive(true);
        bool enemyInside = false;
        foreach (Player enemy in ActiveCombatants)
        {
            if (!IsAreaSnapTarget(enemy) || (enemy.LockAimPoint - point).sqrMagnitude > s.radius * s.radius ||
                !MagicProjectile.HasBlastSight(point, enemy)) continue;
            enemyInside = true;
            break;
        }
        areaPreview.SetArea(point, s.radius, enemyInside ? new Color(1f,.15f,.1f,.85f) : new Color(1f,1f,1f,.7f), 0f);
    }

    private void ClearAreaAimPreview()
    {
        if (areaPreview != null) MagicVfxPool.Release(areaPreview.gameObject);
        areaPreview = null;
        ResetLocalAreaAim();
        areaAimCamera = null;
        previewNeedsRelease = false;
    }
}
