using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Presentation only. Reuse on any magic detail panel; never equips or changes a loadout.
// Panel visibility stays with the owning screen / designer-authored Button.OnClick events.
[DisallowMultipleComponent]
public sealed class MagicDescriptionUI : MonoBehaviour
{
    [Header("Shared data (empty = Resources/MagicStatTable)")]
    [SerializeField] private MagicStatTable magicTable;

    [Header("Optional icon / name / description")]
    [SerializeField] private Image magicIcon;
    [SerializeField] private TMP_Text magicNameText;
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField] private TMP_Text categoryText;
    [SerializeField] private TMP_Text castTypeText;
    [SerializeField] private TMP_Text featuresText;

    [Header("Read-only Sliders (0..1 display values map to each Slider's range)")]
    [SerializeField] private Slider damageSlider;
    [SerializeField] private Slider aimTimeSlider;
    [SerializeField] private Slider rangeSlider;
    [SerializeField] private Slider manaCostSlider;

    [Header("Optional ratings beside Sliders")]
    [SerializeField] private TMP_Text damageLabel;
    [SerializeField] private TMP_Text aimTimeLabel;
    [SerializeField] private TMP_Text rangeLabel;
    [SerializeField] private TMP_Text manaCostLabel;

    [Header("Optional fixed-stage perks (empty table = Resources/PerkStatTable)")]
    [SerializeField] private PerkStatTable perkTable;
    [Tooltip("선택한 마법의 1단계 특전 설명. 실제 획득 여부와 관계없이 강화 효과를 미리 보여줍니다.")]
    [SerializeField] private TMP_Text perk1DescriptionText;
    [Tooltip("선택한 마법의 2단계 특전 설명. 수치 치환은 특전 선택/Tab UI와 동일합니다.")]
    [SerializeField] private TMP_Text perk2DescriptionText;

    private MagicType currentMagic = MagicType.None;
    private MagicStatTable currentTable;
    private PerkStatTable currentPerkTable;

    // Can be called from Button.OnClick using a MagicType ID, not a table array index.
    // A prefab-local button may follow its screen's SelectMagic callback. Keep that
    // screen's table override instead of switching back to a different serialized table.
    public void ShowMagic(int magicId) => ShowMagic((MagicType)magicId, currentTable != null ? currentTable : magicTable, currentPerkTable);

    // Screens can supply their configured table so all previews use the same data as their icons.
    public void ShowMagic(MagicType magic, MagicStatTable sourceTable, PerkStatTable sourcePerkTable = null)
    {
        currentMagic = magic;
        currentTable = sourceTable != null ? sourceTable : magicTable != null ? magicTable : MagicStatTable.Default;
        bool valid = currentTable != null && magic >= MagicType.Fire && magic <= MagicType.Smoke;
        MagicStatEntry stats = valid ? currentTable.GetStats(magic) : default;

        if (magicIcon != null)
        {
            Sprite icon = valid ? currentTable.GetIcon(magic) : null;
            if (magicIcon.sprite != icon) magicIcon.sprite = icon;
            magicIcon.enabled = icon != null;
        }
        SetText(magicNameText, valid ? stats.displayName : null);
        SetText(descriptionText, valid ? stats.description : null);
        SetText(categoryText, valid ? stats.displayCategory : null);
        SetText(castTypeText, valid ? stats.displayCastType : null);
        SetText(featuresText, valid ? stats.displayFeatures : null);
        SetSlider(damageSlider, stats.displayDamage);
        SetSlider(aimTimeSlider, stats.displayAimTime);
        SetSlider(rangeSlider, stats.displayRange);
        SetSlider(manaCostSlider, stats.displayManaCost);
        SetText(damageLabel, valid ? stats.displayDamageLabel : null);
        SetText(aimTimeLabel, valid ? stats.displayAimTimeLabel : null);
        SetText(rangeLabel, valid ? stats.displayRangeLabel : null);
        SetText(manaCostLabel, valid ? stats.displayManaCostLabel : null);
        ShowPerks(valid ? magic : MagicType.None, sourcePerkTable);
    }

    // Refresh after editing a table during Play Mode, without selecting/equipping again.
    public void Refresh() => ShowMagic(currentMagic, currentTable, currentPerkTable);
    public void Clear() => ShowMagic(MagicType.None, currentTable, currentPerkTable);

    private void ShowPerks(MagicType magic, PerkStatTable sourceTable)
    {
        // Main/custom panels do not need to assign these optional fields.
        if (perk1DescriptionText == null && perk2DescriptionText == null) return;
        currentPerkTable = sourceTable != null ? sourceTable : perkTable != null ? perkTable : PerkStatTable.Default;
        SetPerkDescription(perk1DescriptionText, PerkRules.Spell(magic, 1));
        SetPerkDescription(perk2DescriptionText, PerkRules.Spell(magic, 2));
    }

    private void SetPerkDescription(TMP_Text target, PerkId id)
    {
        if (target == null) return;
        PerkDefinition perk = id != PerkId.None && currentPerkTable != null ? currentPerkTable.Get(id) : null;
        // Reuse the live perk UI's formatter; do not leak {percent}/{seconds} tokens
        // or retain another spell's description when a definition is missing.
        SetText(target, perk != null ? BattlePerkUI.Description(perk) : null);
    }

    private static void SetSlider(Slider slider, float value)
    {
        if (slider == null) return;
        slider.interactable = false;
        slider.wholeNumbers = false;
        // Never invoke user OnValueChanged handlers while displaying a spell's information.
        float fill = float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp01(value);
        slider.SetValueWithoutNotify(slider.minValue + (slider.maxValue - slider.minValue) * fill);
    }

    private static void SetText(TMP_Text target, string value)
    {
        value ??= string.Empty;
        if (target != null && target.text != value) target.text = value;
    }
}
