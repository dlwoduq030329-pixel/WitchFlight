using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using UnityEngine.Events;

public class GridHPBar : MonoBehaviour
{
    [Header("HP cells")]
    [SerializeField] public Transform gridParent;
    [SerializeField] private Image cellTemplate;
    [SerializeField, Min(0.1f)] private float healthPerCell = 10f;
    [SerializeField, Range(0f, 1f)] private float emptyCellAlpha = 0.15f;
    [Tooltip("기존 중첩 칸 그룹은 런타임에만 숨깁니다.")]
    [SerializeField] private GameObject[] legacyCellGroups;
    [Range(0f, 1f)] public float currentPercent = 1f;
    [Header("Damage feedback")]
    [SerializeField] private bool flashOnDamage = true;
    [SerializeField] private Color damageFlashColor = new Color(1f, 0.2f, 0.2f, 1f);
    [SerializeField, Min(0.01f)] private float damageFlashSeconds = 0.25f;
    [Tooltip("실제 HP 감소량. 최초 연결, 최대 HP 변경, 리스폰은 제외됩니다.")]
    [SerializeField] private UnityEvent<float> onDamaged = new UnityEvent<float>();

    private readonly List<Image> cells = new();
    private readonly List<Color> baseColors = new();
    private Image[] legacyCells;
    private float lastHp = -1f, lastMaxHp = -1f, flashRemaining;
    private int displayedCellCount = -1;
    private bool initialized;

    private void Awake()
    {
        if (lastMaxHp < 0f && cellTemplate == null) UpdateHP(currentPercent);
    }

    public static int CellCount(float maximum, float perCell)
    {
        if (!IsFinite(maximum) || !IsFinite(perCell) || maximum <= 0f || perCell <= 0f) return 0;
        return Mathf.CeilToInt(Mathf.Min(1024f, maximum / perCell));
    }

    public static float CellFill(float hp, float maximum, float perCell, int index)
    {
        if (!IsFinite(hp) || !IsFinite(maximum) || !IsFinite(perCell) || maximum <= 0f || perCell <= 0f || index < 0) return 0f;
        return Mathf.Clamp01((Mathf.Clamp(hp, 0f, maximum) - index * perCell) / perCell);
    }

