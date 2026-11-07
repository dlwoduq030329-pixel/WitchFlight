using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable] public sealed class MissionEntryState
{
    public string id;
    public float progress;
    public bool claimed;
    public float[] magicHits = new float[18];
    public List<string> days = new();
}
[Serializable] public sealed class MissionMatchCursor
{
    public string id;
    public float[] counters = new float[28];
    public bool completed;
}
[Serializable] public sealed class MissionSaveState
{
    public int version = 1;
    public string dailyPeriodKey, weeklyPeriodKey, weeklyMissionId;
    public List<MissionEntryState> entries = new();
    public List<MissionMatchCursor> matches = new();
}

public static class MissionProgression
{
    public static event Action Changed;
    public static MissionSaveState State { get; private set; }
    private static MissionCatalog catalog;
    private static string account;
    private static float nextSave;
    public static void Flush() { if(Ready) Save(); }
    public static IReadOnlyList<MissionDefinition> Definitions =>
        (catalog != null ? catalog : catalog = Resources.Load<MissionCatalog>("MissionCatalog"))?.Missions;
    public static DateTime PeriodDay(DateTime utc) => utc.AddHours(9).AddHours(-6).Date;
    public static DateTime PeriodWeek(DateTime utc)
    {
        DateTime day = PeriodDay(utc);
        return day.AddDays(-((int)day.DayOfWeek + 6) % 7);
    }
    public static MissionSaveState Parse(string json)
    {
        var state = string.IsNullOrWhiteSpace(json) ? new MissionSaveState() : JsonUtility.FromJson<MissionSaveState>(json);
        if (state == null || state.version != 1) throw new InvalidOperationException("지원하지 않는 미션 저장 데이터입니다.");
        state.entries ??= new(); state.matches ??= new();
        return state;
    }
    public static void Load()
    {
        State = null;
        var db = DatabaseManager.Instance;
        if (db == null || !db.CanModifyAccountData) return;
        account = db.LoadedAccountId;
        try { State = Parse(DataConfig.missionState); EnsurePeriods(); Changed?.Invoke(); }
        catch (Exception e) { Debug.LogError("미션 데이터 보존: " + e.Message); }
    }
    public static bool Ready => State != null && DatabaseManager.Instance != null &&
        DatabaseManager.Instance.CanModifyAccountData && account == DatabaseManager.Instance.LoadedAccountId;
    public static MissionDefinition Find(string id)
    {
        if (Definitions != null) foreach (var d in Definitions) if (d != null && d.enabled && d.missionId == id) return d;
        return null;
    }
    public static List<MissionDefinition> Active()
    {
        var result = new List<MissionDefinition>();
        if (State == null || Definitions == null) return result;
        var weekly = Find(State.weeklyMissionId); if (weekly != null) result.Add(weekly);
        var daily = new List<MissionDefinition>();
        foreach (var d in Definitions) if (d != null && d.enabled && d.period == MissionPeriod.Daily) daily.Add(d);
        daily.Sort((a,b) => a.sortOrder != b.sortOrder ? a.sortOrder.CompareTo(b.sortOrder) : string.CompareOrdinal(a.missionId,b.missionId));
        for (int i=0; i<Math.Min(5,daily.Count); i++) result.Add(daily[i]);
        return result;
    }
    public static MissionEntryState Entry(MissionSaveState state, string id)
    {
        var entry = state.entries.Find(e => e.id == id);
        if (entry == null) { entry = new MissionEntryState { id = id }; state.entries.Add(entry); }
        entry.days ??= new();
        if (entry.magicHits == null || entry.magicHits.Length != 18) entry.magicHits = new float[18];
        return entry;
    }
    public static void EnsurePeriods()
    {
        if (!Ready || Definitions == null) return;
        DateTime now = DateTime.UtcNow;
        string day = PeriodDay(now).ToString("yyyy-MM-dd"), week = PeriodWeek(now).ToString("yyyy-MM-dd");
        bool dailyReset = State.dailyPeriodKey != day, weeklyReset = State.weeklyPeriodKey != week;
        if (!dailyReset && !weeklyReset) return;
        State.entries.RemoveAll(e => { var d = Find(e.id); return d == null || (d.period == MissionPeriod.Daily ? dailyReset : weeklyReset); });
        State.dailyPeriodKey = day; State.weeklyPeriodKey = week;
        if (weeklyReset)
        {
            var pool = new List<MissionDefinition>();
            foreach (var d in Definitions) if (d != null && d.enabled && d.period == MissionPeriod.Weekly) pool.Add(d);
            pool.Sort((a,b) => a.sortOrder != b.sortOrder ? a.sortOrder.CompareTo(b.sortOrder) : string.CompareOrdinal(a.missionId,b.missionId));
            int weeks = (int)((PeriodWeek(now) - new DateTime(2026,1,5)).TotalDays / 7);
            State.weeklyMissionId = pool.Count == 0 ? "" : pool[((weeks % pool.Count)+pool.Count)%pool.Count].missionId;
        }
        Save();
    }
    private static void Save() { DataConfig.missionState = JsonUtility.ToJson(State); Changed?.Invoke(); }
    public static void Observe(PlayerData data)
    {
        if (!Ready || data == null || data.Object == null || !data.Object.IsValid || !data.Object.HasInputAuthority || data.IsMatchBot ||
            NetworkGameManager.Instance == null || (NetworkGameManager.Instance.IsTraining || NetworkGameManager.Instance.IsDeathMatch)) return;
        string id = data.MissionMatchId.ToString(); if (string.IsNullOrEmpty(id)) return;
        EnsurePeriods();
        var cursor = State.matches.Find(c => c.id == id);
        if (cursor == null) { cursor = new MissionMatchCursor { id = id }; State.matches.Add(cursor); }
        if (cursor.counters == null || cursor.counters.Length != 28) cursor.counters = new float[28];
        bool finished = data.MissionCompleted && !cursor.completed;
        bool changed = finished;
        var delta = new float[28];
        for (int i=0; i<28; i++)
        {
            float total = data.MissionCounters[i];
            delta[i] = Mathf.Max(0,total-cursor.counters[i]);
            if (delta[i] > 0) changed = true;
            cursor.counters[i] = total;
        }
        if (!changed) return;
        foreach (var d in Active())
        {
            var e = Entry(State,d.missionId); if (e.claimed) continue;
            switch (d.objective)
            {
                case MissionObjective.MatchComplete: if (finished) e.progress++; break;
                case MissionObjective.MatchWin: if (finished && data.MissionWon) e.progress++; break;
                case MissionObjective.MatchesWithKillOrAssist: if (finished && data.MissionCounters[2]>0) e.progress++; break;
                case MissionObjective.DistinctPlayDays:
                    if (finished && !e.days.Contains(State.dailyPeriodKey)) e.days.Add(State.dailyPeriodKey);
                    e.progress=e.days.Count; break;
                case MissionObjective.DistinctMagicHits:
                    int types=0;
                    for(int i=0;i<18;i++) { e.magicHits[i]+=delta[10+i]; if(e.magicHits[i]>=d.requiredHitsPerMagic) types++; }
                    e.progress=types; break;
                default: e.progress += delta[(int)d.objective]; break;
            }
        }
        cursor.completed |= finished;
        // Keep active sessions; old completed sessions cannot be replayed by the live room.
        while(State.matches.Count>128) { int old=State.matches.FindIndex(c=>c.completed && c!=cursor); if(old<0)break; State.matches.RemoveAt(old); }
        if(finished || Time.unscaledTime>=nextSave) { nextSave=Time.unscaledTime+10f; Save(); }
        else Changed?.Invoke();
    }
}
