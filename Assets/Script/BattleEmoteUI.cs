using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public sealed class EmoteSlotView
{
    public Image icon;
    public TMP_Text label;
    public GameObject highlight;
    [NonSerialized] internal Vector3 baseScale;
}

[Serializable]
public sealed class EmoteDisplayView
{
    public GameObject root;
    public Image icon;
    public TMP_Text senderName;
    [Tooltip("감정표현 프리팹이 생성될 위치. 이 표시 Root 내부에 할당하세요. 비우면 Icon 위치를 사용합니다.")]
    public RectTransform prefabAnchor;
    [Tooltip("직접 할당한 UI에도 아래 Anchor/Position을 적용합니다. 끄면 직접 배치한 위치를 유지합니다.")]
    public bool overridePosition;
    public Vector2 anchor = new Vector2(.5f, 0f);
    public Vector2 position = new Vector2(0f, 340f);
    [NonSerialized] internal CanvasGroup group;
    [NonSerialized] internal Vector3 baseScale;
    [NonSerialized] internal bool generated;
    [NonSerialized] internal float startedAt = -1000f;
    [NonSerialized] internal GameObject prefabInstance;
    [NonSerialized] internal bool usesPrefab;
    [NonSerialized] internal float lifetime;
    [NonSerialized] internal Material originalNameMaterial, nameOutlineMaterial;
}

// Attach to an always-active Battle object, NOT inside one of the toggled UI roots.
[DefaultExecutionOrder(-300), DisallowMultipleComponent]
public sealed class BattleEmoteUI : MonoBehaviour
{
    [Header("Four equipped IDs from Resources/EmoteTable (future customization)")]
    [SerializeField, Min(1)] private int upEmoteId = 1;
    [SerializeField, Min(1)] private int rightEmoteId = 2;
    [SerializeField, Min(1)] private int downEmoteId = 3;
    [SerializeField, Min(1)] private int leftEmoteId = 4;

    [Header("Hold T / move mouse / release T to send")]
    [SerializeField] private KeyCode holdKey = KeyCode.T;
    [SerializeField, Min(.1f)] private float mouseSensitivity = 22f;
    [SerializeField, Min(1f)] private float selectionRadius = 140f;
    [SerializeField, Min(0f)] private float cancelDeadZone = 28f;

    [Header("Optional custom selection UI (empty = generated)")]
    [SerializeField] private GameObject wheelRoot;
    [SerializeField] private EmoteSlotView up = new EmoteSlotView();
    [SerializeField] private EmoteSlotView right = new EmoteSlotView();
    [SerializeField] private EmoteSlotView down = new EmoteSlotView();
    [SerializeField] private EmoteSlotView left = new EmoteSlotView();
    [Tooltip("선택 방향을 보여줄 가상 커서. Wheel Root 중앙 기준으로 움직이며 OS 커서는 열지 않습니다.")]
    [SerializeField] private RectTransform directionPointer;
    [SerializeField] private TMP_Text selectionText;

    [Header("Optional local / opponent display UI")]
    [SerializeField] private EmoteDisplayView localDisplay = new EmoteDisplayView();
    [SerializeField] private EmoteDisplayView opponentDisplay = new EmoteDisplayView
    { anchor = new Vector2(.5f, 1f), position = new Vector2(0f, -230f) };
    [SerializeField, Min(.1f)] private float displaySeconds = 2.5f;
    [SerializeField, Min(.01f)] private float fadeSeconds = .35f;
    [SerializeField, Min(.01f)] private float popSeconds = .12f;
    [SerializeField] private TMP_FontAsset font;
    [Tooltip("감정표현 사용자 이름의 검은 아웃라인 두께입니다.")]
    [SerializeField, Range(0f, 1f)] private float senderNameOutlineWidth = .2f;

    private static BattleEmoteUI active;
    private readonly EmoteInputGate gate = new EmoteInputGate();
    private readonly Sprite[] defaultIcons = new Sprite[4];
    private EmoteSlotView[] views;
    private Canvas generatedCanvas;
    private Player sampledPlayer;
    private bool uiInitialized;
    private int sampledFrame = -1;
    private float nextRequestAt;
    private EmoteTable Table => EmoteTable.Default;
    public static bool CapturesMouse
    { get { if (active == null) return false; active.SampleInput(); return active.gate.CapturesMouse; } }
    public static bool BlocksCombatInput
    { get { if (active == null) return false; active.SampleInput(); return active.gate.BlocksCombat; } }

