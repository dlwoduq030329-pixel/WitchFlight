using System;
using UnityEngine;

public enum MagicEffectKind
{
    DirectDamage = 0, SlowDamage = 1, AreaDamage = 2, Healing = 3,
    Binding = 4, LifeSteal = 5, GuidedChannel = 6, Mine = 7, BeamChannel = 8,
    Flare = 9, Smoke = 10
}

[Serializable]
public struct MagicStatEntry
{
    public MagicType magic;
    public string displayName;
    [TextArea] public string description;
    public Sprite icon;
    public MagicEffectKind effect;

    [Min(0f)] public float apCost;
    [Min(0f)] public float cooldownSeconds;
    [Min(0.01f)] public float lockChargeSeconds;
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
    [Min(0f)] public float range;
    [Min(0f)] public float damage;
    [Min(0f)] public float hitStunSeconds;
    [Min(0f)] public float knockbackForce;
    [Range(0f, 1f)] public float movementMultiplier;
    [Tooltip("Ice 둔화, Binding 속박, Mine / Smoke 유지 시간. Smoke는 최대 120초.")]
    [Min(0f)] public float effectDuration;
    [Tooltip("광역/기뢰/연막 반경. Smoke는 최대 100이며 캐릭터 조준점이 범위 안에 있는지 검사합니다.")]
    [Min(0f)] public float radius;
    [Tooltip("기뢰가 멈춘 뒤 무장되기까지의 시간.")]
    [Min(0f)] public float activationDelay;
    [Tooltip("기뢰를 발사하여 전진시키는 거리.")]
    [Min(0f)] public float placementDistance;
    public bool requiresTarget;
    public bool requiresFullLock;

    [Header("Health / channel costs")]
    [Tooltip("현재 HP가 이 값 이하이면 발동 불가. Dark 기본값 50.")]
    [Min(0f)] public float healthCost;
    [Tooltip("Healing의 즉시 회복량.")]
    [Min(0f)] public float healing;
    [Tooltip("Dark가 패링되지 않고 적에게 실제 피해를 입혔을 때의 회복량.")]
    [Min(0f)] public float healOnHit;
    [Min(0f)] public float damagePerSecond;
    [Min(0f)] public float apPerSecond;
    [Tooltip("최대 MP 대비 초당 소모 비율. Curse 기본 0.5 = 만충전에서 2초.")]
    [Range(0f, 1f)] public float maxApFractionPerSecond;
    [Tooltip("지속 피해 판정 간격. 프레임/RPC마다 피해를 보내지 않습니다.")]
    [Min(0.02f)] public float channelTickSeconds;
    [Tooltip("Curse 자동 유도의 조준 방향 기준 반각(도).")]
    [Range(1f, 90f)] public float autoAimHalfAngle;

    [Header("Optional local-only VFX (empty = generated fallback)")]
    [SerializeField] public GameObject castVfxPrefab;
    [SerializeField] public GameObject projectileVfxPrefab;
    [SerializeField] public GameObject impactVfxPrefab;
    [Tooltip("LineRenderer 사용 또는 길이 1의 +Z 방향 프리팹.")]
    [SerializeField] public GameObject beamVfxPrefab;
    [Tooltip("VFX 유지 시간(초). Flare는 캐릭터 등 뒤에서 계속 분출하는 시간이며 추적 해제/무적 지속 시간이 아닙니다.")]
    [Min(0.05f)] public float vfxLifetime;

    [Header("Flare / Smoke (visual-only prefabs, optional)")]
    [Tooltip("플레어는 +Z로 지속 분출하는 VFX(등 뒤를 따라가며 ParticleSystem을 World 공간으로 재생). 연막은 반경 1인 VFX(서버 반경에 맞춰 확대). 비우면 기본 파티클을 생성합니다.")]
    [SerializeField] public GameObject utilityVfxPrefab;
    [SerializeField, Min(0f)] public float flareVfxBackOffset;
    [SerializeField, Min(0f)] public float flareVfxSpeed;

    public bool IsChanneled => effect == MagicEffectKind.GuidedChannel || effect == MagicEffectKind.BeamChannel;
    public bool UsesAutoTarget => requiresTarget || effect == MagicEffectKind.GuidedChannel;
    public float ChannelManaPerSecond(float maxAp) =>
        Mathf.Max(0f, apPerSecond) + Mathf.Max(0f, maxAp) * Mathf.Max(0f, maxApFractionPerSecond);
}

