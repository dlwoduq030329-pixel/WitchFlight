using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using TMPro;

// Viewer-local cameras: never writes aim, movement or replicated combat state.
[RequireComponent(typeof(Camera)), DisallowMultipleComponent, DefaultExecutionOrder(200)]
public sealed class RearThreatCamera : MonoBehaviour
{
    [Header("Left view (optional custom UI)")]
    [SerializeField] private RawImage customView;
    [SerializeField] private GameObject customRoot;
    [SerializeField] private TMP_Text customCaption;
    [Header("Right view (optional custom UI)")]
    [SerializeField] private RawImage rightCustomView;
    [SerializeField] private GameObject rightCustomRoot;
    [SerializeField] private TMP_Text rightCustomCaption;
    [Tooltip("전투/훈련 중 적이 없어도 두 창을 켜서 위치와 크기를 확인합니다.")]
    [SerializeField] private bool previewInBattle;
    [Tooltip("보조 카메라 최대 갱신률. 실제 게임 FPS보다 높게 갱신되지는 않습니다.")]
    [SerializeField, Range(5, 120)] private int framesPerSecond = 60;
    [SerializeField] private int overlaySortingOrder = 100;
    private Camera main;
    private readonly List<Threat> candidates = new();
    private readonly List<Vector3> shardPositions = new(8);
    private readonly RaycastHit[] sightHits = new RaycastHit[32];
    private readonly Collider[] sightOverlaps = new Collider[16];
    private View left, right;
    private readonly UniversalRenderPipeline.SingleCameraRequest request = new();
    private readonly List<Renderer> renderers = new();
    private readonly List<Renderer> temporarilyHidden = new();
    private static readonly Dictionary<GameObject, Player> localEffects = new();
    private readonly List<GameObject> expiredEffects = new();
    private SpeedCameraEffects speedEffects;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetEffects() => localEffects.Clear();

    public static void ExcludeLocalEffect(GameObject root, Player owner)
    {
        if (root == null) return;
        if (owner != null && owner == Player.LocalPlayer) localEffects[root] = owner;
        else localEffects.Remove(root); // A pooled effect can next belong to a different caster.
    }

    private sealed class Threat
    {
        public int side;
        public Vector3 point;
        public bool locking, projectile;
        public float distanceSquared;
    }
    private sealed class View
    {
        public Camera camera;
        public RenderTexture texture;
        public RawImage image, custom;
        public GameObject root, canvas;
        public RectTransform panel;
        public CanvasGroup group;
        public TMP_Text caption;
        public Texture oldTexture;
        public bool visible;
        public float reveal, nextFrame;
    }

    private void Awake()
    {
        main = GetComponent<Camera>();
        speedEffects = GetComponent<SpeedCameraEffects>();
        if (customView != null) customView.enabled = false;
        if (rightCustomView != null) rightCustomView.enabled = false;
        HideCustomRoot(customView, customRoot);
        HideCustomRoot(rightCustomView, rightCustomRoot);
    }

