using UnityEngine;

// Attach to SUIMONO_Module. Other scenes without this component retain their original settings.
[DisallowMultipleComponent]
public sealed class WaterCaptureSettings : MonoBehaviour
{
    [Tooltip("URP Asset Renderer List의 WaterCapture_Renderer 위치. 기본 카메라는 변경하지 않습니다.")]
    [Min(0)] public int rendererIndex = 1;
    [Range(10f, 60f)] public float reflectionFramesPerSecond = 30f;
    [Range(10f, 60f)] public float screenCaptureFramesPerSecond = 30f;
    [Tooltip("카메라가 이 거리 이상 움직이면 갱신 주기를 기다리지 않습니다.")]
    [Min(.01f)] public float screenMovementThreshold = .25f;
    [Min(.01f)] public float reflectionMovementThreshold = 2f;
    [Range(.1f, 15f)] public float screenRotationThreshold = .5f;
    [Range(.1f, 15f)] public float reflectionRotationThreshold = 3f;
    [Min(10f)] public float reflectionRenderDistance = 600f;
    [Min(10f)] public float transparencyRenderDistance = 250f;
    public bool captureShadows;
    public bool halfPrecisionTextures = true;

    public static bool NeedsRefresh(bool initialized, float elapsed, float fps, float movementSquared,
        float movementThreshold, float rotation, float rotationThreshold, bool configurationChanged)
    {
        return !initialized || configurationChanged || elapsed >= 1f / Mathf.Max(1f, fps) ||
            movementSquared >= movementThreshold * movementThreshold || rotation >= rotationThreshold;
    }
}
