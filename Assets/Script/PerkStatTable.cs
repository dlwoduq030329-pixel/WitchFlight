using System;
using UnityEngine;

// Stable identifiers, independent of array order and MagicType IDs.
public enum PerkId
{
    None = 0, FireBurn = 1, FireIgnition = 2, IceSlow = 3, IceMark = 4,
    VisionAim = 5, VisionCombo = 6, ThunderRadius = 7, ThunderCore = 8,
    HealingPower = 9, HealingCleanse = 10, BindingLock = 11, BindingCost = 12,
    // Keep the serialized ID/name: CurseEfficiency now slows its victim while channel damage continues.
    DarkCost = 13, DarkHarvest = 14, CurseRange = 15, CurseEfficiency = 16,
    MineArming = 17, MineRadius = 18, BeamWidth = 19, BeamFocus = 20,
    FlarePulse = 21, FlareEfficiency = 22, SmokeRadius = 23, SmokeDuration = 24,
    // Keep legacy enum names/IDs for saved perk masks: BrakingRune = 수호진, FirstWind = 돌풍의 축복.
    GuardianKnot = 25, WindTurning = 26, BrakingRune = 27, FirstWind = 28,
    Tailwind = 29, DeepMana = 30, ManaBreath = 31, FrugalRune = 32,
    EchoSpell = 33, ManaRemnant = 34,
    BladeCharges = 35, BladeWound = 36, StormCount = 37, StormPrecision = 38,
    RainCharge = 39, RainPursuit = 40, TimeRadius = 41, TimeHealing = 42,
    FestivalSpeed = 43, FestivalGuard = 44
}

[Serializable]
public sealed class PerkDefinition
{
    public PerkId id;
    public string displayName;
    [TextArea] public string description;
    public Sprite icon;
    [Tooltip("비율은 0.2 = 20%. 정수 효과는 HP/초/피해량. 각 항목의 설명을 참고하세요.")]
    [Min(0f)] public float amount;
    [Tooltip("보조 값. 불꽃: MP 할인, 사역마: 공격 간격 감소, 확장렌즈: 피해 보너스, 칼날 집중: 기본 피해.")]
    [Min(0f)] public float secondaryAmount;
    [Tooltip("화상 지속 / 얼음 연계 창 / 정화 보호 / 연속 명중 창 / 마나 환급 쿨타임(초).")]
    [Min(0f)] public float seconds;
    [Tooltip("얼음: 채널 보너스 길이(초), 천둥: 중심 반경 비율, 열 집중: 연속 피해 시간(초), 칼날 집중: 최대 MP 대비 충전 비율.")]
    [Min(0f)] public float condition;
}

[CreateAssetMenu(fileName = "PerkStatTable", menuName = "WitchFlight/Combat/Perk Stat Table")]
public sealed class PerkStatTable : ScriptableObject
{
    [Header("44 effects (IDs and fixed stage order never depend on array order)")]
    public PerkDefinition[] perks = CreateDefaults();
    [Header("XP balancing - maximum four choices, one mastered slot")]
    [Min(1)] public int firstChoiceXp = 100;
    [Min(1.01f)] public float xpGrowth = 1.5f;
    [Min(0.1f)] public float passiveInterval = 2f;
    [Min(0)] public int passiveXp = 4;
    [Header("Flag carrier penalties (combat XP is never reduced)")]
    [Tooltip("깃발 보유 중 전진·후진·부스트 목표 속도 배율. 0.8 = 20% 감속. 선회 성능은 유지합니다.")]
    [Range(0.05f, 1f)] public float flagCarrierSpeedMultiplier = 0.8f;
    [Tooltip("깃발 보유 중 자동 경험치 획득 속도 배율. 0.125 = 일반 2 XP/초 대비 0.25 XP/초. 공격·처치 보상에는 적용하지 않습니다.")]
    [Range(0f, 1f)] public float flagCarrierPassiveXpMultiplier = 0.125f;
    [Header("Combat and round XP")]
    [Min(0)] public int roundBonusXp = 30;
    [Range(0.01f, 1f)] public float damageFraction = 0.25f;
    [Min(1)] public int damageQuotasPerLife = 4;
    [Min(0)] public int damageXp = 12;
    [Min(0)] public int killXp = 30;
    [Min(0f)] public float killRewardCooldown = 20f;
    [Min(0)] public int bindingXp = 4;
    [Min(0f)] public float bindingRewardCooldown = 10f;
    [Min(0)] public int flareXp = 4;
    [Min(0f)] public float flareRewardCooldown = 10f;
    [Min(0)] public int flagXp = 10;
    [Min(1f)] public float actionWindowSeconds = 60f;
    [Min(0)] public int actionXpLimit = 90;
    [Header("Stacking limits")]
    [Range(0f, 0.9f)] public float manaDiscountCap = 0.55f;
    [Range(0f, 0.9f)] public float cooldownDiscountCap = 0.4f;
    [Min(0.05f)] public float burnTickSeconds = 0.5f;
    private static PerkStatTable cached;
    public static PerkStatTable Default
    {
        get
        {
            if (cached != null) return cached;
            cached = Resources.Load<PerkStatTable>("PerkStatTable");
            if (cached == null) { cached = CreateInstance<PerkStatTable>(); cached.hideFlags = HideFlags.DontSave; }
            return cached;
        }
    }
    public PerkDefinition Get(PerkId id)
    {
        if (perks != null) foreach (var p in perks) if (p != null && p.id == id) return p;
        return null;
    }
    public float Amount(PerkId id) => Mathf.Max(0f, Get(id)?.amount ?? 0f);
    public int RequiredXp(int earned) => PerkRules.RequiredXp(firstChoiceXp, xpGrowth, earned);

