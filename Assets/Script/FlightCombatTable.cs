using UnityEngine;

[CreateAssetMenu(menuName = "WitchFlight/Combat/Flight Combat Table")]
public sealed class FlightCombatTable : ScriptableObject
{
    private static FlightCombatTable cached;
    public static FlightCombatTable Default => cached != null ? cached : cached = Resources.Load<FlightCombatTable>("FlightCombatTable");
    [Header("Body ram (host authoritative)")]
    public bool enableRamming = true;
    [Min(0f)] public float minimumRamSpeed = 12f;
    [Min(0f)] public float ramDamage = 30f;
    [Min(.1f)] public float ramCooldown = 1f;
    [Min(0f)] public float ramKnockback = 7f;
    [Min(0f)] public float ramStun = .15f;
    [Min(.1f)] public float droppedFlagOwnerDelay = 1.2f;
    public bool dropFlagOnWallCrash = true;
    [Header("Spawn protection")]
    [Min(0f)] public float spawnProtectionSeconds = 2.5f;
    [Tooltip("몸 중심에서 회전 보호막 중심까지의 거리(m). 보호막 자체 크기와 별개입니다.")]
    [Min(0f)] public float spawnProtectionOrbitRadius = .8f;
    [Min(.05f)] public float spawnShieldScale = .5f;
    [Range(0f, 1f)] public float spawnShieldOpacity = .5f;
    [Header("벽 충돌 보호")]
    [Min(0f)] public float wallCrashGraceSeconds = 3f;
    [Tooltip("벽으로 실제 이동해 온 속도가 이 값 미만이면 접촉만으로 피해를 주지 않습니다.")]
    [Min(.1f)] public float minimumWallImpactSpeed = 3f;
    [Header("급제동: 최고 속도 전진 중 Q")]
    public bool enableCobra = true;
    [Range(.5f, 1f)] public float cobraMinimumSpeedRatio = .9f;
    [Min(.2f)] public float cobraSeconds = 1.4f;
    [Min(.5f)] public float cobraCooldown = 4f;
    [Range(30f, 100f)] public float cobraPitch = 80f;
    [Min(0f)] public float cobraLift = 2f;
    [Tooltip("기수를 세운 중간 구간에서 진입 방향의 반대로 미끄러지는 최대 속도(m/s).")]
    [Min(0f)] public float cobraBackwardSpeed = 6f;
    [Header("Smoke interior visibility")]
    [Min(0f)] public float smokeClearDistance = 5f;
    [Min(.1f)] public float smokeFadeDistance = 2f;
    public Color smokeInteriorColor = new Color(.22f, .24f, .26f, .995f);
    [Header("Death presentation (local body, no gameplay collisions)")]
    public bool fallingDeath = true;
    [Min(.2f)] public float deathVisualSeconds = 8f;
    [Min(1f)] public float deathGravity = 16f;
    [Min(1f)] public float broomGravity = 10f;
    [Tooltip("사망 복제 뼈대를 관절별로 시뮬레이션합니다. 플레이어/네트워크 판정에는 영향을 주지 않습니다.")]
    public bool limpDeathBody = true;
    [Range(0f, 2f)] public float deathLimbMotion = .8f;
    [Min(.01f)] public float deathJointRadius = .09f;
    [Min(.1f)] public float deathWaterSinkSpeed = 2.5f;
    [Tooltip("시체가 수면을 통과할 때 한 번만 재생. 비우면 기존 수면 파문을 사용합니다.")]
    public GameObject deathWaterSplashPrefab;
    [Header("Portal presentation (+Z normal)")]
    public GameObject portalEntryPrefab;
    public GameObject portalExitPrefab;
    [Min(.1f)] public float portalVfxSeconds = .7f;
    [Min(.1f)] public float portalVfxSize = 2f;
    [Header("Rear-view mirror")]
    public bool rearViewEnabled = true;
    [Tooltip("메인 카메라 기준 좌후방/우후방 90도 구역의 적 감지 거리. 같은 구역에 여러 명이 있어도 화면은 하나만 켭니다. 훈련 봇 포함.")]
    [Min(1f)] public float rearViewDistance = 100f;
    [Tooltip("거리와 무관하게 카메라 좌후방/우후방에서 나를 록온하는 적 또는 날아오는 적 투사체가 있는 구역 화면을 켭니다.")]
    public bool showLockingThreats = true;
    [Tooltip("감지한 적/투사체를 직접 바라보는 후방 화면의 가로 시야각입니다.")]
    [Range(90f, 120f)] public float rearViewFov = 110f;
    [Tooltip("화면 좌상단 원점이 아닌 Unity 뷰포트 좌하단 원점, 0..1 비율입니다.")]
    [InspectorName("Left View Rect (position / size)")]
    public Rect rearViewRect = new Rect(.015f, .65f, .31f, .29f);
    [Tooltip("오른쪽 위협 화면 위치·크기. 화면 좌하단 원점의 0..1 비율입니다.")]
    public Rect rightThreatViewRect = new Rect(.675f, .65f, .31f, .29f);
    [Tooltip("비워두면 말풍선 모양을 자동 생성합니다. Sprite의 불투명한 부분에만 카메라 영상이 표시됩니다.")]
    public Sprite threatViewMask;
}
