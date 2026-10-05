using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Main scene customization only. Assign designer-owned UI; no windows are generated.
// Place on the customization root, NOT on the detail panel or equip button we hide.
[DisallowMultipleComponent]
public sealed class MagicCustomizationUI : MonoBehaviour
{
    [Serializable]
    public sealed class MagicChoice
    {
        public MagicType magic;
        public Button button;
        [Tooltip("목록 버튼의 아이콘 Image(선택). 버튼 배경/테두리 Image와 구분해서 연결하세요.")]
        public Image buttonImage;
        [Tooltip("목록 버튼, 미리보기, 장착 슬롯에 사용할 이미지. 비우면 MagicStatTable의 icon을 사용합니다.")]
        public Sprite icon;
        [Tooltip("비우면 MagicStatTable의 displayName 또는 enum 이름을 표시합니다.")]
        public string displayName;
        [TextArea] public string description;
    }

    [Header("Magic image buttons (enum IDs, not array indices)")]
    [SerializeField] private MagicChoice[] magicChoices =
    {
        new MagicChoice { magic = MagicType.Fire },
        new MagicChoice { magic = MagicType.Ice },
        new MagicChoice { magic = MagicType.Vision },
        new MagicChoice { magic = MagicType.Thunder },
        new MagicChoice { magic = MagicType.Flare },
        new MagicChoice { magic = MagicType.Smoke },
        new MagicChoice { magic = MagicType.Dark },
        new MagicChoice { magic = MagicType.Decoy },
        new MagicChoice { magic = MagicType.Mine },
        new MagicChoice { magic = MagicType.Scane }
    };
    [Tooltip("비우면 Resources/MagicStatTable을 사용합니다. 마법 수치는 변경하지 않습니다.")]
    [SerializeField] private MagicStatTable magicTable;

    [Header("Editable slots (1 and 2; slot 3 remains fixed parry)")]
    [SerializeField] private Button slot1Button;
    [SerializeField] private Image slot1Image;
    [SerializeField] private GameObject slot1Highlight;
    [SerializeField] private Button slot2Button;
    [SerializeField] private Image slot2Image;
    [SerializeField] private GameObject slot2Highlight;
    [SerializeField] private Sprite emptySlotSprite;
    [SerializeField, Range(1, 2)] private int defaultSlot = 1;

    [Header("Selection details and confirmation (all optional)")]
    [Tooltip("선택 전에는 숨길 상세 UI. 이 스크립트가 붙은 오브젝트/부모는 지정하지 마세요.")]
    [SerializeField] private GameObject selectionPanel;
    [SerializeField] private Image selectedMagicImage;
    [SerializeField] private TMP_Text selectedMagicName;
    [SerializeField] private TMP_Text selectedMagicDescription;
    [Tooltip("마법을 선택하면 표시됩니다. OnClick은 자동 연결되므로 직접 중복 연결하지 마세요.")]
    [SerializeField] private Button equipButton;
    [SerializeField] private TMP_Text equipButtonText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button cancelSelectionButton;

    [Header("Optional UI events")]
    [Tooltip("로컬 장착 완료 시 슬롯 번호(1 또는 2)를 전달합니다. 서버 저장 완료 이벤트는 아닙니다.")]
    [SerializeField] private UnityEvent<int> onEquipped = new UnityEvent<int>();
    [SerializeField] private UnityEvent<string> onEquipFailed = new UnityEvent<string>();

    public int SelectedSlot { get; private set; } = 1;
    public MagicType SelectedMagic { get; private set; } = MagicType.None;

    private readonly List<(Button button, UnityAction action)> listeners = new List<(Button, UnityAction)>();
    private bool lastProfileReady, lastCanEdit;

    private bool ProfileReady => DatabaseManager.Instance != null &&
        DatabaseManager.Instance.IsDataConfigReady && DatabaseManager.Instance.HasLoadedProfile;
    private bool CanEdit => ProfileReady &&
        (NetworkGameManager.Instance == null || !NetworkGameManager.Instance.IsMatching);

