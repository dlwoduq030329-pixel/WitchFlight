using UnityEngine;
using UnityEngine.UI;

// Uses authored segment positions and sizes. Capacity only changes visibility.
[DisallowMultipleComponent]
public sealed class DashChargeHud : MonoBehaviour
{
    [SerializeField] private Slider[] segments = new Slider[6];
    [SerializeField] private CanvasGroup visibility;
    [Tooltip("충전 표시만 부드럽게 따라가는 시간. 소모는 즉시 반영합니다.")]
    [SerializeField, Min(.001f)] private float fillSmoothingSeconds = .04f;
    [Header("충전 완료 반짝임")]
    [SerializeField] private Color rechargeFlashColor = Color.white;
    [SerializeField, Min(.01f)] private float rechargeFlashSeconds = .3f;
    private Graphic[] fillGraphics;
    private Color[] fillColors;
    private float[] flashStarted;
    private Player previousPlayer;
    private int previousCharges = -1;
    private int previousCapacity = -1;

    private void OnEnable()
    {
        previousPlayer = null; previousCharges = -1; previousCapacity = -1;
        // HUD visibility toggles frequently between rounds. Reuse storage while
        // still rebinding the authored graphics and their current colours each enable.
        if (fillGraphics == null || fillGraphics.Length != segments.Length)
        {
            fillGraphics = new Graphic[segments.Length];
            fillColors = new Color[segments.Length];
            flashStarted = new float[segments.Length];
        }
        for (int i = 0; i < segments.Length; i++)
        {
            fillGraphics[i] = segments[i] != null && segments[i].fillRect != null
                ? segments[i].fillRect.GetComponent<Graphic>() : null;
            if (fillGraphics[i] != null) fillColors[i] = fillGraphics[i].color;
            flashStarted[i] = -1f;
        }
    }

    private void ClearRechargeFlashes()
    {
        if (flashStarted == null) return;
        for (int i = 0; i < flashStarted.Length; i++)
        {
            if (flashStarted[i] < 0f) continue;
            if (fillGraphics[i] != null) fillGraphics[i].color = fillColors[i];
            flashStarted[i] = -1f;
        }
    }

    private void OnDisable() => ClearRechargeFlashes();

    private void UpdateRechargeFlash(int index, bool completed, bool filled)
    {
        Graphic graphic = fillGraphics[index];
        if (graphic == null) return;
        if (completed)
        {
            // Capture the authored/current tint before changing it; never alter bar dimensions.
            if (flashStarted[index] < 0f) fillColors[index] = graphic.color;
            flashStarted[index] = Time.unscaledTime;
        }
        if (flashStarted[index] < 0f) return;
        float t = (Time.unscaledTime - flashStarted[index]) / Mathf.Max(.01f, rechargeFlashSeconds);
        if (!filled || t >= 1f)
        {
            graphic.color = fillColors[index];
            flashStarted[index] = -1f;
            return;
        }
        graphic.color = Color.Lerp(rechargeFlashColor, fillColors[index], Mathf.SmoothStep(0f, 1f, t));
    }

    private void UpdateCapacity(int capacity)
    {
        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i] == null) continue;
            bool active = i < capacity;
            if (segments[i].gameObject.activeSelf != active)
                segments[i].gameObject.SetActive(active);
        }
        previousCapacity = capacity;
    }

    private void LateUpdate()
    {
        var player = Player.LocalPlayer;
        bool visible = player != null && player.Object != null && player.Object.IsValid && player.IsAlive &&
            BattleManager.Instance != null && BattleManager.Instance.IsGameplayActive && !CombatPresentation.MenuOpen;
        if (visibility != null) visibility.alpha = visible ? 1 : 0;
        if (!visible)
        {
            ClearRechargeFlashes();
            previousPlayer = null; previousCharges = -1;
            return;
        }
        int count = player.DashCharges;
        bool capacityChanged = previousCapacity != player.MaximumDashCharges;
        bool charged = !capacityChanged && previousPlayer == player && previousCharges >= 0 && count > previousCharges;
        if (capacityChanged || previousPlayer != player) ClearRechargeFlashes();
        if (capacityChanged) UpdateCapacity(player.MaximumDashCharges);
        bool snap = capacityChanged || previousPlayer != player || previousCharges != count;
        float progress = player.DashRechargeProgress;
        float blend = 1f - Mathf.Exp(-Time.unscaledDeltaTime / Mathf.Max(.001f, fillSmoothingSeconds));
        for (int i = 0; i < Mathf.Min(segments.Length, previousCapacity); i++)
        {
            var slider = segments[i];
            if (slider == null) continue;
            // Consume the first authored slot first; keep positions and capacity visibility intact.
            int chargeIndex = Mathf.Min(segments.Length, previousCapacity) - 1 - i;
            float target = chargeIndex < count ? 1 : chargeIndex == count ? progress : 0;
            float value = snap || target < slider.value || target >= 1 ? target : Mathf.Lerp(slider.value, target, blend);
            slider.SetValueWithoutNotify(value);
            UpdateRechargeFlash(i, charged && chargeIndex >= previousCharges && chargeIndex < count, chargeIndex < count);
        }
        previousPlayer = player; previousCharges = count;
    }
}
