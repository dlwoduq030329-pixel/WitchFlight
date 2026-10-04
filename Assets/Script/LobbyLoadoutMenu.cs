using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Optional designer-owned UI; selection still uses DataConfig -> PlayerData.
public sealed class LobbyLoadoutMenu : MonoBehaviour
{
    [Header("Scene UI (no automatic windows or controls)")]
    [SerializeField] private GameObject lobbyUiRoot;
    [SerializeField] private GameObject loadoutPanel;
    [SerializeField] private Button toggleButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private TMP_Dropdown hatDropdown;
    [SerializeField] private TMP_Dropdown broomDropdown;
    [SerializeField] private TMP_Dropdown magic1Dropdown;
    [SerializeField] private TMP_Dropdown magic2Dropdown;
    [SerializeField] private Slider hairLengthSlider;
    [Header("Optional descriptions")]
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text hatStatsText;
    [SerializeField] private TMP_Text broomStatsText;
    [SerializeField] private TMP_Text magic1StatsText;
    [SerializeField] private TMP_Text magic2StatsText;
    [SerializeField] private TMP_Text hairLengthText;

    private NetworkGameManager manager;
    private EquipmentStatTable equipmentTable;
    private MagicStatTable magicTable;
    private bool expanded;
    private bool CanEdit => SceneManager.GetActiveScene().buildIndex == 0 && manager != null && !manager.IsMatching;

    private void Awake()
    {
        manager = GetComponent<NetworkGameManager>();
        equipmentTable = Resources.Load<EquipmentStatTable>("EquipmentStatTable");
        magicTable = Resources.Load<MagicStatTable>("MagicStatTable");
    }

    private void OnEnable()
    {
        if (toggleButton != null) toggleButton.onClick.AddListener(TogglePanel);
        if (closeButton != null) closeButton.onClick.AddListener(ClosePanel);
        if (hatDropdown != null) hatDropdown.onValueChanged.AddListener(SetHat);
        if (broomDropdown != null) broomDropdown.onValueChanged.AddListener(SetBroom);
        if (magic1Dropdown != null) magic1Dropdown.onValueChanged.AddListener(SetMagic1);
        if (magic2Dropdown != null) magic2Dropdown.onValueChanged.AddListener(SetMagic2);
        if (hairLengthSlider != null) hairLengthSlider.onValueChanged.AddListener(SetHairLength);
        RefreshUI();
    }

    private void Update() => RefreshUI();
    public void TogglePanel() { expanded = !expanded; RefreshUI(); }
    public void OpenPanel() { expanded = true; RefreshUI(); }
    public void ClosePanel() { expanded = false; RefreshUI(); }

    // Dropdown indices are zero-based. Stored enum IDs remain one-based.
    public void SetHat(int index) { if (CanEdit) DataConfig.hatIndex = Mathf.Clamp(index, 0, 2) + 1; }
    public void SetBroom(int index) { if (CanEdit) DataConfig.broomIndex = Mathf.Clamp(index, 0, 2) + 1; }
    public void SetMagic1(int index) { if (CanEdit) DataConfig.magic1Index = Mathf.Clamp(index, 0, 9) + 1; }
    public void SetMagic2(int index) { if (CanEdit) DataConfig.magic2Index = Mathf.Clamp(index, 0, 9) + 1; }
    public void SetHairLength(float value)
    {
        if (CanEdit && DatabaseManager.Instance != null && DatabaseManager.Instance.IsDataConfigReady)
            DataConfig.bangsLength = float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp01(value);
    }

