using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public sealed class BattleMovementLog
{
    public int formatVersion = 1;
    public int partIndex;
    public string matchId, startedUtc, scenePath, gameVersion, mode, endReason;
    public bool training;
    public float sampleInterval, duration;
    public List<BattleMovementTrack> players = new();
}

[Serializable]
public sealed class BattleMovementTrack
{
    public int participant, team;
    public string nickname;
    public bool bot;
    public List<BattleMovementPoint> points = new();
}

[Serializable]
public struct BattleMovementPoint
{
    public float time;
    public Vector3 position;
    public int round, segment;
    public bool alive, flag;
}

public static class BattleMovementLogFiles
{
    public static string DirectoryPath => Path.Combine(Application.persistentDataPath, "BattleLogs");
    public static string[] List()
    {
        if (!Directory.Exists(DirectoryPath)) return Array.Empty<string>();
        var files = Directory.GetFiles(DirectoryPath, "*.json");
        Array.Sort(files, StringComparer.Ordinal);
        Array.Reverse(files);
        return files;
    }
    // Caller serializes Unity data on the main thread; disk writes may run in a worker.
    public static void Write(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, json, new System.Text.UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(temporary, path, null);
        else File.Move(temporary, path);
    }
    public static BattleMovementLog Read(string path)
    {
        if (new FileInfo(path).Length > 64L * 1024 * 1024) throw new InvalidDataException("로그가 64 MB 제한을 초과합니다.");
        return Parse(File.ReadAllText(path));
    }
    public static BattleMovementLog Parse(string json)
    {
        var log = JsonUtility.FromJson<BattleMovementLog>(json);
        if (log == null || log.formatVersion != 1 || log.players == null || log.players.Count > 64 ||
            !Guid.TryParseExact(log.matchId,"N",out _) || log.partIndex<0)
            throw new InvalidDataException("지원하지 않는 동선 로그입니다.");
        int count = 0;
        foreach (var track in log.players)
        {
            if (track == null || track.points == null) throw new InvalidDataException("플레이어 경로가 없습니다.");
            count += track.points.Count;
            float previous = -1f;
            foreach (var p in track.points)
            {
                if (!Finite(p.time) || p.time < previous || !Finite(p.position.x) || !Finite(p.position.y) || !Finite(p.position.z))
                    throw new InvalidDataException("잘못된 위치/시간 기록입니다.");
                previous = p.time;
            }
        }
        if (count > 250000 || !Finite(log.duration) || log.duration < 0f) throw new InvalidDataException("로그 크기 또는 시간이 잘못되었습니다.");
        return log;
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
