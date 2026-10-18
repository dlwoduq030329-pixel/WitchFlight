using System;
using UnityEngine;

// Stable identifiers, independent of array order and MagicType IDs.
public enum PerkId
{
    None = 0, FireBurn = 1, FireIgnition = 2, IceSlow = 3, IceMark = 4,
    VisionAim = 5, VisionCombo = 6, ThunderRadius = 7, ThunderCore = 8,
    HealingPower = 9, HealingCleanse = 10, BindingLock = 11, BindingCost = 12,
    DarkCost = 13, DarkHarvest = 14, CurseRange = 15, CurseEfficiency = 16,
    MineArming = 17, MineRadius = 18, BeamWidth = 19, BeamFocus = 20,
    FlarePulse = 21, FlareEfficiency = 22, SmokeRadius = 23, SmokeDuration = 24,
    GuardianKnot = 25, WindTurning = 26, BrakingRune = 27, FirstWind = 28,
    Tailwind = 29, DeepMana = 30, ManaBreath = 31, FrugalRune = 32,
    EchoSpell = 33, ManaRemnant = 34
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
    [Tooltip("보조 비율. 불꽃: MP 할인, 플레어: 성공 시 쿨타임 할인.")]
    [Range(0f, 1f)] public float secondaryAmount;
    [Tooltip("화상 지속 / 얼음 연계 창 / 정화 보호 / 연속 명중 창 / 광선 발동 / 추가 플레어 지연 / 마나 환급 쿨타임(초).")]
    [Min(0f)] public float seconds;
    [Tooltip("얼음: 채널 보너스 길이(초), 천둥: 중심 반경 비율, 광선: 조준 단절 유예(초).")]
    [Min(0f)] public float condition;
}