    private void OnEnable()
    {
        if (magicTable == null) magicTable = Resources.Load<MagicStatTable>("MagicStatTable");
        SelectedSlot = Mathf.Clamp(defaultSlot, 1, 2);
        SelectedMagic = MagicType.None;
        if (magicChoices != null)
        {
            foreach (MagicChoice choice in magicChoices)
            {
                if (choice == null || !IsSelectableMagic(choice.magic)) continue;
                int magicId = (int)choice.magic;
                Bind(choice.button, () => SelectMagic(magicId));
            }
        }
        Bind(slot1Button, () => SelectSlot(1));
        Bind(slot2Button, () => SelectSlot(2));
        Bind(equipButton, EquipSelectedMagic);
        Bind(cancelSelectionButton, CancelSelection);
        DataConfig.Changed += RefreshFromDataConfig;
        SetStatus(string.Empty);
        RefreshFromDataConfig();
    }

    private void OnDisable()
    {
        DataConfig.Changed -= RefreshFromDataConfig;
        foreach (var listener in listeners)
            if (listener.button != null) listener.button.onClick.RemoveListener(listener.action);
        listeners.Clear();
        SelectedMagic = MagicType.None;
    }

    private void Update()
    {
        // Login loads DataConfig silently. Only poll readiness, not all sprites every frame.
        bool ready = ProfileReady;
        bool editable = CanEdit;
        if (ready == lastProfileReady && editable == lastCanEdit) return;
        SelectedMagic = MagicType.None;
        SetStatus(string.Empty);
        RefreshFromDataConfig();
    }

    private void Bind(Button button, UnityAction action)
    {
        if (button == null) return;
        button.onClick.AddListener(action);
        listeners.Add((button, action));
    }

    // Optional manual Button.OnClick entry point: slots are 1 and 2, not 0 and 1.
    public void SelectSlot(int slot)
    {
        if (slot != 1 && slot != 2) { Fail("마법은 1번 또는 2번 슬롯에 장착할 수 있습니다."); return; }
        SelectedSlot = slot;
        SetStatus(string.Empty);
        RefreshFromDataConfig();
    }

    // Optional manual entry point. Pass the MagicType numeric value (Fire=1, Ice=2...).
    // Preview only: never write to DataConfig or the backend here.
    public void SelectMagic(int magicId)
    {
        if (!CanEdit) { Fail(EditBlockedMessage()); return; }
        MagicType magic = (MagicType)magicId;
        if (!IsSelectableMagic(magic) || FindChoice(magic) == null)
        {
            CancelSelection();
            Fail("목록에 등록된 마법을 선택해주세요.");
            return;
        }
        SelectedMagic = magic;
        SetStatus(string.Empty);
        RefreshFromDataConfig();
    }

    public void EquipSelectedMagic()
    {
        // Recheck at confirmation: login/matching state can change after selection.
        if (!CanEdit) { Fail(EditBlockedMessage()); return; }
        if (!IsSelectableMagic(SelectedMagic) || FindChoice(SelectedMagic) == null)
        {
            Fail("장착할 마법을 먼저 선택해주세요.");
            return;
        }
        if (EquippedMagic(SelectedSlot) == SelectedMagic) return;

        int slot = SelectedSlot;
        MagicType magic = SelectedMagic;
        // DatabaseManager already observes Changed and saves asynchronously with retries.
        // Do not perform a second synchronous save or change the other slot / appearance.
        if (slot == 1) DataConfig.magic1Index = (int)magic;
        else DataConfig.magic2Index = (int)magic;
        RefreshFromDataConfig();
        SetStatus($"{slot}번 슬롯에 {MagicName(magic)} 장착 완료");
        onEquipped.Invoke(slot);
    }

    public void CancelSelection()
    {
        SelectedMagic = MagicType.None;
        SetStatus(string.Empty);
        RefreshFromDataConfig();
    }

