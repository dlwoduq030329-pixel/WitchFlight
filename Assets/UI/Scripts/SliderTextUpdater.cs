using TMPro;
using UnityEngine;

public class SliderTextUpdater : MonoBehaviour
{
    public TextMeshProUGUI targetText;

    public void UpdateSliderValue(float value)
    {
        // 0~1 사이의 값을 0~100 백분율로 변환 (소수점 없이 정수로)
        int percentage = Mathf.RoundToInt(value * 100f);

        // 뒤에 % 기호까지 붙여서 깔끔하게 출력
        targetText.text = percentage.ToString() + "";
    }
}
