using UnityEngine;

// Attach to SUIMONO_Module. Other scenes without this component retain their original settings.
[DisallowMultipleComponent]
public sealed class WaterCaptureSettings : MonoBehaviour
{
    [Tooltip("URP Asset Renderer List의 WaterCapture_Renderer 위치. 기본 카메라는 변경하지 않습니다.")]
    [Min(0)] public int rendererIndex = 1;
    [Range(10f, 60f)] public float reflectionFramesPerSecond = 30f;
    [Range(10f, 60f)] public float screenCaptureFramesPerSecond = 30f;
    [Tooltip("이동/회전 중 화면 캡처 최대 갱신률. 이동해도 이 상한을 넘지 않습니다.")]
    [Range(10f, 120f)] public float screenMotionFramesPerSecond = 60f;
    [Tooltip("살아 있는 파문/왜곡 입자가 없으면 해당 캡처를 쉬게 합니다.")]
    public bool skipIdleScreenEffects = true;
    [Tooltip("카메라가 이 거리 이상 움직이면 이동 중 갱신률을 사용합니다.")]
    [Min(.01f)] public float screenMovementThreshold = .25f;
    [Min(.01f)] public float reflectionMovementThreshold = 2f;
    [Range(.1f, 15f)] public float screenRotationThreshold = .5f;
    [Range(.1f, 15f)] public float reflectionRotationThreshold = 3f;
    [Min(10f)] public float reflectionRenderDistance = 600f;
    [Min(10f)] public float transparencyRenderDistance = 250f;
    public bool captureShadows;
    public bool halfPrecisionTextures = true;

    public static bool NeedsRefresh(bool initialized, float elapsed, float fps, float movementSquared,
        float movementThreshold, float rotation, float rotationThreshold, bool configurationChanged,
        float maximumMotionFps = 60f)
    {
        bool moving = movementSquared >= movementThreshold * movementThreshold || rotation >= rotationThreshold;
        float rate = moving ? Mathf.Max(fps, maximumMotionFps) : fps;
        return !initialized || configurationChanged || elapsed + .00001f >= 1f / Mathf.Max(1f, rate);
    }
}
