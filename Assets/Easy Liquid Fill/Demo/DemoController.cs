namespace PixeLadder.EasyLiquidFill.Demo
{
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// A simple controller for the demo scene to link UI sliders to the liquid animation.
    /// </summary>
    public class DemoController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private LiquidAnimationController liquid;
        [SerializeField] private Slider fillSlider;
        [SerializeField] private Slider inclinationSlider;
        [SerializeField] private Text fillText;
        [SerializeField] private Text inclinationText;

        private void Update()
        {
            // Failsafe in case references are not set.
            if (!liquid || !fillSlider || !inclinationSlider) return;

            // Update the controller with slider values (Original Logic).
            liquid.fillAmount = fillSlider.value;
            liquid.originalInclination = 90 - inclinationSlider.value;

            // Update the UI text labels, with null-checks for robustness.
            if (fillText) fillText.text = $"{liquid.fillAmount * 100f:F0}%";
            if (inclinationText) inclinationText.text = $"{liquid.originalInclination:F0}°";
        }

        public void TriggerSwirl()
        {
            liquid.TriggerSwirl(1f);
        }
    }
}