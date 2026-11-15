using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Attach to the user's authored UI and assign its existing sliders. Creates no UI objects.
[DisallowMultipleComponent]
public sealed class BasicAttackHud : MonoBehaviour
{
    [Tooltip("왼쪽부터 충전 칸 Slider를 연결하세요. 기본 3칸. 위치와 크기는 변경하지 않습니다.")]
    [SerializeField] private Slider[] chargeSliders = new Slider[3];
    [SerializeField] private TMP_Text chargeText;
    [SerializeField] private Image attackIcon;
    [Tooltip("선택 사항: 기본공격 모드 표시. 이 컴포넌트가 붙은 오브젝트나 부모는 지정하지 마세요.")]
    [SerializeField] private GameObject selectedIndicator;
    [Header("대쉬와 동일한 충전 연출")]
    [SerializeField, Min(.001f)] private float fillSmoothingSeconds = .04f;
    [SerializeField] private Color rechargeFlashColor = Color.white;
    [SerializeField, Min(.01f)] private float rechargeFlashSeconds = .3f;
    private int lastCount = -1, lastCapacity = -1;
    private Player previousPlayer;
    private Graphic[] fillGraphics;
    private Color[] fillColors;
    private float[] flashStarted;

    private void OnEnable()
    {
        lastCount = lastCapacity = -1;
        previousPlayer = null;
        if (fillGraphics == null || fillGraphics.Length != chargeSliders.Length)
        {
            fillGraphics = new Graphic[chargeSliders.Length];
            fillColors = new Color[chargeSliders.Length];
            flashStarted = new float[chargeSliders.Length];
        }
        if (attackIcon != null && BasicAttackSettings.Default != null) attackIcon.sprite = BasicAttackSettings.Default.icon;
        foreach (var slider in chargeSliders)
            if (slider != null) { slider.minValue = 0; slider.maxValue = 1; slider.wholeNumbers = false; slider.interactable = false; }
        for (int i = 0; i < chargeSliders.Length; i++)
        {
            var slider = chargeSliders[i];
            fillGraphics[i] = slider != null && slider.fillRect != null ? slider.fillRect.GetComponent<Graphic>() : null;
            if (fillGraphics[i] != null) fillColors[i] = fillGraphics[i].color;
            flashStarted[i] = -1f;
        }
    }

    private void Update()
    {
        var player = Player.LocalPlayer;
        bool valid = player != null && player.Object != null && player.Object.IsValid && player.IsAlive;
        int count = valid ? player.BasicAttackCharges : 0;
        int capacity = valid ? player.BasicAttackCapacity : 3;
        float progress = valid ? player.BasicAttackRechargeProgress : 0;
        bool capacityChanged = capacity != lastCapacity;
        bool charged = valid && !capacityChanged && previousPlayer == player && lastCount >= 0 && count > lastCount;
        if (!valid || capacityChanged || previousPlayer != player) ClearRechargeFlashes();
        bool snap = !valid || capacityChanged || previousPlayer != player || lastCount != count;
        float blend = 1f - Mathf.Exp(-Time.unscaledDeltaTime / Mathf.Max(.001f, fillSmoothingSeconds));
        for (int i = 0; i < chargeSliders.Length; i++)
        {
            var slider = chargeSliders[i]; if (slider == null) continue;
            if (capacityChanged && slider.gameObject != gameObject && !transform.IsChildOf(slider.transform))
                slider.gameObject.SetActive(i < capacity);
            float target = i < count ? 1 : i == count && i < capacity ? progress : 0;
            float value = snap || target < slider.value || target >= 1 ? target : Mathf.Lerp(slider.value, target, blend);
            if (!Mathf.Approximately(slider.value, value)) slider.SetValueWithoutNotify(value);
            UpdateRechargeFlash(i, charged && i >= lastCount && i < count, valid && i < count);
        }
        if (count != lastCount || capacity != lastCapacity)
        {
            if (chargeText != null) chargeText.text = count + "/" + capacity;
            lastCount = count; lastCapacity = capacity;
        }
        previousPlayer = valid ? player : null;
        if (selectedIndicator != null && !transform.IsChildOf(selectedIndicator.transform))
        {
            bool selected = valid && player.CurrentMagicSlot == 0;
            if (selectedIndicator.activeSelf != selected) selectedIndicator.SetActive(selected);
        }
    }

    private void UpdateRechargeFlash(int index, bool completed, bool filled)
    {
        Graphic graphic = fillGraphics[index];
        if (graphic == null) return;
        if (completed)
        {
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
}
