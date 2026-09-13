using UnityEngine;

namespace Lovatto.DamagePointer
{
    [DisallowMultipleComponent]
    /// <summary>
    /// Lightweight indicator implementation that only handles color, lifetime and rotation.
    /// </summary>
    public class StandardDamageIndicator : DamageIndicatorBase
    {
        [Tooltip("How long the indicator stays visible in seconds.")]
        [Min(0.01f)]
        public float duration = 1.25f;

        [Header("Fade")]
        [Tooltip("Enable a fade-in when the indicator is spawned.")]
        public bool enableStartFade = false;

        [Tooltip("Duration of the start fade stage in seconds.")]
        [Min(0.01f)]
        public float startFadeDuration = 0.12f;

        [Tooltip("Curve used for start fade alpha. X = normalized fade time, Y = alpha.")]
        public AnimationCurve startFadeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Tooltip("Enable a fade-out near the end of the indicator lifetime. Disable to keep alpha fixed until it disappears.")]
        public bool enableFadeOut = false;

        [Tooltip("Duration of fade-out in seconds, counted from the end of the indicator lifetime.")]
        [Min(0.01f)]
        public float fadeOutDuration = 0.2f;

        [Tooltip("Curve used for fade-out alpha. X = normalized fade time, Y = alpha.")]
        public AnimationCurve fadeOutCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);

        [Tooltip("Multiplies the incoming damage color alpha.")]
        [Range(0f, 1f)] public float opacityMultiplier = 1f;

        public IndicatorAnimations startAnimation = IndicatorAnimations.None;
        [SerializeField] private Animator animator = null;

        private float remainingLife;
        private float elapsed;
        private float resolvedDuration;
        private float inverseStartFadeDuration;
        private float inverseFadeOutDuration;
        private float fadeOutStartTime;

        private void OnEnable()
        {
            if (animator != null && startAnimation != IndicatorAnimations.None)
            {
                animator.Play(startAnimation.ToString(), 0, 0f);
            }
        }

        public override void Activate(in DamageIndicatorActivationContext context)
        {
            resolvedDuration = Mathf.Max(0.01f, duration);
            remainingLife = resolvedDuration;
            elapsed = 0f;
            inverseStartFadeDuration = enableStartFade ? 1f / Mathf.Max(0.01f, startFadeDuration) : 0f;

            if (enableFadeOut)
            {
                float resolvedFadeOutDuration = Mathf.Min(Mathf.Max(0.01f, fadeOutDuration), resolvedDuration);
                inverseFadeOutDuration = 1f / resolvedFadeOutDuration;
                fadeOutStartTime = resolvedDuration - resolvedFadeOutDuration;
            }
            else
            {
                inverseFadeOutDuration = 0f;
                fadeOutStartTime = float.MaxValue;
            }

            ApplyColor(context.DamageColor);

            UpdateRotation(context.CameraPosition, context.CameraForwardPlanar, context.DamagePosition, context.HasValidCameraForwardPlanar);
        }

        public override bool Tick(in DamageIndicatorTickContext context)
        {
            elapsed += context.DeltaTime;
            remainingLife -= context.DeltaTime;
            if (remainingLife <= 0f)
            {
                return false;
            }

            UpdateRotation(context.CameraPosition, context.CameraForwardPlanar, context.DamagePosition, context.HasValidCameraForwardPlanar);
            ApplyColor(context.DamageColor);
            return true;
        }

        private void ApplyColor(Color baseColor)
        {
            if (Image == null)
            {
                return;
            }

            float alpha = 1f;

            if (enableStartFade)
            {
                float startFadeT = Mathf.Clamp01(elapsed * inverseStartFadeDuration);
                alpha *= Mathf.Clamp01(startFadeCurve.Evaluate(startFadeT));
            }

            if (enableFadeOut && elapsed >= fadeOutStartTime)
            {
                float fadeOutElapsed = elapsed - fadeOutStartTime;
                float fadeOutT = Mathf.Clamp01(fadeOutElapsed * inverseFadeOutDuration);
                alpha *= Mathf.Clamp01(fadeOutCurve.Evaluate(fadeOutT));
            }

            Color color = baseColor;
            color.a = Mathf.Clamp01(baseColor.a * opacityMultiplier * alpha);
            Image.color = color;
        }

        private void UpdateRotation(Vector3 cameraPosition, Vector3 cameraForwardPlanar, Vector3 damagePosition, bool hasValidCameraForwardPlanar)
        {
            if (Rect == null)
            {
                return;
            }

            float angleToDamage = hasValidCameraForwardPlanar
                ? DamageIndicatorUtility.CalculatePlanarSignedAngle(cameraPosition, cameraForwardPlanar, damagePosition)
                : 0f;

            Rect.localEulerAngles = new Vector3(0f, 0f, -angleToDamage);
        }
    }
}