    private void Awake()
    {
        if (font == null)
        {
            var hud = GetComponent<BattleHud>();
            if (hud != null) font = hud.FeedbackFont;
        }
        if (font == null) font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        ValidateRoot(ref wheelRoot);
        if (wheelRoot == null) BuildWheel();
        up ??= new EmoteSlotView(); right ??= new EmoteSlotView();
        down ??= new EmoteSlotView(); left ??= new EmoteSlotView();
        views = new[] { up, right, down, left };
        for (int i = 0; i < views.Length; i++)
        {
            EnsureSlot(views[i], i);
            views[i].baseScale = views[i].icon.transform.localScale;
        }
        if (selectionText == null)
            selectionText = Label("Selection", wheelRoot.transform, Vector2.zero, new Vector2(200,60), 20f);
        if (directionPointer == null)
        {
            directionPointer = Picture("DirectionPointer", wheelRoot.transform, Vector2.zero, new Vector2(10,10), Teal).rectTransform;
            directionPointer.localRotation = Quaternion.Euler(0,0,45);
        }
        ReadOnly(wheelRoot);
        localDisplay ??= new EmoteDisplayView();
        opponentDisplay ??= new EmoteDisplayView { anchor = new Vector2(.5f,1), position = new Vector2(0,-230) };
        EnsureDisplay(localDisplay, "MyEmote", Teal);
        EnsureDisplay(opponentDisplay, "OpponentEmote", new Color(1f,.35f,.45f));
        Show(wheelRoot, false);
        uiInitialized = true;
    }

    private void OnEnable()
    {
        // A failed Awake must not capture flight/combat input with an unusable wheel.
        if (!uiInitialized) return;
        active = this;
        gate.Reset(Input.GetKey(holdKey));
        sampledFrame = -1;
    }

    private void SampleInput()
    {
        if (sampledFrame == Time.frameCount) return;
        sampledFrame = Time.frameCount;
        Player player = Player.LocalPlayer;
        bool samePlayer = sampledPlayer == player;
        sampledPlayer = player;
        bool allowed = samePlayer && Application.isFocused && Cursor.lockState == CursorLockMode.Locked &&
            !CombatPresentation.MenuOpen && BattleManager.Instance != null && BattleManager.Instance.IsGameplayActive &&
            player != null && player.Object != null && player.Object.IsValid && player.Object.HasInputAuthority &&
            player.IsAlive && !player.IsReturningToMap && !Input.GetKey(KeyCode.Space) && !Input.GetKey(KeyCode.Tab);
        gate.Sample(allowed, Input.GetKey(holdKey), Input.GetKey(KeyCode.Escape),
            Input.GetAxisRaw("Mouse X") * mouseSensitivity, Input.GetAxisRaw("Mouse Y") * mouseSensitivity,
            Input.GetMouseButton(0), Input.GetMouseButton(1), cancelDeadZone, selectionRadius);
    }

    private void Update()
    {
        if (!uiInitialized) return;
        SampleInput();
        Show(wheelRoot, gate.Showing);
        if (gate.Showing) RefreshWheel();
        if (gate.Choice >= 0 && Time.unscaledTime >= nextRequestAt && Table != null &&
            sampledPlayer != null && sampledPlayer.RequestEmote(GetSlotEmote(gate.Choice)))
            nextRequestAt = Time.unscaledTime + Table.MinimumInterval;
        bool playing = BattleManager.Instance != null && BattleManager.Instance.IsGameplayActive;
        AnimateDisplay(localDisplay, playing);
        AnimateDisplay(opponentDisplay, playing);
    }

    // Stable slot numbers: 0=Up, 1=Right, 2=Down, 3=Left. Saving to an account can be added by its UI.
    public int GetSlotEmote(int slot) => slot == 0 ? upEmoteId : slot == 1 ? rightEmoteId :
        slot == 2 ? downEmoteId : slot == 3 ? leftEmoteId : 0;
    public bool SetSlotEmote(int slot, int emoteId)
    {
        if (slot < 0 || slot > 3 || Table == null || !Table.TryGet(emoteId, out _)) return false;
        switch ((EmoteDirection)slot)
        {
            case EmoteDirection.Up: upEmoteId = emoteId; break;
            case EmoteDirection.Right: rightEmoteId = emoteId; break;
            case EmoteDirection.Down: downEmoteId = emoteId; break;
            case EmoteDirection.Left: leftEmoteId = emoteId; break;
        }
        return true;
    }

