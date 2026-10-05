using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Put on an always-active Main scene object, outside the panels being toggled.
public sealed class LobbyPlayerInitializer : MonoBehaviour
{
    [SerializeField] private PlayerEquipment equipment;
    [SerializeField] private Transform characterSearchRoot;
    [Tooltip("Main의 커스터마이징 창. 이 창이 열려 있을 때만 미리보기 빗자루/지팡이를 표시합니다.")]
    [SerializeField] private GameObject customizationPanel;
    [SerializeField] private TMP_Text nicknameText;
    [Header("Local preview and bangs slider (0..1)")]
    [SerializeField] private PlayerAppearance appearance;
    [SerializeField] private Slider bangsLengthSlider;
    [SerializeField] private TMP_Text bangsLengthText;
    [SerializeField] private Slider bangsDirectionSlider;
    [SerializeField] private TMP_Text bangsDirectionText;
    [SerializeField] private Slider sideHairLengthSlider;
    [SerializeField] private TMP_Text sideHairLengthText;
    [Header("Hair color presets (button indices start at 0)")]
    [Tooltip("Example swatches only. Set the exact desired colors in the Inspector.")]
    [SerializeField] private Color[] hairColorPresets =
    {
        new Color32(157, 161, 173, 255),
        new Color32(156, 94, 100, 255),
        new Color32(175, 153, 132, 255),
        new Color32(189, 163, 164, 255),
        new Color32(61, 51, 64, 255),
        new Color32(174, 160, 190, 255),
        new Color32(62, 55, 60, 255),
        new Color32(112, 93, 93, 255),
        new Color32(184, 167, 164, 255)
    };
    [Header("Cloth color presets (button indices start at 0)")]
    [SerializeField] private Color[] clothColorPresets =
    {
        Color.white, new Color32(60, 60, 65, 255), new Color32(160, 65, 75, 255),
        new Color32(90, 120, 185, 255), new Color32(115, 160, 130, 255),
        new Color32(165, 130, 190, 255), new Color32(205, 170, 120, 255),
        new Color32(215, 160, 180, 255), new Color32(130, 115, 105, 255)
    };
    [Header("Eye texture presets (button indices start at 0)")]
    [Tooltip("Empty uses Resources/EyeTexturePresetTable. Use the same table on PlayerAppearance.")]
    [SerializeField] private EyeTexturePresetTable eyeTexturePresets;
    [Header("Connect your model's customization setters (Dynamic values)")]
    [SerializeField] private UnityEvent<int> onHairStylePreset = new UnityEvent<int>();
    [SerializeField] private UnityEvent<float> onBangsLength = new UnityEvent<float>();
    [SerializeField] private UnityEvent<float> onBangsDirection = new UnityEvent<float>();
    [SerializeField] private UnityEvent<float> onSideHairLength = new UnityEvent<float>();
    [SerializeField] private UnityEvent<float> onAhogeLength = new UnityEvent<float>();
    [SerializeField] private UnityEvent<Color> onHairColor = new UnityEvent<Color>();
    [SerializeField] private UnityEvent<Color> onClothColor = new UnityEvent<Color>();
    [SerializeField] private UnityEvent<Color> onEyeColor = new UnityEvent<Color>();
    [SerializeField] private UnityEvent<int> onHatIndex = new UnityEvent<int>();
    [SerializeField] private UnityEvent<int> onBroomIndex = new UnityEvent<int>();
    [SerializeField] private UnityEvent<int> onWandIndex = new UnityEvent<int>();
    [SerializeField] private UnityEvent onInitialized = new UnityEvent();
    [SerializeField] private UnityEvent onSaved = new UnityEvent();
    [SerializeField] private UnityEvent<string> onSaveFailed = new UnityEvent<string>();

    public PlayerConfig CurrentConfig { get; private set; }

    private void OnEnable()
    {
        RefreshEquipmentVisibility();
        BindSlider(bangsLengthSlider, SetBangsLength);
        BindSlider(bangsDirectionSlider, SetBangsDirection);
        BindSlider(sideHairLengthSlider, SetSideHairLength);
        DataConfig.Changed += RefreshCustomizationUI;
        RefreshCustomizationUI();
    }

