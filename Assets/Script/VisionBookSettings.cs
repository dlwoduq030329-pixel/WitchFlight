using UnityEngine;

[CreateAssetMenu(menuName = "WitchFlight/Combat/Vision Books")]
public sealed class VisionBookSettings : ScriptableObject
{
    [Header("Empowered thunder slow aura")]
    [Tooltip("강화 번개 시전 대기 범위에서 감속되는 적에게 표시할 로컬 VFX. 반복 재생 파티클 프리팹을 지정하세요.")]
    public GameObject thunderSlowAuraPrefab;
    public Vector3 thunderSlowAuraOffset = new Vector3(0f, 1f, 0f);
    [Min(.01f)] public float thunderSlowAuraScale = 1f;
    [Range(1, 12)] public int bookCount = 2;
    [Min(0f)] public float empoweredDamageBonus = .5f;
    [Min(0f)] public float minimumDamageBonus = 30f;
    [ColorUsage(false, true)] public Color pageGlowColor = new Color(.62f, .2f, 1f);
    [ColorUsage(false, true)] public Color arcaneBlueColor = new Color(.12f,.38f,1f,1f);
    [Min(0f)] public float pageGlowIntensity = 10f;
    [Min(0f)] public float pageLightIntensity = 5f;
    [Header("Empowered body electricity")]
    [ColorUsage(false, true)] public Color chargedElectricColor = new Color(.38f, .3f, 1f);
    [Min(0f)] public float chargedElectricIntensity = 3f;
    [Min(.001f)] public float chargedElectricWidth = .09f;
    [Min(0f)] public float chargedElectricReach = .5f;
    [Min(0f)] public float chargedAuraIntensity = 2f;
    [Range(0f,1f)] public float chargedAuraOpacity = .45f;
    [Min(0f)] public float chargedAuraDrag = .045f;
    [Min(0f)] public float chargedAuraMaxBend = .85f;
    [Min(0f)] public float chargedAuraMoteRate = 18f;
    [Min(.1f)] public float chargedEnergyHeight = 1.4f;
    [Min(.1f)] public float chargedEnergyRiseSpeed = 1.6f;
    [Min(.01f)] public float chargedEnergyWidth = .7f;
    [ColorUsage(false, true)] public Color chargedAuraColor = new Color(.58f,.18f,1f,1f);
    [Min(.1f)] public float pageLightRange = 4f;
    [Header("Charged wand focus")]
    [Min(.01f)] public float wandFocusRadius = .24f;
    [Min(.001f)] public float wandFocusBandWidth = .025f;
    [Min(0f)] public float wandFocusOrbitSpeed = 220f;
    [Min(0f)] public float wandFocusLightIntensity = 2f;
    [Header("Page energy streams")]
    [Min(0f)] public float energyHeight = 1.2f;
    [Min(0f)] public float energyWidth = .12f;
    [Tooltip("이동 속도에 비례해 마력이 뒤로 늘어나는 길이입니다.")]
    [Min(0f)] public float energyVelocityTrail = .07f;
    [Min(0f)] public float energyMaxTrail = 2f;
    [Min(.025f)] public float energyLifetime = .4f;
    [Min(0f)] public float energyEmissionMultiplier = 2f;
    [Header("Floating page runes")]
    [Min(0f)] public float runeRate = 12f;
    [Min(.1f)] public float runeMaxTrail = 1.5f;
    [Min(.05f)] public float runeLifetime = .45f;
    [Min(.01f)] public float runeSize = .16f;
    [Min(0f)] public float runeRiseSpeed = .6f;
    public Vector3 energyLocalOffset = new Vector3(0, .08f, 0);
    [Min(1f)] public float empoweredRadiusMultiplier = 2f;
    public GameObject closedBookPrefab;
    public GameObject openBookPrefab;
    public GameObject animatedBookPrefab;
    [Tooltip("펼친 상태에서 닫힌 상태로 진행하는 모델 애니메이션입니다.")]
    public AnimationClip bookClosingClip;
    [UnityEngine.Serialization.FormerlySerializedAs("orbitRadius")]
    [Min(.1f)] public float shoulderDistance = 1.1f;
    [UnityEngine.Serialization.FormerlySerializedAs("orbitHeight")]
    public float shoulderHeight = 1.3f;
    public float shoulderForwardOffset = 0f;
    [Tooltip("책등 반대편의 벌어지는 방향은 캐릭터 +Z. 아래 모서리가 앞으로 나오도록 기울입니다.")]
    [Range(-60f, 60f)] public float backwardTiltDegrees = 20f;
    [Tooltip("사용자 책 프리팹의 책등 반대편(벌어지는 방향)을 +Z, 위쪽을 +Y로 맞추는 회전 보정입니다.")]
    public Vector3 prefabRotationCorrection;
    [Tooltip("펼친 프리팹의 종이 면을 +Z, 글의 위쪽을 +Y로 정렬하는 별도 보정입니다. 프리팹 루트 회전도 포함해 조절합니다.")]
    public Vector3 openPrefabRotationCorrection = new Vector3(90f, 0f, 0f);
    [Tooltip("열거나 닫으며 한 바퀴 회전하는 시간. 느림-빠름-느림으로 변화합니다.")]
    [Min(.1f)] public float openingSeconds = 1.6f;
    [Min(0f)] public float bobHeight = .07f;
    [Min(0f)] public float bobSpeed = 2f;
    [Min(.01f)] public float bookScale = 1f;
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
