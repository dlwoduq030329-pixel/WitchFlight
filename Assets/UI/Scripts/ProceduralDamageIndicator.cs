using UnityEngine;

namespace Lovatto.DamagePointer
{
    [DisallowMultipleComponent]
    /// <summary>
    /// Default procedural indicator implementation that animates from a short line into a fading arc.
    /// </summary>
    public class ProceduralDamageIndicator : DamageIndicatorBase
    {
        [Header("Animation")]
        [Tooltip("How quickly the indicator morphs from line shape to arc shape after hold time expires.")]
        public float spreadSpeed = 8f;

        [Tooltip("How long the indicator stays as a line before morphing to an arc (seconds).")]
        public float lineHoldTime = 0.1f;

        [Tooltip("Fade-out duration in seconds once the morph has completed.")]
        public float duration = 2f;

        [Header("Morph")]
        [Tooltip("Initial radius when the indicator first appears.")]
        [Range(0.1f, 0.5f)] public float startRadius = 0.3f;

        [Tooltip("Final radius the indicator settles on after morphing.")]
        [Range(0.1f, 0.5f)] public float baseRadius = 0.4f;

        [Space]
        [Tooltip("Initial arc thickness at spawn.")]
        public float startThickness = 0.15f;

        [Tooltip("Final arc thickness after morph progression.")]
        public float endThickness = 0.03f;

        [Space]
        [Tooltip("Initial arc angle used while the indicator is still line-like.")]
        public float startArcAngle = 4f;

        [Tooltip("Final arc angle reached at full morph progression.")]
        public float endArcAngle = 80f;

        [Space]
        [Tooltip("Initial alpha falloff at spawn. 0 disables radial falloff.")]
        [Range(0f, 8f)] public float startAlphaFalloff = 0f;

        [Tooltip("Final alpha falloff reached at full morph progression.")]
        [Range(0f, 8f)] public float endAlphaFalloff = 0f;

        [Header("Fade")]
        [Tooltip("Enables a short fade-in at indicator spawn before regular lifetime fade-out.")]
        public bool enableStartFade = false;

        [Tooltip("Duration in seconds of the optional fade-in stage.")]
        public float startFadeDuration = 0.12f;

        [Tooltip("Curve used for fade-in alpha when Start Fade is enabled. X = normalized time, Y = alpha.")]
        public AnimationCurve startFadeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Tooltip("Multiplies the incoming damage color alpha.")]
        [Range(0f, 1f)] public float opacityMultiplier = 1f;

        [Header("Optional: Distance -> Line Length")]
        [Tooltip("If enabled, the initial line length uses distance to the hit source.")]
        public bool scaleLineByDistance = false;

        [Tooltip("Distance considered 'near' when scaling initial line length by distance.")]
        public float nearDistance = 2f;

        [Tooltip("Distance considered 'far' when scaling initial line length by distance.")]
        public float farDistance = 30f;

        [Tooltip("Initial line angle used at nearDistance when distance scaling is enabled.")]
        public float nearLineArcAngle = 3f;

        [Tooltip("Initial line angle used at farDistance when distance scaling is enabled.")]
        public float farLineArcAngle = 10f;

        public IndicatorAnimations startAnimation = IndicatorAnimations.None;
        [SerializeField] private Animator animator = null;

        private float life;
        private float morphProgress;
        private float holdTimer;
        private float elapsed;
        private float lineArcAngle;
        private float inverseDuration;
        private float inverseStartFadeDuration;

        private static readonly int AnglePropId = Shader.PropertyToID("_ArcAngle");
        private static readonly int ThicknessPropId = Shader.PropertyToID("_Thickness");
        private static readonly int RadiusPropId = Shader.PropertyToID("_Radius");
        private static readonly int AlphaFalloffPropId = Shader.PropertyToID("_AlphaFalloff");

        private void OnEnable()
        {
            if (animator != null && startAnimation != IndicatorAnimations.None)
            {
                animator.Play(startAnimation.ToString(), 0, 0f);
            }
        }

        public override void Initialize(RectTransform rect, UnityEngine.UI.Image image)
        {
            base.Initialize(rect, image);

            if (RuntimeMaterial == null)
            {
                RuntimeMaterial = Object.Instantiate(DamageIndicator.GetGlobalSettings().GetRenderPipelineMaterial());
                Image.material = RuntimeMaterial;
            }

            if (RuntimeMaterial != null)
            {
                RuntimeMaterial.SetFloat(RadiusPropId, baseRadius);
            }
        }

