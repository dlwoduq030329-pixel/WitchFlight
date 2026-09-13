using UnityEngine;

namespace Lovatto.DamagePointer
{
    /// <summary>
    /// Math helpers used by the damage indicator runtime.
    /// </summary>
    public static class DamageIndicatorUtility
    {
        private const float EpsilonSqr = 0.0001f;

        /// <summary>
        /// Returns camera position and horizontal forward vector used for planar indicator math.
        /// </summary>
        public static bool TryGetPlanarCameraData(Transform playerCamera, out Vector3 cameraPosition, out Vector3 cameraForwardPlanar)
        {
            if (playerCamera == null)
            {
                cameraPosition = Vector3.zero;
                cameraForwardPlanar = Vector3.forward;
                return false;
            }

            cameraPosition = playerCamera.position;
            cameraForwardPlanar = playerCamera.forward;
            cameraForwardPlanar.y = 0f;

            if (cameraForwardPlanar.sqrMagnitude < EpsilonSqr)
            {
                cameraForwardPlanar = Vector3.forward;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Calculates signed planar angle in degrees from camera forward to damage direction.
        /// </summary>
        public static float CalculatePlanarSignedAngle(in Vector3 cameraPosition, in Vector3 cameraForwardPlanar, Vector3 damagePosition)
        {
            if (cameraForwardPlanar.sqrMagnitude < EpsilonSqr)
            {
                return 0f;
            }

            Vector3 dirToDamage = damagePosition - cameraPosition;
            dirToDamage.y = 0f;

            if (dirToDamage.sqrMagnitude < EpsilonSqr)
            {
                return 0f;
            }

            float crossY = (cameraForwardPlanar.z * dirToDamage.x) - (cameraForwardPlanar.x * dirToDamage.z);
            float dot = (cameraForwardPlanar.x * dirToDamage.x) + (cameraForwardPlanar.z * dirToDamage.z);
            return Mathf.Atan2(crossY, dot) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Convenience overload that resolves planar camera data internally.
        /// </summary>
        public static float CalculatePlanarSignedAngle(Transform playerCamera, Vector3 damagePosition)
        {
            if (!TryGetPlanarCameraData(playerCamera, out Vector3 cameraPosition, out Vector3 cameraForwardPlanar))
            {
                return 0f;
            }

            return CalculatePlanarSignedAngle(cameraPosition, cameraForwardPlanar, damagePosition);
        }

        /// <summary>
        /// Normalized distance in [0..1] using linear distance values.
        /// </summary>
        public static float EvaluateDistance01(float nearDistance, float farDistance, float distance)
        {
            float safeFar = Mathf.Max(nearDistance + 0.01f, farDistance);
            return Mathf.InverseLerp(nearDistance, safeFar, distance);
        }

        /// <summary>
        /// Normalized distance in [0..1] using squared distance values to avoid sqrt operations.
        /// </summary>
        public static float EvaluateDistanceSqr01(float nearDistance, float farDistance, float distanceSqr)
        {
            float safeFar = Mathf.Max(nearDistance + 0.01f, farDistance);
            float nearDistanceSqr = nearDistance * nearDistance;
            float farDistanceSqr = safeFar * safeFar;
            return Mathf.InverseLerp(nearDistanceSqr, farDistanceSqr, distanceSqr);
        }
    }
}