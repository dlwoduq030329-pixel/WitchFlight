using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent, RequireComponent(typeof(Button))]
public sealed class EmoteShopItemButton : MonoBehaviour
{
    [Tooltip("EmoteTable의 숫자가 아닌 ItemCatalog 상품 ID입니다. 예: 2.1")]
    [SerializeField] private string itemId;
    [SerializeField] private EmoteShopUI shop;
    [SerializeField] private Image itemIcon;
    [SerializeField] private TMP_Text itemName, priceText;
    [SerializeField] private GameObject ownedCheck;
    private Button button;

    private void Awake()
    {
        button = GetComponent<Button>();
        if (shop == null) shop = GetComponentInParent<EmoteShopUI>(true);
        // Resolve authored children once. Duplicating a card only requires changing Item Id.
        foreach (var child in GetComponentsInChildren<Transform>(true))
        {
            if (child.name == "ItemImage" && itemIcon == null) itemIcon = child.GetComponent<Image>();
            if (child.name == "ItemText" && itemName == null) itemName = child.GetComponent<TMP_Text>();
            if (child.name == "PriceText" && priceText == null) priceText = child.GetComponent<TMP_Text>();
            if (child.name == "CheckImage" && ownedCheck == null) ownedCheck = child.gameObject;
        }
        button.onClick.AddListener(Select);
    }
    private void OnEnable()
    {
        DataConfig.InventoryChanged += Refresh;
        DataConfig.SnapshotApplied += Refresh;
        Refresh();
    }
    private void OnDisable()
    {
        DataConfig.InventoryChanged -= Refresh;
        DataConfig.SnapshotApplied -= Refresh;
    }
    private void OnDestroy() { if (button != null) button.onClick.RemoveListener(Select); }
    private void Select() { if (shop != null) shop.SelectItem(itemId); }
    public void Refresh()
    {
        bool valid = EmoteShopUI.TryGetItem(itemId, out var item);
        bool owned = valid && ItemInventory.OwnsEmote(item.emoteId);
        if (itemIcon != null) { itemIcon.sprite = valid ? EmoteShopUI.Icon(item) : null; itemIcon.enabled = itemIcon.sprite != null; }
        if (itemName != null) itemName.text = valid ? item.displayName : "";
        if (priceText != null) priceText.text = !valid ? "" : owned ? "구매완료" : item.price.ToString("N0");
        if (ownedCheck != null) ownedCheck.SetActive(owned);
        if (button != null) button.interactable = valid && !owned;
    }
}