    private void RefreshWheel()
    {
        for (int i = 0; i < views.Length; i++)
        {
            bool valid = Table != null && Table.TryGet(GetSlotEmote(i), out _);
            EmoteEntry entry = null;
            if (valid) Table.TryGet(GetSlotEmote(i), out entry);
            views[i].icon.sprite = valid ? Icon(entry) : null;
            views[i].icon.enabled = valid;
            views[i].icon.transform.localScale = views[i].baseScale * (valid && gate.SelectedSlot == i ? 1.12f : 1f);
            if (views[i].label != null) views[i].label.text = valid ? entry.displayName : "빈 슬롯";
            Show(views[i].highlight, valid && gate.SelectedSlot == i);
        }
        if (selectionText != null)
            selectionText.text = Time.unscaledTime < nextRequestAt ? "잠시 후 사용 가능" : gate.SelectedSlot < 0
                ? "중앙: 취소\nT를 떼면 확정" : "T를 떼면 확정";
        if (directionPointer != null)
        {
            // Convert the virtual cursor into the pointer parent's space for nested custom UI.
            Vector3 centre = wheelRoot.transform is RectTransform rect ? (Vector3)rect.rect.center : Vector3.zero;
            Vector3 world = wheelRoot.transform.TransformPoint(centre + new Vector3(gate.X, gate.Y, 0));
            directionPointer.position = world;
        }
    }

    public static void Present(Player sender, int emoteId)
    {
        if (active == null || sender == null || sender.Object == null || !sender.Object.IsValid ||
            BattleManager.Instance == null || !BattleManager.Instance.IsGameplayActive ||
            NetworkGameManager.Instance == null || sender.Runner != NetworkGameManager.Instance.Runner ||
            active.Table == null || !active.Table.TryGet(emoteId, out EmoteEntry entry)) return;
        bool mine = sender.Object.HasInputAuthority;
        var view = mine ? active.localDisplay : active.opponentDisplay;
        view.icon.sprite = active.Icon(entry);
        view.icon.enabled = true;
        var data = PlayerData.FindPerkData(sender.Runner, sender.Object.InputAuthority);
        if (view.senderName != null)
        {
            string name = data != null ? data.playerName.ToString() : mine ? "나" : "상대";
            view.senderName.richText = false;
            view.senderName.text = name + " · " + entry.displayName;
        }
        view.startedAt = Time.unscaledTime;
        Show(view.root, true);
        active.PlayDisplayPrefab(view, entry);
        active.AnimateDisplay(view, true);
    }

