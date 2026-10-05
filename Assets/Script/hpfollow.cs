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
    [Tooltip("사용자가 제작한 UI 프리팹을 넣습니다. 비워 두면 체력바를 만들지 않습니다.")]
    [SerializeField] private GameObject hpBarPrefab;
    [Tooltip("머리 본입니다. 비어 있으면 Player 위치를 기준으로 표시합니다.")]
    [SerializeField] private Transform headAnchor;
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, 0.3f, 0f);
    [Tooltip("프리팹의 UI 픽셀 크기를 월드 크기로 바꾸는 배율입니다.")]
    [SerializeField, Min(0.0001f)] private float worldSpaceScale = 0.01f;

    private Player owner;
    private Camera viewCamera;
    private GameObject viewRoot;
    private GameObject attemptedPrefab;
    private Canvas viewCanvas;
    private GridHPBar gridBar;
    private Slider slider;
    private Image filledImage;
    private float healthFraction;
    private float maxHealth;
    private bool healthDirty = true;

    private void Awake()
    {
        owner = GetComponent<Player>();
    }

    private void OnEnable()
    {
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
        if (attemptedPrefab != hpBarPrefab)
        {
            ReleaseView();
            attemptedPrefab = null;
        }

        if (hpBarPrefab == null || !CanShowToLocalPlayer())
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

        Vector3 position = headAnchor != null ? headAnchor.position : transform.position + Vector3.up * 1.5f;
        position += worldOffset;
        Vector3 viewport = viewCamera.WorldToViewportPoint(position);
        if (viewport.z <= 0f || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f)
        {
            SetVisible(false);
            return;
        }

        if (viewRoot == null && attemptedPrefab != hpBarPrefab)
            CreateView();
        if (viewRoot == null)
            return;

        viewCanvas.worldCamera = viewCamera;
        viewRoot.transform.SetPositionAndRotation(position, viewCamera.transform.rotation);
        viewRoot.transform.localScale = Vector3.one * Mathf.Max(0.0001f, worldSpaceScale);
        SetVisible(true);

        if (!healthDirty) return;
        healthDirty = false;
        if (gridBar != null)
            gridBar.UpdateHP(healthFraction);
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
               local != owner && !owner.Object.HasInputAuthority &&
               local.TeamIndex > 0 && owner.TeamIndex > 0 && local.TeamIndex != owner.TeamIndex &&
               local.IsAlive && owner.IsAlive && maxHealth > 0f && !owner.IsStealthed;
    }

    private void CreateView()
    {
        healthDirty = true;
        attemptedPrefab = hpBarPrefab;
        if (hpBarPrefab.GetComponent<RectTransform>() == null)
        {
            Debug.LogWarning("hpfollow: HP Bar Prefab must have a UI RectTransform root.", this);
            return;
        }
        foreach (GridHPBar grid in hpBarPrefab.GetComponentsInChildren<GridHPBar>(true))
        {
            if (grid.gridParent != null)
                continue;
            Debug.LogWarning("hpfollow: assign GridHPBar.gridParent on the HP bar prefab.", this);
            return;
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
            Debug.LogWarning("hpfollow: add GridHPBar, Slider, or a Filled Image to the HP bar prefab.", this);
    }

    private void SetVisible(bool visible)
    {
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
    }
}
