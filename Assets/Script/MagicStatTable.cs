using System;
using UnityEngine;

public enum MagicEffectKind
{
    DirectDamage = 0, SlowDamage = 1, AreaDamage = 2, Healing = 3,
    Binding = 4, LifeSteal = 5, GuidedChannel = 6, Mine = 7, BeamChannel = 8,
    Flare = 9, Smoke = 10, DelayedArea = 11, PursuitSummon = 12
}

[Serializable]
public struct MagicStatEntry
{
    // Authority-only cast snapshot; never serialized into the balancing asset.
    [NonSerialized] public int perkSlot, perkCast;
    [NonSerialized] public PerkDefinition perkFirst, perkSecond;
    [NonSerialized] public Fusion.PlayerRef perkAttacker;
    public MagicType magic;
    public string displayName;
    [TextArea] public string description;
    public Sprite icon;
    [Header("Lock-on charging circle (behind caster, +Z normal)")]
    public GameObject lockChargeCirclePrefab;
    [Min(.1f)] public float lockChargeCircleSize;
    public Vector3 lockChargeCircleOffset;
    public MagicEffectKind effect;

    [Header("Description UI only (Slider values 0..1; does not change combat stats)")]
    [Tooltip("피해량 표시 Slider. 0 = 비움, 1 = 가득 참. 4칸 UI라면 0.25 = 1칸.")]
    [Range(0f, 1f)] public float displayDamage;
    [Range(0f, 1f)] public float displayAimTime;
    [Range(0f, 1f)] public float displayRange;
    [Range(0f, 1f)] public float displayManaCost;
    [Tooltip("Slider 옆 평가 문구입니다. 예: 높음 / 낮음 / 없음. 실제 전투 수치를 자동 환산하지 않습니다.")]
    public string displayDamageLabel;
    public string displayAimTimeLabel;
    public string displayRangeLabel;
    public string displayManaCostLabel;
    [Tooltip("선택: 공격 마법 / 방어 마법 / 보조 마법 등 설명창의 분류.")]
    public string displayCategory;
    [Tooltip("선택: 록온 / 즉발 / 범위 지정 / 소환 등 설명창의 발동 방식.")]
    public string displayCastType;
    [TextArea] public string displayFeatures;

    [Header("Combat stats (independent of description UI)")]
    [Min(0f)] public float apCost;
    [Min(0f)] public float cooldownSeconds;
    [Tooltip("록온에 필요한 시간. 록온을 사용하지 않는 마법은 0.")]
    [Min(0f)] public float lockChargeSeconds;
    [Tooltip("발동까지의 시전 지연(초). 즉발 마법은 0.")]
    [Min(0f)] public float castSeconds;
    [Tooltip("0이면 히트스캔. 양수이면 실제 네트워크 발사체.")]
    [Min(0f)] public float projectileSpeed;
    [Tooltip("비록온 투사체의 선회 속도. 록온 투사체는 현재 대상 위치로 직접 유도되어 이 값을 사용하지 않습니다.")]
    [Min(0f)] public float projectileTurnSpeed;
    [Min(0.01f)] public float projectileLifetime;
    [Tooltip("날아가는 투사체의 충돌 반경입니다. 즉발/직선 광선은 Hitscan Radius를 사용합니다.")]
    [Min(0f)] public float projectileRadius;
    [Tooltip("즉발 마법과 직선 유지 광선의 SphereCast 반경(월드 단위). 0이면 기존 Raycast. 투사체 크기나 광역 폭발 반경과는 별개입니다.")]
    [SerializeField, Min(0f)] public float hitscanRadius;
    [Tooltip("마법 사거리. 소환은 최초 적 탐색 반경이며, 한 번 찾은 적은 이 범위를 벗어나도 추적합니다.")]
    [Min(0f)] public float range;
    [Min(0f)] public float damage;
    [Min(0f)] public float hitStunSeconds;
    [Min(0f)] public float knockbackForce;
    [Range(0f, 1f)] public float movementMultiplier;
    [Tooltip("Ice 둔화, Binding 속박, 소환체 / Smoke 유지 시간. Smoke는 최대 120초.")]
    [Min(0f)] public float effectDuration;
    [Tooltip("낙뢰/연막 반경. 낙뢰는 공중에서도 구형 범위 판정. 소환체 사거리는 Range / Summon Attack Range.")]
    [Min(0f)] public float radius;
    [Tooltip("소환체가 처음 깨어나기까지의 시간. 구 기뢰의 무장 지연으로도 사용.")]
    [Min(0f)] public float activationDelay;
    [Tooltip("플레이어 앞 소환 거리. 벽을 통과하지 않습니다. 구 기뢰의 전진 거리로도 사용.")]
    [Min(0f)] public float placementDistance;
    public bool requiresTarget;
    public bool requiresFullLock;