    private void LateUpdate()
    {
        var settings = FlightCombatTable.Default;
        Player local = Player.LocalPlayer;
        bool active = main != null && main.enabled && settings != null &&
            (settings.rearViewEnabled || settings.showLockingThreats) &&
            BattleManager.Instance != null && BattleManager.Instance.IsGameplayActive && !CombatPresentation.MenuOpen &&
            local != null && local.Object != null && local.Object.IsValid && local.TeamIndex > 0 &&
            local.IsAlive && !local.IsReturningToMap;
        if (!active) { SetVisible(left, false); SetVisible(right, false); return; }
        candidates.Clear();
        foreach (Player enemy in Player.ActiveCombatants)
        {
            if (enemy == null || enemy == local || enemy.Object == null || !enemy.Object.IsValid ||
                !enemy.IsAlive || enemy.Runner != local.Runner || enemy.TeamIndex <= 0 || enemy.TeamIndex == local.TeamIndex ||
                enemy.IsHiddenBySmokeFor(local)) continue;
            Vector3 delta = enemy.LockAimPoint - main.transform.position;
            int side = RearSector(main.transform.InverseTransformDirection(delta));
            if (side == 0) continue;
            bool nearby = settings.rearViewEnabled && delta.sqrMagnitude <= settings.rearViewDistance * settings.rearViewDistance;
            // Replicated target ID is enough: show the attacker as soon as targeting begins,
            // including the first snapshot before charge progress becomes positive.
            // Healing locks are never attack warnings, even during a loadout transition.
            bool locking = settings.showLockingThreats && !enemy.SelectedMagicStats.TargetsAllies &&
                enemy.HasLockTarget(local.Object);
            if ((nearby || locking) && HasClearSight(main.transform.position, enemy.LockAimPoint))
                candidates.Add(new Threat { side = side, point = enemy.LockAimPoint, locking = locking, distanceSquared = delta.sqrMagnitude });
        }
        // Releasing a lock to fire must not close the view while its shot approaches.
        // Classify each shot by its CURRENT position, even if its caster turns or dies.
        if (settings.showLockingThreats)
        {
            foreach (MagicProjectile shot in MagicProjectile.ActiveProjectiles)
            {
                if (shot == null || shot.Object == null || !shot.Object.IsValid || shot.Runner != local.Runner ||
                    shot.Finished || shot.IsConstruct || shot.IsMine || shot.Settled || shot.ShooterTeam <= 0 ||
                    shot.ShooterTeam == local.TeamIndex) continue;
                Vector3 delta = shot.transform.position - main.transform.position;
                int side = RearSector(main.transform.InverseTransformDirection(delta));
                if (side == 0 || (!shot.TargetId.Equals(local.Object.Id) && !shot.TryGetImpactTime(local, out _))) continue;
                if (!HasClearSight(main.transform.position, shot.transform.position)) continue;
                candidates.Add(new Threat { side = side, point = shot.transform.position, projectile = true, distanceSquared = delta.sqrMagnitude });
            }
            // Ice follow-up shards outlive their networked core projectile.
            foreach (IceImpactFollowUpVisual batch in IceImpactFollowUpVisual.ActiveVisuals)
            {
                if (batch == null || batch.Victim != local || batch.Caster == null || batch.Caster.Object == null ||
                    !batch.Caster.Object.IsValid || batch.Caster.Runner != local.Runner ||
                    batch.Caster.TeamIndex <= 0 || batch.Caster.TeamIndex == local.TeamIndex) continue;
                shardPositions.Clear();
                batch.CollectFlyingShardPositions(shardPositions);
                foreach (Vector3 point in shardPositions)
                {
                    Vector3 delta = point - main.transform.position;
                    int side = RearSector(main.transform.InverseTransformDirection(delta));
                    if (side != 0 && HasClearSight(main.transform.position, point))
                        candidates.Add(new Threat { side = side, point = point, projectile = true, distanceSquared = delta.sqrMagnitude });
                }
            }
        }
        // One camera per sector, never one camera per enemy.
        SelectThreats(out Threat first, out Threat second);
        Show(ref left, first, local, settings, false);
        Show(ref right, second, local, settings, true);
    }

    private void HideCustomRoot(RawImage image, GameObject root)
    {
        if (image == null) return;
        root = root != null ? root : image.gameObject;
        if (!transform.IsChildOf(root.transform)) root.SetActive(false);
    }

    private void SelectThreats(out Threat first, out Threat second)
    {
        first = Nearest(-1);
        second = Nearest(1);
    }

    private Threat Nearest(int side)
    {
        Threat best = null;
        foreach (Threat item in candidates)
            if (item.side == side &&
                (best == null || ThreatPriority(item) > ThreatPriority(best) ||
                 (ThreatPriority(item) == ThreatPriority(best) && item.distanceSquared < best.distanceSquared))) best = item;
        return best;
    }

    private static int ThreatPriority(Threat threat) => threat.projectile ? 2 : threat.locking ? 1 : 0;

    internal static int RearSector(Vector3 cameraRelative)
    {
        if (cameraRelative.z >= 0f) return 0; // Entire front 180 degrees, regardless of camera FOV.
        return cameraRelative.x < 0f ? -1 : 1; // Exact rear seam belongs only to the right.
    }

    private static bool IsSightBlocker(Collider collider)
    {
        return collider != null && collider.GetComponentInParent<Player>() == null &&
            collider.GetComponentInParent<MagicProjectile>() == null;
    }