    private static PerkDefinition D(PerkId id, string name, string description, float amount, float seconds = 0f, float secondary = 0f, float condition = 0f)
        => new PerkDefinition { id = id, displayName = name, description = description, amount = amount, seconds = seconds, secondaryAmount = secondary, condition = condition };
    public static PerkDefinition[] CreateDefaults() => new[]
    {
        D(PerkId.FireBurn,"고열 잔불","{seconds}초 동안 총 {amount} 화상 피해. 중첩 없이 갱신.",16,2),
        D(PerkId.FireIgnition,"신속 점화","록온 시간 {percent}% 감소, MP 소모 {secondaryPercent}% 감소.",.45f,0,.25f),
        D(PerkId.IceSlow,"혹한","둔화 강도 {percent}% 증가.",.15f),
        D(PerkId.IceMark,"균열 각인","{seconds}초 내 다른 슬롯의 다음 공격 피해 {percent}% 증가.",.3f,4,0,1),
        D(PerkId.VisionAim,"확장 조준","즉발 판정 반경 {percent}% 증가.",.25f),
        D(PerkId.VisionCombo,"연속 관통","{seconds}초 내 연속 명중의 두 번째 피해 {percent}% 증가.",.75f,4),
        D(PerkId.ThunderRadius,"폭풍권","폭발 반경 {percent}% 증가.",.2f),
        D(PerkId.ThunderCore,"낙뢰 집중","폭발 중심부 피해 {percent}% 증가.",.3f,0,0,.45f),
        D(PerkId.HealingPower,"응급 증폭","회복량 {percent}% 증가.",.5f),
        D(PerkId.HealingCleanse,"정화","둔화·구속 해제. {seconds}초간 새 둔화·구속 지속 시간 {percent}% 감소.",.65f,3),
        D(PerkId.BindingLock,"저주의 결박","결박된 적이 받는 피해량 {percent}% 증가.",0.3f,0.0f,0.0f,0.0f),
        D(PerkId.BindingCost,"온전한 속박","속박 지속시간 {amount}초 증가.",2.0f,0.0f,0.0f,0.0f),
        D(PerkId.DarkCost,"피의 환원","적중 시 체력 회복량 {percent}% 증가.",0.25f,0.0f,0.0f,0.0f),
        D(PerkId.DarkHarvest,"살육의 마법","피해량 {percent}% 증가. 이 마법으로 적 플레이어 처치 시 쿨타임 즉시 초기화.",0.3f,0.0f,0.0f,0.0f),
        D(PerkId.CurseRange,"긴 손아귀","사거리 {percent}% 증가.",.3f),
        D(PerkId.CurseEfficiency,"저주의 늪","저주로 피해를 받는 동안 적의 이동속도 {percent}% 감소.",0.3f,0.0f,0.0f,0.0f),
        D(PerkId.MineArming,"신속한 사역마","소환체의 록온 시간 {percent}% 감소, 공격 간격 {secondaryPercent}% 감소.",.45f,0,.2f),
        D(PerkId.MineRadius,"이중계약","한 번에 사역마를 {amount}마리 소환합니다.",2.0f,0.0f,0.0f,0.0f),
        D(PerkId.BeamWidth,"확장렌즈","광선 판정 반경 {percent}% 증가, 피해량 {secondaryPercent}% 증가.",0.5f,0.0f,0.2f,0.0f),
        D(PerkId.BeamFocus,"열 집중","같은 적에게 {condition}초 연속 피해를 주면 {seconds}초간 총 {amount} 화상 피해. 중첩 없이 갱신.",16.0f,2.0f,0.0f,2.0f),
        D(PerkId.FlarePulse,"텔레키네시스","개별 플레어의 유인 반경이 {amount}m로 증가하고 아군을 추적하는 마법도 유인합니다.",60.0f,0.0f,0.0f,0.0f),
        D(PerkId.FlareEfficiency,"효율 방출","플레어 유지시간 {amount}초 증가.",1.0f,0.0f,0.0f,0.0f),
        D(PerkId.SmokeRadius,"광역 차폐","연막 반경 {percent}% 증가.",.2f),
        D(PerkId.SmokeDuration,"밀집","연막 안의 적이 마법을 록온하는 시간 {percent}% 증가.",0.3f,0.0f,0.0f,0.0f),
        D(PerkId.GuardianKnot,"수호의 매듭","최대 HP {amount} 증가. 즉시 회복 없음.",35),
        D(PerkId.WindTurning,"바람결 타기","선회 능력 {percent}% 증가. 이동 속도는 유지.",.35f),
        D(PerkId.BrakingRune,"수호진","대쉬 시전 시 {amount}초간 무적. 몸 주변에 수호 방패가 회전합니다.",1f),
        D(PerkId.FirstWind,"돌풍의 축복","최대 대쉬 갯수 {amount}개 증가. 추가된 대쉬는 즉시 충전됩니다.",1f),
        D(PerkId.Tailwind,"순풍의 가호","대쉬 충전 시간 {percent}% 감소.",.3f),
        D(PerkId.DeepMana,"깊은 마력샘","최대 MP {percent}% 증가.",.25f),
        D(PerkId.ManaBreath,"마력의 숨결","자연 MP 회복량 {percent}% 증가.",.3f),
        D(PerkId.FrugalRune,"절약의 룬","마법 MP 소모 {percent}% 감소. 패링·부스트 제외.",.2f),
        D(PerkId.EchoSpell,"되울림의 주문","마법 쿨타임 {percent}% 감소. 패링 제외.",.2f),
        D(PerkId.ManaRemnant,"잔향 흡수","적중 시 최대 MP {percent}% 회복. 재발동 {seconds}초. 지속형 마법 제외.",.08f,3),
        D(PerkId.BladeCharges,"연속 검격","검기 공격 횟수가 {amount}회로 증가.",5f),
        D(PerkId.BladeWound,"깊은 상처","상처의 받는 회복량 감소율이 {percent}%로 증가.",0.75f),
        D(PerkId.StormCount,"쏟아지는 마탄","발사하는 마탄이 {amount}발로 증가.",40f),
        D(PerkId.StormPrecision,"집중 사격","마탄의 탄퍼짐 {percent}% 감소.",0.5f),
        D(PerkId.RainCharge,"급속 충전","차징 중 초당 마나 소모량이 {amount}로 증가.",20f),
        D(PerkId.RainPursuit,"칼날 집중","누르는 동안 최대 마나의 50%까지 충전하고, 놓으면 에임 방향으로 검 하나를 발사합니다. 피해 {secondary} + 소모 마나 × {amount}. 충전량에 따라 크기·판정 증가, 헤드샷 2배.",.8f,0f,40f,.5f),
        D(PerkId.TimeRadius,"확장된 시간","시간 역장의 반지름 {percent}% 증가.",0.3f),
        D(PerkId.TimeHealing,"회복의 시간","역장 내부 아군의 체력을 초당 {amount} 회복. 역장끼리 중첩되지 않음.",10f),
        D(PerkId.FestivalSpeed,"축제의 바람","대축제 대상의 이동속도 {percent}% 증가.",0.3f),
        D(PerkId.FestivalGuard,"먀오녀의 수호","대축제 지속 중 피해를 {amount}회 방어.",1f)
    };
}