    private void PlayDisplayPrefab(EmoteDisplayView view, EmoteEntry entry)
    {
        ClearDisplayPrefab(view);
        view.lifetime = Mathf.Max(.1f, displaySeconds);
        GameObject prefab = entry.displayPrefab != null ? entry.displayPrefab : Table != null ? Table.DefaultDisplayPrefab : null;
        if (prefab == null) return;
        if (prefab.GetComponent<RectTransform>() == null ||
            prefab.GetComponentInChildren<Fusion.NetworkObject>(true) != null)
        {
            Debug.LogWarning("Emote display prefab must be local UI with a RectTransform and no NetworkObject.", prefab);
            return;
        }
        Transform anchor = view.prefabAnchor != null && view.prefabAnchor.IsChildOf(view.root.transform)
            ? view.prefabAnchor : view.icon.transform;
        GameObject instance = Instantiate(prefab, anchor, false);
        view.prefabInstance = instance;
        view.usesPrefab = true;
        view.icon.enabled = false;
        instance.SetActive(true);
        ReadOnly(instance);
        float animationSeconds = 0f;
        string animationName = entry.animationName != null ? entry.animationName.Trim() : string.Empty;
        bool namedAnimation = animationName.Length > 0;
        bool matchedAnimation = false;
        foreach (Animator animator in instance.GetComponentsInChildren<Animator>(true))
        {
            if (!animator.isActiveAndEnabled || animator.runtimeAnimatorController == null) continue;
            animator.updateMode = AnimatorUpdateMode.UnscaledTime;
            animator.Rebind();
            animator.Update(0f);
            if (namedAnimation)
            {
                if (TryPlayNamedAnimator(animator, animationName, out float seconds))
                {
                    matchedAnimation = true;
                    animationSeconds = Mathf.Max(animationSeconds, seconds);
                }
                continue;
            }
            for (int layer = 0; layer < animator.layerCount; layer++)
            {
                AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(layer);
                float speed = Mathf.Abs(animator.speed * state.speed * state.speedMultiplier);
                if (speed > .0001f) animationSeconds = Mathf.Max(animationSeconds, state.length / speed);
            }
        }
        foreach (Animation animation in instance.GetComponentsInChildren<Animation>(true))
        {
            if (!animation.isActiveAndEnabled) continue;
            string clipName = namedAnimation ? animationName : animation.clip != null ? animation.clip.name : null;
            AnimationState state = clipName != null ? animation[clipName] : null;
            if (state == null) continue;
            animation.Stop();
            state.time = 0f;
            matchedAnimation |= animation.Play(clipName);
            float speed = Mathf.Abs(state.speed);
            if (speed > .0001f) animationSeconds = Mathf.Max(animationSeconds, state.length / speed);
        }
        if (namedAnimation && !matchedAnimation)
        {
            Debug.LogWarning($"Emote {entry.id}: animation '{animationName}' was not found in '{prefab.name}'. Showing the icon instead.", prefab);
            ClearDisplayPrefab(view);
            view.icon.enabled = true;
            return;
        }
        view.lifetime = entry.prefabDisplaySeconds > 0f ? entry.prefabDisplaySeconds :
            animationSeconds > 0f ? animationSeconds : Mathf.Max(.1f, displaySeconds);
    }

    private static bool TryPlayNamedAnimator(Animator animator, string animationName, out float seconds)
    {
        seconds = 0f;
        for (int layer = 0; layer < animator.layerCount; layer++)
        {
            int hash = Animator.StringToHash(animationName);
            if (!animator.HasState(layer, hash))
            {
                hash = Animator.StringToHash(animator.GetLayerName(layer) + "." + animationName);
                if (!animator.HasState(layer, hash)) continue;
            }
            animator.Play(hash, layer, 0f);
            animator.Update(0f);
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(layer);
            float speed = Mathf.Abs(animator.speed * state.speed * state.speedMultiplier);
            seconds = speed > .0001f ? state.length / speed : 0f;
            return true;
        }
        return false;
    }

    private static void ClearDisplayPrefab(EmoteDisplayView view)
    {
        if (view == null) return;
        if (view.prefabInstance != null) { view.prefabInstance.SetActive(false); Destroy(view.prefabInstance); }
        view.prefabInstance = null;
        view.usesPrefab = false;
    }

    private void AnimateDisplay(EmoteDisplayView view, bool playing)
    {
        float age = Time.unscaledTime - view.startedAt;
        if (!playing) view.startedAt = -1000f;
        float duration = view.usesPrefab ? Mathf.Max(.1f, view.lifetime) : Mathf.Max(.1f, displaySeconds);
        bool visible = playing && age >= 0f && age < duration;
        Show(view.root, visible);
        if (!visible) { view.startedAt = -1000f; ClearDisplayPrefab(view); view.root.transform.localScale = view.baseScale; return; }
        ApplyPosition(view);
        if (view.usesPrefab)
        {
            view.group.alpha = 1f;
            view.root.transform.localScale = view.baseScale;
            return; // The authored prefab owns its entrance/exit animation.
        }
        float fade = Mathf.Min(Mathf.Max(.01f, fadeSeconds), Mathf.Max(.1f, displaySeconds));
        view.group.alpha = Mathf.Clamp01((displaySeconds - age) / fade);
        float pop = Mathf.Clamp01(age / Mathf.Max(.01f, popSeconds));
        view.root.transform.localScale = view.baseScale * Mathf.Lerp(.8f, 1f, 1f - (1f-pop)*(1f-pop));
    }

