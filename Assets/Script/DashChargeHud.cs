using UnityEngine;
using UnityEngine.UI;

// Authored beneath BattleUI's moving forward-aim marker. No runtime UI creation
// or scene search: its parent supplies the position and normal HUD visibility.
[DisallowMultipleComponent]
public sealed class DashChargeHud : MonoBehaviour
{
    [SerializeField] private Slider[] segments = new Slider[Player.MaximumDashCharges];
    [SerializeField] private CanvasGroup visibility;
    [Tooltip("충전 표시만 부드럽게 따라가는 시간. 소모는 즉시 반영합니다.")]
    [SerializeField, Min(.001f)] private float fillSmoothingSeconds = .04f;
    private Player previousPlayer;
    private int previousCharges = -1;

    private void OnEnable() { previousPlayer = null; previousCharges = -1; }

    private void LateUpdate()
    {
        var player = Player.LocalPlayer;
        bool visible = player != null && player.Object != null && player.Object.IsValid && player.IsAlive &&
            BattleManager.Instance != null && BattleManager.Instance.IsGameplayActive && !CombatPresentation.MenuOpen;
        if (visibility != null) visibility.alpha = visible ? 1 : 0;
        if (!visible) return;
        int count = player.DashCharges;
        bool snap = previousPlayer != player || previousCharges != count;
        float progress = player.DashRechargeProgress;
        float blend = 1f - Mathf.Exp(-Time.unscaledDeltaTime / Mathf.Max(.001f, fillSmoothingSeconds));
        for (int i = 0; i < segments.Length; i++)
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
