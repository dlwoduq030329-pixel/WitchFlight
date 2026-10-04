using System;
using UnityEngine;

public enum MagicEffectKind
{
    DirectDamage,
    SlowDamage,
    AreaDamage,
    Interrupt,
    Stealth,
    MovementBuff,
    Decoy,
    Mine,
    Scan
}

[Serializable]
public struct MagicStatEntry
{
    [Tooltip("PlayerData.magic1 / magic2와 연결되는 마법 종류입니다.")]
    public MagicType magic;

    [Tooltip("Inspector에서 표시할 이름입니다. Dark enum 값은 기존 저장값 호환을 위해 Wind로 표시합니다.")]
    public string displayName;
    [Tooltip("로컬 BattleMagicUI에 표시할 아이콘입니다.")]
    public Sprite icon;

    public MagicEffectKind effect;

    [Min(0f)] public float apCost;
    [Tooltip("이 마법 종류만의 재사용 대기 시간(초)입니다.")]
    [Min(0f)] public float cooldownSeconds;
    [Min(0.01f)] public float lockChargeSeconds;
    [Min(0f)]
    [Tooltip("좌클릭을 놓은 뒤 발사되기까지의 시전 시간입니다.")]
    public float castSeconds;
    [Min(0f)]
    [Tooltip("발사체 속도(m/s)입니다. 0이면 즉발 광선 판정을 사용합니다.")]
    public float projectileSpeed;
    [Min(0f)]
    [Tooltip("유도 발사체의 선회 속도(도/초)입니다.")]
    public float projectileTurnSpeed;
    [Min(0.01f)]
    [Tooltip("발사체의 최대 유지 시간(초)입니다.")]
    public float projectileLifetime;
    [Min(0.01f)]
    [Tooltip("발사체의 충돌 반경(m)입니다.")]
    public float projectileRadius;
    [Min(0f)] public float range;
    [Min(0f)] public float damage;
    [Min(0f)] public float hitStunSeconds;
    [Min(0f)] public float knockbackForce;

    [Min(0.05f)]
    [Tooltip("이동 배율입니다. Ice는 1 미만, Wind는 1 초과를 사용합니다.")]
    public float movementMultiplier;

    [Min(0f)]
    [Tooltip("은신, 버프, 둔화, 스캔, 분신, 지뢰 유지 시간에 사용됩니다.")]
    public float effectDuration;

    [Min(0f)]
    [Tooltip("Thunder, Mine, Scane의 범위입니다.")]
    public float radius;

    [Min(0f)]
    [Tooltip("Mine 활성화 지연 시간입니다. Decoy에서는 즉시 시작하는 은신의 유지 시간입니다.")]
    public float activationDelay;

    [Min(0f)]
    [Tooltip("Smoke가 발동 가능한 최소 적 거리입니다.")]
    public float minimumEnemyDistance;

    [Min(0f)]
    [Tooltip("Mine을 현재 기체 전방에 설치할 거리입니다.")]
    public float placementDistance;

    public bool requiresTarget;
    public bool requiresFullLock;
}

[CreateAssetMenu(fileName = "MagicStatTable", menuName = "WitchFlight/Combat/Magic Stat Table")]
public sealed class MagicStatTable : ScriptableObject
{
    [Header("Parry (right mouse button)")]
    [Min(0f)] public float parryApCost = 8f;
    [Min(0.01f)] public float parryWindowSeconds = 0.3f;
    [Min(0f)] public float parryCooldownSeconds = 2f;
    public Sprite parryIcon;

    [Tooltip("등록되지 않은 마법을 선택했을 때의 안전한 기본값입니다.")]
    public MagicStatEntry fallback = new MagicStatEntry { cooldownSeconds = 2f };

    [Tooltip("Fire부터 Scane까지의 마법 수치를 편집합니다.")]
    public MagicStatEntry[] magics = CreateDefaultEntries();

    private static MagicStatEntry[] CreateDefaultEntries()
    {
        var entries = new MagicStatEntry[10];
        for (int i = 0; i < entries.Length; i++)
            entries[i] = new MagicStatEntry { magic = (MagicType)(i + 1), cooldownSeconds = 2f };
        return entries;
    }

    public MagicStatEntry GetStats(MagicType magic)
    {
        if (magics != null)
        {
            foreach (MagicStatEntry entry in magics)
            {
                if (entry.magic == magic)
                    return entry;
            }
        }

        return fallback;
    }
}
