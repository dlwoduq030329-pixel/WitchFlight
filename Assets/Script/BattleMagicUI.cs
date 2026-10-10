using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;

// Local-only view: binds the local Player's loadout and replicated cooldowns.
// Attach to an active Battle Canvas object and assign existing UI; creates no objects.
[DisallowMultipleComponent]
public sealed class BattleMagicUI : MonoBehaviour
{
    [Header("Magic 1 UI")]
    [FormerlySerializedAs("magic1Image")]
    [SerializeField] private Image magic1BackImage;
    [SerializeField] private Image magic1SliderImage;
    [SerializeField] private Slider magic1CooldownSlider;
    [Header("Magic 2 UI")]
    [FormerlySerializedAs("magic2Image")]
    [SerializeField] private Image magic2BackImage;
    [SerializeField] private Image magic2SliderImage;
    [SerializeField] private Slider magic2CooldownSlider;
    [Header("Parry UI (fixed slot 3)")]
    [FormerlySerializedAs("parryImage")]
    [SerializeField] private Image parryBackImage;
    [SerializeField] private Image parrySliderImage;
    [SerializeField] private Slider parryCooldownSlider;

    [Header("Shared magic icons")]
    [Tooltip("모든 마법 아이콘과 Parry Icon은 이 테이블에서 가져옵니다. 비우면 Resources/MagicStatTable을 사용합니다.")]
    [SerializeField] private MagicStatTable magicTable;

    private Player owner;
    private MagicType displayedMagic1, displayedMagic2;
    private readonly float[] previousRemaining = { -1f, -1f, -1f };

    private bool CanBind(Player player)
    {
        return player != null && player.Object != null && player.Object.IsValid &&
            player.Object.HasInputAuthority && player.Runner != null && player.Runner.IsRunning &&
            player.GetMagicInSlot(1) != MagicType.None && BattleManager.Instance != null &&
            BattleManager.Instance.IsGameplayActive;
    }

    private void OnEnable()
    {
        Clear();
        magicInit();
    }

    private void Update()
    {
        Player local = Player.LocalPlayer;
        if (!CanBind(local))
        {
            Clear();
            return;
        }
        if (owner != local || displayedMagic1 != local.GetMagicInSlot(1) || displayedMagic2 != local.GetMagicInSlot(2))
            magicInit();
        magiccoolDown();
    }

    // Auto-called when gameplay starts, the local player respawns, or loadout changes.
    // Can also be wired to a UnityEvent. Sprites are mapped by enum in MagicStatTable.
    public void magicInit()
    {
        Player local = Player.LocalPlayer;
        if (!CanBind(local)) { Clear(); return; }
        owner = local;
        displayedMagic1 = owner.GetMagicInSlot(1);
        displayedMagic2 = owner.GetMagicInSlot(2);
        ApplyMagicSprites(displayedMagic1, magic1BackImage, magic1SliderImage);
        ApplyMagicSprites(displayedMagic2, magic2BackImage, magic2SliderImage);
        ApplyParrySprites();
        for (int i = 0; i < previousRemaining.Length; i++) previousRemaining[i] = -1f;
        magiccoolDown();
    }

    private void ApplyMagicSprites(MagicType magic, Image backImage, Image sliderImage)
    {
        if (magicTable == null) magicTable = MagicStatTable.Default;
        Sprite tableIcon = magicTable != null ? magicTable.GetIcon(magic) : null;
        SetIcon(backImage, tableIcon);
        SetIcon(sliderImage, tableIcon);
    }

    private void ApplyParrySprites()
    {
        if (magicTable == null) magicTable = MagicStatTable.Default;
        Sprite icon = magicTable != null ? magicTable.parryIcon : null;
        SetIcon(parryBackImage, icon);
        SetIcon(parrySliderImage, icon);
    }

    // Reads actual remaining time every frame. Rejected casts never start a fake timer.
    // Re-enabling this UI resumes the remaining time instead of restarting the cooldown.
    public void magiccoolDown()
    {
        if (!CanBind(owner)) return;
        UpdateSlider(magic1CooldownSlider, 1);
        UpdateSlider(magic2CooldownSlider, 2);
        UpdateSlider(parryCooldownSlider, 3);
    }

    private void UpdateSlider(Slider slider, int slot)
    {
        float duration = owner.GetMagicCooldownDuration(slot);
        float remaining = owner.GetMagicCooldownRemaining(slot);
        float progress = duration > 0f ? 1f - Mathf.Clamp01(remaining / duration) : 1f;
        // First observed frame of a new cast starts empty. A re-enabled UI instead
        // resumes the actual progress because its previousRemaining is -1.
        float previous = previousRemaining[slot - 1];
        if (previous >= 0f && remaining > previous && remaining > 0f) progress = 0f;
        previousRemaining[slot - 1] = remaining;
        SetSlider(slider, progress);
    }

    private static void SetSlider(Slider slider, float value)
    {
        if (slider == null) return;
        slider.interactable = false;
        slider.wholeNumbers = false;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.SetValueWithoutNotify(value);
    }

    private static void SetIcon(Image image, Sprite icon)
    {
        if (image == null) return;
        image.sprite = icon;
        image.enabled = icon != null;
    }

    private void Clear()
    {
        owner = null;
        SetIcon(magic1BackImage, null);
        SetIcon(magic1SliderImage, null);
        SetIcon(magic2BackImage, null);
        SetIcon(magic2SliderImage, null);
        SetIcon(parryBackImage, null);
        SetIcon(parrySliderImage, null);
        for (int i = 0; i < previousRemaining.Length; i++) previousRemaining[i] = -1f;
        SetSlider(magic1CooldownSlider, 0f);
        SetSlider(magic2CooldownSlider, 0f);
        SetSlider(parryCooldownSlider, 0f);
    }

    private void OnDisable() => Clear();
}