    [Header("Thunder: hold to aim, release to confirm (world-space sphere)")]
    [Tooltip("범위 확정 시 번쩍인 뒤 폭발/피해까지의 시간. 기본 1.5초. Cast Seconds와 별개이며 위치는 고정됩니다.")]
    [Min(0.1f)] public float strikeDelay;
    [Tooltip("아무것도 조준하지 않을 때 공중 지정 거리. 누르는 동안 휠로 거리를 조절합니다.")]
    [Min(1f)] public float areaAimDistance;
    [Min(0.1f)] public float areaAimScrollSpeed;
    [Tooltip("반경 1 기준의 범위 프리팹. Collider / NetworkObject 없이 제작. 비우면 원 3개로 구형 범위를 표시합니다.")]
    public GameObject areaPreviewVfxPrefab;
    public GameObject areaWarningVfxPrefab;
    [Tooltip("범위 확정 순간 한 번 번쩍일 프리팹. 중심 원점, 아래 Reference Radius 기준으로 제작. 비우면 기본 섬광을 생성합니다.")]
    public GameObject thunderFlashVfxPrefab;
    [Tooltip("Strike Delay 후 작게 시작해 팍 커질 폭발 오브젝트. 비우면 Impact Vfx Prefab, 그것도 비우면 기본 구체를 사용합니다.")]
    public GameObject thunderBurstVfxPrefab;
    [Tooltip("프리팹이 Scale 1일 때의 반경. 0은 기본 1로 처리. 실제 피해 반경 / 이 값으로 크기를 맞춥니다.")]
    [Min(0.01f)] public float thunderVfxReferenceRadius;
    [Tooltip("폭발 확대 시간. 0은 기본 0.12초로 처리합니다.")]
    [Min(0.01f)] public float thunderBurstExpandSeconds;
    [Tooltip("최종 크기 대비 시작 크기. 0은 기본 0.08(8%)로 처리합니다.")]
    [Range(0.01f, 1f)] public float thunderBurstStartScale;

    [Header("Mine replacement: pursuing familiar (multiple summons)")]
    [Tooltip("플레이어당 동시 소환 수. 한도에서 새로 소환하면 가장 오래된 소환체를 교체합니다.")]
    [Range(1, 16)] public int summonMaxCount;
    [Min(0.1f)] public float summonMoveSpeed;
    [Min(1f)] public float summonAttackRange;
    [Tooltip("추적하는 적에게 유지할 거리. 시전자와의 거리가 아닙니다.")]
    [Min(0.1f)] public float summonFollowDistance;
    [Tooltip("발사 시작 사이의 최소 간격. Lock Charge Seconds가 더 길면 록온 완료를 기다립니다.")]
    [Min(0.1f)] public float summonAttackInterval;
    [Tooltip("소환체 본체의 피격 반경. 이동 충돌이나 투사체 크기와는 별개입니다.")]
    [Min(0.05f)] public float summonHitRadius;
    [Tooltip("광선·저주가 합산하여 이 피해에 도달하면 소환체 소멸. 다른 공격(속박 포함)은 1회 적중 시 소멸.")]
    [Min(1f)] public float summonChannelHealth;
    [Tooltip("비우면 마법 구체와 회전 고리를 생성. 투사체 외형은 Projectile VFX Prefab에 별도로 할당합니다.")]
    public GameObject summonVfxPrefab;

