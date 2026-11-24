using UnityEngine;
using UnityEngine.UI;

// Scene-authored local feedback. Reads the replicated ice timer, never changes combat.
[AddComponentMenu("UI/WitchFlight Ice Hit Screen Overlay")]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class IceHitScreenOverlay : MaskableGraphic
{
    [SerializeField, Range(.03f, .35f)] private float borderWidth = .18f;
    [SerializeField, Range(0f, 1f)] private float opacity = .85f;
    [SerializeField, Min(.01f)] private float fadeInSeconds = .15f;
    [SerializeField, Min(.01f)] private float fadeOutSeconds = .65f;
    [Tooltip("에디터에서 서리 효과 미리보기. 실제 플레이에서는 얼음 감속 상태만 사용합니다.")]
    [SerializeField] private bool previewInEditor = true;
    private Material instanceMaterial;
    private Material authoredMaterial;
    private float visibility;
    private static readonly int WidthId = Shader.PropertyToID("_BorderWidth");
    private static readonly int AspectId = Shader.PropertyToID("_Aspect");

    protected override void OnEnable()
    {
        raycastTarget = false;
        base.OnEnable();
        visibility = 0f;
        if (Application.isPlaying && m_Material != null)
        {
            authoredMaterial = m_Material;
            instanceMaterial = new Material(m_Material);
            instanceMaterial.hideFlags = HideFlags.DontSave;
            material = instanceMaterial;
        }
        RefreshParameters();
        canvasRenderer.SetAlpha(Application.isPlaying ? 0f : previewInEditor ? opacity : 0f);
    }

    private void Update()
    {
        if (!Application.isPlaying) return;
        Player player = Player.LocalPlayer;
        bool valid = player != null && player.Object != null && player.Object.IsValid && player.IsAlive &&
            !player.IsReturningToMap && BattleManager.Instance != null && BattleManager.Instance.IsGameplayActive && !BattleHud.MenuOpen;
        if (!valid) visibility = 0f;
        else
        {
            bool frozen = player.HasIceSlowAudio;
            visibility = Mathf.MoveTowards(visibility, frozen ? 1f : 0f,
                Time.unscaledDeltaTime / Mathf.Max(.01f, frozen ? fadeInSeconds : fadeOutSeconds));
        }
        canvasRenderer.SetAlpha(Mathf.SmoothStep(0f, 1f, visibility) * opacity);
        RefreshParameters();
    }

    private void RefreshParameters()
    {
        // Editor previews use the shared authored defaults; runtime settings are isolated.
        if (instanceMaterial == null) return;
        instanceMaterial.SetFloat(WidthId, borderWidth);
        Rect rect = rectTransform.rect;
        instanceMaterial.SetFloat(AspectId, rect.width / Mathf.Max(1f, rect.height));
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = GetPixelAdjustedRect();
        vh.AddVert(new Vector3(r.xMin, r.yMin), color, new Vector2(0, 0));
        vh.AddVert(new Vector3(r.xMin, r.yMax), color, new Vector2(0, 1));
        vh.AddVert(new Vector3(r.xMax, r.yMax), color, new Vector2(1, 1));
        vh.AddVert(new Vector3(r.xMax, r.yMin), color, new Vector2(1, 0));
        vh.AddTriangle(0, 1, 2); vh.AddTriangle(2, 3, 0);
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (instanceMaterial != null)
        {
            material = authoredMaterial;
            Destroy(instanceMaterial);
            instanceMaterial = null;
        }
        visibility = 0f;
    }
}
