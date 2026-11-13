using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Uses the existing store confirmation panel. No UI creation or per-frame scans.
public sealed class EmoteShopUI : MonoBehaviour
{
    [SerializeField] private GameObject purchaseCheck;
    [SerializeField] private Button confirmButton, cancelButton;
    [SerializeField] private VoiceShopUI voiceShop;
    [SerializeField] private DOTweenAnimation confirmationTween;
    [SerializeField] private StorePurchaseFeedback purchaseFeedback;
    [SerializeField] private UnityEngine.Events.UnityEvent onPurchaseSucceeded = new();
    private static EmoteShopUI active;
    private string pendingId;
    private TMP_Text dialog, itemName, price, confirmLabel;
    private Image itemIcon;
    private GameObject itemRoot;
    private bool purchasing;

    private void Awake()
    {
        if (purchaseCheck == null) return;
        foreach (var child in purchaseCheck.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == "DialogText") dialog = child.GetComponent<TMP_Text>();
            if (child.name == "Item") itemRoot = child.gameObject;
            if (child.name == "ItemText") itemName = child.GetComponent<TMP_Text>();
            if (child.name == "PriceText") price = child.GetComponent<TMP_Text>();
            if (child.name == "ItemImage") itemIcon = child.GetComponent<Image>();
        }
        if (confirmButton != null) confirmLabel = confirmButton.GetComponentInChildren<TMP_Text>(true);
    }
    private void OnDisable() => CancelPurchase();
    public static void CancelActive() { if (active != null) active.CancelPurchase(); }
    public static bool TryGetItem(string id, out ItemDefinition item)
    {
        item = null;
        return ItemCatalog.Default != null && ItemCatalog.Default.TryGet(id, out item) &&
            item.category == ItemCategory.Emote && EmoteTable.Default != null && EmoteTable.Default.TryGet(item.emoteId, out _);
    }
    public static Sprite Icon(ItemDefinition item) => item.icon != null ? item.icon :
        EmoteTable.Default != null && EmoteTable.Default.TryGet(item.emoteId, out var entry) ? entry.icon : null;

    public void SelectItem(string id)
    {
        if (purchasing || !TryGetItem(id, out var item) || ItemInventory.OwnsEmote(item.emoteId)) return;
        if (purchaseCheck == null || confirmButton == null || cancelButton == null) return;
        voiceShop?.CancelPurchase();
        CancelActive();
        active = this;
        pendingId = id;
        if (itemRoot != null) itemRoot.SetActive(true);
        if (dialog != null) dialog.text = "이모티콘을 구매하시겠습니까?";
        if (itemName != null) itemName.text = item.displayName;
        if (price != null) price.text = item.price.ToString("N0");
        if (itemIcon != null) itemIcon.sprite = Icon(item);
        if (confirmLabel != null) confirmLabel.text = "구매";
        confirmButton.interactable = true;
        confirmButton.onClick.AddListener(ConfirmPurchase);
        cancelButton.onClick.AddListener(CancelPurchase);
        purchaseCheck.SetActive(true);
        if (confirmationTween != null) confirmationTween.DORestart();
    }
    public void ConfirmPurchase()
    {
        // Existing confirm animations may hide the panel before runtime listeners execute.
        if (purchasing || active != this || string.IsNullOrEmpty(pendingId)) return;
        var db = DatabaseManager.Instance;
        string message = "로그인 후 구매할 수 있습니다.";
        bool success = false;
        purchasing = true;
        try { if (db != null) success = db.TryPurchaseEmote(pendingId, out message); }
        finally { purchasing = false; }
        if (success)
        {
            CancelPurchase();
            purchaseFeedback?.PrepareSuccess();
            onPurchaseSucceeded.Invoke();
        }
        else if (message == "재화가 부족합니다." && purchaseFeedback != null)
        {
            CancelPurchase();
            purchaseFeedback.ShowInsufficientFunds();
        }
        else
        {
            if (dialog != null) dialog.text = message;
            purchaseCheck.SetActive(true);
            if (confirmationTween != null) confirmationTween.DORestart();
        }
    }
    public void CancelPurchase()
    {
        if (confirmButton != null) confirmButton.onClick.RemoveListener(ConfirmPurchase);
        if (cancelButton != null) cancelButton.onClick.RemoveListener(CancelPurchase);
        bool wasActive = active == this;
        if (wasActive) active = null;
        pendingId = null;
        if (wasActive && purchaseCheck != null) purchaseCheck.SetActive(false);
    }
}