    public static float DamageDelta(float oldHp, float oldMaximum, float hp, float maximum)
    {
        if (!IsFinite(oldHp) || !IsFinite(hp) || !IsFinite(maximum) ||
            oldMaximum <= 0f || oldMaximum != maximum || oldHp < 0f) return 0f;
        return Mathf.Max(0f, Mathf.Clamp(oldHp, 0f, maximum) - Mathf.Clamp(hp, 0f, maximum));
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    public void ResetBinding()
    {
        lastHp = lastMaxHp = -1f;
        flashRemaining = 0f;
    }

    public void UpdateHealth(float current, float maximum)
    {
        if (!IsFinite(maximum) || maximum < 0f) maximum = 0f;
        current = IsFinite(current) ? Mathf.Clamp(current, 0f, maximum) : 0f;
        if (lastHp == current && lastMaxHp == maximum && initialized) return;
        float damage = DamageDelta(lastHp, lastMaxHp, current, maximum);
        lastHp = current;
        lastMaxHp = maximum;
        currentPercent = maximum > 0f ? current / maximum : 0f;
        if (cellTemplate != null && gridParent != null)
        {
            InitializeCells();
            int required = CellCount(maximum, healthPerCell);
            while (cells.Count < required)
            {
                Image cell = Instantiate(cellTemplate, gridParent, false);
                cell.name = "HP_Cell (runtime " + cells.Count + ")";
                AddCell(cell, baseColors.Count > 0 ? baseColors[0] : Color.white);
            }
            bool countChanged = displayedCellCount != required;
            for (int i = 0; i < cells.Count; i++)
                if (cells[i] != null && cells[i].gameObject.activeSelf != (i < required))
                    cells[i].gameObject.SetActive(i < required);
            displayedCellCount = required;
            if (countChanged && gridParent is RectTransform rect) LayoutRebuilder.MarkLayoutForRebuild(rect);
            if (damage > 0f && flashOnDamage) flashRemaining = damageFlashSeconds;
            RefreshCells();
        }
        else UpdateLegacy(currentPercent);
        if (damage > 0f) onDamaged?.Invoke(damage);
    }

    private void InitializeCells()
    {
        if (initialized) return;
        initialized = true;
        if (legacyCellGroups != null)
            foreach (GameObject group in legacyCellGroups)
                if (group != null && group.transform != gridParent && !cellTemplate.transform.IsChildOf(group.transform))
                    group.SetActive(false);
        foreach (Transform child in gridParent)
        {
            if (!child.TryGetComponent(out Image cell) ||
                (cell != cellTemplate && !child.name.StartsWith("HP_Cell"))) continue;
            AddCell(cell, cell.color);
        }
    }

    private void AddCell(Image cell, Color baseColor)
    {
        cell.type = Image.Type.Filled;
        cell.fillMethod = Image.FillMethod.Horizontal;
        cell.fillOrigin = 0;
        cell.raycastTarget = false;
        var layout = cell.GetComponent<LayoutElement>();
        if (layout == null) layout = cell.gameObject.AddComponent<LayoutElement>();
        layout.minWidth = layout.preferredWidth = 0f;
        layout.flexibleWidth = 1f;
        cells.Add(cell);
        baseColors.Add(baseColor);
    }

    private void Update()
    {
        if (flashRemaining <= 0f) return;
        flashRemaining = Mathf.Max(0f, flashRemaining - Time.unscaledDeltaTime);
        RefreshCells();
    }

    private void RefreshCells()
    {
        float flash = flashOnDamage ? Mathf.Clamp01(flashRemaining / Mathf.Max(0.01f, damageFlashSeconds)) : 0f;
        for (int i = 0; i < displayedCellCount && i < cells.Count; i++)
        {
            Image cell = cells[i];
            if (cell == null) continue;
            float fill = CellFill(lastHp, lastMaxHp, healthPerCell, i);
            cell.fillAmount = fill > 0f ? fill : 1f;
            Color color = Color.Lerp(baseColors[i], damageFlashColor, flash);
            color.a = (fill > 0f ? 1f : emptyCellAlpha) * baseColors[i].a;
            cell.color = color;
        }
    }

    // Existing percentage-based UnityEvents / enemy bars remain compatible.
    public void UpdateHP(float percent)
    {
        currentPercent = IsFinite(percent) ? Mathf.Clamp01(percent) : 0f;
        if (cellTemplate != null && lastMaxHp >= 0f) UpdateHealth(currentPercent * lastMaxHp, lastMaxHp);
        else UpdateLegacy(currentPercent);
    }

    private void UpdateLegacy(float percent)
    {
        if (legacyCells == null && gridParent != null) legacyCells = gridParent.GetComponentsInChildren<Image>(true);
        if (legacyCells == null) return;
        int count = Mathf.RoundToInt(legacyCells.Length * percent);
        if (displayedCellCount == count) return;
        displayedCellCount = count;
        for (int i = 0; i < legacyCells.Length; i++)
        {
            if (legacyCells[i] == null) continue;
            Color color = legacyCells[i].color;
            color.a = i < count ? 1f : 0f;
            legacyCells[i].color = color;
        }
    }

    public void UpdateFromPlayerData(PlayerData data)
    {
        bool valid = data != null && data.Object != null && data.Object.IsValid && data.Runner != null && data.Runner.IsRunning;
        UpdateHealth(valid ? data.BattleCurrentHp : 0f, valid ? data.BattleMaxHp : 0f);
    }

    public void Test_DecreaseHP() => UpdateHP(currentPercent - 0.1f);
}