[CreateAssetMenu(fileName = "PerkStatTable", menuName = "WitchFlight/Combat/Perk Stat Table")]
public sealed class PerkStatTable : ScriptableObject
{
    [Header("34 effects (IDs and fixed stage order never depend on array order)")]
    public PerkDefinition[] perks = CreateDefaults();
    [Header("XP balancing - maximum four choices, one mastered slot")]
    [Min(1)] public int firstChoiceXp = 100;
    [Min(1.01f)] public float xpGrowth = 1.5f;
    [Min(0.1f)] public float passiveInterval = 2f;
    [Min(0)] public int passiveXp = 4;
    [Min(0)] public int roundBonusXp = 30;
    [Range(0.01f, 1f)] public float damageFraction = 0.25f;
    [Min(1)] public int damageQuotasPerLife = 4;
    [Min(0)] public int damageXp = 6;
    [Min(0)] public int killXp = 12;
    [Min(0f)] public float killRewardCooldown = 20f;
    [Min(0)] public int bindingXp = 4;
    [Min(0f)] public float bindingRewardCooldown = 10f;
    [Min(0)] public int flareXp = 4;
    [Min(0f)] public float flareRewardCooldown = 10f;
    [Min(0)] public int flagXp = 10;
    [Min(1f)] public float actionWindowSeconds = 60f;
    [Min(0)] public int actionXpLimit = 30;
    [Header("Stacking limits")]
    [Range(0f, 0.9f)] public float manaDiscountCap = 0.4f;
    [Range(0f, 0.9f)] public float cooldownDiscountCap = 0.3f;
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
        D(PerkId.FireBurn,"고열 잔불","{seconds}초 동안 총 {amount} 화상 피해. 중첩 없이 갱신.",8,2),
        D(PerkId.FireIgnition,"신속 점화","록온 시간 {percent}% 감소, MP 소모 {secondaryPercent}% 감소.",.3f,0,.15f),
        D(PerkId.IceSlow,"혹한","둔화 강도 {percent}% 증가.",.2f),
        D(PerkId.IceMark,"균열 각인","{seconds}초 내 다른 슬롯의 다음 공격 피해 {percent}% 증가.",.25f,3,0,1),
        D(PerkId.VisionAim,"확장 조준","즉발 판정 반경 {percent}% 증가.",.2f),
        D(PerkId.VisionCombo,"연속 관통","{seconds}초 내 연속 명중의 두 번째 피해 {percent}% 증가.",.35f,3),
        D(PerkId.ThunderRadius,"폭풍권","폭발 반경 {percent}% 증가.",.15f),
        D(PerkId.ThunderCore,"낙뢰 집중","폭발 중심부 피해 {percent}% 증가.",.45f,0,0,.35f),
        D(PerkId.HealingPower,"응급 증폭","회복량 {percent}% 증가.",.2f),
        D(PerkId.HealingCleanse,"정화","둔화·구속 해제. {seconds}초간 새 둔화·구속 지속 시간 {percent}% 감소.",.5f,2),
        D(PerkId.BindingLock,"신속 결박","록온 시간 {percent}% 감소.",.2f),
        D(PerkId.BindingCost,"절제된 결박","MP 소모 {percent}% 감소.",.35f),
        D(PerkId.DarkCost,"절제된 제물","시전 체력 소모 {percent}% 감소.",.2f),
        D(PerkId.DarkHarvest,"생명 수확","실제 적중 시 회복량 {percent}% 증가.",.5f),
        D(PerkId.CurseRange,"긴 손아귀","사거리 {percent}% 증가.",.15f),
        D(PerkId.CurseEfficiency,"지속 흡수","초당 MP 소모 {percent}% 감소.",.25f),
        D(PerkId.MineArming,"신속한 사역마","소환체의 록온 시간 {percent}% 감소.",.3f),
        D(PerkId.MineRadius,"추적의 각인","소환체의 탐색·공격 사거리 {percent}% 증가.",.3f),
        D(PerkId.BeamWidth,"확장 렌즈","광선 판정 반경 {percent}% 증가.",.25f),
        D(PerkId.BeamFocus,"열 집중","같은 적에게 {seconds}초 연속 명중 시 피해 {percent}% 증가.",.35f,.75f,0,.2f),
        D(PerkId.FlarePulse,"연속 살포","{seconds}초 뒤 추적 해제 1회 추가.",1,.35f),
        D(PerkId.FlareEfficiency,"효율 방출","추적 해제 성공 시 MP {percent}% 환급, 이번 쿨타임 {secondaryPercent}% 감소.",.5f,0,.2f),
        D(PerkId.SmokeRadius,"광역 차폐","연막 반경 {percent}% 증가.",.15f),
        D(PerkId.SmokeDuration,"고밀도 잔류","연막 유지 시간 {amount}초 증가.",3),
        D(PerkId.GuardianKnot,"수호의 매듭","최대 HP {amount} 증가. 즉시 회복 없음.",20),
        D(PerkId.WindTurning,"바람결 타기","선회 능력 {percent}% 증가. 이동 속도는 유지.",.2f),
        D(PerkId.BrakingRune,"멈춤의 룬","제동 성능 {percent}% 증가.",.25f),
        D(PerkId.FirstWind,"첫바람의 축복","정지에서 1단계까지 가속 {percent}% 증가.",.25f),
        D(PerkId.Tailwind,"순풍의 가호","부스트 MP 소모 {percent}% 감소.",.2f),
        D(PerkId.DeepMana,"깊은 마력샘","최대 MP {percent}% 증가.",.15f),
        D(PerkId.ManaBreath,"마력의 숨결","자연 MP 회복량 {percent}% 증가.",.15f),
        D(PerkId.FrugalRune,"절약의 룬","마법 MP 소모 {percent}% 감소. 패링·부스트 제외.",.08f),
        D(PerkId.EchoSpell,"되울림의 주문","마법 쿨타임 {percent}% 감소. 패링 제외.",.1f),
        D(PerkId.ManaRemnant,"잔향 흡수","적중 시 최대 MP {percent}% 회복. 재발동 {seconds}초.",.04f,4)
    };
}
