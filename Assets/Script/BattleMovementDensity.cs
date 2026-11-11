using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;

// Diagnostic-only analysis. No scene queries, gameplay state or renderer access.
public static class BattleMovementDensity
{
    public sealed class Result
    {
        public readonly Dictionary<Vector3Int, double> seconds = new();
        public readonly SortedDictionary<int, double> altitudeSeconds = new();
        public float horizontalSize, verticalSize;
        public double totalSeconds, peakSeconds;
        public int matches, players, samples, duplicates, skippedMaps, versions;
        public Vector3Int min, max;
        public string scenePath;
    }

    public static Result Build(IEnumerable<BattleMovementLog> logs, float horizontalSize, float verticalSize,
        bool includeHumans, bool includeBots, int round, string scenePath, CancellationToken cancellation = default)
    {
        var result = new Result { horizontalSize=Mathf.Clamp(horizontalSize, 2f, 200f),
            verticalSize=Mathf.Clamp(verticalSize, 2f, 200f), scenePath=scenePath };
        var seen = new HashSet<(string match, int participant, float time)>();
        var matches = new HashSet<string>();
        var versions = new HashSet<string>();
        var players = new HashSet<(string match, int participant)>();
        int work = 0;
        foreach (var log in logs)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!string.Equals(log.scenePath ?? "", scenePath ?? "", StringComparison.OrdinalIgnoreCase))
            { result.skippedMaps++; continue; }
            float interval = log.sampleInterval > 0f && !float.IsInfinity(log.sampleInterval) ? log.sampleInterval : .25f;
            foreach (var track in log.players)
            {
                if (track.bot ? !includeBots : !includeHumans) continue;
                for (int i=0; i<track.points.Count; i++)
                {
                    if ((work++ & 4095)==0) cancellation.ThrowIfCancellationRequested();
                    var p = track.points[i];
                    if (!p.alive || (round!=0 && p.round!=round)) continue;
                    float duration = Mathf.Min(interval, Mathf.Max(0f, log.duration-p.time));
                    Vector3 end = p.position;
                    if (i+1<track.points.Count)
                    {
                        var next=track.points[i+1]; float gap=next.time-p.time;
                        // Do not connect deaths, portals, round changes or missing intervals.
                        if (next.alive && next.round==p.round && next.segment==p.segment &&
                            gap>0f && gap<=Mathf.Max(1f, interval*4f))
                        { duration=gap; end=next.position; }
                    }
                    if (duration<=0f) continue;
                    if (!seen.Add((log.matchId, track.participant, p.time))) { result.duplicates++; continue; }
                    if (seen.Count>2000000) throw new InvalidDataException("통합 기록이 200만 좌표를 넘습니다. 날짜 범위를 나눠 가져오세요.");
                    matches.Add(log.matchId); versions.Add(log.gameVersion ?? ""); players.Add((log.matchId,track.participant)); result.samples++;
                    Vector3 delta=end-p.position;
                    float spans=Mathf.Max(Mathf.Abs(delta.x)/result.horizontalSize,
                        Mathf.Abs(delta.y)/result.verticalSize, Mathf.Abs(delta.z)/result.horizontalSize);
                    int steps=Mathf.Clamp(Mathf.CeilToInt(spans*2f),1,128);
                    double weight=(double)duration/steps;
                    for(int j=0;j<steps;j++)
                    {
                        Vector3 position=Vector3.Lerp(p.position,end,(j+.5f)/steps);
                        var key=new Vector3Int(Mathf.FloorToInt(position.x/result.horizontalSize),
                            Mathf.FloorToInt(position.y/result.verticalSize),Mathf.FloorToInt(position.z/result.horizontalSize));
                        result.seconds.TryGetValue(key,out double value); value+=weight; result.seconds[key]=value;
                        result.altitudeSeconds.TryGetValue(key.y,out double heightValue); result.altitudeSeconds[key.y]=heightValue+weight;
                        result.totalSeconds+=weight; result.peakSeconds=Math.Max(result.peakSeconds,value);
                        if(result.seconds.Count==1) result.min=result.max=key;
                        else {result.min=Vector3Int.Min(result.min,key);result.max=Vector3Int.Max(result.max,key);}
                        if(result.seconds.Count>200000) throw new InvalidDataException("공간 구간이 너무 많습니다. 가로·세로 구간 크기를 키워 주세요.");
                    }
                }
            }
        }
        result.matches=matches.Count; result.players=players.Count; result.versions=versions.Count;
        return result;
    }
}