    [Header("Optional summon animations (Animator on Summon VFX Prefab)")]
    [Tooltip("소환수 이동 모션. 이동 중 반복하며 정지하면 첫 자세를 유지합니다. 비워 두면 생략합니다.")]
    public AnimationClip summonMoveAnimation;
    [Tooltip("유도탄을 실제 발사할 때 1회 재생. 공격 판정과 발사 간격은 애니메이션 길이에 영향받지 않습니다.")]
    public AnimationClip summonAttackAnimation;
    [Tooltip("피격 소멸/수명 종료 시 1회 재생 후 외형 제거. 전투 판정은 즉시 사라집니다. 비우면 즉시 외형 제거.")]
    public AnimationClip summonDeathAnimation;

    [Header("Health / channel costs")]
    [Tooltip("현재 HP가 이 값 이하이면 발동 불가. Dark 기본값 30.")]
    [Min(0f)] public float healthCost;
    [Tooltip("Healing의 즉시 회복량.")]
    [Min(0f)] public float healing;
    [Tooltip("Dark가 패링되지 않고 적에게 실제 피해를 입혔을 때의 회복량.")]
    [Min(0f)] public float healOnHit;
    [Min(0f)] public float damagePerSecond;
    [Tooltip("실제 MP의 초당 소모량. Curse 기본 0.5 MP/초, Razier 기본 1 MP/초.")]
    [Min(0f)] public float apPerSecond;
    [Tooltip("추가로 소모할 최대 MP 대비 초당 비율. 현재 기본값은 0이며 Ap Per Second의 고정량만 사용합니다.")]
    [Range(0f, 1f)] public float maxApFractionPerSecond;
    [Tooltip("지속 피해 판정 간격. 프레임/RPC마다 피해를 보내지 않습니다.")]
    [Min(0.02f)] public float channelTickSeconds;
    [Tooltip("Curse 자동 유도의 조준 방향 기준 반각(도).")]
    [Range(1f, 90f)] public float autoAimHalfAngle;

    [Header("1. Lock-on complete VFX (at Player Magic Cast Root)")]
    [Tooltip("록온 완료 순간 지팡이 끝의 기존 MAGICROOT에 붙여 재생합니다. 발사/록온 취소 시 제거. 비우면 작은 빛을 생성합니다. NetworkObject 없는 연출 프리팹만 넣으세요.")]
    [SerializeField] public GameObject lockOnCompleteVfxPrefab;
    [Header("2. Main projectile VFX (visual only)")]
    [Tooltip("실제 투사체에 붙일 외형입니다. 기존 Projectile Vfx Prefab 할당을 그대로 사용합니다. 피해/충돌 판정은 Player의 네트워크 투사체가 담당합니다.")]
    [SerializeField] public GameObject projectileVfxPrefab;
    [Header("3. Secondary projectile VFX (no extra damage)")]
    [Tooltip("마법별 작은 연출용 투사체. 얼음은 메인 탄 적중 후 몸에서 발사되는 8개, 다른 투사체 마법은 기존 위성탄 연출에 사용합니다. 비우면 테이블 공통 프리팹/기본 연출을 사용합니다.")]
    [SerializeField] public GameObject secondaryProjectileVfxPrefab;
    [Header("4. Impact VFX")]
    [Tooltip("적중 연출. 얼음은 후속 작은 탄 8개가 전부 도착한 뒤 한 번 재생합니다. 후속 연출을 끄거나 벽/패링에 부딪힌 경우에는 즉시 재생합니다.")]
    [SerializeField] public GameObject impactVfxPrefab;
    [Header("Cast / instant / channeled VFX")]
    [Tooltip("발사/시전 순간의 연출입니다. 록온 완료 연출과 별개이며 즉발 마법에도 사용합니다.")]
    [SerializeField] public GameObject castVfxPrefab;
    [Tooltip("즉발/유지 광선 연출. LineRenderer 사용 또는 길이 1의 +Z 방향 프리팹. 천둥은 전용 Flash / Burst 슬롯을 사용합니다.")]
    [SerializeField] public GameObject beamVfxPrefab;
    [Tooltip("광선 LineRenderer에 사용할 재질. 프리팹 없이 재질만 넣어도 적용됩니다. 프리팹에 LineRenderer가 있으면 그 재질을 덮어쓰며, 없으면 적용하지 않습니다. 비우면 프리팹 재질 또는 기본 마법 색상을 사용합니다.")]
    [SerializeField] public Material beamMaterial;
    [Tooltip("록온 완료/시전/적중 VFX 유지 시간(초). 록온 완료 VFX는 발사/취소 시 먼저 제거됩니다. Flare는 등 뒤 분출 시간이며 추적 해제/무적 지속 시간이 아닙니다.")]
    [Min(0.05f)] public float vfxLifetime;

