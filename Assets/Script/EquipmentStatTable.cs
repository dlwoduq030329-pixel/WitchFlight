using System;
using UnityEngine;

[Serializable]
public struct HatStatEntry
{
    [Tooltip("PlayerData.hat과 연결되는 모자 종류입니다.")]
    public HatType hat;

    [Min(1f)]
    [Tooltip("이 모자를 착용했을 때의 AP 최대치입니다.")]
    public float maxAp;

    [Min(0f)]
    [Tooltip("초당 AP 회복량입니다.")]
    public float apRecoveryPerSecond;
}

[Serializable]
public struct BroomStatEntry
{
    [Tooltip("PlayerData.broom과 연결되는 빗자루 종류입니다.")]
    public BroomType broom;

    [Min(1f)]
    [Tooltip("이 빗자루를 착용했을 때의 HP 최대치입니다.")]
    public float maxHp;

    [Min(0f)]
    [Tooltip("일반 선회의 최대 각속도(도/초)입니다.")]
    public float turnSpeed;

    [Min(0.01f)]
    [Tooltip("입력 시 선회 속도가 목표값에 도달하는 반응 속도입니다.")]
    public float turnAcceleration;

    [Min(0.01f)]
    [Tooltip("입력을 놓았을 때 선회 속도가 0으로 돌아오는 반응 속도입니다.")]
    public float turnReturnSpeed;

    [Min(0f)]
    [Tooltip("적 고정 추적 중의 최대 선회 각속도(도/초)입니다.")]
    public float lockOnTurnSpeed;
}

[CreateAssetMenu(fileName = "EquipmentStatTable", menuName = "WitchFlight/Combat/Equipment Stat Table")]
public sealed class EquipmentStatTable : ScriptableObject
{
    [Header("Fallback values")]
    [Tooltip("None 또는 목록에 없는 모자가 선택됐을 때 사용할 기본 AP 값입니다.")]
    public HatStatEntry defaultHat;

    [Tooltip("None 또는 목록에 없는 빗자루가 선택됐을 때 사용할 기본 전투 값입니다.")]
    public BroomStatEntry defaultBroom;

    [Header("Hats (3)")]
    [Tooltip("Classic, Twisted, Elemental 세 모자의 AP 능력치를 편집합니다.")]
    public HatStatEntry[] hats = new HatStatEntry[3];

    [Header("Brooms (3)")]
    [Tooltip("Slow, Standard, Speed 세 빗자루의 HP/선회 능력치를 편집합니다.")]
    public BroomStatEntry[] brooms = new BroomStatEntry[3];

    public HatStatEntry GetHatStats(HatType selectedHat)
    {
        if (hats != null)
        {
            foreach (HatStatEntry entry in hats)
            {
                if (entry.hat == selectedHat)
                    return entry;
            }
        }

        return defaultHat;
    }

    public BroomStatEntry GetBroomStats(BroomType selectedBroom)
    {
        if (brooms != null)
        {
            foreach (BroomStatEntry entry in brooms)
            {
                if (entry.broom == selectedBroom)
                    return entry;
            }
        }

        return defaultBroom;
    }
}