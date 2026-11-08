using TMPro;
using UnityEngine;
using UnityEngine.UI;

// All UI objects and references are authored in Main; no runtime UI creation/search.
public sealed class ClothColorPaletteUI : MonoBehaviour
{
    [SerializeField] private PlayerAppearance preview;
    [SerializeField] private GameObject priceDialog, paletteDialog;
    [SerializeField] private TMP_Text priceText, statusText, colorText;
    [SerializeField] private Button openPaletteButton, priceCancelButton, applyButton, cancelButton;
    [SerializeField] private ColorPaletteSurface saturationValue, hueStrip;
    [SerializeField] private RectTransform colorCursor, hueCursor;
    [SerializeField] private Image colorSwatch;
    [SerializeField] private GameObject eyeShapeOptions;
    [SerializeField] private Button fourWhiteButton, sparklingButton;
    [SerializeField] private TMP_Text fourWhiteLabel, sparklingLabel;
    private ColorPaletteTarget target;
    private Color selectedEyeKey;
    private float hue, saturation, value;
    private Color selected;
    private string account;
    private bool editing, saving;

    private void Awake()
    {
        openPaletteButton.onClick.AddListener(OpenPalette);
        priceCancelButton.onClick.AddListener(Cancel);
        applyButton.onClick.AddListener(Apply);
        cancelButton.onClick.AddListener(Cancel);
        fourWhiteButton.onClick.AddListener(() => SelectEyeShape(true));
        sparklingButton.onClick.AddListener(() => SelectEyeShape(false));
        priceText.text = $": {DatabaseManager.ClothPalettePrice:N0}";
    }

    private void OnEnable()
    {
        DataConfig.Changed += RefreshPreview;
        DataConfig.SnapshotApplied += RefreshPreview;
        DatabaseManager.DataConfigReadyChanged += RefreshPreview;
    }

    private void OnDisable()
    {
        DataConfig.Changed -= RefreshPreview;
        DataConfig.SnapshotApplied -= RefreshPreview;
        DatabaseManager.DataConfigReadyChanged -= RefreshPreview;
        Close();
    }

    public void Open()
        => OpenTarget(ColorPaletteTarget.Clothes);
    public void OpenHair() => OpenTarget(ColorPaletteTarget.Hair);
    public void OpenEyes() => OpenTarget(ColorPaletteTarget.Eyes);

    private void OpenTarget(ColorPaletteTarget nextTarget)
    {
        var db = DatabaseManager.Instance;
        if (db == null || !db.CanModifyAccountData || saving || preview == null) return;
        Close();
        target = nextTarget;
        account = db.LoadedAccountId;
        priceDialog.SetActive(true);
    }

    private bool ValidAccount => DatabaseManager.Instance != null && DatabaseManager.Instance.HasLoadedProfile &&
        DatabaseManager.Instance.IsDataConfigReady && DatabaseManager.Instance.LoadedAccountId == account;

    private void OpenPalette()
    {
        if (!ValidAccount || saving) { Close(); return; }
        priceDialog.SetActive(false);
        paletteDialog.SetActive(true);
        editing = true;
        eyeShapeOptions.SetActive(target == ColorPaletteTarget.Eyes);
        Color saved = target == ColorPaletteTarget.Clothes ? DataConfig.clothColor : target == ColorPaletteTarget.Hair ? DataConfig.hairColor :
            DataConfig.eyePaletteColor.a > .5f ? DataConfig.eyePaletteColor : DataConfig.eyeColor;
        Color.RGBToHSV(saved, out hue, out saturation, out value);
        if (target == ColorPaletteTarget.Eyes)
        {
            var table = EyeTexturePresetTable.Default;
            if (table == null || !table.TryGetPaletteShape(table.IsFourWhite(DataConfig.eyeColor), out var shape))
            { Close(); return; }
            selectedEyeKey = shape.savedColorKey;
            UpdateShapeLabels();
        }
        string part = target == ColorPaletteTarget.Clothes ? "옷" : target == ColorPaletteTarget.Hair ? "머리" : "눈";
        statusText.text = $"{part} 색상 미리보기 · 확인 시 5,000 재화 사용";
        SetControls(true);
        UpdateColor();
    }

    public void PickColor(bool isHue, Vector2 position)
    {
        if (!editing || saving || !ValidAccount) return;
        if (isHue) hue = position.y;
        else { saturation = position.x; value = position.y; }
        UpdateColor();
    }

    private void UpdateColor()
    {
        selected = (Color32)Color.HSVToRGB(hue, saturation, value);
        saturationValue.SetHue(hue);
        Position(colorCursor, new Vector2(saturation, value));
        Position(hueCursor, new Vector2(.5f, hue));
        colorSwatch.color = selected;
        colorText.text = "#" + ColorUtility.ToHtmlStringRGB(selected);
        ApplyPreview();
    }

    private static void Position(RectTransform marker, Vector2 point)
    {
        marker.anchorMin = marker.anchorMax = point;
        marker.anchoredPosition = Vector2.zero;
    }

    private void RefreshPreview()
    {
        if (!editing) return;
        if (!ValidAccount) { Close(); return; }
        ApplyPreview();
    }

    private void ApplyPreview()
    {
        switch (target)
        {
            case ColorPaletteTarget.Clothes: preview.ApplyClothColor(selected); break;
            case ColorPaletteTarget.Hair: preview.ApplyHairColor(selected); break;
            case ColorPaletteTarget.Eyes: preview.ApplyEyeColor(selectedEyeKey, selected); break;
        }
    }

    private void SelectEyeShape(bool fourWhite)
    {
        if (!editing || saving || !ValidAccount || target != ColorPaletteTarget.Eyes) return;
        var table = EyeTexturePresetTable.Default;
        if (table == null || !table.TryGetPaletteShape(fourWhite, out var shape)) return;
        selectedEyeKey = shape.savedColorKey;
        UpdateShapeLabels();
        ApplyPreview();
    }

    private void UpdateShapeLabels()
    {
        bool fourWhite = EyeTexturePresetTable.Default.IsFourWhite(selectedEyeKey);
        fourWhiteLabel.text = fourWhite ? "사백안 (선택)" : "사백안";
        sparklingLabel.text = fourWhite ? "초롱초롱눈" : "초롱초롱눈 (선택)";
    }

    private void Apply()
    {
        if (!editing || saving || !ValidAccount) return;
        saving = true;
        SetControls(false);
        statusText.text = "색상 저장 중…";
        DatabaseManager.Instance.PurchasePaletteColor(target, selected, selectedEyeKey, (success, message) =>
        {
            if (this == null) return;
            saving = false;
            SetControls(true);
            if (success) Close();
            else if (editing) statusText.text = message;
        });
    }

    private void SetControls(bool enabled)
    {
        applyButton.interactable = cancelButton.interactable = enabled;
        saturationValue.raycastTarget = hueStrip.raycastTarget = enabled;
        fourWhiteButton.interactable = sparklingButton.interactable = enabled;
    }

    private void Cancel() { if (!saving) Close(); }

    private void Close()
    {
        bool restore = editing;
        editing = false;
        if (restore && preview != null)
        {
            switch (target)
            {
                case ColorPaletteTarget.Clothes: preview.ApplyClothColor(DataConfig.clothColor); break;
                case ColorPaletteTarget.Hair: preview.ApplyHairColor(DataConfig.hairColor); break;
                case ColorPaletteTarget.Eyes: preview.ApplyEyeColor(DataConfig.eyeColor, DataConfig.eyePaletteColor); break;
            }
        }
        priceDialog.SetActive(false);
        paletteDialog.SetActive(false);
    }
}
