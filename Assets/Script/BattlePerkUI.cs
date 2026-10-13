using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public sealed class PerkCardView
{
    public Image icon;
    public TMP_Text title;
    public TMP_Text description;
    public TMP_Text hint;
}
[Serializable]
public sealed class PerkPlayerView
{
    public TMP_Text playerName;
    public Image profile;
    public TMP_Text slot1Text, slot2Text;
    public Image slot1Icon, slot2Icon;
    [Tooltip("Four acquired effects. A mastered slot shows both stage 1 and stage 2.")]
    public PerkCardView[] effects = new PerkCardView[4];
}

// Keep this on the always-active BattleManager, never inside a managed UI root.
[DefaultExecutionOrder(-200), DisallowMultipleComponent]
public sealed class BattlePerkUI : MonoBehaviour
{
    [Header("Shared style / icons (empty = project defaults)")]
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private MagicStatTable magicTable;
    [SerializeField] private ProfileImageTable profileTable;
    [Header("XP below HP (empty = generated bar)")]
    [SerializeField] private Slider experienceSlider;
    [SerializeField] private TMP_Text experienceText;
    [Tooltip("Optional HP bar RectTransform. The generated XP bar follows its lower edge without changing the HP prefab.")]
    [SerializeField] private RectTransform experienceAnchor;
    [Header("Hold Space: compact cards beside MP (empty root = generated)")]
    [SerializeField] private GameObject selectionRoot;
    [SerializeField] private PerkCardView leftCandidate = new();
    [SerializeField] private PerkCardView rightCandidate = new();
    [SerializeField] private TMP_Text availableChoicesText;
    [Header("Acquired icon feedback (empty root = generated)")]
    [SerializeField] private GameObject acquiredRoot;
    [SerializeField] private Image acquiredIcon;
    [SerializeField] private TMP_Text acquiredText;
    [Header("Hold Tab: read-only comparison (empty root = generated)")]
    [SerializeField] private GameObject comparisonRoot;
    [SerializeField] private PerkPlayerView ownView = new();
    [SerializeField] private PerkPlayerView opponentView = new();
    [Header("Presentation only - never delays input")]
    [SerializeField, Min(0f)] private float openSeconds = .12f;
    [SerializeField, Min(0f)] private float collapseSeconds = .18f;
    [SerializeField, Min(0f)] private float iconHoldSeconds = .35f;
    [SerializeField, Min(0f)] private float fadeSeconds = .2f;

