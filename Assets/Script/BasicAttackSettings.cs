using UnityEngine;

[CreateAssetMenu(menuName = "WitchFlight/Basic Attack Settings")]
public sealed class BasicAttackSettings : ScriptableObject
{
    private static BasicAttackSettings cached;
    public static BasicAttackSettings Default => cached != null ? cached : cached = Resources.Load<BasicAttackSettings>("BasicAttackSettings");
    [Min(0)] public float damage = 15;
    [Min(1)] public int capacity = 3;
    [Min(.05f)] public float rechargeSeconds = 4;
    [Min(.05f)] public float shotInterval = .2f;
    [Min(1)] public float projectileSpeed = 95;
    [Min(0)] public float turnSpeed = 220;
    [Min(1)] public float range = 130;
    [Range(0, 90)] public float aimAssistAngle = 12;
    [Tooltip("에임에서 가장 가까운 적과 이 각도 이내 차이인 후보는 현재 체력이 적은 순서로 선택합니다.")]
    [Range(0, 10)] public float similarAimAngle = 2;
    [Min(.01f)] public float hitRadius = .12f;
    public Sprite icon;
    public GameObject projectileVfxPrefab, impactVfxPrefab, castVfxPrefab;
    [Tooltip("기본공격 뒤를 따라오는 작은 보조 투사체 VFX. 비워두면 MagicStatTable의 공통 보조 투사체를 사용합니다. 피해 판정은 없습니다.")]
    public GameObject secondaryProjectileVfxPrefab;

    public static MagicStatEntry Stats
    {
        get
        {
            var s = Default;
            float speed = s != null ? Mathf.Max(1, s.projectileSpeed) : 95;
            float range = s != null ? Mathf.Max(1, s.range) : 130;
            return new MagicStatEntry {
                magic = MagicType.BasicAttack, displayName = "기본공격", effect = MagicEffectKind.DirectDamage,
                damage = s != null ? Mathf.Max(0, s.damage) : 15,
                projectileSpeed = speed, projectileTurnSpeed = s != null ? Mathf.Max(0, s.turnSpeed) : 220,
                projectileLifetime = range / speed, range = range,
                projectileRadius = s != null ? Mathf.Max(.01f, s.hitRadius) : .12f,
                movementMultiplier = 1, vfxLifetime = 1.5f,
                icon = s != null ? s.icon : null,
                projectileVfxPrefab = s != null ? s.projectileVfxPrefab : null,
                secondaryProjectileVfxPrefab = s != null ? s.secondaryProjectileVfxPrefab : null,
                impactVfxPrefab = s != null ? s.impactVfxPrefab : null,
                castVfxPrefab = s != null ? s.castVfxPrefab : null
            };
        }
    }
}
