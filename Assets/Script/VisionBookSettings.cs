using UnityEngine;

[CreateAssetMenu(menuName = "WitchFlight/Combat/Vision Books")]
public sealed class VisionBookSettings : ScriptableObject
{
    [Range(1, 12)] public int bookCount = 2;
    [Min(0f)] public float empoweredDamage = 60f;
    [Min(1f)] public float empoweredRadiusMultiplier = 2f;
    public GameObject closedBookPrefab;
    public GameObject openBookPrefab;
    [UnityEngine.Serialization.FormerlySerializedAs("orbitRadius")]
    [Min(.1f)] public float shoulderDistance = 1.1f;
    [UnityEngine.Serialization.FormerlySerializedAs("orbitHeight")]
    public float shoulderHeight = 1.2f;
    public float shoulderForwardOffset = 0f;
    [Tooltip("책등 반대편의 벌어지는 방향은 캐릭터 +Z. 아래 모서리가 앞으로 나오도록 기울입니다.")]
    [Range(-60f, 60f)] public float backwardTiltDegrees = 20f;
    [Tooltip("사용자 책 프리팹의 책등 반대편(벌어지는 방향)을 +Z, 위쪽을 +Y로 맞추는 회전 보정입니다.")]
    public Vector3 prefabRotationCorrection;
    [Tooltip("두 바퀴 전체 시간. 첫 바퀴는 닫힌 상태, 두 번째 바퀴에서 펼칩니다.")]
    [Min(.1f)] public float openingSeconds = 1.6f;
    [Min(0f)] public float bobHeight = .07f;
    [Min(0f)] public float bobSpeed = 2f;
    [Min(.01f)] public float bookScale = .35f;
    private static VisionBookSettings cached;
    public static VisionBookSettings Default
    {
        get
        {
            if (cached == null) cached = Resources.Load<VisionBookSettings>("VisionBookSettings");
            if (cached == null) { cached = CreateInstance<VisionBookSettings>(); cached.hideFlags = HideFlags.DontSave; }
            return cached;
        }
    }
}
