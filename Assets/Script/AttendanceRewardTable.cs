using UnityEngine;

[CreateAssetMenu(fileName = "AttendanceRewardTable", menuName = "WitchFlight/Attendance Rewards")]
public sealed class AttendanceRewardTable : ScriptableObject
{
    [System.Serializable]
    public sealed class DayReward
    {
        [Min(0)] public int money;
        public Sprite icon;
    }
    [Tooltip("1~7일차 보상. 한국시간 오전 6시 기준, 하루 누락 시 1일차부터 다시 시작합니다.")]
    [SerializeField] private DayReward[] days =
    {
        new DayReward { money = 100 }, new DayReward { money = 150 }, new DayReward { money = 200 },
        new DayReward { money = 250 }, new DayReward { money = 300 }, new DayReward { money = 400 },
        new DayReward { money = 1000 }
    };
    private static AttendanceRewardTable cached;
    public static AttendanceRewardTable Default => cached != null ? cached : cached = Resources.Load<AttendanceRewardTable>("AttendanceRewardTable");
    public bool TryGet(int day, out DayReward reward)
    {
        reward = days != null && days.Length == 7 && day >= 1 && day <= 7 ? days[day - 1] : null;
        return reward != null && reward.money >= 0;
    }
}