    [Header("Flare / Smoke (visual-only prefabs, optional)")]
    [Tooltip("플레어는 +Z로 지속 분출하는 VFX(등 뒤를 따라가며 ParticleSystem을 World 공간으로 재생). 연막은 반경 1인 VFX(서버 반경에 맞춰 확대). 비우면 기본 파티클을 생성합니다.")]
    [SerializeField] public GameObject utilityVfxPrefab;
    [SerializeField, Min(0f)] public float flareVfxBackOffset;
    [SerializeField, Min(0f)] public float flareVfxSpeed;
    [Tooltip("플레어 크기 배율. 기본 입자와 직접 지정한 플레어 프리팹에 적용합니다. 이전 데이터의 0은 기본값 1.8로 처리합니다.")]
    [SerializeField, Min(0.1f)] public float flareVfxScale;
    [Tooltip("기본 플레어의 발광 강도. 직접 지정한 VFX의 재질은 변경하지 않습니다. 이전 데이터의 0은 기본값 2로 처리합니다.")]
    [SerializeField, Min(0.1f)] public float flareVfxBrightness;

    public bool IsChanneled => effect == MagicEffectKind.GuidedChannel || effect == MagicEffectKind.BeamChannel;
    public bool TargetsAllies => effect == MagicEffectKind.Healing;
    public bool UsesLockIndicator => requiresTarget || TargetsAllies;
    public bool UsesAutoTarget => UsesLockIndicator || effect == MagicEffectKind.GuidedChannel;
    public float ChannelManaPerSecond(float maxAp) =>
        Mathf.Max(0f, apPerSecond) + Mathf.Max(0f, maxAp) * Mathf.Max(0f, maxApFractionPerSecond);
}

[CreateAssetMenu(fileName = "MagicStatTable", menuName = "WitchFlight/Combat/Magic Stat Table")]
public sealed class MagicStatTable : ScriptableObject
{
    private static MagicStatTable defaultTable;
    public static MagicStatTable Default
    {
        get
        {
            if (defaultTable == null) defaultTable = Resources.Load<MagicStatTable>("MagicStatTable");
            return defaultTable;
        }
    }

    // All magic UI uses this lookup. None/invalid IDs must never show a fallback spell.
    public Sprite GetIcon(MagicType magic)
    {
        return magic >= MagicType.Fire && magic <= MagicType.Smoke ? GetStats(magic).icon : null;
    }

    public GameObject GetSecondaryProjectileVfx(MagicStatEntry stats, bool iceFollowUp = false)
    {
        if (stats.secondaryProjectileVfxPrefab != null) return stats.secondaryProjectileVfxPrefab;
        return iceFollowUp ? iceImpactShardVfxPrefab : projectileSatelliteVfxPrefab;
    }

