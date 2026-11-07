using System;
using System.Globalization;
using UnityEngine;

[Serializable]
public sealed class AttendanceState
{
    public int version = 1;
    public string lastRewardDay = "";
    public int streakDay;
}

public static class AttendanceSchedule
{
    public static DateTime Day(DateTime utc) => utc.AddHours(9).AddHours(-6).Date;
    public static DateTime NextResetUtc(DateTime utc) => Day(utc).AddDays(1).AddHours(-3);
    public static AttendanceState Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}") return new AttendanceState();
        var state = JsonUtility.FromJson<AttendanceState>(json);
        if (state == null || state.version != 1 || state.streakDay < 0 || state.streakDay > 7 ||
            (state.streakDay == 0 ? !string.IsNullOrEmpty(state.lastRewardDay) :
                !DateTime.TryParseExact(state.lastRewardDay, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)))
            throw new InvalidOperationException("출석 저장 데이터를 확인해주세요.");
        return state;
    }
    public static bool TryPlan(AttendanceState state, DateTime utc, out AttendanceState next)
    {
        DateTime day = Day(utc);
        next = state;
        DateTime last = DateTime.MinValue;
        if (state.streakDay > 0)
            last = DateTime.ParseExact(state.lastRewardDay, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        // Same day, repeated login, or an unexpected future receipt must never award twice.
        if (last >= day) return false;
        next = new AttendanceState
        {
            lastRewardDay = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            streakDay = last == day.AddDays(-1) && state.streakDay < 7 ? state.streakDay + 1 : 1
        };
        return true;
    }
}
