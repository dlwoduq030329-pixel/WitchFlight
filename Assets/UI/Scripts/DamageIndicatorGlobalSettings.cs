using UnityEngine;

namespace Lovatto.DamagePointer
{
    /// <summary>
    /// Policy used when an indicator is triggered with a non-empty reuse key.
    /// </summary>
    public enum DamageIndicatorReusePolicy
    {
        /// <summary>
        /// Always spawn/recycle slots without keyed reuse.
        /// </summary>
        Disabled = 0,

        /// <summary>
        /// Reuse currently visible indicator with the same key, regardless of direction.
        /// </summary>
        ReuseVisibleIndicatorForSameKey = 1,

        /// <summary>
        /// Reuse currently visible indicator with the same key only when incoming direction is similar.
        /// </summary>
        ReuseVisibleIndicatorForSameKeyAndSimilarDirection = 2,
    }

    public enum IndicatorRPType
    {
        BuiltIn,
        URP,
        HDRP
    }

    [CreateAssetMenu(fileName = "DamageIndicatorGlobalSettings", menuName = "Lovatto/Damage Indicator/Global Settings")]
    /// <summary>
    /// Shared settings asset that controls pooling, timing mode, and keyed reuse behavior.
    /// </summary>
    public class DamageIndicatorGlobalSettings : ScriptableObject
    {
        public IndicatorRPType renderPipelineType = IndicatorRPType.URP;
        [Tooltip("Total number of indicators preallocated in the pool.")]
        [Min(1)] public int poolSize = 8;

        [Tooltip("Use Time.unscaledDeltaTime for animation updates (useful for pause menus or slow-motion).")]
        public bool useUnscaledTime = false;

        [Tooltip("When the pool is full, reuses the oldest active indicator slot instead of ignoring new hits.")]
        public bool recycleOldestWhenPoolIsFull = true;

        [Tooltip("How repeated hits with the same key are reused while still visible.")]
        public DamageIndicatorReusePolicy reusePolicy = DamageIndicatorReusePolicy.Disabled;

        [Tooltip("Maximum angular difference (degrees) allowed for Similar Direction reuse policy.")]
        [Range(0f, 180f)]
        public float maxReuseAngleDelta = 18f;

        public Material[] proceduralMaterials;

        public Material GetRenderPipelineMaterial()
        {
            if (proceduralMaterials == null || proceduralMaterials.Length == 0)
            {
                return null;
            }

            switch (renderPipelineType)
            {
                case IndicatorRPType.BuiltIn:
                    return proceduralMaterials.Length > 0 ? proceduralMaterials[0] : null;
                case IndicatorRPType.URP:
                    return proceduralMaterials.Length > 1 ? proceduralMaterials[1] : null;
                case IndicatorRPType.HDRP:
                    return proceduralMaterials.Length > 2 ? proceduralMaterials[2] : null;
                default:
                    return null;
            }
        }
    }
}