    // Can also be wired to LobbyPlayerInitializer.OnInitialized for a silent server load.
    public void RefreshFromDataConfig()
    {
        lastProfileReady = ProfileReady;
        lastCanEdit = CanEdit;
        if (!lastCanEdit) SelectedMagic = MagicType.None;

        SetIcon(slot1Image, lastProfileReady ? MagicIcon(EquippedMagic(1)) ?? emptySlotSprite : emptySlotSprite);
        SetIcon(slot2Image, lastProfileReady ? MagicIcon(EquippedMagic(2)) ?? emptySlotSprite : emptySlotSprite);
        SetVisible(slot1Highlight, SelectedSlot == 1);
        SetVisible(slot2Highlight, SelectedSlot == 2);
        if (slot1Button != null) slot1Button.interactable = lastCanEdit;
        if (slot2Button != null) slot2Button.interactable = lastCanEdit;
        if (magicChoices != null)
        {
            foreach (MagicChoice choice in magicChoices)
            {
                if (choice?.button == null) continue;
                choice.button.interactable = lastCanEdit && IsSelectableMagic(choice.magic);
                Sprite icon = MagicIcon(choice.magic);
                // Preserve designer-authored button art when no replacement is assigned.
                if (icon != null) SetIcon(choice.buttonImage, icon);
            }
        }

        MagicChoice selected = FindChoice(SelectedMagic);
        bool hasSelection = selected != null && IsSelectableMagic(SelectedMagic);
        bool alreadyEquipped = hasSelection && EquippedMagic(SelectedSlot) == SelectedMagic;
        SetVisible(selectionPanel, hasSelection);
        SetIcon(selectedMagicImage, hasSelection ? MagicIcon(SelectedMagic) : null);
        if (selectedMagicName != null) selectedMagicName.text = hasSelection ? MagicName(SelectedMagic) : string.Empty;
        if (selectedMagicDescription != null) selectedMagicDescription.text = hasSelection ? selected.description ?? string.Empty : string.Empty;
        if (equipButtonText != null) equipButtonText.text = alreadyEquipped ? "장착 중" : $"{SelectedSlot}번 슬롯에 장착";
        if (equipButton != null)
        {
            SetVisible(equipButton.gameObject, hasSelection);
            equipButton.interactable = lastCanEdit && hasSelection && !alreadyEquipped;
        }
    }

    private MagicChoice FindChoice(MagicType magic)
    {
        if (magicChoices != null)
            foreach (MagicChoice choice in magicChoices)
                if (choice != null && choice.magic == magic) return choice;
        return null;
    }

    private Sprite MagicIcon(MagicType magic)
    {
        if (!IsSelectableMagic(magic)) return null;
        MagicChoice choice = FindChoice(magic);
        if (choice?.icon != null) return choice.icon;
        return magicTable != null ? magicTable.GetStats(magic).icon : null;
    }

    private string MagicName(MagicType magic)
    {
        string name = FindChoice(magic)?.displayName;
        if (string.IsNullOrWhiteSpace(name) && magicTable != null) name = magicTable.GetStats(magic).displayName;
        if (!string.IsNullOrWhiteSpace(name)) return name;
        return magic == MagicType.Dark ? "Wind" : magic == MagicType.Scane ? "Scan" : magic.ToString();
    }

    private static MagicType EquippedMagic(int slot) => (MagicType)(slot == 1 ? DataConfig.magic1Index : DataConfig.magic2Index);
    private static bool IsSelectableMagic(MagicType magic) => magic >= MagicType.Fire && magic <= MagicType.Scane;
    private string EditBlockedMessage() => !ProfileReady ? "로그인 및 유저 정보 불러오기를 먼저 완료해주세요." : "매칭 중이거나 방에 입장한 상태에서는 마법을 변경할 수 없습니다.";
    private void SetStatus(string message) { if (statusText != null) statusText.text = message; }
    private void Fail(string message) { SetStatus(message); onEquipFailed.Invoke(message); }

    private void SetVisible(GameObject target, bool visible)
    {
        if (target == null || target.activeSelf == visible) return;
        // A mistaken Inspector assignment must not disable this controller/its parent.
        if (!visible && transform.IsChildOf(target.transform)) return;
        target.SetActive(visible);
    }

    private static void SetIcon(Image image, Sprite sprite)
    {
        if (image == null) return;
        image.sprite = sprite;
        image.enabled = sprite != null;
    }
}
