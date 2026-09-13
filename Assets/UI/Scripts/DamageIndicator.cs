using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using Lovatto.DamagePointer;

/// <summary>
/// Central manager that pools and updates directional damage indicators.
/// </summary>
public class DamageIndicator : MonoBehaviour
{
    #region Fields and Properties
    [Header("Indicator")]
    [Tooltip("UI prefab instantiated for each pooled indicator slot. Must include a DamageIndicatorBase implementation or one will be added at runtime.")]
    public GameObject indicatorPrefab;
    [Space]
    [Header("Dependencies")]
    [Tooltip("Camera used to orient indicators in screen space. Usually your active player or gameplay camera.")]
    public Transform playerCamera;

    [Tooltip("Optional parent RectTransform for all spawned indicators. If empty, this object transform is used.")]
    [SerializeField] private RectTransform indicatorsParent = null;

    [Tooltip("Optional shared settings asset. If assigned, it overrides local fallback values.")]
    public DamageIndicatorGlobalSettings globalSettings;

    private DamageIndicatorSlot[] pool;
    private int activationSequence;
    private int activeIndicatorsCount;
    private readonly Dictionary<string, DamageIndicatorSlot> visibleIndicatorsByKey = new Dictionary<string, DamageIndicatorSlot>();

    private int ResolvedPoolSize => globalSettings != null ? Mathf.Max(1, globalSettings.poolSize) : 3;

    private bool UseUnscaledTime => globalSettings != null && globalSettings.useUnscaledTime;

    private bool RecycleOldestWhenPoolIsFull => globalSettings == null || globalSettings.recycleOldestWhenPoolIsFull;

    private DamageIndicatorReusePolicy ReusePolicy => globalSettings != null ? globalSettings.reusePolicy : DamageIndicatorReusePolicy.Disabled;

    private float MaxReuseAngleDelta => globalSettings != null ? Mathf.Clamp(globalSettings.maxReuseAngleDelta, 0f, 180f) : 0f;

    private bool isHidingIndicators = false;
    #endregion

    #region Unity Callbacks
    private void Awake()
    {
        BuildPool();
    }

    private void OnDestroy()
    {
        CleanupPoolInstances();
    }

    private void Update()
    {
        if (playerCamera == null || pool == null || activeIndicatorsCount == 0)
        {
            return;
        }

        float deltaTime = UseUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        if (deltaTime <= 0f)
        {
            return;
        }

        bool hasValidPlanarCameraForward = DamageIndicatorUtility.TryGetPlanarCameraData(playerCamera, out Vector3 cameraPosition, out Vector3 cameraForwardPlanar);
        DamageIndicatorSlot[] localPool = pool;

        for (int i = 0; i < localPool.Length; i++)
        {
            DamageIndicatorSlot slot = localPool[i];
            if (!slot.active || slot.indicator == null)
            {
                continue;
            }

            bool keepAlive = slot.indicator.Tick(new DamageIndicatorTickContext(
                deltaTime,
                playerCamera,
                cameraPosition,
                cameraForwardPlanar,
                hasValidPlanarCameraForward,
                slot.damagePosition,
                slot.color));

            if (!keepAlive)
            {
                DeactivateSlot(slot);
            }
        }
    }
    #endregion

    #region Public API
    /// <summary>
    /// Triggers a damage indicator at the specified world position with the given color and optional reuse key.
    /// @param damagePosition World position the indicator should point to.
    /// @param indicatorColor Color applied to the indicator visuals. Alpha is respected by default shaders.
    /// @param key Optional string key to enable reuse policies configured in global settings. Keyed
    /// </summary>
    public static void SetDamageIndicator(Vector3 damagePosition, Color indicatorColor, string key = null)
    {
        if (Instance != null) Instance.TriggerDamageSense(damagePosition, indicatorColor, key);
    }

    public void TriggerDamageSense(Vector3 position, Color color)
    {
        TriggerDamageSense(position, color, null);
    }

