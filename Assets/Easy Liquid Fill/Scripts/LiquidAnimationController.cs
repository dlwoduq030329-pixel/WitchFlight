namespace PixeLadder.EasyLiquidFill
{
    using UnityEngine;

    /// <summary>
    /// The main controller for the liquid animation. This component drives the
    /// LiquidAnimator with physics-based swirling and fill effects.
    /// </summary>
    [ExecuteAlways]
    public class LiquidAnimationController : MonoBehaviour
    {
        #region Fields
        [Header("Core References")]
        [Tooltip("The LiquidAnimator component that this controller will drive.")]
        public LiquidAnimator liquidAnimator;

        [Header("Fill Settings")]
        [Tooltip("The target fill amount of the liquid (0 to 1).")]
        [Range(0f, 1f)] public float fillAmount = 0.5f;
        [Tooltip("Duration in seconds for liquid fill to animate to its target.")]
        [SerializeField, Min(0f)] public float fillDuration = 0.5f;

        [Header("Tilt & Swirl Settings")]
        [Tooltip("The base inclination of the container in degrees.")]
        [Range(-90f, 90f)] public float originalInclination = 10f;
        [Tooltip("The maximum angle in degrees the liquid will tilt when swirling.")]
        [SerializeField, Range(0f, 90f)] public float maxTiltAmount = 45f;
        [Tooltip("Duration in seconds for the swirl effect to decay.")]
        [SerializeField, Min(0.1f)] public float swirlDuration = 2f;
        [Tooltip("The oscillation frequency of the swirl effect in cycles per second.")]
        [SerializeField, Min(0.1f)] public float swirlSpeed = 3f;

        [Header("Color Settings")]
        [SerializeField] private Color fillColor = new Color(0.8f, 0.8f, 0.8f);
        [SerializeField] private Color surfaceColor = Color.white;

        // --- Private State ---
        private float currentFill;
        private float previousFillTarget;
        private float initialTiltAmplitude;
        private float swirlDecayTimer;
        private float oscillationTimer;
        #endregion

        #region Unity Lifecycle
        private void Start()
        {
            currentFill = fillAmount;
            previousFillTarget = fillAmount;
            if (liquidAnimator) liquidAnimator.SetFill(currentFill);
            swirlDecayTimer = swirlDuration;
            initialTiltAmplitude = 0f;
        }

        private void Update()
        {
            if (liquidAnimator == null) return;

            HandleFillChangeDetection();
            AnimateFillAmount();
            AnimateSwirl();
            ApplyColors();
        }
        #endregion

        #region Public API
        /// <summary>
        /// Manually triggers the swirl animation with a specific intensity.
        /// Can be called from other scripts in response to gameplay events.
        /// </summary>
        /// <param name="intensity">The strength of the swirl.</param>
        public void TriggerSwirl(float intensity)
        {
            initialTiltAmplitude = maxTiltAmount * intensity;
            swirlDecayTimer = 0f;
        }
        #endregion

        #region Private Logic
        private void HandleFillChangeDetection()
        {
            if (Mathf.Approximately(fillAmount, previousFillTarget)) return;

            // When the fill amount is changed from the Inspector,
            // trigger a swirl based on the magnitude of the change.
            float fillChangeMagnitude = Mathf.Abs(fillAmount - currentFill);
            TriggerSwirl(fillChangeMagnitude);

            previousFillTarget = fillAmount;
        }

        private void AnimateFillAmount()
        {
            currentFill = (fillDuration > 0f)
                ? Mathf.Lerp(currentFill, fillAmount, Time.deltaTime / fillDuration)
                : fillAmount;

            liquidAnimator.SetFill(currentFill);
        }

        private void AnimateSwirl()
        {
            float currentTiltAmplitude = 0f;
            if (swirlDecayTimer < swirlDuration)
            {
                swirlDecayTimer += Time.deltaTime;
                float decayProgress = Mathf.Clamp01(swirlDecayTimer / swirlDuration);
                currentTiltAmplitude = initialTiltAmplitude * (1f - decayProgress);
            }

            oscillationTimer += Time.deltaTime;
            float angle = oscillationTimer * 2f * Mathf.PI * swirlSpeed;
            float tiltX = Mathf.Sin(-angle) * currentTiltAmplitude + originalInclination;
            float tiltZ = Mathf.Cos(angle) * currentTiltAmplitude;
            liquidAnimator.SetInclination(tiltX, tiltZ);
        }

        private void ApplyColors()
        {
            liquidAnimator.SetFillColor(fillColor);
            liquidAnimator.SetSurfaceColor(surfaceColor);
        }
        #endregion
    }
}