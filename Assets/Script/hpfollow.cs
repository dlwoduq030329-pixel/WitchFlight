using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// A local-only view of replicated Player HP. Never attach a NetworkObject to the HP UI.
[DisallowMultipleComponent]
[RequireComponent(typeof(Player))]
[DefaultExecutionOrder(100)]
public sealed class hpfollow : MonoBehaviour
{
    [Header("HP bar")]
    [Tooltip("사용자가 제작한 UI 프리팹을 넣습니다. 비워 두면 기본 체력바를 자동으로 만듭니다.")]
    [SerializeField] private GameObject hpBarPrefab;
    [Tooltip("머리 본입니다. 비어 있으면 Player 위치를 기준으로 표시합니다.")]
    [SerializeField] private Transform headAnchor;
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, 0.3f, 0f);
    [Tooltip("프리팹의 UI 픽셀 크기를 월드 크기로 바꾸는 배율입니다.")]
    [SerializeField, Min(0.0001f)] private float worldSpaceScale = 0.01f;
    [Tooltip("먼 거리에서도 읽을 수 있는 체력바의 최소 화면 높이(픽셀). 0이면 원래 월드 크기만 사용합니다.")]
    [SerializeField, Min(0f)] private float minimumScreenHeight = 8f;

    [Header("Enemy halo (local view only)")]
    [SerializeField] private bool showEnemyHalo = true;
    [Tooltip("비우면 부드러운 붉은 원형 후광을 생성합니다.")]
    [SerializeField] private Sprite enemyHaloSprite;
    [SerializeField] private Color enemyHaloColor = new Color(1f, 0.08f, 0.12f, 0.6f);
    [SerializeField] private Vector2 enemyHaloSize = new Vector2(3.2f, 3.2f);
    [Tooltip("Lock Aim Point에서 후광 중심까지의 월드 오프셋입니다.")]
    [SerializeField] private Vector3 enemyHaloOffset = new Vector3(0f, -0.25f, 0f);
    [Tooltip("카메라에서 보았을 때 몸보다 뒤에 놓을 거리입니다.")]
    [SerializeField, Min(0f)] private float enemyHaloDepthOffset = 0.6f;
    [Header("Visibility")]
    [SerializeField] private LayerMask obstructionMask = ~0;
    [SerializeField, Min(0.02f)] private float visibilityRefreshInterval = 0.1f;

    private Player owner;
    private Camera viewCamera;
    private GameObject viewRoot;
    private GameObject attemptedPrefab;
    private bool attemptedCreation;
    private RectTransform fallbackFill;
    private Canvas viewCanvas;
    private GridHPBar gridBar;
    private Slider slider;
    private Image filledImage;
    private float healthFraction;
    private float maxHealth;
    private bool healthDirty = true;
    private GameObject haloRoot;
    private Canvas haloCanvas;
    private Graphic haloGraphic;
    private Sprite renderedHaloSprite;
    private readonly RaycastHit[] visibilityHits = new RaycastHit[32];
    private float nextVisibilityCheck;
    private bool lineOfSight;
    private Player visibilityObserver;

    private void Awake()
    {
        owner = GetComponent<Player>();
    }

    private void OnEnable()
    {
        nextVisibilityCheck = 0f;
        if (owner == null) owner = GetComponent<Player>();
        owner.HealthChanged += OnHealthChanged;
        // Re-enabling a view may have missed an event while it was disabled.
        if (owner.Object != null && owner.Object.IsValid)
            OnHealthChanged(owner.NowHp, owner.MaxHp);
    }

    private void OnHealthChanged(float hp, float maxHp)
    {
        maxHealth = maxHp;
        healthFraction = maxHp > 0f ? Mathf.Clamp01(hp / maxHp) : 0f;
        healthDirty = true;
    }

    private void LateUpdate()
    {
        // Allow assigning/replacing the prefab in the Inspector during Play Mode.
        if (attemptedCreation && attemptedPrefab != hpBarPrefab)
        {
            ReleaseView();
            attemptedPrefab = null;
        }

        if (!CanShowToLocalPlayer())
        {
            SetVisible(false);
            return;
        }

        if (viewCamera == null || !viewCamera.isActiveAndEnabled)
            viewCamera = Camera.main;
        if (viewCamera == null)
        {
            SetVisible(false);
            return;
        }

        Player local = Player.LocalPlayer;
        if (visibilityObserver != local || Time.unscaledTime >= nextVisibilityCheck)
        {
            visibilityObserver = local;
            nextVisibilityCheck = Time.unscaledTime + Mathf.Max(0.02f, visibilityRefreshInterval);
            lineOfSight = HasLineOfSight(local);
        }
        if (!lineOfSight)
        {
            SetVisible(false);
            return;
        }

        Vector3 position = headAnchor != null ? headAnchor.position : transform.position + Vector3.up * 1.5f;
        position += worldOffset;
        Vector3 viewport = viewCamera.WorldToViewportPoint(position);
        if (viewport.z <= 0f || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f)
        {
            SetVisible(false);
            return;
        }

        if (viewRoot == null && !attemptedCreation)
            CreateView();
        if (viewRoot == null)
            return;

        viewCanvas.worldCamera = viewCamera;
        viewRoot.transform.SetPositionAndRotation(position, viewCamera.transform.rotation);
        float pixelWorldSize = viewCamera.orthographic ? 2f * viewCamera.orthographicSize :
            2f * viewport.z * Mathf.Tan(viewCamera.fieldOfView * Mathf.Deg2Rad * 0.5f);
        float minScale = pixelWorldSize * Mathf.Max(0f, minimumScreenHeight) /
            (Mathf.Max(1, viewCamera.pixelHeight) * Mathf.Max(1f, ((RectTransform)viewRoot.transform).rect.height));
        viewRoot.transform.localScale = Vector3.one * Mathf.Max(Mathf.Max(0.0001f, worldSpaceScale), minScale);
        SetVisible(true);
        UpdateEnemyHalo();

        if (!healthDirty) return;
        healthDirty = false;
        if (fallbackFill != null) fallbackFill.anchorMax = new Vector2(healthFraction, 1f);
        else if (gridBar != null)
            gridBar.UpdateHealth(healthFraction * maxHealth, maxHealth);
        else if (slider != null)
            slider.SetValueWithoutNotify(healthFraction);
        else if (filledImage != null)
            filledImage.fillAmount = healthFraction;
    }

    private bool CanShowToLocalPlayer()
    {
        if (BattleManager.Instance == null || !BattleManager.Instance.IsGameplayActive)
            return false;
        Player local = Player.LocalPlayer;
        return owner != null && owner.Object != null && owner.Object.IsValid &&
               local != null && local.Object != null && local.Object.IsValid &&
               local.Runner == owner.Runner &&
               local != owner && !owner.Object.HasInputAuthority &&
               local.TeamIndex > 0 && owner.TeamIndex > 0 && local.TeamIndex != owner.TeamIndex &&
               local.IsAlive && owner.IsAlive && maxHealth > 0f && !owner.IsStealthed &&
               !owner.IsHiddenBySmokeFor(local);
    }

    private bool HasLineOfSight(Player local)
    {
        Vector3 origin = viewCamera.transform.position;
        Vector3 delta = owner.LockAimPoint - origin;
        float distance = delta.magnitude;
        if (distance < 0.01f) return true;
        int count = Physics.RaycastNonAlloc(origin, delta / distance, visibilityHits, distance,
            obstructionMask, QueryTriggerInteraction.Ignore);
        // A full buffer could have omitted the wall. Conservatively hide this frame.
        if (count == visibilityHits.Length) return false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = visibilityHits[i];
            if (hit.collider == null || hit.transform.IsChildOf(owner.transform) ||
                (local != null && hit.transform.IsChildOf(local.transform))) continue;
            return false;
        }
        return true;
    }

    private void UpdateEnemyHalo()
    {
        if (!showEnemyHalo)
        {
            if (haloRoot != null) haloRoot.SetActive(false);
            return;
        }
        if (haloRoot != null && renderedHaloSprite != enemyHaloSprite)
        {
            haloRoot.SetActive(false);
            Destroy(haloRoot);
            haloRoot = null;
        }
        if (haloRoot == null)
        {
            haloRoot = new GameObject("Enemy red halo (local)", typeof(RectTransform), typeof(Canvas));
            haloRoot.layer = 5;
            if (gameObject.scene.IsValid() && gameObject.scene.isLoaded)
                SceneManager.MoveGameObjectToScene(haloRoot, gameObject.scene);
            haloCanvas = haloRoot.GetComponent<Canvas>();
            haloCanvas.renderMode = RenderMode.WorldSpace;
            var glow = new GameObject("Glow", typeof(RectTransform), typeof(CanvasRenderer),
                enemyHaloSprite != null ? typeof(Image) : typeof(CombatFeedbackGraphic));
            glow.layer = 5;
            glow.transform.SetParent(haloRoot.transform, false);
            haloGraphic = glow.GetComponent<Graphic>();
            haloGraphic.raycastTarget = false;
            if (haloGraphic is Image image) { image.sprite = enemyHaloSprite; image.preserveAspect = true; }
            else ((CombatFeedbackGraphic)haloGraphic).Glow = true;
            renderedHaloSprite = enemyHaloSprite;
        }
        haloCanvas.worldCamera = viewCamera;
        haloGraphic.color = enemyHaloColor;
        haloGraphic.rectTransform.sizeDelta = new Vector2(Mathf.Max(0.01f, enemyHaloSize.x), Mathf.Max(0.01f, enemyHaloSize.y));
        // World-space depth testing puts the glow behind the body, not over its face.
        haloRoot.transform.SetPositionAndRotation(owner.LockAimPoint + enemyHaloOffset +
            viewCamera.transform.forward * Mathf.Max(0f, enemyHaloDepthOffset), viewCamera.transform.rotation);
        haloRoot.SetActive(true);
    }

    private void CreateView()
    {
        healthDirty = true;
        attemptedPrefab = hpBarPrefab;
        attemptedCreation = true;
        if (hpBarPrefab == null) { CreateFallback(); return; }
        if (hpBarPrefab.GetComponentInChildren<Fusion.NetworkObject>(true) != null)
        {
            Debug.LogWarning("hpfollow: HP Bar Prefab must be local UI without a NetworkObject. Using fallback.", this);
            CreateFallback(); return;
        }
        if (hpBarPrefab.GetComponent<RectTransform>() == null)
        {
            Debug.LogWarning("hpfollow: HP Bar Prefab must have a UI RectTransform root. Using fallback.", this);
            CreateFallback(); return;
        }
        foreach (GridHPBar grid in hpBarPrefab.GetComponentsInChildren<GridHPBar>(true))
        {
            if (grid.gridParent != null)
                continue;
            Debug.LogWarning("hpfollow: assign GridHPBar.gridParent on the HP bar prefab. Using fallback.", this);
            CreateFallback(); return;
        }

        // A separate scene object keeps flight pitch/scale and equipment visibility from moving the UI.
        viewRoot = new GameObject("Enemy HP - " + gameObject.name, typeof(RectTransform), typeof(Canvas));
        viewRoot.layer = 5; // Unity's UI layer.
        viewRoot.SetActive(false);
        Scene scene = gameObject.scene;
        if (scene.IsValid() && scene.isLoaded)
            SceneManager.MoveGameObjectToScene(viewRoot, scene);

        viewCanvas = viewRoot.GetComponent<Canvas>();
        viewCanvas.renderMode = RenderMode.WorldSpace;
        viewCanvas.worldCamera = viewCamera;

        GameObject instance = Instantiate(hpBarPrefab, viewRoot.transform, false);
        instance.SetActive(true);
        RectTransform rect = instance.GetComponent<RectTransform>();
        Vector2 size = rect.sizeDelta;
        if (size.x <= 0f) size.x = 120f;
        if (size.y <= 0f) size.y = 20f;
        ((RectTransform)viewRoot.transform).sizeDelta = size;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition3D = Vector3.zero;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;

        foreach (Canvas canvas in instance.GetComponentsInChildren<Canvas>(true))
        {
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = viewCamera;
            canvas.overrideSorting = false;
        }
        foreach (CanvasScaler scaler in instance.GetComponentsInChildren<CanvasScaler>(true))
            scaler.enabled = false;
        foreach (GraphicRaycaster raycaster in instance.GetComponentsInChildren<GraphicRaycaster>(true))
            raycaster.enabled = false;
        foreach (Graphic graphic in instance.GetComponentsInChildren<Graphic>(true))
            graphic.raycastTarget = false;

        gridBar = instance.GetComponentInChildren<GridHPBar>(true);
        if (gridBar == null)
        {
            slider = instance.GetComponentInChildren<Slider>(true);
            if (slider != null)
            {
                slider.interactable = false;
                slider.wholeNumbers = false;
                slider.minValue = 0f;
                slider.maxValue = 1f;
            }
            else
            {
                foreach (Image image in instance.GetComponentsInChildren<Image>(true))
                {
                    if (image.type != Image.Type.Filled)
                        continue;
                    filledImage = image;
                    break;
                }
            }
        }

        if (gridBar == null && slider == null && filledImage == null)
        {
            Debug.LogWarning("hpfollow: add GridHPBar, Slider, or a Filled Image. Using fallback.", this);
            Destroy(viewRoot);
            CreateFallback();
        }
    }

    private void CreateFallback()
    {
        viewRoot = new GameObject("Enemy HP (generated)", typeof(RectTransform), typeof(Canvas));
        viewRoot.layer = 5;
        if (gameObject.scene.IsValid() && gameObject.scene.isLoaded)
            SceneManager.MoveGameObjectToScene(viewRoot, gameObject.scene);
        viewCanvas = viewRoot.GetComponent<Canvas>();
        viewCanvas.renderMode = RenderMode.WorldSpace;
        ((RectTransform)viewRoot.transform).sizeDelta = new Vector2(120f, 12f);
        var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
        background.layer = 5;
        background.transform.SetParent(viewRoot.transform, false);
        var backgroundRect = (RectTransform)background.transform;
        backgroundRect.anchorMin = Vector2.zero;
        backgroundRect.anchorMax = Vector2.one;
        backgroundRect.offsetMin = backgroundRect.offsetMax = Vector2.zero;
        background.GetComponent<Image>().color = new Color(0.03f, 0.03f, 0.03f, 0.85f);
        background.GetComponent<Image>().raycastTarget = false;
        var fill = new GameObject("Health", typeof(RectTransform), typeof(Image));
        fill.layer = 5;
        fill.transform.SetParent(background.transform, false);
        fallbackFill = (RectTransform)fill.transform;
        fallbackFill.anchorMin = Vector2.zero;
        fallbackFill.anchorMax = new Vector2(healthFraction, 1f);
        fallbackFill.offsetMin = fallbackFill.offsetMax = Vector2.zero;
        fill.GetComponent<Image>().color = new Color(0.9f, 0.2f, 0.25f);
        fill.GetComponent<Image>().raycastTarget = false;
        healthDirty = true;
    }

    private void SetVisible(bool visible)
    {
        if (!visible && haloRoot != null && haloRoot.activeSelf) haloRoot.SetActive(false);
        if (viewRoot != null && viewRoot.activeSelf != visible)
            viewRoot.SetActive(visible);
    }

    private void OnDisable()
    {
        if (owner != null) owner.HealthChanged -= OnHealthChanged;
        SetVisible(false);
    }

    private void OnDestroy()
    {
        ReleaseView();
        if (haloRoot != null) Destroy(haloRoot);
    }

    private void ReleaseView()
    {
        if (viewRoot != null)
        {
            viewRoot.SetActive(false);
            Destroy(viewRoot);
        }
        viewRoot = null;
        viewCanvas = null;
        gridBar = null;
        slider = null;
        filledImage = null;
        fallbackFill = null;
        attemptedCreation = false;
    }
}
