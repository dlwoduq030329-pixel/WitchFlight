using UnityEngine;
using UnityEngine.UI;

// Authored overlay; only presentation is local. The host still confirms every shot.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class InstantMagicScopeUI : MaskableGraphic
{
    [SerializeField] private CanvasGroup[] hideWhileAiming;
    private float[] previousAlpha;
    private bool hiding;
    private float weight;
    private void Start()
    {
        if (hideWhileAiming != null && hideWhileAiming.Length > 0) return;
        // Legacy raid HUD is a prefab instance. Bind its siblings once, keeping
        // the crosshair alive and avoiding scene-wide searches while aiming.
        foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            foreach (RectTransform child in canvas.GetComponentsInChildren<RectTransform>(true))
            {
                if (child.name != "Crosshair" || child.parent == null || child.parent.name != "BattleUI") continue;
                var groups = new System.Collections.Generic.List<CanvasGroup>();
                foreach (Transform sibling in child.parent)
                {
                    if (sibling == child || sibling.name == "EnemyHpBar" || sibling.name.StartsWith("VSUI") || sibling.name == "VFX_Victory" || sibling.name == "VFX_Lose") continue;
                    var group = sibling.GetComponent<CanvasGroup>();
                    groups.Add(group != null ? group : sibling.gameObject.AddComponent<CanvasGroup>());
                }
                hideWhileAiming = groups.ToArray();
                return;
            }
        }
    }
    private static float aimStartedAt = -1f;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetAimDelay() => aimStartedAt = -1f;

    public static bool Requested
    {
        get
        {
            if (!CanRequestScope) { aimStartedAt = -1f; return false; }
            if (aimStartedAt < 0f) aimStartedAt = Time.unscaledTime;
            var settings = FlightCombatTable.Default;
            float delay = settings != null ? Mathf.Max(0f, settings.instantScopeHoldDelay) : .1f;
            return Time.unscaledTime - aimStartedAt >= delay;
        }
    }

    private static bool CanRequestScope
    {
        get
        {
            if (!Application.isFocused || BattleHud.MenuOpen || CombatPresentation.MenuOpen ||
                BattlePerkUI.BlocksCombatInput || BattleEmoteUI.BlocksCombatInput ||
                Cursor.lockState != CursorLockMode.Locked || !ControlSettings.Held(ControlAction.MagicUse)) return false;
            Player player = Player.LocalPlayer;
            if (player != null && player.Object != null && player.Object.IsValid)
                return BattleManager.Instance != null && BattleManager.Instance.IsGameplayActive &&
                    player.IsInstantScopeAiming && player.LocalMagicInputSlot == player.CurrentMagicSlot;
            return RobbyMagicPractice.Instance != null && RobbyMagicPractice.Instance.IsInstantScopeAiming;
        }
    }
    public static float LookSensitivityMultiplier => Requested ? .15f : 1f;

    public static float Advance(float current, bool aiming)
    {
        var settings = FlightCombatTable.Default;
        float seconds = aiming ? (settings != null ? settings.instantScopeZoomSeconds : .15f) :
            (settings != null ? settings.instantScopeReturnSeconds : .04f);
        return seconds <= 0f ? (aiming ? 1f : 0f) : Mathf.MoveTowards(current, aiming ? 1f : 0f, Time.unscaledDeltaTime / seconds);
    }
    public static float ScopedFov(float original, float weight)
    {
        var settings = FlightCombatTable.Default;
        return Mathf.Lerp(original, Mathf.Min(original, settings != null ? settings.instantScopeFov : 35f),
            Mathf.SmoothStep(0f, 1f, weight));
    }
    protected override void OnEnable()
    {
        raycastTarget = false;
        base.OnEnable();
        weight = 0f;
        canvasRenderer.SetAlpha(0f);
    }
    private void Update()
    {
        if (!Application.isPlaying) return;
        bool requested = Requested;
        weight = Advance(weight, requested);
        var settings = FlightCombatTable.Default;
        canvasRenderer.SetAlpha(Mathf.SmoothStep(0f, 1f, weight) * (settings != null ? settings.instantScopeDarkness : .82f));
        if (requested && !hiding)
        {
            int count = hideWhileAiming != null ? hideWhileAiming.Length : 0;
            if (previousAlpha == null || previousAlpha.Length != count) previousAlpha = new float[count];
            for (int i = 0; i < previousAlpha.Length; i++)
                if (hideWhileAiming[i] != null) previousAlpha[i] = hideWhileAiming[i].alpha;
            hiding = true;
        }
        if (requested && hiding)
        {
            for (int i = 0; i < previousAlpha.Length; i++)
                if (hideWhileAiming[i] != null) hideWhileAiming[i].alpha = 0f;
        }
        else RestoreHud();
    }
    private void RestoreHud()
    {
        if (!hiding) return;
        for (int i = 0; i < previousAlpha.Length; i++)
            if (hideWhileAiming[i] != null) hideWhileAiming[i].alpha = previousAlpha[i];
        hiding = false;
    }
    protected override void OnDisable() { RestoreHud(); weight = 0f; base.OnDisable(); }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = GetPixelAdjustedRect();
        const int segments = 96;
        for (int j = 0; j <= segments; j++)
        {
            float angle = j * Mathf.PI * 2f / segments;
            Vector2 d = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            float outer = .5f / Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.y));
            for (int band = 0; band < 3; band++)
            {
                float radius = band == 0 ? .23f : band == 1 ? .43f : outer;
                Color tint = new Color(.015f, .025f, .04f, band == 0 ? 0f : band == 1 ? .75f : 1f);
                vh.AddVert(new Vector3(r.center.x + d.x * radius * r.width, r.center.y + d.y * radius * r.height), tint, Vector2.zero);
            }
            if (j == 0) continue;
            for (int band = 0; band < 2; band++)
            {
                int a = (j - 1) * 3 + band, b = j * 3 + band;
                vh.AddTriangle(a, b, b + 1); vh.AddTriangle(a, b + 1, a + 1);
            }
        }
    }
}
