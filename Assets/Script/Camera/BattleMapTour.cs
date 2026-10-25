using UnityEngine;
using UnityEngine.UI;

// The host owns the phase clock. Each client samples the same tour locally.
[DefaultExecutionOrder(1000)]
[DisallowMultipleComponent]
public sealed class BattleMapTour : MonoBehaviour
{
    [Tooltip("실제 게임 카메라. 비워두면 CameraManager의 카메라를 찾습니다.")]
    [SerializeField] private Camera viewCamera;
    [Tooltip("01→02, 03→04, 05→06 순서. 위치와 회전을 그대로 사용합니다. 파란 Z축이 시선 방향입니다.")]
    [SerializeField] private Transform[] points = new Transform[6];
    [Tooltip("켜면 훈련장 입장 시에도 맵 둘러보기 → 3·2·1을 재생합니다. 입장 전에 설정하세요. 일반 전투에는 영향이 없습니다.")]
    [SerializeField] private bool playInTraining;
    [Tooltip("각 두 지점 사이 이동 시간. 온라인에서는 호스트 설정을 공유합니다.")]
    [SerializeField, Min(0.1f)] private float moveSeconds = 3f;
    [Tooltip("각 장면의 밝아짐/암전 시간. 마지막 게임 시점 복귀에도 적용합니다.")]
    [SerializeField, Min(0.05f)] private float fadeSeconds = 0.6f;
    [SerializeField, Range(20f, 100f)] private float fieldOfView = 60f;
    [Tooltip("선택 사항: 직접 만든 전체 화면 검정 Image. 비워두면 자동 생성합니다.")]
    [SerializeField] private Image fadeImage;

    private CameraFollow follow;
    private bool running, cameraOwned, followWasEnabled;
    private Vector3 savedPosition;
    private Quaternion savedRotation;
    private float savedFov;
    private GameObject generatedFade;
    private Color savedFadeColor;
    private bool savedFadeEnabled;
    private bool fadeCached;

    public float MoveSeconds => Mathf.Max(0.1f, moveSeconds);
    public float FadeSeconds => Mathf.Max(0.05f, fadeSeconds);
    public bool ShouldPlay(bool training) => IsConfigured && (!training || playInTraining);
    public bool IsConfigured
    {
        get
        {
            if (!isActiveAndEnabled || points == null || points.Length != 6) return false;
            foreach (Transform point in points) if (point == null) return false;
            return true;
        }
    }

    private void LateUpdate()
    {
        BattleFlag flag = BattleFlag.Instance;
        bool active = flag != null && flag.Object != null && flag.Object.IsValid &&
            flag.Phase == BattleStartPhase.MapTour && IsConfigured;
        if (!active) { StopTour(); return; }
        if (!running && !BeginTour()) return;

        SampleTimeline(flag.TourDuration - flag.PhaseRemainingSeconds,
            flag.TourMoveSeconds, flag.TourFadeSeconds, out int shot, out float travel, out float black);
        if (shot < 3)
        {
            Transform from = points[shot * 2], to = points[shot * 2 + 1];
            float t = Mathf.SmoothStep(0f, 1f, travel);
            viewCamera.transform.SetPositionAndRotation(Vector3.Lerp(from.position, to.position, t),
                Quaternion.Slerp(from.rotation, to.rotation, t));
            viewCamera.fieldOfView = fieldOfView;
        }
        else
        {
            // Return under full black, then reveal the gameplay camera before 3-2-1.
            RestoreCamera();
        }
        if (fadeImage != null)
        {
            fadeImage.enabled = true;
            fadeImage.color = new Color(0f, 0f, 0f, black);
        }
    }

    internal static void SampleTimeline(float elapsed, float move, float fade,
        out int shot, out float travel, out float black)
    {
        move = Mathf.Max(0.1f, move);
        fade = Mathf.Max(0.05f, fade);
        elapsed = Mathf.Max(0f, elapsed);
        float length = move + 2f * fade;
        shot = Mathf.Min(3, Mathf.FloorToInt(elapsed / length));
        float local = elapsed - shot * length;
        travel = shot == 3 ? 1f : Mathf.Clamp01((local - fade) / move);
        black = shot == 3 || local < fade ? 1f - Mathf.Clamp01(local / fade)
            : Mathf.Clamp01((local - fade - move) / fade);
    }

    private bool BeginTour()
    {
        if (viewCamera == null && CameraManager.Instance != null)
            viewCamera = CameraManager.Instance.GetComponent<Camera>();
        if (viewCamera == null) return false;
        savedPosition = viewCamera.transform.position;
        savedRotation = viewCamera.transform.rotation;
        savedFov = viewCamera.fieldOfView;
        follow = viewCamera.GetComponent<CameraFollow>();
        followWasEnabled = follow != null && follow.enabled;
        if (followWasEnabled) follow.enabled = false;
        cameraOwned = true;
        EnsureFade();
        running = true;
        return true;
    }

    private void EnsureFade()
    {
        if (fadeImage == null)
        {
            generatedFade = new GameObject("Map tour fade (generated)", typeof(Canvas));
            generatedFade.transform.SetParent(transform, false);
            Canvas canvas = generatedFade.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32760;
            GameObject screen = new GameObject("Black", typeof(RectTransform), typeof(Image));
            screen.transform.SetParent(generatedFade.transform, false);
            fadeImage = screen.GetComponent<Image>();
            fadeImage.raycastTarget = false;
            RectTransform rect = fadeImage.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
        savedFadeColor = fadeImage.color;
        savedFadeEnabled = fadeImage.enabled;
        fadeCached = true;
    }

    private void RestoreCamera()
    {
        if (!cameraOwned) return;
        cameraOwned = false;
        if (viewCamera != null)
        {
            viewCamera.transform.SetPositionAndRotation(savedPosition, savedRotation);
            viewCamera.fieldOfView = savedFov;
        }
        if (follow != null)
        {
            follow.enabled = followWasEnabled;
            Player local = Player.LocalPlayer;
            if (followWasEnabled && local != null && local.Object != null && local.Object.IsValid)
                follow.SetTarget(local.gameObject);
        }
    }

    private void StopTour()
    {
        if (!running) return;
        RestoreCamera();
        if (fadeImage != null && fadeCached)
        {
            fadeImage.color = savedFadeColor;
            fadeImage.enabled = generatedFade == null && savedFadeEnabled;
        }
        fadeCached = running = false;
    }

    private void OnDisable() => StopTour();
    private void OnDestroy() { if (generatedFade != null) Destroy(generatedFade); }

    private void OnDrawGizmosSelected()
    {
        if (points == null) return;
        for (int i = 0; i < points.Length; i++)
        {
            Transform point = points[i];
            if (point == null) continue;
            Gizmos.color = i % 2 == 0 ? Color.cyan : Color.yellow;
            Gizmos.DrawSphere(point.position, 1f);
            Gizmos.DrawRay(point.position, point.forward * 12f);
            if (i % 2 == 0 && i + 1 < points.Length && points[i + 1] != null)
                Gizmos.DrawLine(point.position, points[i + 1].position);
        }
    }
}