    private void LateUpdate() => RefreshEquipmentVisibility();

    private void RefreshEquipmentVisibility()
    {
        if (equipment == null && characterSearchRoot != null)
            equipment = characterSearchRoot.GetComponentInChildren<PlayerEquipment>(true);
        if (equipment != null)
            equipment.SetFlightEquipmentVisible(customizationPanel != null && customizationPanel.activeInHierarchy);
    }

    private void OnDisable()
    {
        if (bangsLengthSlider != null) bangsLengthSlider.onValueChanged.RemoveListener(SetBangsLength);
        if (bangsDirectionSlider != null) bangsDirectionSlider.onValueChanged.RemoveListener(SetBangsDirection);
        if (sideHairLengthSlider != null) sideHairLengthSlider.onValueChanged.RemoveListener(SetSideHairLength);
        DataConfig.Changed -= RefreshCustomizationUI;
        if (equipment != null) equipment.SetFlightEquipmentVisible(false);
    }

    private bool CanCustomize => DatabaseManager.Instance != null &&
        DatabaseManager.Instance.IsDataConfigReady && DatabaseManager.Instance.HasLoadedProfile;

    private static float UnitValue(float value)
        => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp01(value);

    private static void BindSlider(Slider slider, UnityAction<float> listener)
    {
        if (slider == null) return;
        slider.wholeNumbers = false;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.onValueChanged.AddListener(listener);
    }

    // Slider input only changes data. PlayerAppearance reacts to DataConfig.Changed.
    public void SetBangsLength(float value)
    {
        if (CanCustomize) DataConfig.bangsLength = UnitValue(value);
    }

    public void SetBangsDirection(float value)
    {
        if (CanCustomize) DataConfig.bangsDirection = UnitValue(value);
    }

    public void SetSideHairLength(float value)
    {
        if (CanCustomize) DataConfig.sideHairLength = UnitValue(value);
    }

    // Button.OnClick -> SetHairColorPreset(int). No script is needed on each button.
    public void SetHairColorPreset(int index)
    {
        if (hairColorPresets == null || index < 0 || index >= hairColorPresets.Length)
        {
            Debug.LogWarning($"Hair color preset index is out of range: {index}", this);
            return;
        }
        SetHairColor(hairColorPresets[index]);
    }

    public void SetHairColor(Color color)
    {
        DatabaseManager database = DatabaseManager.Instance;
        if (database == null || !database.IsDataConfigReady || !database.HasLoadedProfile) return;
        // Swatches choose RGB, not transparency; alpha 0 must not hide the player's hair.
        color.a = 1f;
        PlayerConfig config = DataConfig.GetPlayerConfig();
        config.hairColor = color;
        DataConfig.hairColor = config.Sanitized().hairColor;
    }

    public void SetClothColorPreset(int index)
    {
        if (clothColorPresets == null || index < 0 || index >= clothColorPresets.Length)
        {
            Debug.LogWarning($"Cloth color preset index is out of range: {index}", this);
            return;
        }
        SetClothColor(clothColorPresets[index]);
    }

    public void SetEyeColorPreset(int index)
    {
        if (!CanCustomize) return;
        EyeTexturePresetTable table = eyeTexturePresets != null ? eyeTexturePresets : EyeTexturePresetTable.Default;
        if (table == null || !table.TryGetPreset(index, out EyeTexturePresetTable.Preset preset))
        {
            Debug.LogWarning($"Eye color preset index is out of range: {index}", this);
            return;
        }
        Color key = preset.savedColorKey;
        key.a = 1f;
        DataConfig.eyeColor = key;
    }

    public void SetClothColor(Color color)
    {
        if (!CanCustomize) return;
        color.a = 1f;
        PlayerConfig config = DataConfig.GetPlayerConfig();
        config.clothColor = color;
        DataConfig.clothColor = config.Sanitized().clothColor;
    }

