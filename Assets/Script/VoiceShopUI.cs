using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public sealed class VoiceShopUI : MonoBehaviour
{
    [System.Serializable]
    private sealed class ProductBinding
    {
        public Button button;
        public string itemId;
    }
    // Baked from existing OnClick SelectVoice(string) events; never manually assigned.
    [SerializeField, HideInInspector] private List<ProductBinding> products = new();
    [SerializeField] private string selectedItemId = "3.2";
    [SerializeField] private TMP_Text itemNameText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Image itemIcon;
    [SerializeField] private Button purchaseButton;
    [SerializeField] private Button equipButton;
    [Header("구매 확인: 이 컴포넌트는 purchaseCheck 바깥의 상점에 배치")]
    [SerializeField] private GameObject purchaseCheck;
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button cancelButton;
    [SerializeField] private TMP_Text purchaseCheckNameText;
    [SerializeField] private TMP_Text purchaseCheckPriceText;
    [Header("목소리 미리듣기 (Voice 볼륨 적용)")]
    [SerializeField, Range(0f, 1f)] private float previewVolume = 1f;
    [SerializeField] private UnityEvent onPurchaseSucceeded = new();
    [SerializeField] private UnityEvent onPurchaseFailed = new();

    private static VoiceShopUI activeSelection;
    private string previewedItemId;
    private string pendingPurchaseId;
    private AudioSource previewSource;
    private Coroutine previewRoutine;
    private bool purchasing;
    private bool pendingEquip;
    private TMP_Text confirmLabel;
    private GameObject resolvedPurchaseCheck;
    private GameObject confirmationItem;
    private Image confirmationImage;
    private TMP_Text confirmationName, confirmationPrice, confirmationDialog;
    private string purchaseDialog;
    private Button selectedProductButton;

    private void Awake()
    {
        if (purchaseCheck != null && !transform.IsChildOf(purchaseCheck.transform))
            purchaseCheck.SetActive(false);
    }

    private void OnEnable()
    {
#if UNITY_EDITOR
        RebuildProductBindings();
#endif
        foreach (var product in products)
            if (product.button != null) BindProduct(product.button, product.itemId);
        DataConfig.InventoryChanged += Refresh;
        DataConfig.MoneyChanged += Refresh;
        if (purchaseButton != null) purchaseButton.onClick.AddListener(PurchaseSelectedVoice);
        if (equipButton != null) equipButton.onClick.AddListener(EquipSelectedVoice);
        Refresh();
    }
    private void OnDisable()
    {
        DataConfig.InventoryChanged -= Refresh;
        DataConfig.MoneyChanged -= Refresh;
        if (purchaseButton != null) purchaseButton.onClick.RemoveListener(PurchaseSelectedVoice);
        if (equipButton != null) equipButton.onClick.RemoveListener(EquipSelectedVoice);
        ClearSelection();
        if (activeSelection == this) activeSelection = null;
    }
    // Product button OnClick(string): first click previews; repeat click opens confirmation.
    public void SelectVoice(string itemId)
    {
        if (purchasing || !TryGetVoice(itemId, out var item)) return;
        var selected = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
        var clickedButton = selected != null ? selected.GetComponentInParent<Button>() : null;
        if (clickedButton != null && clickedButton.transform.IsChildOf(transform) &&
            (purchaseCheck == null || !clickedButton.transform.IsChildOf(purchaseCheck.transform)))
        {
            BindProduct(clickedButton, itemId);
            selectedProductButton = clickedButton;
        }
        else
        {
            selectedProductButton = null;
            foreach (var product in products)
                if (product.itemId == itemId && product.button != null) { selectedProductButton = product.button; break; }
        }
        bool repeat = activeSelection == this && previewedItemId == itemId;
        if (activeSelection != null && activeSelection != this) activeSelection.ClearSelection();
        activeSelection = this;
        if (selectedItemId != itemId) CancelPurchase();
        selectedItemId = itemId;
        SetStatus("");
        Refresh();
        if (repeat)
        {
            PurchaseSelectedVoice();
            return;
        }
        previewedItemId = itemId;
        PlayPreview(item.voicePack);
        if (ItemInventory.HasItem(itemId))
            SetStatus(ItemInventory.GetEquippedItemId(ItemCategory.Voice) == itemId
                ? "장착중인 목소리입니다." : "보유중입니다. 장착 버튼으로 적용할 수 있습니다.");
    }
    public void ClickSelectedVoice() => SelectVoice(selectedItemId);

    private void BindProduct(Button button, string id)
    {
        var card = button.GetComponent<VoiceShopItemButton>();
        if (card == null) card = button.gameObject.AddComponent<VoiceShopItemButton>();
        card.Configure(this, id);
    }

#if UNITY_EDITOR
    // Only editor code reads serialized UnityEvent arguments. Players use the baked list.
    public void RebuildProductBindings()
    {
        products.Clear();
        foreach (var button in GetComponentsInChildren<Button>(true))
        {
            if (purchaseCheck != null && button.transform.IsChildOf(purchaseCheck.transform)) continue;
            var serialized = new UnityEditor.SerializedObject(button);
            var calls = serialized.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
            if (calls == null) continue;
            for (int i = 0; i < calls.arraySize; i++)
            {
                var call = calls.GetArrayElementAtIndex(i);
                if (call.FindPropertyRelative("m_Target").objectReferenceValue != this ||
                    call.FindPropertyRelative("m_MethodName").stringValue != nameof(SelectVoice) ||
                    call.FindPropertyRelative("m_Mode").intValue != 5 ||
                    call.FindPropertyRelative("m_CallState").intValue == 0) continue;
                string id = call.FindPropertyRelative("m_Arguments.m_StringArgument").stringValue;
                products.Add(new ProductBinding { button = button, itemId = id });
                break;
            }
        }
    }
#endif

    // Kept for existing purchase-button bindings; purchasing now requires ConfirmPurchase.
    public void PurchaseSelectedVoice()
    {
        if (purchasing || !TryGetVoice(selectedItemId, out var item)) return;
        if (purchaseCheck == null || transform.IsChildOf(purchaseCheck.transform))
        {
            SetStatus("구매 확인 UI를 상점의 VoiceShopUI에 연결해주세요.");
            return;
        }
        if (activeSelection != null && activeSelection != this) activeSelection.ClearSelection();
        activeSelection = this;
        CancelPurchase();
        pendingPurchaseId = selectedItemId;
        pendingEquip = ItemInventory.HasItem(selectedItemId);
        UpdateConfirmationContent(item, pendingEquip);
        if (purchaseCheckNameText != null) purchaseCheckNameText.text = item.displayName;
        if (purchaseCheckPriceText != null) purchaseCheckPriceText.text = pendingEquip ? "보유중" : item.price.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        if (confirmButton != null) confirmButton.onClick.AddListener(ConfirmPurchase);
        if (cancelButton != null) cancelButton.onClick.AddListener(CancelPurchase);
        RefreshConfirmation();
        purchaseCheck.SetActive(true);
        RefreshConfirmation();
    }
    public void ConfirmPurchase()
    {
        if (purchasing || string.IsNullOrEmpty(pendingPurchaseId) || purchaseCheck == null || !purchaseCheck.activeInHierarchy) return;
        string itemId = pendingPurchaseId;
        if (itemId != selectedItemId) { CancelPurchase(); return; }
        // An equip confirmation can never fall through into a paid purchase.
        if (pendingEquip || ItemInventory.HasItem(itemId))
        {
            bool equipped = ItemInventory.TryEquipItem(itemId);
            SetStatus(equipped ? "목소리를 장착했습니다." : "계정 데이터가 준비되지 않았거나 보유 정보를 확인할 수 없습니다.");
            if (equipped) CancelPurchase();
            Refresh();
            return;
        }
        var db = DatabaseManager.Instance;
        if (db == null) { SetStatus("로그인 후 구매할 수 있습니다."); return; }
        bool success;
        purchasing = true;
        try
        {
            success = db.TryPurchaseVoice(itemId, out string message);
            SetStatus(message);
        }
        finally { purchasing = false; }
        if (success) CancelPurchase();
        Refresh();
        if (success) onPurchaseSucceeded.Invoke(); else onPurchaseFailed.Invoke();
    }
    public void CancelPurchase()
    {
        pendingPurchaseId = null;
        pendingEquip = false;
        if (confirmButton != null) confirmButton.onClick.RemoveListener(ConfirmPurchase);
        if (cancelButton != null) cancelButton.onClick.RemoveListener(CancelPurchase);
        if (purchaseCheck != null && purchaseCheck.activeSelf && !transform.IsChildOf(purchaseCheck.transform))
            purchaseCheck.SetActive(false);
    }
    private void ClearSelection()
    {
        previewedItemId = null;
        CancelPurchase();
        StopPreview();
    }
    private void StopPreview()
    {
        if (previewRoutine != null) StopCoroutine(previewRoutine);
        previewRoutine = null;
        if (previewSource != null) { previewSource.Stop(); previewSource.clip = null; }
    }
    private void PlayPreview(CharacterVoicePack pack)
    {
        StopPreview();
        var clip = pack != null ? pack.GetPreviewClip() : null;
        if (clip == null) { SetStatus("미리듣기 음성이 없습니다."); return; }
        if (previewSource == null)
        {
            var audioObject = new GameObject("Voice shop preview");
            audioObject.transform.SetParent(transform, false);
            previewSource = audioObject.AddComponent<AudioSource>();
            previewSource.playOnAwake = false;
            previewSource.spatialBlend = 0f;
            previewSource.ignoreListenerPause = true;
        }
        previewSource.outputAudioMixerGroup = GameAudioSettings.Group(GameAudioCategory.Voice);
        previewSource.volume = previewVolume;
        previewRoutine = StartCoroutine(PlayLoadedPreview(clip));
    }
    private IEnumerator PlayLoadedPreview(AudioClip clip)
    {
        if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
        while (clip.loadState == AudioDataLoadState.Loading) yield return null;
        if (clip.loadState == AudioDataLoadState.Loaded)
        {
            previewSource.clip = clip;
            previewSource.Play();
        }
        else SetStatus("미리듣기 음성을 불러오지 못했습니다.");
        previewRoutine = null;
    }
    private static bool TryGetVoice(string id, out ItemDefinition item)
    {
        item = null;
        return ItemCatalog.Default != null && ItemCatalog.Default.TryGet(id, out item) && item.category == ItemCategory.Voice;
    }
    public void EquipSelectedVoice()
    {
        bool success = ItemCatalog.Default != null && ItemCatalog.Default.TryGet(selectedItemId, out var item) &&
            item.category == ItemCategory.Voice && ItemInventory.TryEquipItem(selectedItemId);
        SetStatus(success ? "목소리를 장착했습니다." : "보유한 목소리만 장착할 수 있습니다."); Refresh();
    }
    public void UseBasicVoice()
    {
        bool changed = ItemInventory.TryEquipItem(ItemInventory.BasicVoiceItemId);
        SetStatus(changed ? "기본 목소리로 변경했습니다." : "기본 목소리이거나 계정 데이터를 불러오는 중입니다."); Refresh();
    }
    private void SetStatus(string text) { if (statusText != null) statusText.text = text; }
    private void ResolveConfirmationContent()
    {
        if (resolvedPurchaseCheck == purchaseCheck) return;
        resolvedPurchaseCheck = purchaseCheck;
        confirmationItem = null;
        confirmationImage = null;
        confirmationName = confirmationPrice = confirmationDialog = null;
        if (purchaseCheck == null) return;
        foreach (var child in purchaseCheck.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == "Item" && confirmationItem == null) confirmationItem = child.gameObject;
            if (child.name == "DialogText") confirmationDialog = child.GetComponent<TMP_Text>();
        }
        if (confirmationItem != null)
        {
            foreach (var child in confirmationItem.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == "ItemImage") confirmationImage = child.GetComponent<Image>();
                if (child.name == "ItemText") confirmationName = child.GetComponent<TMP_Text>();
                if (child.name == "PriceText") confirmationPrice = child.GetComponent<TMP_Text>();
            }
        }
        purchaseDialog = confirmationDialog != null ? confirmationDialog.text : "상품을 구매하시겠습니까?";
    }

    private void UpdateConfirmationContent(ItemDefinition item, bool forEquip)
    {
        ResolveConfirmationContent();
        if (confirmationItem != null) confirmationItem.SetActive(!forEquip);
        if (confirmationDialog != null) confirmationDialog.text = forEquip ? "장착 하시겠습니까?" : purchaseDialog;
        if (forEquip) return;
        if (confirmationName != null) confirmationName.text = item.displayName;
        if (confirmationPrice != null) confirmationPrice.text = item.price.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        Sprite sprite = item.icon;
        // Prefer the product card's actual artwork, including cards whose catalog icon is empty.
        if (selectedProductButton != null)
            foreach (var image in selectedProductButton.GetComponentsInChildren<Image>(true))
                if (image.name == "ItemImage" && image.sprite != null) { sprite = image.sprite; break; }
        if (confirmationImage != null) confirmationImage.sprite = sprite;
    }
    private void RefreshConfirmation()
    {
        if (confirmButton == null || string.IsNullOrEmpty(pendingPurchaseId)) return;
        if (confirmLabel == null) confirmLabel = confirmButton.GetComponentInChildren<TMP_Text>(true);
        bool owned = ItemInventory.HasItem(pendingPurchaseId);
        bool equipped = ItemInventory.GetEquippedItemId(ItemCategory.Voice) == pendingPurchaseId;
        if (TryGetVoice(pendingPurchaseId, out var item)) UpdateConfirmationContent(item, owned || pendingEquip);
        if (confirmLabel != null) confirmLabel.text = equipped ? "장착중" : owned || pendingEquip ? "장착" : "구매";
        confirmButton.interactable = !equipped && !purchasing;
    }
    public void Refresh()
    {
        bool valid = ItemCatalog.Default != null && ItemCatalog.Default.TryGet(selectedItemId, out _);
        ItemDefinition item = null;
        if (valid) ItemCatalog.Default.TryGet(selectedItemId, out item);
        valid = valid && item.category == ItemCategory.Voice;
        bool owned = valid && ItemInventory.HasItem(selectedItemId);
        bool equipped = valid && ItemInventory.GetEquippedItemId(ItemCategory.Voice) == selectedItemId;
        if (itemNameText != null) itemNameText.text = valid ? item.displayName : "";
        if (itemIcon != null) itemIcon.sprite = valid ? item.icon : null;
        if (purchaseButton != null) purchaseButton.interactable = valid && !owned;
        if (equipButton != null) equipButton.interactable = owned && !equipped;
        RefreshConfirmation();
    }
}
