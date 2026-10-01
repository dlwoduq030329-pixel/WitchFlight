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

    [Min(0.01f)]
    [Tooltip("W/S 3단계에서의 전진·후진 최대 이동 속도(m/s)입니다.")]
    public float maxSpeed;

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

    [Min(0.01f)]
    [Tooltip("W/S 단계 변경 후 목표 속도에 도달하는 속도입니다.")]
    public float speedStageTransitionSpeed;

    [Min(0.01f)]
    [Tooltip("감속 단계에서 현재 속도를 줄이는 속도입니다.")]
    public float brakeSpeed;

    [Min(1f)]
    [Tooltip("Shift 부스트 중 목표 속도에 적용할 배율입니다.")]
    public float boostMultiplier;

    [Min(0f)]
    [Tooltip("Shift를 누르는 동안 초당 소모할 AP입니다. 부스트 중 자연 AP 회복은 중단됩니다. 0이면 무료입니다.")]
    public float boostApCostPerSecond;

    // Legacy serialized values: retained for asset compatibility, unused by held boost.
    [HideInInspector]
    public float boostDuration;

    [HideInInspector]
    public float boostCooldown;
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
    [Tooltip("Slow, Standard, Speed 세 빗자루의 HP/이동/선회 능력치를 편집합니다.")]
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