[CreateAssetMenu(fileName = "MagicStatTable", menuName = "WitchFlight/Combat/Magic Stat Table")]
public sealed class MagicStatTable : ScriptableObject
{
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
    [Header("Parry (right click; nullifies, never reflects)")]
    [Min(0f)] public float parryApCost = 16f;
    [Min(0.01f)] public float parryWindowSeconds = 0.3f;
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
            lockChargeSeconds = 1f, projectileLifetime = 4f, projectileRadius = 0.12f,
            movementMultiplier = 1f, channelTickSeconds = 0.1f, autoAimHalfAngle = 20f,
            vfxLifetime = 1.5f
        };
        switch (magic)
        {
            case MagicType.Fire:
                e.effect = MagicEffectKind.DirectDamage; e.damage = 45; e.apCost = 60;
                e.range = 220; e.cooldownSeconds = 3.75f; e.lockChargeSeconds = 0.8f;
                e.projectileSpeed = 75; e.projectileTurnSpeed = 220; e.projectileLifetime = 5f;
                e.requiresTarget = e.requiresFullLock = true;
                e.description = "높은 피해의 유도 마법. 길게 록온한 뒤 버튼을 놓아 발사합니다."; break;
            case MagicType.Ice:
                e.effect = MagicEffectKind.SlowDamage; e.damage = 18; e.apCost = 36;
                e.range = 190; e.cooldownSeconds = 2.7f; e.lockChargeSeconds = 0.35f;
                e.projectileSpeed = 85; e.projectileTurnSpeed = 320;
                e.movementMultiplier = 0.6f; e.effectDuration = 2.5f;
                e.requiresTarget = e.requiresFullLock = true;
                e.description = "빠르게 록온하여 발사합니다. 적중한 적의 이동 속도를 낮춥니다."; break;
            case MagicType.Vision:
                e.effect = MagicEffectKind.DirectDamage; e.damage = 30; e.apCost = 16;
                e.range = 260; e.cooldownSeconds = 0.9f; e.projectileRadius = 0; e.hitscanRadius = 0.3f;
                e.description = "적은 마나로 긴 사거리의 즉발 광선을 발사합니다."; break;
            case MagicType.Thunder:
                e.effect = MagicEffectKind.AreaDamage; e.damage = 35; e.apCost = 80;
                e.range = 100; e.cooldownSeconds = 6; e.castSeconds = 0.3f; e.radius = 12;
                e.projectileRadius = 0; e.hitscanRadius = 0.3f;
                e.description = "짧은 시전 후 정면 조준 지점에 광역 피해를 줍니다."; break;
            case MagicType.Mine:
                e.effect = MagicEffectKind.Mine; e.damage = 70; e.apCost = 50;
                e.cooldownSeconds = 7.5f; e.projectileSpeed = 20; e.placementDistance = 20;
                e.activationDelay = 0.5f; e.effectDuration = 15; e.radius = 8; e.projectileRadius = 0.3f;
                e.description = "전방에 기뢰를 발사합니다. 무장 후 자신과 아군을 포함한 누구든 접근하면 폭발합니다."; break;
            case MagicType.Dark:
                e.effect = MagicEffectKind.LifeSteal; e.damage = 65; e.healthCost = 50; e.healOnHit = 60;
                e.range = 180; e.cooldownSeconds = 4.5f; e.projectileRadius = 0; e.hitscanRadius = 0.3f;
                e.description = "체력 50을 소모하는 즉발 공격. 적에게 피해를 입히면 체력 60을 회복합니다."; break;
            case MagicType.Healing:
                e.effect = MagicEffectKind.Healing; e.healing = 40; e.apCost = 20; e.cooldownSeconds = 18;
                e.description = "체력 40을 즉시 회복합니다. 재사용 대기 시간이 깁니다."; break;
            case MagicType.Binding:
                e.effect = MagicEffectKind.Binding; e.apCost = 50; e.cooldownSeconds = 18;
                e.range = 190; e.effectDuration = 3; e.lockChargeSeconds = 0.6f;
                e.projectileSpeed = 75; e.projectileTurnSpeed = 300;
                e.requiresTarget = e.requiresFullLock = true;
                e.description = "록온 후 유도 마법을 발사합니다. 피해 없이 3초 동안 적의 이동을 막습니다."; break;
            case MagicType.Curse:
                e.effect = MagicEffectKind.GuidedChannel; e.range = 35; e.damagePerSecond = 12;
                e.maxApFractionPerSecond = 0.5f; e.cooldownSeconds = 0.375f;
                e.description = "누르는 동안 가까운 적을 자동 추적합니다. 벽이나 화면 밖으로 벗어나면 끊어집니다."; break;
            case MagicType.Razier:
                e.effect = MagicEffectKind.BeamChannel; e.range = 100; e.damagePerSecond = 18;
                e.apPerSecond = 40; e.cooldownSeconds = 0.375f; e.projectileRadius = 0; e.hitscanRadius = 0.3f;
                e.description = "누르는 동안 정면에 직선 광선을 유지하며 마나를 소모합니다."; break;
            case MagicType.Flare:
                e.effect = MagicEffectKind.Flare; e.apCost = 24; e.cooldownSeconds = 4.5f;
                e.flareVfxBackOffset = 0.7f; e.flareVfxSpeed = 14f; e.vfxLifetime = 3f;
                e.displayName = "플레어";
                e.description = "등 뒤로 플레어를 방출하여 이미 자신을 추적 중인 유도탄의 추적을 끊습니다. 무적이나 회피 이동은 부여하지 않습니다."; break;
            case MagicType.Smoke:
                e.effect = MagicEffectKind.Smoke; e.apCost = 40; e.cooldownSeconds = 15;
                e.radius = 24; e.effectDuration = 5;
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
