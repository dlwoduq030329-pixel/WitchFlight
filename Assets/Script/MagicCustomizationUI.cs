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
    public enum EquipmentCategory { Magic, Hat, Broom }

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

    [Serializable]
    public sealed class HatChoice
    {
        public HatType hat;
        public Button button;
        public Image buttonImage;
        public Sprite icon;
        public string displayName;
        [TextArea] public string description;
    }

    [Serializable]
    public sealed class BroomChoice
    {
        public BroomType broom;
        public Button button;
        public Image buttonImage;
        public Sprite icon;
        public string displayName;
        [TextArea] public string description;
    }

    [Header("Hat image buttons (same selection/confirmation flow as magic)")]
    [SerializeField] private HatChoice[] hatChoices =
    {
        new HatChoice { hat = HatType.Classic },
        new HatChoice { hat = HatType.Twisted },
        new HatChoice { hat = HatType.Elemental },
        new HatChoice { hat = HatType.Serenity },
        new HatChoice { hat = HatType.Cosmic }
    };
    [SerializeField] private Button hatSlotButton;
    [SerializeField] private Image hatSlotImage;
    [SerializeField] private GameObject hatSlotHighlight;

    [Header("Broom image buttons (same selection/confirmation flow as magic)")]
    [SerializeField] private BroomChoice[] broomChoices =
    {
        new BroomChoice { broom = BroomType.Slow },
        new BroomChoice { broom = BroomType.Standard },
        new BroomChoice { broom = BroomType.Speed }
    };
    [SerializeField] private Button broomSlotButton;
    [SerializeField] private Image broomSlotImage;
    [SerializeField] private GameObject broomSlotHighlight;

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

    [Header("Description panel visibility (content always follows the selected item)")]
    [FormerlySerializedAs("manageSelectionDetails")]
    [Tooltip("설명창을 코드가 켜고 끌지 여부입니다. 기존 OnClick이 창을 켠다면 끄세요. 이름/설명/아이콘은 이 옵션과 관계없이 갱신합니다.")]
    [SerializeField] private bool manageSelectionPanel;
    [Tooltip("선택 전에는 숨길 상세 UI. 이 스크립트가 붙은 오브젝트/부모는 지정하지 마세요.")]
    [SerializeField] private GameObject selectionPanel;
    [FormerlySerializedAs("selectedMagicImage")]
    [SerializeField] private Image selectedItemImage;
    [FormerlySerializedAs("selectedMagicName")]
    [SerializeField] private TMP_Text selectedItemName;
    [FormerlySerializedAs("selectedMagicDescription")]
    [SerializeField] private TMP_Text selectedItemDescription;
    [Header("Equip confirmation (existing OnClick events are preserved)")]
    [Tooltip("모자/빗자루/마법 공용 장착 버튼. OnClick은 자동 연결되므로 장착 함수를 중복 연결하지 마세요.")]
    [SerializeField] private Button equipButton;
    [SerializeField] private TMP_Text equipButtonText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button cancelSelectionButton;

    [Header("Optional UI events")]
    [Tooltip("로컬 장착 완료 시 슬롯 번호(1 또는 2)를 전달합니다. 서버 저장 완료 이벤트는 아닙니다.")]
    [SerializeField] private UnityEvent<int> onEquipped = new UnityEvent<int>();
    [Tooltip("모자 장착 완료 시 HatType의 정수 값을 전달합니다. 서버 저장 완료는 아닙니다.")]
    [SerializeField] private UnityEvent<int> onHatEquipped = new UnityEvent<int>();
    [Tooltip("빗자루 장착 완료 시 BroomType의 정수 값을 전달합니다. 서버 저장 완료는 아닙니다.")]
    [SerializeField] private UnityEvent<int> onBroomEquipped = new UnityEvent<int>();
    [SerializeField] private UnityEvent<string> onEquipFailed = new UnityEvent<string>();

    public int SelectedSlot { get; private set; } = 1;
    public MagicType SelectedMagic { get; private set; } = MagicType.None;
    public HatType SelectedHat { get; private set; } = HatType.None;
    public BroomType SelectedBroom { get; private set; } = BroomType.None;
    public EquipmentCategory SelectedCategory { get; private set; } = EquipmentCategory.Magic;
    public EquipmentCategory SelectedItemCategory { get; private set; } = EquipmentCategory.Magic;

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
        SelectedCategory = EquipmentCategory.Magic;
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
        if (hatChoices != null)
            foreach (HatChoice choice in hatChoices)
            {
                if (choice == null || !IsSelectableHat(choice.hat)) continue;
                int index = (int)choice.hat;
                Bind(choice.button, () => SelectHat(index));
            }
        if (broomChoices != null)
            foreach (BroomChoice choice in broomChoices)
            {
                if (choice == null || !IsSelectableBroom(choice.broom)) continue;
                int index = (int)choice.broom;
                Bind(choice.button, () => SelectBroom(index));
            }
        Bind(hatSlotButton, SelectHatSlot);
        Bind(broomSlotButton, SelectBroomSlot);
        Bind(slot1Button, () => SelectSlot(1));
        Bind(slot2Button, () => SelectSlot(2));
        Bind(equipButton, EquipSelectedItem);
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
        SelectedCategory = EquipmentCategory.Magic;
        RefreshTargetSlot();
    }

    public void SelectHatSlot() => SelectEquipmentSlot(EquipmentCategory.Hat);
    public void SelectBroomSlot() => SelectEquipmentSlot(EquipmentCategory.Broom);

    private void SelectEquipmentSlot(EquipmentCategory category)
    {
        if (!CanEdit) { RejectSelection(EditBlockedMessage()); return; }
        SelectedCategory = category;
        RefreshTargetSlot();
    }

    private void RefreshTargetSlot()
    {
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
        SelectedItemCategory = EquipmentCategory.Magic;
        SetStatus(string.Empty);
        RefreshFromDataConfig();
    }

    public void SelectHat(int hatId)
    {
        if (!CanEdit) { RejectSelection(EditBlockedMessage()); return; }
        HatType hat = (HatType)hatId;
        if (!IsSelectableHat(hat) || FindHatChoice(hat) == null)
        {
            RejectSelection("목록에 등록된 모자를 선택해주세요.");
            return;
        }
        SelectedHat = hat;
        SelectedItemCategory = EquipmentCategory.Hat;
        SetStatus(string.Empty);
        RefreshFromDataConfig();
    }

    public void SelectBroom(int broomId)
    {
        if (!CanEdit) { RejectSelection(EditBlockedMessage()); return; }
        BroomType broom = (BroomType)broomId;
        if (!IsSelectableBroom(broom) || FindBroomChoice(broom) == null)
        {
            RejectSelection("목록에 등록된 빗자루를 선택해주세요.");
            return;
        }
        SelectedBroom = broom;
        SelectedItemCategory = EquipmentCategory.Broom;
        SetStatus(string.Empty);
        RefreshFromDataConfig();
    }

    // Backward-compatible entry point for existing manually wired buttons.
    public void EquipSelectedMagic() => EquipSelectedItem();

    public void EquipSelectedItem()
    {
        if (isEquipping) return;
        // Recheck at confirmation: login/matching state can change after selection.
        if (!CanEdit) { RejectSelection(EditBlockedMessage()); return; }
        if (!HasSelectedItem || !HasValidTargetSlot)
        {
            RejectSelection("장착할 장비와 슬롯을 선택해주세요.");
            return;
        }
        if (SelectedItemCategory != SelectedCategory)
        {
            Fail("선택한 장비와 같은 종류의 슬롯을 선택해주세요.");
            return;
        }
        if (IsSelectedItemEquipped) return;

        int slot = SelectedSlot;
        EquipmentCategory category = SelectedCategory;
        int index = SelectedItemIndex;
        string itemName = SelectedItemName();
        string targetName = SelectedTargetName();
        isEquipping = true;
        try
        {
            // Snapshot BOTH choices at confirmation, not when a spell was picked.
            // Retain the candidate for another slot; same-slot repeat is a no-op.
            // DatabaseManager already observes Changed and saves asynchronously.
            switch (category)
            {
                case EquipmentCategory.Hat: DataConfig.hatIndex = index; break;
                case EquipmentCategory.Broom: DataConfig.broomIndex = index; break;
                default:
                    if (slot == 1) DataConfig.magic1Index = index;
                    else DataConfig.magic2Index = index;
                    break;
            }
            RefreshFromDataConfig();
            SetStatus($"{targetName}에 {itemName} 장착 완료");
            if (category == EquipmentCategory.Hat) onHatEquipped.Invoke(index);
            else if (category == EquipmentCategory.Broom) onBroomEquipped.Invoke(index);
            else onEquipped.Invoke(slot);
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
        SelectedHat = HatType.None;
        SelectedBroom = BroomType.None;
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
        if (hatChoices != null)
            foreach (HatChoice choice in hatChoices)
            {
                if (choice?.button == null) continue;
                choice.button.interactable = lastCanEdit && IsSelectableHat(choice.hat);
                if (choice.icon != null && !IsEquippedSlotImage(choice.buttonImage)) SetIcon(choice.buttonImage, choice.icon);
            }
        if (broomChoices != null)
            foreach (BroomChoice choice in broomChoices)
            {
                if (choice?.button == null) continue;
                choice.button.interactable = lastCanEdit && IsSelectableBroom(choice.broom);
                if (choice.icon != null && !IsEquippedSlotImage(choice.buttonImage)) SetIcon(choice.buttonImage, choice.icon);
            }

        RefreshSelectionUI();
        refreshControlsAfterClick = isActiveAndEnabled;
    }

    private void RefreshSelectionUI()
    {
        if (manageSelectionPanel)
            SetVisible(selectionPanel, HasSelectedItem);
        RefreshSelectionContent();
        RefreshEquipButton();
    }

    private void RefreshSelectionContent()
    {
        bool hasSelection = HasSelectedItem;
        // Panel activation and content are independent. Existing OnClick may show
        // the panel, while these assigned fields always describe the chosen magic.
        // A preview accidentally assigned to a loadout Image cannot erase it.
        if (!IsEquippedSlotImage(selectedItemImage))
            SetIcon(selectedItemImage, hasSelection ? SelectedItemIcon() : null);
        SetText(selectedItemName, hasSelection ? SelectedItemName() : string.Empty);
        SetText(selectedItemDescription, hasSelection ? SelectedItemDescription() : string.Empty);
    }

    private void RefreshEquippedSlots()
    {
        // The bottom row always shows SAVED equipment, never the pending candidate.
        bool ready = ProfileReady;
        SetIcon(slot1Image, ready ? MagicIcon(EquippedMagic(1)) ?? emptySlotSprite : emptySlotSprite);
        SetIcon(slot2Image, ready ? MagicIcon(EquippedMagic(2)) ?? emptySlotSprite : emptySlotSprite);
        SetIcon(hatSlotImage, ready ? FindHatChoice((HatType)DataConfig.hatIndex)?.icon ?? emptySlotSprite : emptySlotSprite);
        SetIcon(broomSlotImage, ready ? FindBroomChoice((BroomType)DataConfig.broomIndex)?.icon ?? emptySlotSprite : emptySlotSprite);
    }

    private void RefreshSlotControls()
    {
        SetVisible(slot1Highlight, SelectedCategory == EquipmentCategory.Magic && SelectedSlot == 1);
        SetVisible(slot2Highlight, SelectedCategory == EquipmentCategory.Magic && SelectedSlot == 2);
        SetVisible(hatSlotHighlight, SelectedCategory == EquipmentCategory.Hat);
        SetVisible(broomSlotHighlight, SelectedCategory == EquipmentCategory.Broom);
        if (slot1Button != null) slot1Button.interactable = CanEdit;
        if (slot2Button != null) slot2Button.interactable = CanEdit;
        if (hatSlotButton != null) hatSlotButton.interactable = CanEdit;
        if (broomSlotButton != null) broomSlotButton.interactable = CanEdit;
    }

    private void RefreshEquipButton()
    {
        bool hasSelection = HasSelectedItem;
        bool matchingCategory = SelectedItemCategory == SelectedCategory;
        bool alreadyEquipped = hasSelection && HasValidTargetSlot && matchingCategory && IsSelectedItemEquipped;
        if (equipButtonText != null) equipButtonText.text = alreadyEquipped ? "장착 중" :
            hasSelection && !matchingCategory ? "같은 종류의 슬롯을 선택해주세요" : $"{SelectedTargetName()}에 장착";
        if (equipButton != null)
        {
            SetVisible(equipButton.gameObject, hasSelection);
            equipButton.interactable = CanEdit && HasValidTargetSlot && matchingCategory && hasSelection && !alreadyEquipped && !isEquipping;
        }
    }

    private bool IsEquippedSlotImage(Image image) => image != null &&
        (image == slot1Image || image == slot2Image || image == hatSlotImage || image == broomSlotImage);

    private HatChoice FindHatChoice(HatType hat)
    {
        if (hatChoices != null)
            foreach (HatChoice choice in hatChoices)
                if (choice != null && choice.hat == hat) return choice;
        return null;
    }

    private BroomChoice FindBroomChoice(BroomType broom)
    {
        if (broomChoices != null)
            foreach (BroomChoice choice in broomChoices)
                if (choice != null && choice.broom == broom) return choice;
        return null;
    }

    private bool HasSelectedItem => SelectedItemCategory switch
    {
        EquipmentCategory.Hat => IsSelectableHat(SelectedHat) && FindHatChoice(SelectedHat) != null,
        EquipmentCategory.Broom => IsSelectableBroom(SelectedBroom) && FindBroomChoice(SelectedBroom) != null,
        _ => IsSelectableMagic(SelectedMagic) && FindChoice(SelectedMagic) != null
    };

    private bool HasValidTargetSlot => SelectedCategory == EquipmentCategory.Hat || SelectedCategory == EquipmentCategory.Broom ||
        (SelectedCategory == EquipmentCategory.Magic && (SelectedSlot == 1 || SelectedSlot == 2));

    private int SelectedItemIndex => SelectedItemCategory switch
    {
        EquipmentCategory.Hat => (int)SelectedHat,
        EquipmentCategory.Broom => (int)SelectedBroom,
        _ => (int)SelectedMagic
    };

    private bool IsSelectedItemEquipped => SelectedCategory switch
    {
        EquipmentCategory.Hat => DataConfig.hatIndex == SelectedItemIndex,
        EquipmentCategory.Broom => DataConfig.broomIndex == SelectedItemIndex,
        _ => (int)EquippedMagic(SelectedSlot) == SelectedItemIndex
    };

    private string SelectedTargetName() => SelectedCategory switch
    {
        EquipmentCategory.Hat => "모자 슬롯",
        EquipmentCategory.Broom => "빗자루 슬롯",
        _ => $"{SelectedSlot}번 슬롯"
    };

    private Sprite SelectedItemIcon() => SelectedItemCategory switch
    {
        EquipmentCategory.Hat => FindHatChoice(SelectedHat)?.icon,
        EquipmentCategory.Broom => FindBroomChoice(SelectedBroom)?.icon,
        _ => MagicIcon(SelectedMagic)
    };

    private string SelectedItemName()
    {
        if (SelectedItemCategory == EquipmentCategory.Magic) return MagicName(SelectedMagic);
        string name = SelectedItemCategory == EquipmentCategory.Hat ? FindHatChoice(SelectedHat)?.displayName : FindBroomChoice(SelectedBroom)?.displayName;
        return !string.IsNullOrWhiteSpace(name) ? name :
            SelectedItemCategory == EquipmentCategory.Hat ? SelectedHat.ToString() : SelectedBroom.ToString();
    }

    private string SelectedItemDescription() => (SelectedItemCategory switch
    {
        EquipmentCategory.Hat => FindHatChoice(SelectedHat)?.description,
        EquipmentCategory.Broom => FindBroomChoice(SelectedBroom)?.description,
        _ => FindChoice(SelectedMagic)?.description
    }) ?? string.Empty;

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
    private static bool IsSelectableHat(HatType hat) => hat >= HatType.Classic && hat <= HatType.Cosmic;
    private static bool IsSelectableBroom(BroomType broom) => broom >= BroomType.Slow && broom <= BroomType.Speed;
    private string EditBlockedMessage() => !ProfileReady ? "로그인 및 유저 정보 불러오기를 먼저 완료해주세요." : "매칭 중이거나 방에 입장한 상태에서는 장비를 변경할 수 없습니다.";
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
