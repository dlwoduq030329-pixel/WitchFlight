using System;
using System.Globalization;
using BackEnd;
using UnityEngine;

public partial class DatabaseManager
{
    public bool IsAttendanceInProgress { get; private set; }
    public int AttendanceDay { get; private set; }
    public bool AttendanceClaimedToday { get; private set; }
    public int AttendanceCompletedDays { get; private set; }
    public bool AttendanceStatusKnown { get; private set; }
    public string AttendanceMessage { get; private set; } = "";
    public static event Action AttendanceChanged;
    private bool attendanceRequested, attendanceNotice;
    private bool attendanceClaimRequested, attendancePrompt, attendancePromptShown;
    private int attendanceNoticeDay, attendanceNoticeMoney;
    private double nextAttendanceCheck;

    private void ResetAttendance()
    {
        IsAttendanceInProgress = attendanceRequested = attendanceNotice = false;
        attendanceClaimRequested = attendancePrompt = attendancePromptShown = false;
        AttendanceCompletedDays = 0; AttendanceStatusKnown = false;
        AttendanceDay = 0; AttendanceClaimedToday = false; AttendanceMessage = "";
        nextAttendanceCheck = 0;
        AttendanceChanged?.Invoke();
    }

    public bool ConsumeAttendanceNotice(out int day, out int money)
    {
        day = attendanceNoticeDay; money = attendanceNoticeMoney;
        bool available = attendanceNotice;
        attendanceNotice = false;
        return available;
    }

    public void RequestAttendanceCheck()
    {
        if (!HasLoadedProfile || !IsDataConfigReady || IsAttendanceInProgress || Time.realtimeSinceStartupAsDouble < nextAttendanceCheck) return;
        attendanceRequested = true;
        TryStartAttendance();
    }

    public bool ConsumeAttendancePrompt()
    {
        if (!attendancePrompt || attendancePromptShown || !IsDataConfigReady) return false;
        attendancePrompt = false;
        attendancePromptShown = true;
        return true;
    }

    public void ClaimAttendanceReward()
    {
        if (!HasLoadedProfile || !IsDataConfigReady || IsAttendanceInProgress || !AttendanceStatusKnown || AttendanceClaimedToday) return;
        attendanceRequested = attendanceClaimRequested = true;
        TryStartAttendance();
    }

