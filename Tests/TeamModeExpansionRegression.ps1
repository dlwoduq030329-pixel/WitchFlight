# Production rules/admission methods with deterministic Fusion doubles; not a live transport test.
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$network=Get-Content Assets/Script/NetworkGameManager.cs -Raw
function Method([string]$name) {
 $m=[regex]::Match($network,'(?ms)^    (?:private|public) (?:static )?\w+ '+$name+'\([^{}]*?\)\r?\n    \{.*?^    \}')
 if(!$m.Success){throw "Missing $name"}; $m.Value -replace '^    private ','    public '
}
$source=@'
using System;
using System.Collections.Generic;
using Fusion;
namespace Fusion {
 public struct PlayerRef:IEquatable<PlayerRef> {
  public int RawEncoded; public static implicit operator PlayerRef(int v)=>new PlayerRef{RawEncoded=v};
  public bool Equals(PlayerRef p)=>RawEncoded==p.RawEncoded;
  public override bool Equals(object o)=>o is PlayerRef p&&Equals(p);
  public override int GetHashCode()=>RawEncoded;
  public static bool operator==(PlayerRef a,PlayerRef b)=>a.Equals(b);
  public static bool operator!=(PlayerRef a,PlayerRef b)=>!a.Equals(b);
 }
 public class NetworkRunner { public bool IsRunning=true; public PlayerRef LocalPlayer=1; public List<PlayerRef> ActivePlayers=new(); public Info SessionInfo=new(); public class Info {public bool IsOpen=true;} }
}
public class NetObject {public bool IsValid=true,HasStateAuthority=true;}
public enum Camp {A,B}
public class PlayerData {
 public NetObject Object=new(); public NetworkRunner Runner; public bool IsRoomOwner,IsLoadoutInitialized=true;
 public int teamIndex;public Camp camp;public PlayerRef ParticipantRef;public BattleMatchMode RoomMatchMode=BattleMatchMode.TwoVsTwo;
 public void SetRoomMatchMode(BattleMatchMode mode){RoomMatchMode=BattleMatchRules.Normalize(mode);}
 public static PlayerData FindPerkData(NetworkRunner runner,PlayerRef who)=>NetworkGameManager.Instance.GetPlayerData(who);
}
public static class BattleParticipants {public static IEnumerable<PlayerRef> Refs(NetworkRunner runner)=>runner.ActivePlayers;}
public class NetworkGameManager {
 public static NetworkGameManager Instance; public NetworkRunner _runner=new();
 public bool IsMatching=true,IsWaitingInCodeRoom=true,IsRoomHost=true,IsTraining,IsDeathMatch,IsRaceMode,IsRaidMode,isGameStarting,IsBattleSceneLoaded,cancelRequested,stopInProgress;
 public BattleMatchMode roomMatchMode=BattleMatchMode.TwoVsTwo;
 public BattleMatchMode CurrentMatchMode=>BattleMatchRules.Normalize(GetPlayerData(_runner.LocalPlayer)?.RoomMatchMode??roomMatchMode);
 public int ConnectedPlayerCount=>_runner.ActivePlayers.Count;
 public int RequiredPlayerCount=>BattleMatchRules.PlayerCount(CurrentMatchMode);
 public Dictionary<PlayerRef,PlayerData> playerDatas=new(); public Dictionary<PlayerRef,int> assignedTeamIndexes=new();
 public PlayerData GetPlayerData(PlayerRef who)=>playerDatas.TryGetValue(who,out var d)?d:null;
 public void SetMatchStatus(string value){} public void UpdateWaitingStatus(){}
'@
foreach($name in @('TrySetRoomMatchMode','TrySetWaitingTeam','NextTeamIndex','RefreshRoomAdmission')){$source+=Method $name}
$source+="`n}`n"+((Get-Content Assets/Script/BattleMatchRules.cs -Raw)-replace '(?m)^using .*;\r?\n','')
$source+=@'
public static class TeamExpansionChecks {
 static int checks;
 static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;}
 static PlayerData Join(NetworkGameManager m,int id){
  var d=new PlayerData{Runner=m._runner,ParticipantRef=id,teamIndex=m.NextTeamIndex(),IsRoomOwner=id==1};
  m._runner.ActivePlayers.Add(id);m.playerDatas[id]=d;m.assignedTeamIndexes[id]=d.teamIndex;return d;
 }
 public static int Run(){
  Check(BattleMatchRules.Normalize(BattleMatchMode.Race)==BattleMatchMode.TwoVsTwo,"legacy race falls back to 2v2");
  for(int i=0;i<BattleMatchRules.ModeOptionCount;i++){
   var mode=BattleMatchRules.FromDropdown(i);
   Check(mode!=BattleMatchMode.Race,"race cannot be selected");
   Check(BattleMatchRules.DropdownIndex(mode)==i,"dropdown round trip");
  }
  foreach(var mode in new[]{BattleMatchMode.ThreeVsThree,BattleMatchMode.FourVsFour,BattleMatchMode.FiveVsFive}){
   var m=new NetworkGameManager();NetworkGameManager.Instance=m;Join(m,1);
   int required=(int)mode;
   Check(m.TrySetRoomMatchMode(BattleMatchRules.DropdownIndex(mode)),"host selects larger mode");
   Check(m.RequiredPlayerCount==required&&BattleMatchRules.IsTeamMode(mode),"requested capacity");
   Check(!BattleMatchRules.HasBots(mode),"human modes do not fill bot seats");
   for(int id=2;id<=required;id++){
    m.RefreshRoomAdmission();Check(m._runner.SessionInfo.IsOpen,"admit until selected capacity");Join(m,id);
   }
   m.RefreshRoomAdmission();Check(!m._runner.SessionInfo.IsOpen,"full room closes");
   var roster=new List<PlayerData>();BattleMatchRules.CollectRoster(m._runner,roster);
   Check(BattleMatchRules.IsFullBalanced(roster,required),"all participants balanced");
   Check(!m.TrySetWaitingTeam(m.playerDatas[1],2),"cannot move into full opposing team");
   Check(!m.TrySetRoomMatchMode(1)&&m.RequiredPlayerCount==required,"cannot shrink under current occupancy");
   m.IsRoomHost=false;Check(!m.TrySetRoomMatchMode(0),"guest cannot change mode");m.IsRoomHost=true;
   var last=m.playerDatas[required];m.playerDatas.Remove(required);m.assignedTeamIndexes.Remove(required);m._runner.ActivePlayers.Remove(required);
   m.RefreshRoomAdmission();Check(m._runner.SessionInfo.IsOpen,"departure reopens admission");
   Check(m.NextTeamIndex()==last.teamIndex,"replacement fills vacant side");
   BattleMatchRules.CollectRoster(m._runner,roster);Check(!BattleMatchRules.IsFullBalanced(roster,required),"incomplete roster cannot start");
   Join(m,required+1);BattleMatchRules.CollectRoster(m._runner,roster);
   Check(BattleMatchRules.IsFullBalanced(roster,required),"replacement restores balanced roster");
   roster[1].teamIndex=roster[1].teamIndex==1?2:1;
   Check(!BattleMatchRules.IsFullBalanced(roster,required),"imbalanced roster cannot start");
  }
  return checks;
 }
}
'@
Add-Type -TypeDefinition $source
"Mode selection, 6/8/10-player admission, team capacity and replacement checks passed: $([TeamExpansionChecks]::Run())"