    private Sprite Icon(EmoteEntry entry)
    {
        if (entry.icon != null) return entry.icon;
        int index = Mathf.Clamp((int)entry.fallbackFace, 0, defaultIcons.Length-1);
        if (defaultIcons[index] == null) defaultIcons[index] = EmoteFallbackIcons.Create((DefaultEmoteFace)index);
        return defaultIcons[index];
    }

    private void OnApplicationFocus(bool focus)
    {
        if (focus) return;
        gate.Reset(true);
        sampledFrame = -1;
        Show(wheelRoot, false);
    }
    private void OnDisable()
    {
        if (active == this) active = null;
        gate.Reset();
        Show(wheelRoot, false);
        Show(localDisplay?.root, false); Show(opponentDisplay?.root, false);
        ClearDisplayPrefab(localDisplay); ClearDisplayPrefab(opponentDisplay);
        if (localDisplay != null) localDisplay.startedAt = -1000f;
        if (opponentDisplay != null) opponentDisplay.startedAt = -1000f;
    }
    private void OnDestroy()
    {
        ReleaseNameOutline(localDisplay);
        ReleaseNameOutline(opponentDisplay);
        if (generatedCanvas != null) Destroy(generatedCanvas.gameObject);
        foreach (Sprite icon in defaultIcons)
            if (icon != null) { Destroy(icon.texture); Destroy(icon); }
    }

