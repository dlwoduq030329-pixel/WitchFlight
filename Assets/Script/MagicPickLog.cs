using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LitJson;
using UnityEngine;

[Serializable]
public sealed class MagicPickLog
{
    public string kind="magic_pick";
    public int formatVersion=1;
    public string matchId,startedUtc,recordedUtc,gameVersion,mode;
    public int round;
    public List<MagicPickPlayer> players=new();
}
[Serializable]
public sealed class MagicPickPlayer
{
    public int participant,team,slot1,slot2;
    public string nickname;
    public bool bot;
}
public sealed class MagicPickStatistics
{
    public readonly int[] slotCounts=new int[13], equippedCounts=new int[13];
    public int matches,participantRounds,totalSlots;
    public static MagicPickStatistics Calculate(IEnumerable<MagicPickLog> logs,int roundFilter,int population)
    {
        var result=new MagicPickStatistics();var matches=new HashSet<string>();var counted=new HashSet<string>();
        foreach(var log in logs)
        {
            if(roundFilter!=0 && log.round!=roundFilter) continue;
            foreach(var player in log.players)
            {
                if(population==0 && player.bot || population==1 && !player.bot) continue;
                string key=log.matchId+":"+log.round+":"+player.participant;
                if(!counted.Add(key)) continue;
                matches.Add(log.matchId);result.participantRounds++;
                result.slotCounts[player.slot1]++;result.slotCounts[player.slot2]++;result.totalSlots+=2;
                result.equippedCounts[player.slot1]++;
                if(player.slot2!=player.slot1) result.equippedCounts[player.slot2]++;
            }
        }
        result.matches=matches.Count;return result;
    }
}
public static class MagicPickLogStore
{
    public static string DirectoryPath=>Path.Combine(Application.persistentDataPath,"MagicPickLogs");
    public static string FileName(MagicPickLog log)=>log.matchId+"_R"+log.round+".json";
    public static MagicPickLog Parse(string json)
    {
        var log=JsonUtility.FromJson<MagicPickLog>(json);
        if(log==null || log.kind!="magic_pick" || log.formatVersion!=1 || !Guid.TryParseExact(log.matchId,"N",out _) ||
            log.round<1 || log.round>3 || log.players==null || log.players.Count==0 || log.players.Count>64)
            throw new InvalidDataException("잘못된 마법 픽 로그입니다.");
        var ids=new HashSet<int>();
        foreach(var p in log.players)
            if(p==null || !ids.Add(p.participant) || p.slot1<1 || p.slot1>12 || p.slot2<1 || p.slot2>12)
                throw new InvalidDataException("참가자 또는 마법 ID가 잘못되었습니다.");
        log.players.Sort((a,b)=>a.participant.CompareTo(b.participant));
        return log;
    }
    // 1=new, 0=identical retry, -1=conflict. Never silently replace different historical selections.
    public static int Store(MagicPickLog log)
    {
        string json=JsonUtility.ToJson(log);
        log=Parse(json);json=JsonUtility.ToJson(log);
        string path=Path.Combine(DirectoryPath,FileName(log));
        if(File.Exists(path)) return JsonUtility.ToJson(Parse(File.ReadAllText(path)))==json?0:-1;
        BattleMovementLogFiles.Write(path,json);return 1;
    }
    public static List<MagicPickLog> ReadAll(out int invalid)
    {
        invalid=0;var logs=new List<MagicPickLog>();
        if(!Directory.Exists(DirectoryPath)) return logs;
        foreach(string path in Directory.GetFiles(DirectoryPath,"*.json"))
        {
            try {if(new FileInfo(path).Length>1024*1024) throw new InvalidDataException();logs.Add(Parse(File.ReadAllText(path)));}
            catch(Exception) {invalid++;}
        }
        return logs;
    }
    public static string Import(string path)
    {
        if(new FileInfo(path).Length>64L*1024*1024) throw new InvalidDataException("파일을 64 MB 이하로 나눠 주세요.");
        string text=File.ReadAllText(path).TrimStart('\uFEFF');
        var found=new List<MagicPickLog>();int bad=0;
        void Visit(JsonData node,int depth)
        {
            if(node==null || depth>12) return;
            if(node.IsObject)
            {
                if(node.Keys.Contains("kind") && node["kind"].IsString && (string)node["kind"]=="magic_pick")
                {
                    if(found.Count>=100000) throw new InvalidDataException("한 번에 가져올 수 있는 로그 수를 초과했습니다.");
                    try {found.Add(Parse(node.ToJson()));}catch(Exception){bad++;}return;
                }
                foreach(string key in node.Keys) Visit(node[key],depth+1);
            }
            else if(node.IsArray) {for(int i=0;i<node.Count;i++) Visit(node[i],depth+1);}
            else if(node.IsString)
            {
                string value=((string)node).Trim();
                if(value.StartsWith("{") || value.StartsWith("["))
                {
                    JsonData child=null;try {child=JsonMapper.ToObject(value);}catch(Exception){}
                    if(child!=null) Visit(child,depth+1);
                }
            }
        }
        // LitJson can stop after the first root, so detect JSONL before parsing a whole document.
        var lines=text.Split('\n').Where(line=>!string.IsNullOrWhiteSpace(line)).ToArray();
        var rows=new List<JsonData>();
        if(lines.Length>1)
        {
            foreach(string line in lines)
            {
                JsonData row=null;try{row=JsonMapper.ToObject(line);}catch(Exception){}
                if(row==null || (!row.IsObject && !row.IsArray)) {rows.Clear();break;}
                rows.Add(row);
            }
        }
        if(rows.Count>1) {foreach(var row in rows) Visit(row,0);}
        else
        {
            JsonData root=null;try{root=JsonMapper.ToObject(text);}catch(Exception){}
            if(root!=null) Visit(root,0);
            else
                foreach(string cell in CsvCells(text))
                {JsonData row=null;try{row=JsonMapper.ToObject(cell);}catch(Exception){}if(row!=null) Visit(row,0);}
        }
        if(found.Count==0) throw new InvalidDataException("magic_pick_v1의 payload가 없습니다. 마법 픽 CSV/JSON을 선택하세요.");
        int added=0,duplicates=0,conflicts=0;
        foreach(var log in found)
        {
            switch(Store(log)) {case 1:added++;break;case 0:duplicates++;break;default:conflicts++;break;}
        }
        return $"추가 {added}라운드 · 중복 {duplicates} · 내용 충돌 {conflicts} · 손상 {bad}\n충돌한 기록은 기존 내용을 유지합니다.";
    }
    private static IEnumerable<string> CsvCells(string text)
    {
        var value=new StringBuilder();bool quoted=false;
        for(int i=0;i<text.Length;i++)
        {
            char c=text[i];
            if(c=='"') {if(quoted && i+1<text.Length && text[i+1]=='"'){value.Append('"');i++;}else quoted=!quoted;}
            else if(!quoted && (c==',' || c=='\r' || c=='\n')) {yield return value.ToString();value.Clear();}
            else value.Append(c);
        }
        yield return value.ToString();
    }
}
