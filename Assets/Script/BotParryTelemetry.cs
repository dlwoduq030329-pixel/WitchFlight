using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Host-side observations only. No additional random draws or network messages.
public sealed class BotParryTelemetry : MonoBehaviour
{
    [Serializable] public sealed class Trial
    {
        public int id;
        public string bot, projectile, scene, choice, outcome;
        public float time, probability, baseline, correction, offsetBefore, probabilityAfter, roll, impactSeconds;
        public bool good, started;
    }
    public static readonly List<Trial> Trials = new List<Trial>();
    public static string FilePath { get; private set; }
    public static string Error { get; private set; }
    public static int Version { get; private set; }
    private static StreamWriter writer;
    private static BotParryTelemetry instance;
    private float nextFlush;
    public static string DirectoryPath => Path.Combine(Application.persistentDataPath, "BotParryLogs");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        Close(); Trials.Clear(); Version = 0; instance = null; FilePath = null; Error = null;
    }
    private static void Ensure()
    {
        if (instance != null) return;
        var go = new GameObject("Bot parry telemetry (local log)");
        instance = go.AddComponent<BotParryTelemetry>();
        DontDestroyOnLoad(go);
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            FilePath = Path.Combine(DirectoryPath, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0,8) + ".jsonl");
            writer = new StreamWriter(FilePath, false, System.Text.Encoding.UTF8);
            Debug.Log("[BotParry] Recording decisions and actual attempts: " + FilePath);
        }
        catch (Exception e) { ReportError(e); }
    }
    public static int Begin(string bot, string projectile, float baseline, float correction, float offset,
        float probability, float roll, bool good, string choice, float impact)
    {
        Ensure();
        var row = new Trial { id = Trials.Count + 1, bot = bot, projectile = projectile,
            scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
            time = Time.realtimeSinceStartup, baseline = baseline, correction = correction,
            offsetBefore = offset, probability = probability, probabilityAfter = probability,
            roll = roll, good = good, choice = choice, impactSeconds = impact, outcome = "pending" };
        Trials.Add(row); Write(row); return row.id;
    }
    private static Trial Get(int id) => id > 0 && id <= Trials.Count ? Trials[id - 1] : null;
    public static void Started(int id)
    {
        var row = Get(id); if (row == null || row.outcome != "pending") return;
        row.started = true; Write(row);
    }
    public static void Finish(int id, string outcome, float nextProbability)
    {
        var row = Get(id); if (row == null || row.outcome != "pending") return;
        row.outcome = outcome; row.probabilityAfter = nextProbability; Write(row);
    }
    private static void Write(Trial row)
    {
        Version++;
        if (writer == null) return;
        try { writer.WriteLine(JsonUtility.ToJson(row)); }
        catch (Exception e) { ReportError(e); Close(); }
    }
    public static void Flush()
    {
        try { writer?.Flush(); } catch (Exception e) { ReportError(e); Close(); }
    }
    private static void ReportError(Exception e)
    {
        Error = e.Message;
        Debug.LogWarning("[BotParry] Log file unavailable: " + Error);
    }
    private static void Close()
    {
        try { writer?.Dispose(); } catch { /* Do not interrupt gameplay or shutdown. */ }
        writer = null;
    }
    private void Update()
    {
        if (Time.unscaledTime < nextFlush) return;
        nextFlush = Time.unscaledTime + 1f; Flush();
    }
    private void OnApplicationQuit() { Close(); }
    private void OnDestroy() { if (instance == this) { Close(); instance = null; } }

    // JSONL contains snapshots; keep the latest state per trial, not one trial per line.
    public static List<Trial> Load(string path)
    {
        var result = new SortedDictionary<int, Trial>();
        foreach (string line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var row = JsonUtility.FromJson<Trial>(line);
                if (row != null && row.id > 0) result[row.id] = row;
            }
            catch (ArgumentException) { /* Ignore a partial final line in a live log. */ }
        }
        return new List<Trial>(result.Values);
    }
}
