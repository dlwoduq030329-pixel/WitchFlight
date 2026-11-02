using UnityEngine;

[CreateAssetMenu(menuName = "WitchFlight/Giant Meow Raid")]
public sealed class GiantMeowRaidSettings : ScriptableObject
{
    [Header("Encounter")]
    public float health = 2000f;
    public float healthPerExtraPlayer = 1200f;
    public float height = 90f;
    public float hitRadius = 17f;
    public float entranceSeconds = 8f;
    public float recoverySeconds = 1.1f;
    [Min(0f)] public float spellDamageMultiplier = 3f;
    [Header("Continuous fire barrage")]
    [Min(.1f)] public float barrageInterval = .35f;
    [Min(.1f)] public float retreatBarrageInterval = .25f;
    public float volleyLifetime = 4f;
    public float volleyWarning = 1.5f;
    [Min(1)] public int barrageShotsPerTarget = 4;
    public float volleyDamage = 24f;
    [Min(.1f)] public float barrageSpeedMultiplier = 1.5f;
    [Min(.02f)] public float barrageHitRadius = 1.2f;
    [Header("Vision — 20 staggered, pooled circles")]
    [Min(.1f)] public float visionWarning = 1f;
    [Min(.05f)] public float visionSpawnInterval = .18f;
    [Header("Absorb and retaliate")]
    [Tooltip("보스에게 들어오는 피해 전부를 막고 누적하는 시간입니다.")]
    public float absorbSeconds = 6f;
    [InspectorName("폭발 반경")]
    [Tooltip("흡수가 끝난 뒤 누적 피해에 비례한 공격을 터뜨리는 반경입니다. 빨간 예고 범위도 이 값을 사용합니다.")]
    public float blastRadius = 120f;
    public float blastBaseDamage = 25f;
    public float absorbedDamageMultiplier = .35f;
    public float blastDamageLimit = 120f;
    [Header("Sweeping flame breath")]
    public float breathWarning = 1.8f;
    public float breathSeconds = 3f;
    public float breathLength = 95f;
    public float breathRadius = 6f;
    public float breathSweepDegrees = 110f;
    public float breathDamagePerSecond = 12f;
    public float burnDamagePerSecond = 6f;
    public float burnSeconds = 3f;
    [Header("Thunder at every player — twice the normal radius")]
    public float thunderWarning = 1.4f;
    public float thunderDamage = 35f;
    public float damageTickSeconds = .2f;
    [Header("Route control and cooperation")]
    public float wallWarningSeconds = .8f;
    public float wallInterval = 1.2f;
    public float wallPhaseSeconds = 4f;
    public float wallSpacing = 6f;
    [Min(.1f)] public float wallHitRadius = 4.3f;
    public float wallRange = 180f;
    public float coreHealth = 40f;
    public float coreRadius = 4f;
    public float coreOrbitRadius = 32f;
    public float minimumBlastRadius = 20f;
    public float exposureSeconds = 5f;
    public float exposureDamageMultiplier = 1.75f;
    public float thunderHazardSeconds = 4f;
    public float thunderHazardRadius = 12f;
    public float thunderHazardTick = .5f;
    public float thunderHazardDamage = 8f;
    [Header("Random potion pools")]
    public float potionRestoreAmount = 40f;
    public float potionRespawnSeconds = 8f;
    public float potionMinAltitude = 45f;
    public float potionMaxAltitude = 105f;
    [Header("Ten pooled little witches")]
    public float minionSummonWarning = 1.5f;
    public float minionHealth = 40f;
    public float minionLifetime = 30f;
    public float minionSpeed = 24f;
    public float minionAttackInterval = 2f;
    public float minionDamage = 4f;
    [Header("Paper-plane retreat below half health")]
    public MapBoundaryTable boundary;
    [Min(0f)] public float retreatSpeed = 22f;
    [Min(0f)] public float retreatTurnSpeed = 75f;
    [Min(1f)] public float flightBoundaryInset = 65f;
}
