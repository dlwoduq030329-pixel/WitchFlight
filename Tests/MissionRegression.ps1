$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$source=Get-Content -Raw Assets/Script/MissionProgression.cs
$stubs=@'
namespace UnityEngine {
 public static class Time { public static float unscaledTime=100; }
 public static class Debug { public static void LogError(object v) { throw new System.Exception(v.ToString()); } }
 public static class Mathf { public static float Max(float a,float b)=>System.Math.Max(a,b); }
 public static class Resources { public static T Load<T>(string s) where T:class => MissionCatalog.Current as T; }
 public static class JsonUtility {
  static System.Text.Json.JsonSerializerOptions o=new System.Text.Json.JsonSerializerOptions { IncludeFields=true };
  public static string ToJson(object v)=>System.Text.Json.JsonSerializer.Serialize(v,v.GetType(),o);
  public static T FromJson<T>(string s)=>System.Text.Json.JsonSerializer.Deserialize<T>(s,o);
 }
}
public enum MissionPeriod { Daily,Weekly }
public enum MissionObjective { MatchComplete,DamageDealt,KillOrAssist,ItemCollected,MatchWin,CombatMinutes,ParrySuccess,DistinctMagicHits,DistinctPlayDays,MatchesWithKillOrAssist }
public class MissionDefinition { public string missionId; public bool enabled=true; public MissionPeriod period; public MissionObjective objective; public int target=3,requiredHitsPerMagic=10,sortOrder; }
public class MissionCatalog { public static MissionCatalog Current=new MissionCatalog(); public System.Collections.Generic.List<MissionDefinition> Missions=new System.Collections.Generic.List<MissionDefinition>(); }
public class DatabaseManager { public static DatabaseManager Instance=new DatabaseManager(); public bool CanModifyAccountData=true; public string LoadedAccountId="a"; }
public class NetworkGameManager { public static NetworkGameManager Instance=new NetworkGameManager(); public bool IsTraining,IsDeathMatch; }
public static class DataConfig { public static string missionState=""; }
public class NetworkObject { public bool IsValid=true,HasInputAuthority=true; }
public class PlayerData { public NetworkObject Object=new NetworkObject(); public bool IsMatchBot,MissionCompleted,MissionWon; public string MissionMatchId="match"; public float[] MissionCounters=new float[28]; }
public static class MissionChecks {
 static void Check(bool v,string label) { if(!v)throw new System.Exception(label); }
 public static void Run() {
  var before=new System.DateTime(2026,11,8,20,59,59,System.DateTimeKind.Utc); var after=before.AddSeconds(1);
  Check(MissionProgression.PeriodDay(before)==new System.DateTime(2026,11,8),"before daily reset");
  Check(MissionProgression.PeriodDay(after)==new System.DateTime(2026,11,9),"daily reset 06:00 KST");
  Check(MissionProgression.PeriodWeek(before)==new System.DateTime(2026,11,2),"before Monday reset");
  Check(MissionProgression.PeriodWeek(after)==new System.DateTime(2026,11,9),"Monday reset");
  var c=MissionCatalog.Current;
  c.Missions.Add(new MissionDefinition{missionId="complete",objective=MissionObjective.MatchComplete});
  c.Missions.Add(new MissionDefinition{missionId="damage",objective=MissionObjective.DamageDealt,sortOrder=1});
  c.Missions.Add(new MissionDefinition{missionId="magic",objective=MissionObjective.DistinctMagicHits,sortOrder=2});
  for(int i=0;i<10;i++)c.Missions.Add(new MissionDefinition{missionId="w"+i,period=MissionPeriod.Weekly,sortOrder=i});
  MissionProgression.Load();
  Check(MissionProgression.Active().Count==4,"configured rows");
  Check(MissionProgression.Active()[0].period==MissionPeriod.Weekly,"weekly first");
  var p=new PlayerData(); p.MissionCounters[1]=123;
  NetworkGameManager.Instance.IsDeathMatch=true; MissionProgression.Observe(p);
  Check(MissionProgression.State.matches.Count==0,"deathmatch excluded");
  NetworkGameManager.Instance.IsDeathMatch=false; NetworkGameManager.Instance.IsTraining=true; MissionProgression.Observe(p);
  Check(MissionProgression.State.matches.Count==0,"training excluded");
  NetworkGameManager.Instance.IsTraining=false; MissionProgression.Observe(p); MissionProgression.Observe(p);
  Check(MissionProgression.Entry(MissionProgression.State,"damage").progress==123,"damage once");
  p.MissionCounters[11]=10;p.MissionCounters[12]=9;p.MissionCompleted=true;MissionProgression.Observe(p);MissionProgression.Observe(p);
  Check(MissionProgression.Entry(MissionProgression.State,"complete").progress==1,"completion once");
  Check(MissionProgression.Entry(MissionProgression.State,"magic").progress==1,"magic thresholds");
  MissionProgression.Flush();MissionProgression.Load();MissionProgression.Observe(p);
  Check(MissionProgression.Entry(MissionProgression.State,"complete").progress==1,"reload duplicate ignored");
  MissionProgression.Entry(MissionProgression.State,"complete").claimed=true;
  string weekly=MissionProgression.State.weeklyMissionId;MissionProgression.Entry(MissionProgression.State,weekly).progress=7;
  MissionProgression.State.dailyPeriodKey="2000-01-01";MissionProgression.EnsurePeriods();
  Check(!MissionProgression.Entry(MissionProgression.State,"complete").claimed,"daily receipt resets");
  Check(MissionProgression.Entry(MissionProgression.State,weekly).progress==7,"weekly survives daily reset");
  MissionProgression.State.weeklyPeriodKey="2000-01-03";MissionProgression.EnsurePeriods();
  Check(MissionProgression.Entry(MissionProgression.State,MissionProgression.State.weeklyMissionId).progress==0,"weekly reset");
 }
}
'@
Add-Type -TypeDefinition ($source+$stubs)
[MissionChecks]::Run()
'Mission checks passed: KST boundaries, mode exclusions, deltas, completion dedupe, reload, daily/weekly reset.'
