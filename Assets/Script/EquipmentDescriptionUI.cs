using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Display only: changing a preview never equips or saves equipment.
[DisallowMultipleComponent]
public sealed class EquipmentDescriptionUI : MonoBehaviour
{
    [SerializeField] private EquipmentStatTable equipmentTable;
    [Header("Shared content")]
    [SerializeField] private Image itemIcon;
    [SerializeField] private TMP_Text itemNameText;
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField] private TMP_Text categoryText;
    [SerializeField] private TMP_Text secondaryCategoryText;
    [SerializeField] private Button equipButton;
    [SerializeField] private TMP_Text equipButtonText;
    public Button EquipButton => equipButton;
    [Header("Hat: maximum MP / MP recovery")]
    [SerializeField] private Slider maxApSlider;
    [SerializeField] private Slider apRecoverySlider;
    [SerializeField] private TMP_Text maxApText;
    [SerializeField] private TMP_Text apRecoveryText;
    [Header("Broom: HP / speed / turning / boost MP cost")]
    [SerializeField] private Slider maxHpSlider;
    [SerializeField] private Slider speedSlider;
    [SerializeField] private Slider turnSpeedSlider;
    [SerializeField] private Slider boostCostSlider;
    [SerializeField] private TMP_Text maxHpText;
    [SerializeField] private TMP_Text speedText;
    [SerializeField] private TMP_Text turnSpeedText;
    [SerializeField] private TMP_Text boostCostText;

    private EquipmentStatTable Table => equipmentTable != null ? equipmentTable :
        equipmentTable = Resources.Load<EquipmentStatTable>("EquipmentStatTable");

    public void ShowHat(HatType hat, Sprite icon, string fallbackName, string fallbackDescription)
    {
        HatStatEntry stats = Table != null ? Table.GetHatStats(hat) : default;
        ShowContent(icon, Choose(stats.displayName, fallbackName),
            Choose(stats.description, fallbackDescription), "모자");
        SetSlider(maxApSlider, stats.displayMaxAp);
        SetSlider(apRecoverySlider, stats.displayApRecovery);
        SetText(maxApText, Choose(stats.displayMaxApLabel, "중간"));
        SetText(apRecoveryText, Choose(stats.displayApRecoveryLabel, "중간"));
    }

    public void ShowBroom(BroomType broom, Sprite icon, string fallbackName, string fallbackDescription)
    {
        BroomStatEntry stats = Table != null ? Table.GetBroomStats(broom) : default;
        ShowContent(icon, Choose(stats.displayName, fallbackName),
            Choose(stats.description, fallbackDescription), "빗자루");
        SetSlider(maxHpSlider, stats.displayMaxHp);
        SetSlider(speedSlider, stats.displaySpeed);
        SetSlider(turnSpeedSlider, stats.displayTurnSpeed);
        SetSlider(boostCostSlider, stats.displayBoostCost);
        SetText(maxHpText, Choose(stats.displayMaxHpLabel, "중간"));
        SetText(speedText, Choose(stats.displaySpeedLabel, "중간"));
        SetText(turnSpeedText, Choose(stats.displayTurnSpeedLabel, "중간"));
        SetText(boostCostText, Choose(stats.displayBoostCostLabel, "중간"));
    }

    private void ShowContent(Sprite icon, string title, string description, string category)
    {
        if (itemIcon != null) { itemIcon.sprite = icon; itemIcon.enabled = icon != null; }
        SetText(itemNameText, title);
        SetText(descriptionText, description);
        SetText(categoryText, category);
        SetText(secondaryCategoryText, string.Empty);
    }

    private static string Choose(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
    public void SetEquipState(string caption, bool interactable)
    {
        if (equipButton == null) return;
        equipButton.interactable = interactable;
        if (equipButtonText == null) equipButtonText = equipButton.GetComponentInChildren<TMP_Text>(true);
        SetText(equipButtonText, caption);
    }
    private static void SetText(TMP_Text target, string value)
    {
        if (target != null) target.text = value ?? string.Empty;
    }
    private static void SetSlider(Slider target, float value)
    {
        if (target == null) return;
        target.interactable = false;
        target.wholeNumbers = false;
        if (float.IsNaN(value) || float.IsInfinity(value)) value = 0;
        target.SetValueWithoutNotify(Mathf.Lerp(target.minValue, target.maxValue, Mathf.Clamp01(value)));
    }
}
