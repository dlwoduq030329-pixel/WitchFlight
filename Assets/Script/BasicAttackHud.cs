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
    private int lastCount = -1, lastCapacity = -1;

    private void OnEnable()
    {
        lastCount = lastCapacity = -1;
        if (attackIcon != null && BasicAttackSettings.Default != null) attackIcon.sprite = BasicAttackSettings.Default.icon;
        foreach (var slider in chargeSliders)
            if (slider != null) { slider.minValue = 0; slider.maxValue = 1; slider.wholeNumbers = false; slider.interactable = false; }
    }

    private void Update()
    {
        var player = Player.LocalPlayer;
        bool valid = player != null && player.Object != null && player.Object.IsValid && player.IsAlive;
        int count = valid ? player.BasicAttackCharges : 0;
        int capacity = valid ? player.BasicAttackCapacity : 3;
        float progress = valid ? player.BasicAttackRechargeProgress : 0;
        for (int i = 0; i < chargeSliders.Length; i++)
        {
            var slider = chargeSliders[i]; if (slider == null) continue;
            float value = i < count ? 1 : i == count && i < capacity ? progress : 0;
            if (!Mathf.Approximately(slider.value, value)) slider.SetValueWithoutNotify(value);
        }
        if (count != lastCount || capacity != lastCapacity)
        {
            if (chargeText != null) chargeText.text = count + "/" + capacity;
            lastCount = count; lastCapacity = capacity;
        }
        if (selectedIndicator != null && !transform.IsChildOf(selectedIndicator.transform))
        {
            bool selected = valid && player.CurrentMagicSlot == 0;
            if (selectedIndicator.activeSelf != selected) selectedIndicator.SetActive(selected);
        }
    }
}