    private void TryStartAttendance()
    {
        if (!attendanceClaimRequested && Time.realtimeSinceStartupAsDouble < nextAttendanceCheck) { attendanceRequested = false; return; }
        if (!attendanceRequested || !CanAutoSave || IsAutoSavePending || IsAttendanceInProgress ||
            IsPalettePurchaseInProgress || IsItemPurchaseInProgress || claimingMission) return;
        attendanceRequested = false;
        bool claim = attendanceClaimRequested;
        attendanceClaimRequested = false;
        IsAttendanceInProgress = true;
        AttendanceMessage = "출석 확인 중…";
        AttendanceChanged?.Invoke();
        int session = dataSessionVersion;
        string owner = loadedUserInDate, row = currentPlayerSettingInDate;
        bool Current() => this != null && session == dataSessionVersion && HasLoadedProfile &&
            loadedUserInDate == owner && currentPlayerSettingInDate == row;
        void Finish(string message, bool uncertain = false)
        {
            if (!Current()) return;
            if (uncertain) { IsDataConfigReady = false; LastSaveSucceeded = false; }
            IsAttendanceInProgress = false;
            AttendanceMessage = message;
            AttendanceChanged?.Invoke();
            DataConfigReadyChanged?.Invoke();
        }
        void RetryLater(string message)
        {
            AttendanceStatusKnown = false;
            nextAttendanceCheck = Time.realtimeSinceStartupAsDouble + 10;
            Finish(message);
        }
        void Uncertain()
        {
            LastError = "출석 보상 저장 결과를 확인할 수 없습니다. 다시 로그인해주세요.";
            Finish(LastError, true);
        }
        try
        {
            Backend.GameData.GetMyData(TABLE_NAME, new Where(), read => saveCompletions.Enqueue(() =>
            {
                if (!Current()) return;
                try
                {
                    if (read == null || !read.IsSuccess()) { RetryLater("출석 조회 실패. 잠시 후 다시 확인해주세요."); return; }
                    var rows = read.FlattenRows();
                    if (rows == null || rows.Count != 1 || ReadString(rows[0], "inDate", "") != row)
                    { RetryLater("출석을 저장할 계정 행을 확인할 수 없습니다."); return; }
                    var state = AttendanceSchedule.Parse(ReadString(rows[0], "attendanceState", ""));
                    int balance = ReadPlayerSetting(rows[0]).money;
                    Backend.Utils.GetServerTime(clock => saveCompletions.Enqueue(() =>
                    {
                        if (!Current()) return;
                        try
                        {
                            if (clock == null || !clock.IsSuccess()) { RetryLater("서버 시간 조회 실패. 잠시 후 다시 확인해주세요."); return; }
                            string stamp = clock.GetReturnValuetoJSON()["utcTime"].ToString();
                            if (!DateTimeOffset.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var serverTime))
                            { RetryLater("서버 시간을 확인할 수 없습니다."); return; }
                            DateTime utc = serverTime.UtcDateTime;
                            if (!AttendanceSchedule.TryPlan(state, utc, out var next))
                            {
                                AttendanceDay = state.streakDay;
                                AttendanceClaimedToday = state.lastRewardDay == AttendanceSchedule.Day(utc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                                AttendanceStatusKnown = AttendanceClaimedToday;
                                AttendanceCompletedDays = state.streakDay;
                                attendancePrompt = false;
                                nextAttendanceCheck = Time.realtimeSinceStartupAsDouble + Math.Max(1, (AttendanceSchedule.NextResetUtc(utc) - utc).TotalSeconds);
                                Finish(AttendanceClaimedToday ? "오늘 출석 완료" : "출석 기록의 날짜를 확인해주세요.");
                                return;
                            }
                            AttendanceDay = next.streakDay;
                            AttendanceClaimedToday = false;
                            AttendanceStatusKnown = true;
                            AttendanceCompletedDays = next.streakDay - 1;
                            if (!claim)
                            {
                                attendancePrompt = !attendancePromptShown;
                                nextAttendanceCheck = Time.realtimeSinceStartupAsDouble + Math.Max(1, (AttendanceSchedule.NextResetUtc(utc) - utc).TotalSeconds);
                                Finish("오늘자 출석하기");
                                return;
                            }
                            var table = AttendanceRewardTable.Default;
                            if (table == null || !table.TryGet(next.streakDay, out var reward) || balance > int.MaxValue - reward.money)
                            { RetryLater("출석 보상 설정 또는 재화 보유 한도를 확인해주세요."); return; }
                            int newBalance = balance + reward.money;
                            var param = new Param();
                            param.Add("money", newBalance);
                            param.Add("attendanceState", JsonUtility.ToJson(next));
                            LastSaveSucceeded = false;
                            try
                            {
                                Backend.GameData.UpdateV2(TABLE_NAME, row, owner, param, saved => saveCompletions.Enqueue(() =>
                                {
                                    if (!Current()) return;
                                    if (saved == null || !saved.IsSuccess()) { Uncertain(); return; }
                                    currentPlayerSetting.money = newBalance;
                                    using (DataConfig.BeginChangeBatch(false)) DataConfig.money = newBalance;
                                    AttendanceDay = next.streakDay; AttendanceClaimedToday = true;
                                    AttendanceCompletedDays = next.streakDay;
                                    attendancePrompt = false;
                                    attendanceNotice = true; attendanceNoticeDay = next.streakDay; attendanceNoticeMoney = reward.money;
                                    nextAttendanceCheck = Time.realtimeSinceStartupAsDouble + Math.Max(1, (AttendanceSchedule.NextResetUtc(utc) - utc).TotalSeconds);
                                    LastSaveSucceeded = true; LastError = "";
                                    Finish($"{next.streakDay}일차 출석 완료 · +{reward.money:N0}");
                                }));
                            }
                            catch (Exception) { Uncertain(); }
                        }
                        catch (Exception) { RetryLater("출석 확인 실패. 잠시 후 다시 확인해주세요."); }
                    }));
                }
                catch (Exception) { RetryLater("출석 데이터를 확인할 수 없습니다. 다시 로그인해주세요."); }
            }));
        }
        catch (Exception) { RetryLater("출석 조회를 시작할 수 없습니다. 잠시 후 다시 확인해주세요."); }
    }
}