    [Header("Local projectile prediction (visual only; server decides hits)")]
    public bool predictLocalProjectiles = true;
    [Tooltip("서버 확인을 받지 못한 임시 투사체를 제거할 때까지의 시간입니다.")]
    [Min(0.2f)] public float projectileConfirmationTimeout = 1.2f;
    [Tooltip("확인된 내 투사체의 화면상 선행 예측 상한입니다. 상대 투사체는 원격 보간을 유지합니다.")]
    [Range(0f, 0.3f)] public float projectilePredictionLead = 0.2f;
    [Min(1f)] public float projectileCorrectionSpeed = 18f;
    [Header("Projectile formation (visual only; excludes mines / hitscan)")]
    [Tooltip("작은 탄들이 등 뒤로 물러난 뒤 앞서 날아가며 핵심탄에 합쳐집니다. 피해/네트워크 투사체 수는 늘어나지 않습니다.")]
    [SerializeField] public bool projectileFormationEnabled = true;
    [SerializeField, Range(1, 12)] public int projectileSatelliteCount = 6;
    [Tooltip("비워 두면 마법 색상의 작은 구체와 궤적을 생성합니다. NetworkObject 없는 VFX 프리팹만 사용하세요.")]
    [SerializeField] public GameObject projectileSatelliteVfxPrefab;
    [SerializeField, Min(0.01f)] public float projectileCoreVisualScale = 1.8f;
    [SerializeField, Min(0.01f)] public float projectileSatelliteVisualScale = 0.35f;
    [Tooltip("작은 탄들이 몸 뒤에서 추가로 후퇴하는 거리입니다.")]
    [SerializeField, Min(0f)] public float projectileSatelliteBackDistance = 2f;
    [SerializeField, Min(0.01f)] public float projectileSatelliteBackSeconds = 0.18f;
    [Tooltip("핵심탄 대비 추월 속도입니다. 높일수록 뒤에서 앞으로 빠르게 따라잡습니다.")]
    [SerializeField, Min(1.1f)] public float projectileSatelliteSpeedMultiplier = 2.5f;
    [SerializeField, Min(0f)] public float projectileSatelliteSpread = 1f;
    [SerializeField, Min(0f)] public float projectileSatelliteLeadDistance = 3f;
    [Tooltip("적(대상이 없으면 최대 사거리)까지 남은 거리가 이 값 이하이면 합체를 시작합니다.")]
    [SerializeField, Min(0.01f)] public float projectileMergeStartDistance = 18f;
    [Tooltip("적까지 남은 거리가 이 값 이하이면 합체를 완료합니다. 시작 거리보다 작게 설정하세요.")]
    [SerializeField, Min(0f)] public float projectileMergeEndDistance = 3f;
    [Header("Ice follow-up (8 visual shards; one delayed hit)")]
    [Tooltip("메인 탄 접촉 후 몸에서 작은 탄 8개를 발사하고 마지막 탄 도착 시 피해/둔화를 한 번 적용합니다. 끄면 메인 탄 접촉 즉시 적용합니다.")]
    [SerializeField] public bool iceImpactFollowUpEnabled = true;
    [Tooltip("작은 얼음탄 VFX. 비우면 얼음 결정과 궤적을 생성합니다. NetworkObject 없는 프리팹만 넣으세요.")]
    [SerializeField] public GameObject iceImpactShardVfxPrefab;
    [SerializeField, Range(0.01f, 0.5f)] public float iceImpactLaunchInterval = 0.07f;
    [Tooltip("후속 탄 속도. 접촉 시 거리로 비행 시간을 계산하며 0.18~4초로 제한됩니다. 8번째 도착 시점에 서버가 피해/둔화를 적용합니다. 피해량은 바꾸지 않습니다.")]
    [SerializeField, Min(1f)] public float iceImpactShardSpeed = 110f;
    [SerializeField, Min(0f)] public float iceImpactRingRadius = 0.85f;
    [SerializeField, Min(0f)] public float iceImpactCurve = 2f;
    [SerializeField, Min(0.01f)] public float iceImpactShardScale = 0.3f;
    [Header("Parry (right click; nullifies, never reflects)")]
    [Min(0f)] public float parryApCost = 16f;
    [Min(0.01f)] public float parryWindowSeconds = 0.4f;
    [Min(0f)] public float parryCooldownSeconds = 0.3f;
    public Sprite parryIcon;
    public MagicStatEntry fallback = new MagicStatEntry { magic = MagicType.None };
    public MagicStatEntry[] magics = CreateDefaultEntries();

