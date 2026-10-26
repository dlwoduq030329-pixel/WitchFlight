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
        point = TraceMagic(new MagicStatEntry { range = Mathf.Min(distance,s.range) },
            MagicCastPosition, delta.normalized, out _);
        // Reject invalid / obstructed coordinates rather than detonating at a different position.
        return (point-requested).sqrMagnitude <= .25f;
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
        // Initialize in onBeforeSpawned, so all observers receive the mode in the initial snapshot.
        NetworkObject spawned = Runner.Spawn(magicProjectilePrefab, origin, Quaternion.identity, ParticipantRef,
            onBeforeSpawned: (runner, obj) => obj.GetComponent<MagicProjectile>().InitializeConstruct(this, s, magicProjectilePrefab));
        if (spawned != null)
        {
            if (!area) MagicProjectile.TrimSummons(this, Mathf.Clamp(s.summonMaxCount,1,16), spawned);
            // Telegraph/impact stay with the construct. This is only the caster's launch effect.
            RPC_PresentCast(s.magic, MagicCastPosition, MagicCastPosition, 0f);
        }
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
            areaAimDistance = 0f;
            return;
        }
        if (!held || previewNeedsRelease)
        { if (areaPreview != null) areaPreview.gameObject.SetActive(false); return; }
        MagicStatEntry s = SelectedMagicStats;
        if (areaAimDistance <= 0f) areaAimDistance = Mathf.Max(1f,s.areaAimDistance);
        areaAimDistance = Mathf.Clamp(areaAimDistance + Input.mouseScrollDelta.y * Mathf.Max(.1f,s.areaAimScrollSpeed),1f,Mathf.Max(1f,s.range));
        Camera camera = Camera.main;
        if (camera == null || !TryGetAreaAimPoint(BattleHud.GetMagicAimRay(camera),out Vector3 point))
        { if (areaPreview != null) areaPreview.gameObject.SetActive(false); return; }
        if (areaPreview == null) areaPreview = MagicAreaVisual.Create(s.areaPreviewVfxPrefab, "Thunder aiming (local only)");
        areaPreview.gameObject.SetActive(true);
        areaPreview.SetArea(point, s.radius, new Color(.2f,1f,.9f,.8f), 0f);
    }

    private void ClearAreaAimPreview()
    {
        if (areaPreview != null) Destroy(areaPreview.gameObject);
        areaPreview = null;
        areaAimDistance = 0f;
        previewNeedsRelease = false;
    }
}
