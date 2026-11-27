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
    public float[] counters = new float[29];
    public bool completed;
}
[Serializable] public sealed class MissionSaveState
{
    public int version = 1;
    public string dailyPeriodKey, weeklyPeriodKey, weeklyMissionId;
    public List<string> weeklyMissionIds = new();
    public bool weeklySelectionExpanded;
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
        state.entries ??= new(); state.matches ??= new(); state.weeklyMissionIds ??= new();
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
        foreach (string id in State.weeklyMissionIds)
        { var weekly = Find(id); if (weekly != null && weekly.period == MissionPeriod.Weekly) result.Add(weekly); }
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
        bool expandWeekly = !State.weeklySelectionExpanded;
        if (!dailyReset && !weeklyReset && !expandWeekly) return;
        State.entries.RemoveAll(e => { var d = Find(e.id); return d == null || (d.period == MissionPeriod.Daily ? dailyReset : weeklyReset); });
        State.dailyPeriodKey = day; State.weeklyPeriodKey = week;
        if (weeklyReset || expandWeekly)
        {
            State.weeklyMissionIds ??= new();
            if (weeklyReset) State.weeklyMissionIds.Clear();
            else if (State.weeklyMissionIds.Count == 0)
            {
                // Keep the old week's mission and its progress when upgrading a save.
                var previous = Find(State.weeklyMissionId);
                if (previous != null && previous.period == MissionPeriod.Weekly)
                    State.weeklyMissionIds.Add(previous.missionId);
            }
            var pool = new List<string>();
            foreach (var d in Definitions)
                if (d != null && d.enabled && d.period == MissionPeriod.Weekly &&
                    !State.weeklyMissionIds.Contains(d.missionId) && !pool.Contains(d.missionId)) pool.Add(d.missionId);
            // Draw without replacement, then persist: reopening the UI cannot reroll missions.
            var random = new System.Random(Guid.NewGuid().GetHashCode());
            while (State.weeklyMissionIds.Count < 3 && pool.Count > 0)
            {
                int index = random.Next(pool.Count);
                State.weeklyMissionIds.Add(pool[index]);
                pool.RemoveAt(index);
            }
            State.weeklyMissionId = ""; // Legacy field is only read during migration.
            State.weeklySelectionExpanded = true;
        }
        Save();
    }
    public static bool IsSelectedWeekly(MissionSaveState state, string id) =>
        state != null && state.weeklyMissionIds != null && state.weeklyMissionIds.Contains(id);
    private static void Save() { DataConfig.missionState = JsonUtility.ToJson(State); Changed?.Invoke(); }
    // Used on server snapshots before saving currency and mission progress together.
    public static void AddCurrencyProgress(MissionSaveState state, int amount, DateTime utc)
    {
        if (state == null || amount <= 0 || Definitions == null) return;
        string week = PeriodWeek(utc).ToString("yyyy-MM-dd");
        foreach (var d in Definitions)
        {
            if (d == null || !d.enabled || d.objective != MissionObjective.CurrencyEarned ||
                d.period != MissionPeriod.Weekly || state.weeklyPeriodKey != week || !IsSelectedWeekly(state, d.missionId)) continue;
            var entry = Entry(state, d.missionId);
            if (!entry.claimed) entry.progress = Mathf.Min(d.target, entry.progress + amount);
        }
    }
    public static void RecordCurrencyEarned(int amount)
    {
        if (!Ready || amount <= 0) return;
        EnsurePeriods();
        AddCurrencyProgress(State, amount, DateTime.UtcNow);
        Save();
    }
    public static void Observe(PlayerData data)
    {
        if (!Ready || data == null || data.Object == null || !data.Object.IsValid || !data.Object.HasInputAuthority || data.IsMatchBot ||
            NetworkGameManager.Instance == null || (NetworkGameManager.Instance.IsTraining || NetworkGameManager.Instance.IsDeathMatch)) return;
        string id = data.MissionMatchId.ToString(); if (string.IsNullOrEmpty(id)) return;
        EnsurePeriods();
        var cursor = State.matches.Find(c => c.id == id);
        if (cursor == null) { cursor = new MissionMatchCursor { id = id }; State.matches.Add(cursor); }
        // Preserve previous totals when upgrading saves, preventing duplicate progress.
        if (cursor.counters == null || cursor.counters.Length != 29) Array.Resize(ref cursor.counters, 29);
        bool finished = data.MissionCompleted && !cursor.completed;
        bool changed = finished;
        var delta = new float[29];
        for (int i=0; i<29; i++)
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
                case MissionObjective.CurrencyEarned: break; // Account grants are recorded separately.
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