    public static MagicStatEntry[] CreateDefaultEntries()
    {
        var entries = new MagicStatEntry[(int)MagicType.Smoke];
        for (int i = 0; i < entries.Length; i++) entries[i] = DefaultEntry((MagicType)(i + 1));
        return entries;
    }

    public static MagicStatEntry DefaultEntry(MagicType magic)
    {
        var e = new MagicStatEntry {
            magic = magic, displayName = magic.ToString(), cooldownSeconds = 3f,
            lockChargeSeconds = 0f, projectileLifetime = 4f, projectileRadius = 0.12f,
            movementMultiplier = 1f, channelTickSeconds = 0.1f, autoAimHalfAngle = 20f,
            vfxLifetime = 1.5f
        };
        switch (magic)
        {
            case MagicType.Fire:
                e.effect = MagicEffectKind.DirectDamage; e.damage = 45; e.apCost = 45;
                e.range = 130f; e.cooldownSeconds = 3.75f; e.lockChargeSeconds = 0.8f;
                e.projectileSpeed = 75; e.projectileTurnSpeed = 220; e.projectileLifetime = 5f;
                e.requiresTarget = e.requiresFullLock = true;
                e.description = "높은 피해의 유도 마법. 길게 록온한 뒤 버튼을 놓아 발사합니다."; break;
            case MagicType.Ice:
                e.effect = MagicEffectKind.SlowDamage; e.damage = 25; e.apCost = 30;
                e.range = 90f; e.cooldownSeconds = 2.7f; e.lockChargeSeconds = 0.35f;
                e.projectileSpeed = 85; e.projectileTurnSpeed = 320;
                e.movementMultiplier = 0.4f; e.effectDuration = 2.5f;
                e.requiresTarget = e.requiresFullLock = true;
                e.description = "빠르게 록온하여 발사합니다. 적중한 적의 이동 속도를 낮춥니다."; break;
            case MagicType.Vision:
                e.effect = MagicEffectKind.DirectDamage; e.damage = 25; e.apCost = 30;
                e.range = 150f; e.cooldownSeconds = 2f; e.projectileRadius = 0; e.hitscanRadius = 0.3f;
                e.description = "긴 사거리의 즉발 광선을 발사합니다. 연속 명중으로 적을 압박합니다."; break;
            case MagicType.Thunder:
                e.effect = MagicEffectKind.DelayedArea; e.damage = 100; e.apCost = 65;
                e.range = 300f; e.cooldownSeconds = 7; e.radius = 12;
                e.strikeDelay = 1.5f; e.areaAimDistance = 50; e.areaAimScrollSpeed = 10;
                e.thunderVfxReferenceRadius = 1f; e.thunderBurstExpandSeconds = .12f; e.thunderBurstStartScale = .08f;
                e.projectileRadius = 0; e.hitscanRadius = 0.3f;
                e.description = "누르는 동안 공중의 범위를 조준하고 휠로 거리를 조절합니다. 놓으면 확정된 위치에 잠시 뒤 낙뢰가 떨어집니다."; break;
            case MagicType.Mine:
                e.effect = MagicEffectKind.PursuitSummon; e.damage = 18; e.apCost = 40;
                e.cooldownSeconds = 7.5f; e.projectileSpeed = 45; e.placementDistance = 2;
                e.activationDelay = 0.5f; e.effectDuration = 15; e.projectileRadius = 0.2f;
                e.range = 100f; e.lockChargeSeconds = 0.65f; e.summonMoveSpeed = 28;
                e.summonAttackRange = 50f; e.summonFollowDistance = 18; e.summonAttackInterval = 2;
                e.summonMaxCount = 4;
                e.summonHitRadius = 0.65f; e.summonChannelHealth = 50;
                e.description = "적을 추적하며 록온 유도탄을 발사하는 사역마를 소환합니다. 여러 개를 유지할 수 있으며 벽 너머로 공격하지 않습니다."; break;
            case MagicType.Dark:
                e.effect = MagicEffectKind.LifeSteal; e.damage = 70; e.healthCost = 30; e.healOnHit = 40;
                e.range = 130f; e.cooldownSeconds = 2f; e.projectileRadius = 0; e.hitscanRadius = 0.3f;
                e.description = "체력 30을 소모하는 즉발 공격. 적에게 피해를 입히면 체력 40을 회복합니다."; break;
            case MagicType.Healing:
                e.effect = MagicEffectKind.Healing; e.range = 200f; e.healing = 40; e.apCost = 20; e.cooldownSeconds = 18;
                e.description = "전방의 아군을 즉시 지정해 체력 40을 회복합니다. 지정한 아군이 없으면 자신을 회복합니다."; break;
            case MagicType.Binding:
                e.effect = MagicEffectKind.Binding; e.apCost = 50; e.cooldownSeconds = 18;
                e.range = 142.5f; e.effectDuration = 3; e.lockChargeSeconds = 0.6f;
                e.projectileSpeed = 75; e.projectileTurnSpeed = 300;
                e.requiresTarget = e.requiresFullLock = true;
                e.description = "록온 후 유도 마법을 발사합니다. 피해 없이 3초 동안 적의 이동을 막습니다."; break;
            case MagicType.Curse:
                e.effect = MagicEffectKind.GuidedChannel; e.range = 50f; e.damagePerSecond = 10;
                e.apPerSecond = 0.5f; e.cooldownSeconds = 0.375f;
                e.description = "누르는 동안 가까운 적을 자동 추적합니다. 벽이나 화면 밖으로 벗어나면 끊어집니다."; break;
            case MagicType.Razier:
                e.effect = MagicEffectKind.BeamChannel; e.range = 100f; e.damagePerSecond = 20;
                e.apPerSecond = 1; e.cooldownSeconds = 0.375f; e.projectileRadius = 0; e.hitscanRadius = 0.3f;
                e.description = "누르는 동안 정면에 직선 광선을 유지하며 마나를 소모합니다."; break;
            case MagicType.Flare:
                e.effect = MagicEffectKind.Flare; e.apCost = 50; e.cooldownSeconds = 6;
                e.flareVfxBackOffset = 0.7f; e.flareVfxSpeed = 14f; e.vfxLifetime = 3f;
                e.flareVfxScale = 1.8f; e.flareVfxBrightness = 2f;
                e.displayName = "플레어";
                e.description = "등 뒤로 플레어를 방출하여 이미 자신을 추적 중인 유도탄의 추적을 끊습니다. 무적이나 회피 이동은 부여하지 않습니다."; break;
            case MagicType.Smoke:
                e.effect = MagicEffectKind.Smoke; e.apCost = 40; e.cooldownSeconds = 15;
                // The former perk radius is now the base; SmokeRadius still adds 20% at stage one.
                e.radius = 36; e.effectDuration = 7;
                e.displayName = "연막";
                e.description = "현재 위치에 연막을 남깁니다. 내부의 캐릭터는 적에게 보이지 않지만 록온과 피격 판정은 유지됩니다."; break;
        }
        return e;
    }

    public MagicStatEntry GetStats(MagicType magic)
    {
        if (magics != null)
            foreach (MagicStatEntry entry in magics)
                if (entry.magic == magic) return entry;
        return magic >= MagicType.Fire && magic <= MagicType.Smoke ? DefaultEntry(magic) : fallback;
    }
}