    /// <summary>
    /// Triggers or replays a damage indicator.
    /// Provide a key to enable keyed reuse policies configured in global settings.
    /// </summary>
    public void TriggerDamageSense(Vector3 position, Color color, string key)
    {
        if (isHidingIndicators) return;

        if (pool == null || pool.Length == 0)
        {
            BuildPool();
        }

        bool canUseKeyedReuse = ShouldUseKeyedReuse(key);
        bool requiresDirectionalReuseCheck =
            canUseKeyedReuse && ReusePolicy == DamageIndicatorReusePolicy.ReuseVisibleIndicatorForSameKeyAndSimilarDirection;

        bool hasValidPlanarCameraForward = false;
        bool hasCameraPlanarData = false;
        Vector3 cameraPosition = Vector3.zero;
        Vector3 cameraForwardPlanar = Vector3.forward;

        if (requiresDirectionalReuseCheck)
        {
            hasValidPlanarCameraForward = DamageIndicatorUtility.TryGetPlanarCameraData(playerCamera, out cameraPosition, out cameraForwardPlanar);
            hasCameraPlanarData = true;
        }

        DamageIndicatorSlot slot = canUseKeyedReuse
            ? FindReusableVisibleSlot(
                key,
                position,
                hasValidPlanarCameraForward,
                cameraPosition,
                cameraForwardPlanar)
            : null;

        bool isReplayingExistingSlot = slot != null;

        if (!isReplayingExistingSlot)
        {
            slot = FindAvailableSlot();
        }

        if (slot == null)
        {
            return;
        }

        if (!hasCameraPlanarData)
        {
            hasValidPlanarCameraForward = DamageIndicatorUtility.TryGetPlanarCameraData(playerCamera, out cameraPosition, out cameraForwardPlanar);
            hasCameraPlanarData = true;
        }

        if (slot.active && !isReplayingExistingSlot)
        {
            DeactivateSlot(slot);
        }

        bool wasActiveBeforeActivation = slot.active;

        // Force replay behavior for reused slots so the animation restarts exactly like a fresh activation.
        if (isReplayingExistingSlot)
        {
            if (slot.indicator != null)
            {
                slot.indicator.Deactivate();
            }

            if (slot.rootObject != null)
            {
                slot.rootObject.SetActive(false);
            }
        }

        slot.active = true;
        slot.activationOrder = ++activationSequence;
        slot.reuseKey = canUseKeyedReuse ? key : null;
        slot.damagePosition = position;
        slot.color = color;

        slot.indicator.Activate(new DamageIndicatorActivationContext(
            playerCamera,
            cameraPosition,
            cameraForwardPlanar,
            hasValidPlanarCameraForward,
            position,
            color));

        if (slot.rootObject != null)
        {
            slot.rootObject.SetActive(true);
        }

        if (!wasActiveBeforeActivation)
        {
            activeIndicatorsCount++;
        }

        if (slot.reuseKey != null)
        {
            visibleIndicatorsByKey[slot.reuseKey] = slot;
        }
    }

    public void TriggerDamageSense(Vector3 position)
    {
        TriggerDamageSense(position, Color.white);
    }

    /// <summary>
    /// Deactivates all active indicators immediately.
    /// Useful for cleanup during scene transitions or when the player respawns.
    /// </summary>
    public void ClearAllIndicators()
    {
        if (pool == null)
        {
            return;
        }

        for (int i = 0; i < pool.Length; i++)
        {
            if (pool[i] != null && pool[i].active)
            {
                DeactivateSlot(pool[i]);
            }
        }
    }

    [ContextMenu("Rebuild Pool")]
    public void RebuildPool()
    {
        CleanupPoolInstances();
        BuildPool();
    }

    /// <summary>
    /// Sets the player camera transform used for indicator orientation. Usually your active player or gameplay camera.
    /// </summary>
    /// <param name="newCamera">The new camera transform to use for indicator orientation.</param>
    public static void SetPlayerCamera(Transform newCamera)
    {
        if (Instance != null)
        {
            Instance.playerCamera = newCamera;
        }
    }

    public void HideAllIndicators()
    {
        if (indicatorsParent != null)
        {
            indicatorsParent.gameObject.SetActive(false);
        }

        isHidingIndicators = true;
    }

    public void ShowAllIndicators()
    {
        if (indicatorsParent != null)
        {
            indicatorsParent.gameObject.SetActive(true);
        }

        isHidingIndicators = false;
    }

    public static DamageIndicatorGlobalSettings GetGlobalSettings()
    {
        return Instance.globalSettings;
    }

