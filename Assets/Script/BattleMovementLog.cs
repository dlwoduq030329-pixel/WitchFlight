using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text;
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
    // Atomic replacement also keeps the previous checkpoint available if writing fails.
    public static void Write(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, json, new System.Text.UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(temporary, path, null);
        else File.Move(temporary, path);
    }

    // Same v1 JSON schema as JsonUtility. Stream numeric points into a reusable buffer
    // instead of allocating a growing whole-match JSON string at every checkpoint.
    public static void WriteSnapshot(string path, BattleMovementLog log)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temporary = path + ".tmp";
        var metadata = new BattleMovementLog { formatVersion = log.formatVersion, partIndex = log.partIndex,
            matchId = log.matchId, startedUtc = log.startedUtc, scenePath = log.scenePath,
            gameVersion = log.gameVersion, mode = log.mode, endReason = log.endReason,
            training = log.training, sampleInterval = log.sampleInterval, duration = log.duration };
        // These are plain serializable data objects. JsonUtility supports worker threads.
        string header = JsonUtility.ToJson(metadata);
        char[] number = new char[32];
        using (var writer = new StreamWriter(temporary, false, new UTF8Encoding(false), 16384))
        {
            WriteArrayPrefix(writer, header, "\"players\":[");
            for (int trackIndex = 0; trackIndex < log.players.Count; trackIndex++)
            {
                if (trackIndex > 0) writer.Write(',');
                BattleMovementTrack track = log.players[trackIndex];
                var trackMetadata = new BattleMovementTrack { participant = track.participant,
                    team = track.team, nickname = track.nickname, bot = track.bot };
                WriteArrayPrefix(writer, JsonUtility.ToJson(trackMetadata), "\"points\":[");
                for (int pointIndex = 0; pointIndex < track.points.Count; pointIndex++)
                {
                    if (pointIndex > 0) writer.Write(',');
                    BattleMovementPoint point = track.points[pointIndex];
                    writer.Write("{\"time\":"); WriteNumber(writer, number, point.time);
                    writer.Write(",\"position\":{\"x\":"); WriteNumber(writer, number, point.position.x);
                    writer.Write(",\"y\":"); WriteNumber(writer, number, point.position.y);
                    writer.Write(",\"z\":"); WriteNumber(writer, number, point.position.z);
                    writer.Write("},\"round\":"); WriteInteger(writer, number, point.round);
                    writer.Write(",\"segment\":"); WriteInteger(writer, number, point.segment);
                    writer.Write(point.alive ? ",\"alive\":true" : ",\"alive\":false");
                    writer.Write(point.flag ? ",\"flag\":true}" : ",\"flag\":false}");
                }
                writer.Write("]}");
            }
            writer.Write("]}");
        }
        if (File.Exists(path)) File.Replace(temporary, path, null);
        else File.Move(temporary, path);
    }

    private static void WriteArrayPrefix(TextWriter writer, string json, string field)
    {
        int index = json.LastIndexOf(field, StringComparison.Ordinal);
        if (index < 0 || !json.EndsWith("]}", StringComparison.Ordinal))
            throw new InvalidDataException("동선 로그 메타데이터 형식이 올바르지 않습니다.");
        writer.Write(json.AsSpan(0, index + field.Length));
    }
    private static void WriteNumber(TextWriter writer, char[] buffer, float value)
    {
        // Nine significant digits preserve a float when the JSON is read back.
        if (!value.TryFormat(buffer, out int length, "G9", CultureInfo.InvariantCulture))
            throw new InvalidDataException("동선 좌표를 저장할 수 없습니다.");
        writer.Write(buffer, 0, length);
    }
    private static void WriteInteger(TextWriter writer, char[] buffer, int value)
    {
        value.TryFormat(buffer, out int length, provider: CultureInfo.InvariantCulture);
        writer.Write(buffer, 0, length);
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
