using UnityEngine;
using UnityEngine.UI;

namespace Lovatto.DamagePointer
{
    internal sealed class DamageIndicatorSlot
    {
        public bool active;
        public int activationOrder;
        public string reuseKey;
        public Vector3 damagePosition;
        public Color color;

        public GameObject rootObject;
        public RectTransform rect;
        public Image image;
        public DamageIndicatorBase indicator;
    }

    /// <summary>
    /// Immutable data passed to indicator implementations when a slot is activated.
    /// </summary>
    public readonly struct DamageIndicatorActivationContext
    {
        public readonly Transform PlayerCamera;
        public readonly Vector3 CameraPosition;
        public readonly Vector3 CameraForwardPlanar;
        public readonly bool HasValidCameraForwardPlanar;
        public readonly Vector3 DamagePosition;
        public readonly Color DamageColor;

        /// <summary>
        /// Creates activation context data for the current trigger event.
        /// </summary>
        public DamageIndicatorActivationContext(
            Transform playerCamera,
            Vector3 cameraPosition,
            Vector3 cameraForwardPlanar,
            bool hasValidCameraForwardPlanar,
            Vector3 damagePosition,
            Color damageColor)
        {
            PlayerCamera = playerCamera;
            CameraPosition = cameraPosition;
            CameraForwardPlanar = cameraForwardPlanar;
            HasValidCameraForwardPlanar = hasValidCameraForwardPlanar;
            DamagePosition = damagePosition;
            DamageColor = damageColor;
        }
    }

    /// <summary>
    /// Immutable per-frame data passed to active indicator implementations.
    /// </summary>
    public readonly struct DamageIndicatorTickContext
    {
        public readonly float DeltaTime;
        public readonly Transform PlayerCamera;
        public readonly Vector3 CameraPosition;
        public readonly Vector3 CameraForwardPlanar;
        public readonly bool HasValidCameraForwardPlanar;
        public readonly Vector3 DamagePosition;
        public readonly Color DamageColor;

        /// <summary>
        /// Creates tick context data for one update frame.
        /// </summary>
        public DamageIndicatorTickContext(
            float deltaTime,
            Transform playerCamera,
            Vector3 cameraPosition,
            Vector3 cameraForwardPlanar,
            bool hasValidCameraForwardPlanar,
            Vector3 damagePosition,
            Color damageColor)
        {
            DeltaTime = deltaTime;
            PlayerCamera = playerCamera;
            CameraPosition = cameraPosition;
            CameraForwardPlanar = cameraForwardPlanar;
            HasValidCameraForwardPlanar = hasValidCameraForwardPlanar;
            DamagePosition = damagePosition;
            DamageColor = damageColor;
        }
    }

    public enum IndicatorAnimations
    {
        None,
        ZoomIn,
        ZoomInBounce,
        Shake,
        ZoomOut,
        BigSlide,
        ShakeAndSlide,
        SideShake,
        AlphaHighlight,
    }
}