    private void RefreshUI()
    {
        bool lobby = SceneManager.GetActiveScene().buildIndex == 0;
        SetVisible(lobbyUiRoot, lobby);
        SetVisible(loadoutPanel, lobby && expanded);
        if (!lobby) return;
        bool canEdit = CanEdit;
        if (toggleButton != null) toggleButton.interactable = true;
        int hat = ValidHat(DataConfig.hatIndex);
        int broom = ValidBroom(DataConfig.broomIndex);
        int magic1 = ValidMagic(DataConfig.magic1Index, (int)MagicType.Fire);
        int magic2 = ValidMagic(DataConfig.magic2Index, (int)MagicType.Ice);
        BindDropdown(hatDropdown, hat - 1, canEdit);
        BindDropdown(broomDropdown, broom - 1, canEdit);
        BindDropdown(magic1Dropdown, magic1 - 1, canEdit);
        BindDropdown(magic2Dropdown, magic2 - 1, canEdit);
        float hair = Mathf.Clamp01(DataConfig.bangsLength);
        if (hairLengthSlider != null)
        {
            hairLengthSlider.wholeNumbers = false;
            hairLengthSlider.minValue = 0f;
            hairLengthSlider.maxValue = 1f;
            hairLengthSlider.interactable = canEdit && DatabaseManager.Instance != null && DatabaseManager.Instance.IsDataConfigReady;
            hairLengthSlider.SetValueWithoutNotify(hair);
        }
        Text(statusText, canEdit ? "선택한 장비는 Battle 입장 시 적용됩니다." : "매칭 중에는 장비를 변경할 수 없습니다.");
        Text(hairLengthText, Mathf.RoundToInt(hair * 100f).ToString());
        if (equipmentTable != null)
        {
            HatStatEntry h = equipmentTable.GetHatStats((HatType)hat);
            BroomStatEntry b = equipmentTable.GetBroomStats((BroomType)broom);
            Text(hatStatsText, $"마나 {h.maxAp:0} / 초당 회복 {h.apRecoveryPerSecond:0.#}");
            Text(broomStatsText, $"체력 {b.maxHp:0} / 최고 속도 {b.maxSpeed:0} / 선회 {b.turnSpeed:0}°/s");
        }
        BindMagic(magic1StatsText, magic1);
        BindMagic(magic2StatsText, magic2);
    }

    private void BindMagic(TMP_Text label, int magic)
    {
        if (label == null || magicTable == null) return;
        MagicStatEntry stats = magicTable.GetStats((MagicType)magic);
        string targeting = stats.requiresTarget ? $"록온 {stats.lockChargeSeconds:0.##}초" : "록온 없이 좌클릭을 놓아 사용";
        label.text = $"마나 {stats.apCost:0} / 피해 {stats.damage:0} / {targeting}";
    }

    private static void BindDropdown(TMP_Dropdown dropdown, int value, bool interactable)
    {
        if (dropdown == null) return;
        dropdown.interactable = interactable;
        dropdown.SetValueWithoutNotify(value);
    }
    private static void Text(TMP_Text label, string value) { if (label != null) label.text = value; }
    private static void SetVisible(GameObject root, bool value) { if (root != null && root.activeSelf != value) root.SetActive(value); }
    private static int ValidHat(int selected) => selected >= (int)HatType.Classic && selected <= (int)HatType.Elemental ? selected : (int)HatType.Classic;
    private static int ValidBroom(int selected) => selected >= (int)BroomType.Slow && selected <= (int)BroomType.Speed ? selected : (int)BroomType.Standard;
    private static int ValidMagic(int selected, int fallback) => selected >= (int)MagicType.Fire && selected <= (int)MagicType.Scane ? selected : fallback;

    private void OnDisable()
    {
        if (toggleButton != null) toggleButton.onClick.RemoveListener(TogglePanel);
        if (closeButton != null) closeButton.onClick.RemoveListener(ClosePanel);
        if (hatDropdown != null) hatDropdown.onValueChanged.RemoveListener(SetHat);
        if (broomDropdown != null) broomDropdown.onValueChanged.RemoveListener(SetBroom);
        if (magic1Dropdown != null) magic1Dropdown.onValueChanged.RemoveListener(SetMagic1);
        if (magic2Dropdown != null) magic2Dropdown.onValueChanged.RemoveListener(SetMagic2);
        if (hairLengthSlider != null) hairLengthSlider.onValueChanged.RemoveListener(SetHairLength);
        SetVisible(loadoutPanel, false);
        SetVisible(lobbyUiRoot, false);
    }
}