        public override void Activate(in DamageIndicatorActivationContext context)
        {
            life = 1f;
            morphProgress = 0f;
            holdTimer = lineHoldTime;
            elapsed = 0f;
            inverseDuration = 1f / Mathf.Max(0.0001f, duration);
            inverseStartFadeDuration = enableStartFade ? 1f / Mathf.Max(0.0001f, startFadeDuration) : 0f;
            lineArcAngle = ResolveLineArcAngle(context.PlayerCamera, context.DamagePosition);

            if (RuntimeMaterial != null)
            {
                RuntimeMaterial.SetFloat(AnglePropId, lineArcAngle);
                RuntimeMaterial.SetFloat(ThicknessPropId, startThickness);
                RuntimeMaterial.SetFloat(RadiusPropId, startRadius);
                RuntimeMaterial.SetFloat(AlphaFalloffPropId, startAlphaFalloff);
            }

            if (Rect != null)
            {
                float angleToDamage = context.HasValidCameraForwardPlanar
                    ? DamageIndicatorUtility.CalculatePlanarSignedAngle(context.CameraPosition, context.CameraForwardPlanar, context.DamagePosition)
                    : 0f;

                Rect.localEulerAngles = new Vector3(0f, 0f, -angleToDamage);
            }

            float startFadeAlpha = enableStartFade ? Mathf.Clamp01(startFadeCurve.Evaluate(0f)) : 1f;
            ApplyColor(context.DamageColor, life, startFadeAlpha);
        }

        public override bool Tick(in DamageIndicatorTickContext context)
        {
            elapsed += context.DeltaTime;

            if (holdTimer > 0f)
            {
                holdTimer -= context.DeltaTime;
            }
            else
            {
                morphProgress = Mathf.Clamp01(morphProgress + (context.DeltaTime * spreadSpeed));
            }

            if (holdTimer <= 0f && morphProgress >= 1f)
            {
                life = Mathf.Clamp01(life - (context.DeltaTime * inverseDuration));
            }

            if (life <= 0f)
            {
                return false;
            }

            if (RuntimeMaterial != null)
            {
                float inv = 1f - morphProgress;
                float easeOut = 1f - (inv * inv * inv);
                float currentAngle = Mathf.Lerp(lineArcAngle, endArcAngle, easeOut);
                float currentThickness = Mathf.Lerp(startThickness, endThickness, easeOut);
                float currentRadius = Mathf.Lerp(startRadius, baseRadius, easeOut);
                float currentAlphaFalloff = Mathf.Lerp(startAlphaFalloff, endAlphaFalloff, easeOut);

                RuntimeMaterial.SetFloat(AnglePropId, currentAngle);
                RuntimeMaterial.SetFloat(ThicknessPropId, currentThickness);
                RuntimeMaterial.SetFloat(RadiusPropId, currentRadius);
                RuntimeMaterial.SetFloat(AlphaFalloffPropId, currentAlphaFalloff);
            }

            if (Rect != null)
            {
                float angleToDamage = context.HasValidCameraForwardPlanar
                    ? DamageIndicatorUtility.CalculatePlanarSignedAngle(context.CameraPosition, context.CameraForwardPlanar, context.DamagePosition)
                    : 0f;

                Rect.localEulerAngles = new Vector3(0f, 0f, -angleToDamage);
            }

            float startFadeAlpha = 1f;
            if (enableStartFade)
            {
                float t = Mathf.Clamp01(elapsed * inverseStartFadeDuration);
                startFadeAlpha = Mathf.Clamp01(startFadeCurve.Evaluate(t));
            }

            ApplyColor(context.DamageColor, life, startFadeAlpha);
            return true;
        }

        private void ApplyColor(Color baseColor, float lifeAlpha, float startFadeAlpha)
        {
            if (Image == null)
            {
                return;
            }

            Color color = baseColor;
            color.a = Mathf.Clamp01(baseColor.a * opacityMultiplier * lifeAlpha * startFadeAlpha);
            Image.color = color;
        }

        private float ResolveLineArcAngle(Transform playerCamera, Vector3 damagePosition)
        {
            if (!scaleLineByDistance || playerCamera == null)
            {
                return startArcAngle;
            }

            Vector3 offset = damagePosition - playerCamera.position;
            float distanceSqr = offset.sqrMagnitude;
            float t = DamageIndicatorUtility.EvaluateDistanceSqr01(nearDistance, farDistance, distanceSqr);
            return Mathf.Lerp(nearLineArcAngle, farLineArcAngle, t);
        }
    }
}