    private static BattlePerkUI active;
    private readonly PerkInputGate gate = new();
    private int inputFrame = -1, requestedRevision = -1, awaitedRevision = -1, requestChoiceFrame = -1;
    private PlayerData local;
    private Canvas canvas;
    private RectTransform generatedExperience;
    private readonly Vector3[] hpCorners = new Vector3[4];
    private CanvasGroup selectionGroup, acquiredGroup;
    private float openedAt, feedbackAt = -100f, requestedAt;
    private bool wasShowing, requestPending;
    private string selectionKey, comparisonKey;
    public static bool BlocksCombatInput
    {
        get { if (active == null) return false; active.SampleInput(); return active.gate.BlocksCombat; }
    }
    private bool Allowed => Application.isFocused && !CombatPresentation.MenuOpen && BattleManager.Instance != null &&
        (BattleManager.Instance.Phase == BattleStartPhase.Playing || BattleManager.Instance.Phase == BattleStartPhase.Countdown);
    private void SampleInput()
    {
        if (inputFrame == Time.frameCount) return;
        inputFrame = Time.frameCount;
        gate.Sample(Allowed,Input.GetKey(KeyCode.Space),Input.GetMouseButton(0),Input.GetMouseButton(1));
    }
    private void Awake()
    {
        active = this;
        if (font == null) font = GetComponent<BattleHud>()?.FeedbackFont;
        if (font == null) font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (magicTable == null) magicTable = MagicStatTable.Default;
        if (profileTable == null) profileTable = ProfileImageTable.Default;
        ValidateRoot(ref selectionRoot); ValidateRoot(ref comparisonRoot); ValidateRoot(ref acquiredRoot);
        if (selectionRoot == null) BuildSelection();
        if (comparisonRoot == null) BuildComparison();
        if (acquiredRoot == null) BuildAcquired();
        if (experienceSlider == null) BuildExperience();
        experienceSlider.interactable = false;
        experienceSlider.minValue = 0f; experienceSlider.maxValue = 1f; experienceSlider.wholeNumbers = false;
        selectionGroup = Group(selectionRoot); acquiredGroup = Group(acquiredRoot);
        ReadOnly(selectionRoot); ReadOnly(comparisonRoot); ReadOnly(acquiredRoot);
        foreach (var graphic in experienceSlider.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
        Show(selectionRoot,false); Show(comparisonRoot,false); Show(acquiredRoot,false);
    }
    private void ValidateRoot(ref GameObject root)
    {
        if (root == null || !transform.IsChildOf(root.transform)) return;
        Debug.LogWarning("BattlePerkUI: UI root must not contain its controller. Using generated UI.",this); root = null;
    }
    private static CanvasGroup Group(GameObject root)
    {
        var group = root.GetComponent<CanvasGroup>();
        if (group == null) group = root.AddComponent<CanvasGroup>();
        group.interactable = false; group.blocksRaycasts = false; return group;
    }
    private static void ReadOnly(GameObject root)
    {
        Group(root);
        foreach (var graphic in root.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
    }
    private bool Ready(PlayerData data) => data != null && data.Object != null && data.Object.IsValid && data.PerksInitialized;
    private void Update()
    {
        SampleInput();
        if (local != PlayerData.Local)
        {
            if (local != null) local.PerkSelectionResult -= OnResult;
            local = PlayerData.Local;
            if (local != null) local.PerkSelectionResult += OnResult;
            requestPending = false; awaitedRevision = -1; selectionKey = comparisonKey = null;
        }
        if (Ready(local) && awaitedRevision >= 0 && local.PerkRevision >= awaitedRevision)
        { requestPending = false; awaitedRevision = -1; }
        // No retry/queued input on timeout. A fresh click can safely retry with the current revision.
        if (requestPending && Time.unscaledTime-requestedAt > 3f) { requestPending = false; awaitedRevision = -1; }
        if (gate.Showing && gate.Choice >= 0) Choose(gate.Choice == 1);
        bool cards = gate.Showing && Ready(local);
        if (cards && !wasShowing) openedAt = Time.unscaledTime;
        wasShowing = cards;
        Show(selectionRoot,cards); // Space release hides immediately, regardless of animation.
        if (cards)
        {
            UpdateCandidates();
            float t = openSeconds <= 0 ? 1 : Mathf.Clamp01((Time.unscaledTime-openedAt)/openSeconds);
            selectionGroup.alpha = Mathf.Lerp(.4f,1f,t);
            float collapse = collapseSeconds <= 0 ? 1 : Mathf.Clamp01((Time.unscaledTime-feedbackAt)/collapseSeconds);
            selectionRoot.transform.localScale = Vector3.one * Mathf.Lerp(.92f,1f,Mathf.Min(t,collapse));
        }
        bool compare = Allowed && !gate.Showing && Input.GetKey(KeyCode.Tab);
        Show(comparisonRoot,compare);
        if (compare) UpdateComparison();
        UpdateExperience(); UpdateFeedback();
    }
    // Optional custom UI events. They obey the same held-Space, phase and request guards.
    public void ChooseLeft() => Choose(false);
    public void ChooseRight() => Choose(true);
    private void Choose(bool right)
    {
        SampleInput();
        if (!gate.Showing || gate.Choice != (right ? 1 : 0) || requestChoiceFrame == Time.frameCount || requestPending ||
            !Ready(local) || !local.CanSelectPerks) return;
        requestChoiceFrame = Time.frameCount; requestedRevision = local.PerkRevision;
        requestPending = true; requestedAt = Time.unscaledTime;
        if (!local.RequestPerk(right)) requestPending = false;
    }
    private void OnResult(bool accepted, PerkId id, int revision)
    {
        if (accepted)
        {
            awaitedRevision = revision; feedbackAt = Time.unscaledTime;
            Sprite sprite = Icon(id); SetIcon(acquiredIcon,sprite);
            Text(acquiredText,local.PerkTable.Get(id)?.displayName ?? id.ToString());
        }
        else { requestPending = false; awaitedRevision = -1; }
        selectionKey = null;
    }
    private void UpdateCandidates()
    {
        string key = $"{local.PerkRevision}/{local.PerkChoicesEarned}/{local.magic1}/{local.magic2}/{requestPending}";
        if (selectionKey == key) return;
        selectionKey = key;
        Fill(leftCandidate,local.GetPerkCandidate(false),"좌클릭 · 슬롯 1",local);
        Fill(rightCandidate,local.GetPerkCandidate(true),"우클릭 · 슬롯 2",local);
        Text(availableChoicesText,requestPending ? "선택 확인 중…" : local.PendingPerkChoices > 0 ? $"특전 선택 가능 {local.PendingPerkChoices}회" : "다음 경험치를 모아주세요");
    }
    private void Fill(PerkCardView view, PerkId id, string hint, PlayerData data)
    {
        if (view == null) return;
        var p = data != null ? data.PerkTable.Get(id) : null;
        SetIcon(view.icon,Icon(id)); Text(view.title,p?.displayName ?? "—");
        Text(view.description,p != null ? Description(p) : "선택 가능한 특전이 없습니다.");
        Text(view.hint,PerkRules.Common(id) ? hint.Replace("슬롯 1","공용 증강").Replace("슬롯 2","공용 증강") : hint);
    }
    public static string Description(PerkDefinition p) => (p.description ?? "")
        .Replace("{amount}",p.amount.ToString("0.##")).Replace("{seconds}",p.seconds.ToString("0.##"))
        .Replace("{percent}",(p.amount*100f).ToString("0.#"))
        .Replace("{secondaryPercent}",(p.secondaryAmount*100f).ToString("0.#"));
    private Sprite Icon(PerkId id)
    {
        var p = (Ready(local) ? local.PerkTable : PerkStatTable.Default).Get(id);
        if (p?.icon != null) return p.icon;
        if (magicTable != null)
            for (int i=1;i<=12;i++) if (PerkRules.Spell((MagicType)i,1)==id || PerkRules.Spell((MagicType)i,2)==id) return magicTable.GetIcon((MagicType)i);
        return null;
    }
    private void UpdateExperience()
    {
        bool ready = Ready(local);
        bool max = ready && local.PerkChoicesEarned >= PerkRules.MaxChoices;
        int needed = ready ? local.PerkTable.RequiredXp(local.PerkChoicesEarned) : 1;
        experienceSlider.SetValueWithoutNotify(max ? 1f : ready ? Mathf.Clamp01((float)local.PerkXp/needed) : 0f);
        Text(experienceText,!ready ? "" : max ? "특전 MAX" : $"XP {local.PerkXp}/{needed}  ·  선택 {local.PendingPerkChoices}");
    }
    private void LateUpdate()
    {
        if (generatedExperience == null || experienceAnchor == null || canvas == null) return;
        Show(generatedExperience.gameObject, experienceAnchor.gameObject.activeInHierarchy);
        experienceAnchor.GetWorldCorners(hpCorners);
        Canvas source = experienceAnchor.GetComponentInParent<Canvas>();
        Camera camera = source != null && source.renderMode != RenderMode.ScreenSpaceOverlay ? source.worldCamera : null;
        Vector2 a = RectTransformUtility.WorldToScreenPoint(camera,hpCorners[0]);
        Vector2 b = RectTransformUtility.WorldToScreenPoint(camera,hpCorners[3]);
        var root=(RectTransform)canvas.transform;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(root,a,null,out Vector2 left);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(root,b,null,out Vector2 right);
        generatedExperience.anchorMin=generatedExperience.anchorMax=new Vector2(.5f,.5f);
        generatedExperience.anchoredPosition=(left+right)*.5f+new Vector2(0,-9);
        generatedExperience.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,Mathf.Max(20,Mathf.Abs(right.x-left.x)));
    }
    private void UpdateFeedback()
    {
        float elapsed = Time.unscaledTime-feedbackAt;
        float hold = collapseSeconds+iconHoldSeconds;
        bool show = Allowed && elapsed >= 0 && elapsed < hold+fadeSeconds;
        Show(acquiredRoot,show);
        if (!show) return;
        acquiredGroup.alpha = elapsed < hold ? 1f : 1f-Mathf.Clamp01((elapsed-hold)/Mathf.Max(.001f,fadeSeconds));
        acquiredRoot.transform.localScale = Vector3.one * Mathf.Lerp(1.15f,1f,Mathf.Clamp01(elapsed/Mathf.Max(.001f,collapseSeconds)));
    }
    private void UpdateComparison()
    {
        PlayerData opponent = null;
        if (Ready(local)) foreach (var data in PlayerData.PerkPlayers)
            if (data != local && data != null && data.Object != null && data.Object.IsValid && data.Runner == local.Runner && data.teamIndex != local.teamIndex)
            { opponent = data; break; }
        string key = SnapshotKey(local)+"|"+SnapshotKey(opponent);
        if (comparisonKey == key) return;
        comparisonKey = key; FillPlayer(ownView,local); FillPlayer(opponentView,opponent);
    }
    private string SnapshotKey(PlayerData d) => !Ready(d) ? "loading" : $"{d.Object.Id}/{d.PerkRevision}/{d.magic1}/{d.magic2}/{d.playerName}/{d.playerprofile}";
    private void FillPlayer(PerkPlayerView view, PlayerData d)
    {
        if (view == null) return;
        bool ready = Ready(d);
        Text(view.playerName,ready ? d.playerName.ToString() : "정보를 불러오는 중…");
        SetIcon(view.profile,ready && profileTable != null ? profileTable.GetSprite(d.playerprofile) : null);
        Text(view.slot1Text,ready ? SlotText(d.magic1,d.PerkSlot1Level) : "—");
        Text(view.slot2Text,ready ? SlotText(d.magic2,d.PerkSlot2Level) : "—");
        SetIcon(view.slot1Icon,ready && magicTable != null ? magicTable.GetIcon(d.magic1) : null);
        SetIcon(view.slot2Icon,ready && magicTable != null ? magicTable.GetIcon(d.magic2) : null);
        int n=0;
        if (ready)
        {
            for (int slot=1;slot<=2;slot++) for (int level=1;level<=d.PerkLevel(slot);level++)
                FillRow(view,ref n,PerkRules.Spell(slot==1?d.magic1:d.magic2,level),$"슬롯 {slot} · {level}단계",d);
            for (int i=25;i<=34;i++) if(d.HasCommonPerk((PerkId)i)) FillRow(view,ref n,(PerkId)i,"공용 증강",d);
        }
        if(view.effects != null) for(;n<view.effects.Length;n++)
        {
            var row=view.effects[n]; if(row==null)continue;
            SetIcon(row.icon,null); Text(row.title,"—"); Text(row.description,ready?"아직 획득하지 않음":"정보 대기 중"); Text(row.hint,"");
        }
    }
    private void FillRow(PerkPlayerView view,ref int n,PerkId id,string hint,PlayerData data)
    { if(view.effects!=null && n<view.effects.Length) Fill(view.effects[n],id,hint,data); n++; }
    private string SlotText(MagicType magic,int level) => $"{(magicTable != null ? magicTable.GetStats(magic).displayName : magic.ToString())} · {level}/2";
    private static void Text(TMP_Text t,string s) { if(t!=null && t.text!=s)t.text=s; }
    private static void SetIcon(Image i,Sprite s) { if(i==null)return; i.sprite=s; i.enabled=s!=null; }
    private static void Show(GameObject root,bool show) { if(root!=null && root.activeSelf!=show)root.SetActive(show); }
    private void OnApplicationFocus(bool focus) { if(!focus){Show(selectionRoot,false);Show(comparisonRoot,false);Show(acquiredRoot,false);} }
    private void OnDisable()
    {
        if(active==this)active=null;
        Show(selectionRoot,false);Show(comparisonRoot,false);Show(acquiredRoot,false);
    }
    private void OnEnable() => active=this;
    private void OnDestroy()
    {
        if(local!=null)local.PerkSelectionResult-=OnResult;
        if(canvas!=null)Destroy(canvas.gameObject);
    }
    private RectTransform Rect(string name,Transform parent,Vector2 anchor,Vector2 position,Vector2 size)
    {
        var go=new GameObject(name,typeof(RectTransform)); var r=(RectTransform)go.transform;
        r.SetParent(parent,false);r.anchorMin=r.anchorMax=anchor;r.anchoredPosition=position;r.sizeDelta=size;return r;
    }
    private Transform CanvasRoot()
    {
        if(canvas!=null)return canvas.transform;
        var go=new GameObject("GeneratedPerkHUD",typeof(Canvas),typeof(CanvasScaler));
        canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=85;
        var scaler=go.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
        return go.transform; // No raycaster: this UI never captures the flight cursor.
    }
    private Image Image(string name,Transform parent,Vector2 position,Vector2 size,Color color)
    {
        var i=Rect(name,parent,new Vector2(.5f,.5f),position,size).gameObject.AddComponent<Image>();
        i.color=color;i.raycastTarget=false;i.preserveAspect=true;return i;
    }
    private TMP_Text Label(string name,Transform parent,Vector2 position,Vector2 size,float sizeFont,TextAlignmentOptions align=TextAlignmentOptions.Left)
    {
        var t=Rect(name,parent,new Vector2(.5f,.5f),position,size).gameObject.AddComponent<TextMeshProUGUI>();
        t.font=font;t.fontSize=sizeFont;t.color=Color.white;t.alignment=align;t.raycastTarget=false;
        t.enableAutoSizing=true;t.fontSizeMin=sizeFont*.75f;t.fontSizeMax=sizeFont;return t;
    }
    private PerkCardView Card(Transform parent,string name,Vector2 position,Vector2 size)
    {
        var root=Rect(name,parent,new Vector2(.5f,.5f),position,size);
        var bg=root.gameObject.AddComponent<Image>();bg.color=new Color(.035f,.075f,.10f,.88f);bg.raycastTarget=false;
        Image("Accent",root,new Vector2(-size.x/2+2,0),new Vector2(3,size.y),new Color(.24f,.9f,.86f));
        return new PerkCardView {
            icon=Image("Icon",root,new Vector2(-size.x/2+42,15),new Vector2(52,52),Color.white),
            title=Label("Title",root,new Vector2(34,size.y/2-28),new Vector2(size.x-95,30),22),
            description=Label("Description",root,new Vector2(34,-9),new Vector2(size.x-95,size.y-67),17),
            hint=Label("Hint",root,new Vector2(0,-size.y/2+14),new Vector2(size.x-24,22),14,TextAlignmentOptions.Right)
        };
    }
    private void BuildSelection()
    {
        selectionRoot=Rect("PerkSelection",CanvasRoot(),new Vector2(.5f,0),new Vector2(0,210),new Vector2(1080,160)).gameObject;
        leftCandidate=Card(selectionRoot.transform,"Left",new Vector2(-330,0),new Vector2(360,146));
        rightCandidate=Card(selectionRoot.transform,"Right",new Vector2(330,0),new Vector2(360,146));
        availableChoicesText=Label("Choices",selectionRoot.transform,new Vector2(0,85),new Vector2(280,28),17,TextAlignmentOptions.Center);
    }
    private void BuildAcquired()
    {
        acquiredRoot=Rect("AcquiredPerk",CanvasRoot(),new Vector2(.5f,0),new Vector2(0,325),new Vector2(260,90)).gameObject;
        acquiredIcon=Image("Icon",acquiredRoot.transform,new Vector2(0,15),new Vector2(48,48),Color.white);
        acquiredText=Label("Name",acquiredRoot.transform,new Vector2(0,-26),new Vector2(260,28),19,TextAlignmentOptions.Center);
    }
    private void BuildExperience()
    {
        var root=Rect("PerkXP",CanvasRoot(),new Vector2(0,0),new Vector2(322,45),new Vector2(336,8));
        generatedExperience=root;
        var bg=root.gameObject.AddComponent<Image>();bg.color=new Color(.07f,.13f,.17f,.9f);bg.raycastTarget=false;
        var fill=Image("Fill",root,Vector2.zero,root.sizeDelta,new Color(.24f,.9f,.86f));
        fill.rectTransform.anchorMin=Vector2.zero; fill.rectTransform.anchorMax=Vector2.one;
        fill.rectTransform.sizeDelta=Vector2.zero;
        experienceSlider=root.gameObject.AddComponent<Slider>();experienceSlider.fillRect=fill.rectTransform;experienceSlider.targetGraphic=fill;
        experienceText=Label("XPText",root,new Vector2(0,-17),new Vector2(336,22),14);
    }
    private void BuildComparison()
    {
        comparisonRoot=Rect("PerkComparison",CanvasRoot(),new Vector2(.5f,.5f),Vector2.zero,new Vector2(1920,1080)).gameObject;
        ownView=PlayerPanel("Own",new Vector2(-660,20)); opponentView=PlayerPanel("Opponent",new Vector2(660,20));
    }
    private PerkPlayerView PlayerPanel(string name,Vector2 position)
    {
        var root=Rect(name,comparisonRoot.transform,new Vector2(.5f,.5f),position,new Vector2(440,590));
        var bg=root.gameObject.AddComponent<Image>();bg.color=new Color(.03f,.055f,.08f,.86f);bg.raycastTarget=false;
        var v=new PerkPlayerView();
        v.profile=Image("Profile",root,new Vector2(-171,252),new Vector2(52,52),Color.white);
        v.playerName=Label("Name",root,new Vector2(29,252),new Vector2(325,42),24);
        v.slot1Icon=Image("Slot1Icon",root,new Vector2(-185,197),new Vector2(36,36),Color.white);
        v.slot1Text=Label("Slot1",root,new Vector2(20,197),new Vector2(345,32),20);
        v.slot2Icon=Image("Slot2Icon",root,new Vector2(-185,153),new Vector2(36,36),Color.white);
        v.slot2Text=Label("Slot2",root,new Vector2(20,153),new Vector2(345,32),20);
        for(int i=0;i<4;i++)v.effects[i]=Card(root,"Effect"+i,new Vector2(0,76-i*104),new Vector2(412,96));
        return v;
    }
}