    public void SetEyeColor(Color color)
    {
        if (!CanCustomize) return;
        EyeTexturePresetTable table = eyeTexturePresets != null ? eyeTexturePresets : EyeTexturePresetTable.Default;
        if (table == null || !table.TryResolve(color, out EyeTexturePresetTable.Preset preset)) return;
        Color key = preset.savedColorKey;
        key.a = 1f;
        DataConfig.eyeColor = key;
    }

    private void RefreshCustomizationUI()
    {
        bool ready = CanCustomize;
        if (ready) CurrentConfig = DataConfig.GetPlayerConfig();
        RefreshSlider(bangsLengthSlider, bangsLengthText, DataConfig.bangsLength, ready);
        RefreshSlider(bangsDirectionSlider, bangsDirectionText, DataConfig.bangsDirection, ready);
        RefreshSlider(sideHairLengthSlider, sideHairLengthText, DataConfig.sideHairLength, ready);
    }

    private static void RefreshSlider(Slider slider, TMP_Text text, float value, bool ready)
    {
        if (slider != null)
        {
            slider.interactable = ready;
            if (ready) slider.SetValueWithoutNotify(UnitValue(value));
        }
        if (ready && text != null) text.text = Mathf.RoundToInt(UnitValue(value) * 100f).ToString();
    }

    public bool InitializeFromLoadedData()
    {
        DatabaseManager database = DatabaseManager.Instance;
        if (database == null || !database.ApplyLoadedSettingToDataConfig()) return false;
        RefreshFromDataConfig();
        return true;
    }

    // UI/model setters should not save while processing these initialization events.
    public void RefreshFromDataConfig()
    {
        if (DatabaseManager.Instance == null || !DatabaseManager.Instance.IsDataConfigReady) return;
        CurrentConfig = DataConfig.GetPlayerConfig();
        if (equipment == null && characterSearchRoot != null)
            equipment = characterSearchRoot.GetComponentInChildren<PlayerEquipment>(true);
        if (appearance == null && characterSearchRoot != null)
            appearance = characterSearchRoot.GetComponentInChildren<PlayerAppearance>(true);
        if (appearance == null && equipment != null)
            appearance = equipment.GetComponent<PlayerAppearance>();
        // Login applies DataConfig silently; explicitly initialize and then observe edits.
        if (appearance != null) appearance.BindToDataConfig();
        RefreshCustomizationUI();
        if (nicknameText != null) nicknameText.text = DataConfig.playerName;
        if (equipment != null)
            equipment.ApplyLoadout((HatType)CurrentConfig.hatIndex, (BroomType)CurrentConfig.broomIndex,
                (MagicType)DataConfig.magic1Index, (MagicType)DataConfig.magic2Index);
        onHairStylePreset.Invoke(CurrentConfig.hairStylePreset);
        onBangsLength.Invoke(CurrentConfig.bangsLength);
        onBangsDirection.Invoke(CurrentConfig.bangsDirection);
        onSideHairLength.Invoke(CurrentConfig.sideHairLength);
        onAhogeLength.Invoke(CurrentConfig.ahogeLength);
        onHairColor.Invoke(CurrentConfig.hairColor);
        onClothColor.Invoke(CurrentConfig.clothColor);
        onEyeColor.Invoke(CurrentConfig.eyeColor);
        onHatIndex.Invoke(CurrentConfig.hatIndex);
        onBroomIndex.Invoke(CurrentConfig.broomIndex);
        onWandIndex.Invoke(CurrentConfig.wandIndex);
        onInitialized.Invoke();
        RefreshEquipmentVisibility();
    }

    // Wire the customization Save button here, after writing the selection to DataConfig.
    public void SaveCustomization()
    {
        DatabaseManager database = DatabaseManager.Instance;
        if (database == null || !database.IsDataConfigReady || !database.HasLoadedProfile)
        {
            onSaveFailed.Invoke("로비 데이터 초기화를 먼저 완료해주세요.");
            return;
        }
        database.SaveCurrentSetting(DataConfig.playerName);
        if (database.LastSaveSucceeded) onSaved.Invoke();
        else onSaveFailed.Invoke(database.LastError);
    }
}
