using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
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

    [Header("Description panel visibility (content always follows the selected magic)")]
    [FormerlySerializedAs("manageSelectionDetails")]
    [Tooltip("설명창을 코드가 켜고 끌지 여부입니다. 기존 OnClick이 창을 켠다면 끄세요. 이름/설명/아이콘은 이 옵션과 관계없이 갱신합니다.")]
    [SerializeField] private bool manageSelectionPanel;
    [Tooltip("선택 전에는 숨길 상세 UI. 이 스크립트가 붙은 오브젝트/부모는 지정하지 마세요.")]
    [SerializeField] private GameObject selectionPanel;
    [SerializeField] private Image selectedMagicImage;
    [SerializeField] private TMP_Text selectedMagicName;
    [SerializeField] private TMP_Text selectedMagicDescription;
    [Header("Equip confirmation (existing OnClick events are preserved)")]
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
    private bool isEquipping;
    private bool refreshControlsAfterClick;

    private bool ProfileReady => DatabaseManager.Instance != null &&
        DatabaseManager.Instance.IsDataConfigReady && DatabaseManager.Instance.HasLoadedProfile;
    private bool CanEdit => isActiveAndEnabled && ProfileReady &&
        (NetworkGameManager.Instance == null || !NetworkGameManager.Instance.IsMatching);

    private void OnEnable()
    {
        if (magicTable == null) magicTable = Resources.Load<MagicStatTable>("MagicStatTable");
        SelectedSlot = Mathf.Clamp(defaultSlot, 1, 2);
        ClearPendingSelection();
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
        ClearPendingSelection();
        RefreshSelectionUI();
        refreshControlsAfterClick = false;
    }

    private void Update()
    {
        // Login loads DataConfig silently. Only poll readiness, not all sprites every frame.
        bool ready = ProfileReady;
        bool editable = CanEdit;
        if (ready == lastProfileReady && editable == lastCanEdit) return;
        ClearPendingSelection();
        SetStatus(string.Empty);
        RefreshFromDataConfig();
    }

    private void Bind(Button button, UnityAction action)
    {
        if (button == null) return;
        button.onClick.AddListener(action);
        listeners.Add((button, action));
    }

    private void LateUpdate()
    {
        if (!refreshControlsAfterClick) return;
        refreshControlsAfterClick = false;
        // Existing OnClick handlers may run after ours and touch controls/text.
        // Restore the selected content after the click, but leave panel visibility
        // to the designer's OnClick when automatic panel management is disabled.
        RefreshEquippedSlots();
        RefreshSelectionContent();
        RefreshEquipButton();
    }

    // Optional manual Button.OnClick entry point: slots are 1 and 2, not 0 and 1.
    public void SelectSlot(int slot)
    {
        if (!CanEdit) { RejectSelection(EditBlockedMessage()); return; }
        if (slot != 1 && slot != 2) { Fail("마법은 1번 또는 2번 슬롯에 장착할 수 있습니다."); return; }
        // Slot and spell are independent selections. Mine chosen in slot 1 must
        // remain the candidate when switching to slot 2, without equipping yet.
        SelectedSlot = slot;
        SetStatus(string.Empty);
        RefreshEquippedSlots();
        RefreshSlotControls();
        RefreshEquipButton();
        refreshControlsAfterClick = true;
    }

    // Optional manual entry point. Pass the MagicType numeric value (Fire=1, Ice=2...).
    // Preview only: never write to DataConfig or the backend here.
    public void SelectMagic(int magicId)
    {
        if (!CanEdit) { RejectSelection(EditBlockedMessage()); return; }
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
        if (isEquipping) return;
        // Recheck at confirmation: login/matching state can change after selection.
        if (!CanEdit) { RejectSelection(EditBlockedMessage()); return; }
        if ((SelectedSlot != 1 && SelectedSlot != 2) ||
            !IsSelectableMagic(SelectedMagic) || FindChoice(SelectedMagic) == null)
        {
            RejectSelection("장착할 마법과 1번 또는 2번 슬롯을 선택해주세요.");
            return;
        }
        if (EquippedMagic(SelectedSlot) == SelectedMagic) return;

        int slot = SelectedSlot;
        MagicType magic = SelectedMagic;
        isEquipping = true;
        try
        {
            // Snapshot BOTH choices at confirmation, not when a spell was picked.
            // Retain the candidate for another slot; same-slot repeat is a no-op.
            // DatabaseManager already observes Changed and saves asynchronously.
            if (slot == 1) DataConfig.magic1Index = (int)magic;
            else DataConfig.magic2Index = (int)magic;
            RefreshFromDataConfig();
            SetStatus($"{slot}번 슬롯에 {MagicName(magic)} 장착 완료");
            onEquipped.Invoke(slot);
        }
        finally
        {
            isEquipping = false;
            RefreshEquipButton();
            refreshControlsAfterClick = isActiveAndEnabled;
        }
    }

    public void CancelSelection()
    {
        ClearPendingSelection();
        SetStatus(string.Empty);
        RefreshFromDataConfig();
    }

    private void ClearPendingSelection()
    {
        SelectedMagic = MagicType.None;
    }

    private void RejectSelection(string message)
    {
        CancelSelection();
        Fail(message);
    }

    // Can also be wired to LobbyPlayerInitializer.OnInitialized for a silent server load.
    public void RefreshFromDataConfig()
    {
        lastProfileReady = ProfileReady;
        lastCanEdit = CanEdit;
        if (!lastCanEdit) ClearPendingSelection();

        RefreshEquippedSlots();
        RefreshSlotControls();
        if (magicChoices != null)
        {
            foreach (MagicChoice choice in magicChoices)
            {
                if (choice?.button == null) continue;
                choice.button.interactable = lastCanEdit && IsSelectableMagic(choice.magic);
                Sprite icon = MagicIcon(choice.magic);
                // Preserve designer-authored button art when no replacement is assigned.
                if (icon != null && !IsEquippedSlotImage(choice.buttonImage)) SetIcon(choice.buttonImage, icon);
            }
        }

        RefreshSelectionUI();
        refreshControlsAfterClick = isActiveAndEnabled;
    }

    private void RefreshSelectionUI()
    {
        if (manageSelectionPanel)
            SetVisible(selectionPanel, IsSelectableMagic(SelectedMagic) && FindChoice(SelectedMagic) != null);
        RefreshSelectionContent();
        RefreshEquipButton();
    }

    private void RefreshSelectionContent()
    {
        MagicChoice selected = FindChoice(SelectedMagic);
        bool hasSelection = selected != null && IsSelectableMagic(SelectedMagic);
        // Panel activation and content are independent. Existing OnClick may show
        // the panel, while these assigned fields always describe the chosen magic.
        // A preview accidentally assigned to a loadout Image cannot erase it.
        if (!IsEquippedSlotImage(selectedMagicImage))
            SetIcon(selectedMagicImage, hasSelection ? MagicIcon(SelectedMagic) : null);
        SetText(selectedMagicName, hasSelection ? MagicName(SelectedMagic) : string.Empty);
        SetText(selectedMagicDescription, hasSelection ? selected.description ?? string.Empty : string.Empty);
    }

    private void RefreshEquippedSlots()
    {
        // The bottom row always shows SAVED equipment, never the pending candidate.
        bool ready = ProfileReady;
        SetIcon(slot1Image, ready ? MagicIcon(EquippedMagic(1)) ?? emptySlotSprite : emptySlotSprite);
        SetIcon(slot2Image, ready ? MagicIcon(EquippedMagic(2)) ?? emptySlotSprite : emptySlotSprite);
    }

    private void RefreshSlotControls()
    {
        SetVisible(slot1Highlight, SelectedSlot == 1);
        SetVisible(slot2Highlight, SelectedSlot == 2);
        if (slot1Button != null) slot1Button.interactable = CanEdit;
        if (slot2Button != null) slot2Button.interactable = CanEdit;
    }

    private void RefreshEquipButton()
    {
        bool hasSelection = IsSelectableMagic(SelectedMagic) && FindChoice(SelectedMagic) != null;
        bool validSlot = SelectedSlot == 1 || SelectedSlot == 2;
        bool alreadyEquipped = validSlot && hasSelection && EquippedMagic(SelectedSlot) == SelectedMagic;
        if (equipButtonText != null) equipButtonText.text = alreadyEquipped ? "장착 중" : $"{SelectedSlot}번 슬롯에 장착";
        if (equipButton != null)
        {
            SetVisible(equipButton.gameObject, hasSelection);
            equipButton.interactable = CanEdit && validSlot && hasSelection && !alreadyEquipped && !isEquipping;
        }
    }

    private bool IsEquippedSlotImage(Image image) => image != null && (image == slot1Image || image == slot2Image);

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
        if (image.sprite != sprite) image.sprite = sprite;
        bool visible = sprite != null;
        if (image.enabled != visible) image.enabled = visible;
    }

    private static void SetText(TMP_Text text, string value)
    {
        if (text != null && text.text != value) text.text = value;
    }
}
