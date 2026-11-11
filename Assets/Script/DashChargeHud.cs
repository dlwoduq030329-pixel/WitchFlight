using UnityEngine;
using UnityEngine.UI;

// Authored beneath BattleUI's moving forward-aim marker. No runtime UI creation
// or scene search: its parent supplies the position and normal HUD visibility.
[DisallowMultipleComponent]
public sealed class DashChargeHud : MonoBehaviour
{
    [SerializeField] private Slider[] segments = new Slider[6];
    [SerializeField] private RectTransform leftLine;
    [SerializeField] private RectTransform rightLine;
    [SerializeField, Min(1f)] private float segmentSpacing = 54f;
    [SerializeField, Min(0f)] private float lineOffset = 40f;
    [SerializeField] private CanvasGroup visibility;
    [Tooltip("충전 표시만 부드럽게 따라가는 시간. 소모는 즉시 반영합니다.")]
    [SerializeField, Min(.001f)] private float fillSmoothingSeconds = .04f;
    private Player previousPlayer;
    private int previousCharges = -1;
    private int previousCapacity = -1;

    private void OnEnable() { previousPlayer = null; previousCharges = -1; previousCapacity = -1; }

    private void UpdateCapacity(int capacity)
    {
        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i] == null) continue;
            segments[i].gameObject.SetActive(i < capacity);
            var rect = (RectTransform)segments[i].transform;
            rect.anchoredPosition = new Vector2((i - (capacity - 1) * .5f) * segmentSpacing, rect.anchoredPosition.y);
        }
        float edge = (capacity - 1) * .5f * segmentSpacing + lineOffset;
        if (leftLine != null) leftLine.anchoredPosition = new Vector2(-edge, leftLine.anchoredPosition.y);
        if (rightLine != null) rightLine.anchoredPosition = new Vector2(edge, rightLine.anchoredPosition.y);
        previousCapacity = capacity;
    }

    private void LateUpdate()
    {
        var player = Player.LocalPlayer;
        bool visible = player != null && player.Object != null && player.Object.IsValid && player.IsAlive &&
            BattleManager.Instance != null && BattleManager.Instance.IsGameplayActive && !CombatPresentation.MenuOpen;
        if (visibility != null) visibility.alpha = visible ? 1 : 0;
        if (!visible) return;
        int count = player.DashCharges;
        bool capacityChanged = previousCapacity != player.MaximumDashCharges;
        if (capacityChanged) UpdateCapacity(player.MaximumDashCharges);
        bool snap = capacityChanged || previousPlayer != player || previousCharges != count;
        float progress = player.DashRechargeProgress;
        float blend = 1f - Mathf.Exp(-Time.unscaledDeltaTime / Mathf.Max(.001f, fillSmoothingSeconds));
        for (int i = 0; i < Mathf.Min(segments.Length, previousCapacity); i++)
        {
            var slider = segments[i];
            if (slider == null) continue;
            float target = i < count ? 1 : i == count ? progress : 0;
            float value = snap || target < slider.value || target >= 1 ? target : Mathf.Lerp(slider.value, target, blend);
            slider.SetValueWithoutNotify(value);
        }
        previousPlayer = player; previousCharges = count;
    }
}