    private static readonly Color Teal = new Color(.22f,.9f,.85f);
    private static void Show(GameObject root, bool value)
    { if (root != null && root.activeSelf != value) root.SetActive(value); }
    private void ValidateRoot(ref GameObject root)
    {
        if (root == null || !transform.IsChildOf(root.transform)) return;
        Debug.LogWarning("BattleEmoteUI: UI Root에는 컨트롤러 자신/부모가 아닌 별도 UI 오브젝트를 넣으세요. 기본 UI를 사용합니다.", this);
        root = null;
    }
    private static CanvasGroup ReadOnly(GameObject root)
    {
        // Unity can return a managed "fake null" for a missing native component.
        // ?? bypasses UnityEngine.Object's null comparison and skips AddComponent.
        var group = root.GetComponent<CanvasGroup>();
        if (group == null) group = root.AddComponent<CanvasGroup>();
        group.interactable = false; group.blocksRaycasts = false;
        foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
        return group;
    }
    private RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = anchor;
        rect.anchoredPosition = position; rect.sizeDelta = size;
        return rect;
    }
    private Transform CanvasRoot()
    {
        if (generatedCanvas != null) return generatedCanvas.transform;
        var go = new GameObject("GeneratedEmoteHUD", typeof(Canvas), typeof(CanvasScaler));
        generatedCanvas = go.GetComponent<Canvas>();
        generatedCanvas.renderMode = RenderMode.ScreenSpaceOverlay; generatedCanvas.sortingOrder = 87;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920,1080); scaler.matchWidthOrHeight = .5f;
        return go.transform; // No GraphicRaycaster; selection uses relative mouse movement.
    }
    private Image Picture(string name, Transform parent, Vector2 position, Vector2 size, Color color)
    {
        var image = Rect(name,parent,new Vector2(.5f,.5f),position,size).gameObject.AddComponent<Image>();
        image.color = color; image.raycastTarget = false; image.preserveAspect = true;
        return image;
    }
    private TMP_Text Label(string name, Transform parent, Vector2 position, Vector2 size, float fontSize)
    {
        var text = Rect(name,parent,new Vector2(.5f,.5f),position,size).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font; text.fontSize = fontSize; text.alignment = TextAlignmentOptions.Center;
        text.enableAutoSizing = true; text.fontSizeMin = fontSize*.7f; text.fontSizeMax = fontSize;
        text.color = Color.white; text.raycastTarget = false; text.richText = false;
        return text;
    }
    private void BuildWheel()
    {
        wheelRoot = Rect("EmoteWheel",CanvasRoot(),new Vector2(.5f,.5f),Vector2.zero,new Vector2(500,500)).gameObject;
        Label("Hint",wheelRoot.transform,new Vector2(0,-270),new Vector2(500,35),19).text = "T 유지 · 마우스로 선택 · 떼면 감정표현";
    }
    private void EnsureSlot(EmoteSlotView view, int index)
    {
        if (view.icon != null) return;
        Vector2 direction = index == 0 ? Vector2.up : index == 1 ? Vector2.right : index == 2 ? Vector2.down : Vector2.left;
        var root = Picture(((EmoteDirection)index).ToString(),wheelRoot.transform,direction*174f,new Vector2(124,124),new Color(.025f,.065f,.1f,.88f));
        if (view.highlight == null)
        {
            var highlight = Picture("Highlight",root.transform,Vector2.zero,new Vector2(124,124),new Color(.12f,.7f,.68f,.8f));
            view.highlight = highlight.gameObject;
        }
        view.icon = Picture("Icon",root.transform,new Vector2(0,12),new Vector2(78,78),Color.white);
        if (view.label == null) view.label = Label("Name",root.transform,new Vector2(0,-42),new Vector2(120,28),20);
    }
    private void EnsureDisplay(EmoteDisplayView view, string name, Color accent)
    {
        ValidateRoot(ref view.root);
        // Display roots cannot hide the wheel or each other; use separate sibling panels.
        if (view.root != null && (wheelRoot.transform.IsChildOf(view.root.transform) ||
            view.root.transform.IsChildOf(wheelRoot.transform) || (view != localDisplay && localDisplay.root != null &&
            (view.root.transform.IsChildOf(localDisplay.root.transform) || localDisplay.root.transform.IsChildOf(view.root.transform)))))
        { Debug.LogWarning("BattleEmoteUI: 표시 Root는 선택창/다른 표시 Root와 별도의 형제 오브젝트여야 합니다.",this); view.root = null; }
        view.generated = view.root == null;
        if (view.generated)
        {
            view.root = Rect(name,CanvasRoot(),view.anchor,view.position,new Vector2(240,146)).gameObject;
            Picture("Background",view.root.transform,Vector2.zero,new Vector2(240,146),new Color(.025f,.065f,.1f,.8f));
            Picture("Accent",view.root.transform,new Vector2(0,-71),new Vector2(240,3),accent);
            view.icon = null; view.senderName = null;
        }
        if (view.icon == null) view.icon = Picture("Icon",view.root.transform,new Vector2(0,12),new Vector2(96,96),Color.white);
        if (view.senderName == null) view.senderName = Label("Sender",view.root.transform,new Vector2(0,-52),new Vector2(230,32),18);
        ApplyNameOutline(view);
        view.baseScale = view.root.transform.localScale;
        view.group = ReadOnly(view.root);
        ApplyPosition(view);
        Show(view.root, false);
    }

    private void ApplyNameOutline(EmoteDisplayView view)
    {
        if (view.senderName == null || view.nameOutlineMaterial != null) return;
        Material source = view.senderName.fontSharedMaterial;
        if (source == null || !source.HasProperty(ShaderUtilities.ID_OutlineWidth)) return;
        view.originalNameMaterial = source;
        view.nameOutlineMaterial = new Material(source) { name = "Emote sender outline (local)" };
        view.nameOutlineMaterial.SetColor(ShaderUtilities.ID_OutlineColor, Color.black);
        view.nameOutlineMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, Mathf.Clamp01(senderNameOutlineWidth));
        view.nameOutlineMaterial.EnableKeyword("OUTLINE_ON");
        view.senderName.fontSharedMaterial = view.nameOutlineMaterial;
        view.senderName.extraPadding = true;
        view.senderName.UpdateMeshPadding();
    }

    private static void ReleaseNameOutline(EmoteDisplayView view)
    {
        if (view == null || view.nameOutlineMaterial == null) return;
        if (view.senderName != null && view.senderName.fontSharedMaterial == view.nameOutlineMaterial)
        {
            view.senderName.fontSharedMaterial = view.originalNameMaterial;
            view.senderName.UpdateMeshPadding();
        }
        Destroy(view.nameOutlineMaterial);
        view.nameOutlineMaterial = null;
        view.originalNameMaterial = null;
    }
    private static void ApplyPosition(EmoteDisplayView view)
    {
        if ((!view.generated && !view.overridePosition) || !(view.root.transform is RectTransform rect)) return;
        rect.anchorMin = rect.anchorMax = view.anchor;
        rect.anchoredPosition = view.position;
    }
}
