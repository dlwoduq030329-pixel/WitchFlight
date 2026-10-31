using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LitJson;
using UnityEngine;

public static class BattleMovementImport
{
    // Console exports can contain a JSON column, flattened payload columns, or a JSON array.
    public static string[] Import(string path)
    {
        if(new FileInfo(path).Length>64L*1024*1024) throw new InvalidDataException("한 번에 64 MB 이하로 나누어 가져오세요.");
        string text=File.ReadAllText(path).TrimStart('\uFEFF');
        var packets=new Dictionary<string,SortedDictionary<int,BattleMovementLog>>();
        int totalPoints=0;
        void Add(string json)
        {
            var log=BattleMovementLogFiles.Parse(json);
            if(!packets.TryGetValue(log.matchId,out var parts)) packets.Add(log.matchId,parts=new());
            if(parts.ContainsKey(log.partIndex)) return; // Retries may appear twice in the console.
            totalPoints+=log.players.Sum(p=>p.points.Count);
            if(totalPoints>1000000) throw new InvalidDataException("기록이 너무 많습니다. 날짜 범위를 나눠 주세요.");
            parts.Add(log.partIndex,log);
        }
        void Visit(JsonData node,int depth)
        {
            if(node==null || depth>12) return;
            if(node.IsObject)
            {
                if(node.Keys.Contains("payload") && node["payload"].IsString)
                {
                    Add(BattleMovementUpload.Decode((string)node["payload"]));
                    return;
                }
                if(node.Keys.Contains("matchId") && node.Keys.Contains("players")) { Add(node.ToJson()); return; }
                foreach(string key in node.Keys) Visit(node[key],depth+1);
            }
            else if(node.IsArray) { for(int i=0;i<node.Count;i++) Visit(node[i],depth+1); }
            else if(node.IsString)
            {
                string value=((string)node).Trim();
                if(value.StartsWith("H4sI",StringComparison.Ordinal)) {Add(BattleMovementUpload.Decode(value));return;}
                if(value.StartsWith("{") || value.StartsWith("["))
                {
                    JsonData nested=null;
                    try { nested=JsonMapper.ToObject(value); } catch(JsonException) { }
                    if(nested!=null) Visit(nested,depth+1);
                }
            }
        }
        JsonData root=null;
        try { root=JsonMapper.ToObject(text); } catch(JsonException) { }
        if(root!=null) Visit(root,0);
        else
        {
            // JSON lines and quoted CSV exports are both supported.
            foreach(string line in text.Split('\n'))
            {
                JsonData row=null;
                try { row=JsonMapper.ToObject(line.Trim()); } catch(Exception) { }
                if(row!=null) Visit(row,0);
            }
            foreach(string cell in CsvCells(text))
            {
                string value=cell.Trim();
                if(value.StartsWith("H4sI",StringComparison.Ordinal)) Add(BattleMovementUpload.Decode(value));
                else if(value.StartsWith("{") || value.StartsWith("["))
                {
                    JsonData row=null;
                    try { row=JsonMapper.ToObject(value); } catch(JsonException) { }
                    if(row!=null) Visit(row,0);
                }
            }
        }
        if(packets.Count==0) throw new InvalidDataException("동선 payload가 없습니다. battle_movement_v1의 payload를 포함해 내보내세요.");
        var outputs=new List<string>();
        foreach(var match in packets)
        {
            var last=match.Value.Values.Last();
            var merged=new BattleMovementLog { matchId=last.matchId,startedUtc=last.startedUtc,scenePath=last.scenePath,
                mode=last.mode,training=last.training,gameVersion=last.gameVersion,sampleInterval=last.sampleInterval,
                duration=last.duration,endReason=last.endReason };
            bool missing=match.Value.Keys.First()!=0 || match.Value.Count!=match.Value.Keys.Last()+1;
            if(missing || last.endReason=="recording") merged.endReason="partial_server_export";
            foreach(var group in match.Value.Values.SelectMany(x=>x.players).GroupBy(x=>x.participant))
            {
                var identity=group.Last();
                var track=new BattleMovementTrack {participant=identity.participant,team=identity.team,nickname=identity.nickname,bot=identity.bot};
                track.points=group.SelectMany(x=>x.points).GroupBy(p=>p.time).Select(x=>x.Last()).OrderBy(p=>p.time).ToList();
                merged.players.Add(track);
            }
            // Unique export names prevent a partial re-export overwriting an earlier complete recording.
            string output=Path.Combine(BattleMovementLogFiles.DirectoryPath,"server_"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff")+"_"+match.Key+"_"+Guid.NewGuid().ToString("N").Substring(0,8)+".json");
            string json=JsonUtility.ToJson(merged);
            BattleMovementLogFiles.Parse(json);
            BattleMovementLogFiles.Write(output,json);
            outputs.Add(output);
        }
        return outputs.ToArray();
    }
    private static IEnumerable<string> CsvCells(string text)
    {
        var cell=new StringBuilder(); bool quoted=false;
        for(int i=0;i<text.Length;i++)
        {
            char c=text[i];
            if(c=='"')
            {
                if(quoted && i+1<text.Length && text[i+1]=='"') {cell.Append('"');i++;}
                else quoted=!quoted;
            }
            else if(!quoted && (c==',' || c=='\n' || c=='\r')) {yield return cell.ToString();cell.Clear();}
            else cell.Append(c);
        }
        yield return cell.ToString();
    }
}