    private bool HasClearSight(Vector3 origin, Vector3 point)
    {
        // Rays can miss a solid wall enclosing their origin.
        int count = Physics.OverlapSphereNonAlloc(origin, .01f, sightOverlaps, ~0, QueryTriggerInteraction.Ignore);
        Collider[] overlaps = count == sightOverlaps.Length
            ? Physics.OverlapSphere(origin, .01f, ~0, QueryTriggerInteraction.Ignore) : sightOverlaps;
        if (overlaps != sightOverlaps) count = overlaps.Length;
        for (int i = 0; i < count; i++) if (IsSightBlocker(overlaps[i])) return false;

        Vector3 delta = point - origin;
        count = Physics.RaycastNonAlloc(origin, delta.normalized, sightHits, delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
        RaycastHit[] hits = count == sightHits.Length
            ? Physics.RaycastAll(origin, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore) : sightHits;
        if (hits != sightHits) count = hits.Length;
        for (int i = 0; i < count; i++) if (IsSightBlocker(hits[i].collider)) return false;
        return true;
    }

    internal static float NextRenderTime(float now, float previousDeadline, int fps)
    {
        float interval = 1f / Mathf.Clamp(fps, 5, 120);
        // Preserve cadence after a late frame; never do multiple catch-up renders.
        return previousDeadline > 0f
            ? previousDeadline + (Mathf.Floor(Mathf.Max(0f, now + .0001f - previousDeadline) / interval) + 1f) * interval
            : now + interval;
    }

    private void Show(ref View view, Threat threat, Player local, FlightCombatTable settings, bool isRight)
    {
        if (threat == null && !previewInBattle) { SetVisible(view, false); return; }
        // Custom UI may be replaced or removed while this camera survives a scene transition.
        if (view != null && (view.image == null || view.camera == null || view.root == null)) Release(ref view);
        if (view == null) view = CreateView(isRight, settings);
        SetVisible(view, true);
        if (view.canvas != null)
        {
            Canvas canvas = view.canvas.GetComponent<Canvas>();
            Canvas wind = speedEffects != null ? speedEffects.WindCanvas : null;
            canvas.sortingLayerID = wind != null ? wind.sortingLayerID : 0;
            canvas.sortingOrder = Mathf.Clamp(Mathf.Max(overlaySortingOrder, wind != null ? wind.sortingOrder + 1 : 0), -32768, 32767);
        }
        if (view.panel != null)
        {
            Rect rect = isRight ? settings.rightThreatViewRect : settings.rearViewRect;
            view.panel.anchorMin = new Vector2(rect.xMin, rect.yMin);
            view.panel.anchorMax = new Vector2(rect.xMax, rect.yMax);
            view.reveal = Mathf.MoveTowards(view.reveal, 1f, Time.unscaledDeltaTime / .15f);
            view.group.alpha = view.reveal;
            view.panel.localScale = Vector3.one * Mathf.Lerp(.96f, 1f, view.reveal);
        }
        if (view.caption != null)
        {
            bool locking = threat != null && threat.locking;
            bool incoming = threat != null && threat.projectile;
            string label = (isRight ? "우후방" : "좌후방") + (incoming ? " · 투사체" : locking ? " · 록온" : "");
            if (view.caption.font == null || !view.caption.font.HasCharacters(label))
                label = (isRight ? "REAR RIGHT" : "REAR LEFT") + (incoming ? " · INCOMING" : locking ? " · LOCK" : "");
            view.caption.text = threat != null ? $"{label}  ·  {Mathf.Sqrt(threat.distanceSquared):0}m" : label;
            view.caption.color = locking || incoming ? new Color(1f, .65f, .5f) : Color.white;
        }
        if (Time.unscaledTime + .0001f < view.nextFrame) return;
        view.nextFrame = NextRenderTime(Time.unscaledTime, view.nextFrame, framesPerSecond);
        // Main-camera axes select the sector only. Aim at the actual visible threat.
        Vector3 direction = threat != null ? threat.point - main.transform.position
            : main.transform.rotation * Quaternion.Euler(0f, isRight ? 135f : -135f, 0f) * Vector3.forward;
        Vector3 up = Mathf.Abs(Vector3.Dot(direction.normalized, Vector3.up)) > .99f ? Vector3.forward : Vector3.up;
        view.camera.transform.SetPositionAndRotation(main.transform.position, Quaternion.LookRotation(direction, up));
        Rect rectSize = view.image.rectTransform.rect;
        if (rectSize.height > 0f) view.camera.aspect = rectSize.width / rectSize.height;
        view.camera.fieldOfView = Camera.HorizontalToVerticalFieldOfView(Mathf.Clamp(settings.rearViewFov, 90f, 120f), view.camera.aspect);
        float far = settings.rearViewDistance + 30f;
        foreach (Threat candidate in candidates)
            if (candidate.side == (isRight ? 1 : -1)) far = Mathf.Max(far, Mathf.Sqrt(candidate.distanceSquared) + 30f);
        view.camera.farClipPlane = far;
        int mask = main.cullingMask;
        int ui = LayerMask.NameToLayer("UI"), portraits = LayerMask.NameToLayer("BattlePortrait");
        if (ui >= 0) mask &= ~(1 << ui);
        if (portraits >= 0) mask &= ~(1 << portraits);
        view.camera.cullingMask = mask;
        SyncRenderSettings(main, view.camera);
        request.destination = view.texture;
        try
        {
            HideLocalMagic(local);
            HideRenderers(local.gameObject);
            if (GraphicsSettings.currentRenderPipeline != null) RenderPipeline.SubmitRenderRequest(view.camera, request);
            else { view.camera.targetTexture = view.texture; view.camera.Render(); }
        }
        finally
        {
            // Main camera and other players keep their original visuals, even on render failure.
            RestoreLocalMagic();
        }
    }

    private void RestoreLocalMagic()
    {
        foreach (Renderer renderer in temporarilyHidden)
            if (renderer != null) renderer.forceRenderingOff = false;
        temporarilyHidden.Clear();
    }

    private void HideLocalMagic(Player local)
    {
        foreach (var effect in localEffects)
        {
            if (effect.Key == null || effect.Value == null) { expiredEffects.Add(effect.Key); continue; }
            if (effect.Value == local) HideRenderers(effect.Key);
        }
        foreach (GameObject expired in expiredEffects) localEffects.Remove(expired);
        expiredEffects.Clear();
        foreach (MagicProjectile projectile in MagicProjectile.ActiveProjectiles)
        {
            if (projectile == null || projectile.Object == null || !projectile.Object.IsValid ||
                projectile.Runner != local.Runner || !projectile.ShooterId.Equals(local.Object.Id)) continue;
            HideRenderers(projectile.gameObject);
        }
        foreach (IceImpactFollowUpVisual shards in IceImpactFollowUpVisual.ActiveVisuals)
        {
            if (shards != null && shards.Caster == local) HideRenderers(shards.gameObject);
        }
        renderers.Clear();
    }

    private void HideRenderers(GameObject root)
    {
        renderers.Clear();
        root.GetComponentsInChildren<Renderer>(false, renderers);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !renderer.enabled || renderer.forceRenderingOff) continue;
            temporarilyHidden.Add(renderer);
            renderer.forceRenderingOff = true;
        }
    }

    private void SetVisible(View view, bool show)
    {
        if (view == null) return;
        if (show && !view.visible) { view.nextFrame = 0f; view.reveal = 0f; }
        view.visible = show;
        if (view.image != null) view.image.enabled = show;
        // Only dedicated UI roots may be toggled, never the camera's ancestor.
        if (view.root != null && !transform.IsChildOf(view.root.transform)) view.root.SetActive(show);
    }

    private View CreateView(bool isRight, FlightCombatTable settings)
    {
        var view = new View();
        // URP uses an external target's format for its intermediate color buffer too.
        // An LDR target clamps spell brightness before bloom even with allowHDR enabled.
        view.texture = new RenderTexture(640, 384, 24, RenderTextureFormat.DefaultHDR) { name = isRight ? "Right threat" : "Left rear threat", antiAliasing = 1 };
        view.texture.Create();
        var cameraObject = new GameObject(view.texture.name + " camera (local)", typeof(Camera));
        cameraObject.transform.SetParent(transform, false);
        view.camera = cameraObject.GetComponent<Camera>();
        view.camera.enabled = false;
        view.camera.nearClipPlane = .1f;
        view.camera.aspect = 640f / 384f;
        view.camera.clearFlags = CameraClearFlags.Skybox;
        view.camera.allowMSAA = false;
        var data = view.camera.GetUniversalAdditionalCameraData();
        data.renderShadows = false;
        SyncRenderSettings(main, view.camera);
        view.custom = isRight ? rightCustomView : customView;
        view.caption = isRight ? rightCustomCaption : customCaption;
        if (view.custom != null)
        {
            view.image = view.custom;
            view.oldTexture = view.custom.texture;
            view.root = (isRight ? rightCustomRoot : customRoot) ?? view.custom.gameObject;
        }
        else CreateDefaultPanel(view, isRight, settings);
        view.image.texture = view.texture;
        view.image.raycastTarget = false;
        return view;
    }

    internal static void SyncRenderSettings(Camera source, Camera destination)
    {
        destination.allowHDR = source.allowHDR;
        destination.backgroundColor = source.backgroundColor;
        var sourceData = source.GetUniversalAdditionalCameraData();
        var data = destination.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = sourceData.renderPostProcessing;
        data.volumeLayerMask = sourceData.volumeLayerMask;
        // Evaluate the same local volumes as the viewer, not at the offset threat camera.
        data.volumeTrigger = sourceData.volumeTrigger != null ? sourceData.volumeTrigger : source.transform;
        destination.SetVolumeFrameworkUpdateMode(VolumeFrameworkUpdateMode.EveryFrame);
        // Distortion/soft particles need textures belonging to THIS camera. Missing
        // textures can sample black or stale data from another camera's render.
        data.requiresColorOption = CameraOverrideOption.On;
        data.requiresDepthOption = CameraOverrideOption.On;
    }

    private static void Stretch(RectTransform rect, float inset = 0f)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.one * inset; rect.offsetMax = Vector2.one * -inset;
    }

    private static void AddShape(GameObject root, Sprite sprite, bool mirror, Color color)
    {
        Graphic graphic;
        if (sprite != null)
        {
            var image = root.AddComponent<Image>(); image.sprite = sprite; graphic = image;
        }
        else
        {
            var bubble = root.AddComponent<ThreatBubbleGraphic>(); bubble.Mirrored = mirror; graphic = bubble;
        }
        graphic.color = color; graphic.raycastTarget = false;
    }

    private static void CreateDefaultPanel(View view, bool isRight, FlightCombatTable settings)
    {
        view.canvas = new GameObject(isRight ? "Right threat HUD" : "Left rear HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        var canvas = view.canvas.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 30;
        var scaler = view.canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f); scaler.matchWidthOrHeight = .5f;
        view.root = view.canvas;
        var panel = new GameObject("Comic threat panel", typeof(RectTransform), typeof(CanvasGroup));
        panel.transform.SetParent(view.canvas.transform, false);
        view.panel = (RectTransform)panel.transform; view.panel.offsetMin = view.panel.offsetMax = Vector2.zero;
        view.group = panel.GetComponent<CanvasGroup>(); view.group.blocksRaycasts = view.group.interactable = false;
        var outline = new GameObject("Bubble outline", typeof(RectTransform), typeof(CanvasRenderer));
        outline.transform.SetParent(panel.transform, false); Stretch((RectTransform)outline.transform);
        AddShape(outline, settings.threatViewMask, isRight, new Color(.025f, .03f, .045f, 1f));
        var mask = new GameObject("Bubble mask", typeof(RectTransform), typeof(CanvasRenderer));
        mask.transform.SetParent(panel.transform, false); Stretch((RectTransform)mask.transform, 4f);
        AddShape(mask, settings.threatViewMask, isRight, Color.white);
        mask.AddComponent<Mask>().showMaskGraphic = false;
        var image = new GameObject("Camera image", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        image.transform.SetParent(mask.transform, false);
        view.image = image.GetComponent<RawImage>(); Stretch(view.image.rectTransform);
        var badge = new GameObject("Caption background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        badge.transform.SetParent(mask.transform, false);
        var badgeRect = (RectTransform)badge.transform; badgeRect.anchorMin = new Vector2(.18f, .69f); badgeRect.anchorMax = new Vector2(.82f, .83f);
        badgeRect.offsetMin = badgeRect.offsetMax = Vector2.zero;
        badge.GetComponent<Image>().color = new Color(.035f, .05f, .07f, .8f); badge.GetComponent<Image>().raycastTarget = false;
        var label = new GameObject("Caption", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        label.transform.SetParent(badge.transform, false); view.caption = label.GetComponent<TextMeshProUGUI>(); Stretch(view.caption.rectTransform, 2f);
        var hud = FindFirstObjectByType<BattleHud>(); if (hud != null && hud.FeedbackFont != null) view.caption.font = hud.FeedbackFont;
        view.caption.fontSize = 20f; view.caption.alignment = TextAlignmentOptions.Center; view.caption.raycastTarget = false;
    }

    private void Release(ref View view)
    {
        if (view == null) return;
        SetVisible(view, false);
        if (view.custom != null) view.custom.texture = view.oldTexture;
        if (view.camera != null) Destroy(view.camera.gameObject);
        if (view.canvas != null) Destroy(view.canvas);
        if (view.texture != null) { view.texture.Release(); Destroy(view.texture); }
        view = null;
    }
    private void OnDisable() { Release(ref left); Release(ref right); candidates.Clear(); }
}
