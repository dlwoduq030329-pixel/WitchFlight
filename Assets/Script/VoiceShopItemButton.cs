using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Each card retains its own identity. UI references are resolved once, not every frame.
[DisallowMultipleComponent]
[RequireComponent(typeof(Button))]
public sealed class VoiceShopItemButton : MonoBehaviour
{
    [Tooltip("ItemCatalog의 고정 ID. 기존 SelectVoice 연결에서 자동 설정됩니다.")]
    [SerializeField, HideInInspector] private string itemId;

    public void Configure(VoiceShopUI owner, string id)
    {
        shop = owner;
        itemId = id;
        ResolveReferences();
        Refresh();
    }

    private Button button;
    private TMP_Text priceLabel;
    private GameObject checkImage;
    private VoiceShopUI shop;
    private bool automaticClick;

    private void Awake() => ResolveReferences();

    private void ResolveReferences()
    {
        if (button == null) button = GetComponent<Button>();
        if (shop == null) shop = GetComponentInParent<VoiceShopUI>(true);
        if (checkImage == null)
            foreach (var child in GetComponentsInChildren<Transform>(true))
                if (child != transform && child.name == "CheckImage" && child.GetComponentInParent<Button>(true) == button)
                {
                    checkImage = child.gameObject;
                    break;
                }
        if (priceLabel != null) return;
        // PriceText is the existing StoreUI prefab convention. Avoid ItemText/product names.
        TMP_Text fallback = null;
        int fallbackCount = 0;
        foreach (var label in GetComponentsInChildren<TMP_Text>(true))
        {
            if (label.GetComponentInParent<Button>() != button) continue;
            if (string.Equals(label.name, "PriceText", System.StringComparison.OrdinalIgnoreCase))
            {
                priceLabel = label;
                return;
            }
            if (label.name == "Text" || label.name == "Text (TMP)")
            {
                fallback = label;
                fallbackCount++;
            }
        }
        if (fallbackCount == 1) priceLabel = fallback;
    }

    private void OnEnable()
    {
        ResolveReferences();
        DataConfig.InventoryChanged += Refresh;
        DataConfig.MoneyChanged += Refresh;
        // Existing explicit SelectVoice bindings must not cause a first click to count twice.
        automaticClick = true;
        for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
        {
            if (button.onClick.GetPersistentTarget(i) is VoiceShopUI &&
                button.onClick.GetPersistentListenerState(i) != UnityEngine.Events.UnityEventCallState.Off &&
                (button.onClick.GetPersistentMethodName(i) == nameof(VoiceShopUI.SelectVoice) ||
                 button.onClick.GetPersistentMethodName(i) == nameof(VoiceShopUI.ClickSelectedVoice)))
                automaticClick = false;
        }
        if (automaticClick) button.onClick.AddListener(Select);
        Refresh();
    }

    private void OnDisable()
    {
        DataConfig.InventoryChanged -= Refresh;
        DataConfig.MoneyChanged -= Refresh;
        if (button != null && automaticClick) button.onClick.RemoveListener(Select);
    }

    private void Select()
    {
        if (shop == null) shop = GetComponentInParent<VoiceShopUI>(true);
        if (shop != null) shop.SelectVoice(itemId);
        else Debug.LogWarning("VoiceShopItemButton: 상위 StoreUI에 VoiceShopUI를 추가해주세요.", this);
    }

    public void Refresh()
    {
        if (priceLabel == null) ResolveReferences();
        if (ItemCatalog.Default == null || !ItemCatalog.Default.TryGet(itemId, out var item) || item.category != ItemCategory.Voice)
        {
            if (priceLabel != null) priceLabel.text = "";
            if (checkImage != null) checkImage.SetActive(false);
            return;
        }
        bool equipped = ItemInventory.GetEquippedItemId(ItemCategory.Voice) == itemId;
        if (checkImage != null) checkImage.SetActive(equipped);
        if (priceLabel != null) priceLabel.text = equipped ? "장착중" : ItemInventory.HasItem(itemId) ? "보유중" :
            item.price.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
    }
}