    private static DamageIndicator _instance;
    public static DamageIndicator Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<DamageIndicator>();
            }
            return _instance;
        }
    }
    #endregion

    #region Private Methods

    private DamageIndicatorSlot FindAvailableSlot()
    {
        if (pool == null)
        {
            return null;
        }

        bool recycleOldestWhenPoolIsFull = RecycleOldestWhenPoolIsFull;
        DamageIndicatorSlot oldest = null;
        int oldestOrder = int.MaxValue;

        for (int i = 0; i < pool.Length; i++)
        {
            DamageIndicatorSlot slot = pool[i];

            if (!slot.active)
            {
                return slot;
            }

            if (recycleOldestWhenPoolIsFull && slot.activationOrder < oldestOrder)
            {
                oldestOrder = slot.activationOrder;
                oldest = slot;
            }
        }

        return recycleOldestWhenPoolIsFull ? oldest : null;
    }

    private DamageIndicatorSlot FindReusableVisibleSlot(
        string key,
        Vector3 incomingDamagePosition,
        bool hasValidPlanarCameraForward,
        Vector3 cameraPosition,
        Vector3 cameraForwardPlanar)
    {
        if (!visibleIndicatorsByKey.TryGetValue(key, out DamageIndicatorSlot slot) || slot == null || !slot.active)
        {
            visibleIndicatorsByKey.Remove(key);
            return null;
        }

        if (ReusePolicy == DamageIndicatorReusePolicy.ReuseVisibleIndicatorForSameKeyAndSimilarDirection)
        {
            if (!hasValidPlanarCameraForward)
            {
                return null;
            }

            float currentAngle = DamageIndicatorUtility.CalculatePlanarSignedAngle(cameraPosition, cameraForwardPlanar, slot.damagePosition);
            float incomingAngle = DamageIndicatorUtility.CalculatePlanarSignedAngle(cameraPosition, cameraForwardPlanar, incomingDamagePosition);
            float angleDelta = Mathf.Abs(Mathf.DeltaAngle(currentAngle, incomingAngle));

            if (angleDelta > MaxReuseAngleDelta)
            {
                return null;
            }
        }

        return slot;
    }

    private bool ShouldUseKeyedReuse(string key)
    {
        return ReusePolicy != DamageIndicatorReusePolicy.Disabled && !string.IsNullOrWhiteSpace(key);
    }

    private void DeactivateSlot(DamageIndicatorSlot slot)
    {
        if (slot == null || !slot.active)
        {
            return;
        }

        slot.active = false;
        slot.activationOrder = 0;

        if (!string.IsNullOrEmpty(slot.reuseKey))
        {
            if (visibleIndicatorsByKey.TryGetValue(slot.reuseKey, out DamageIndicatorSlot mappedSlot) && mappedSlot == slot)
            {
                visibleIndicatorsByKey.Remove(slot.reuseKey);
            }

            slot.reuseKey = null;
        }

        activeIndicatorsCount = Mathf.Max(0, activeIndicatorsCount - 1);

        if (slot.indicator != null)
        {
            slot.indicator.Deactivate();
        }

        if (slot.rootObject != null)
        {
            slot.rootObject.SetActive(false);
        }
    }

    private void BuildPool()
    {
        if (indicatorPrefab == null)
        {
            Debug.LogError("DamageIndicator requires an indicatorPrefab to build the pool.", this);
            pool = null;
            return;
        }

        int poolSize = ResolvedPoolSize;
        pool = new DamageIndicatorSlot[poolSize];
        activationSequence = 0;
        activeIndicatorsCount = 0;
        visibleIndicatorsByKey.Clear();

        Transform poolParent = indicatorsParent != null ? indicatorsParent : transform;

        for (int i = 0; i < poolSize; i++)
        {
            GameObject go = Instantiate(indicatorPrefab, poolParent);

            RectTransform rect = go.GetComponent<RectTransform>();
            Image image = go.GetComponentInChildren<Image>();
            DamageIndicatorBase indicator = go.GetComponent<DamageIndicatorBase>();

            if (indicator == null)
            {
                indicator = go.AddComponent<ProceduralDamageIndicator>();
            }

            if (rect == null)
            {
                rect = go.GetComponentInChildren<RectTransform>();
            }

            if (image == null)
            {
                Debug.LogWarning("Damage indicator prefab has no Image component. Indicator visuals will not render correctly.", go);
            }

            indicator.Initialize(rect, image);

            pool[i] = new DamageIndicatorSlot
            {
                active = false,
                activationOrder = 0,
                reuseKey = null,
                damagePosition = Vector3.zero,
                color = Color.white,
                rootObject = go,
                rect = rect,
                image = image,
                indicator = indicator
            };

            go.SetActive(false);
        }
    }

    private void CleanupPoolInstances()
    {
        if (pool == null)
        {
            return;
        }

        for (int i = 0; i < pool.Length; i++)
        {
            DamageIndicatorSlot slot = pool[i];
            if (slot == null)
            {
                continue;
            }

            if (slot.indicator != null)
            {
                slot.indicator.Cleanup();
            }

            if (slot.rootObject != null)
            {
                Destroy(slot.rootObject);
            }
        }

        pool = null;
        activeIndicatorsCount = 0;
        visibleIndicatorsByKey.Clear();
    }
    #endregion
}