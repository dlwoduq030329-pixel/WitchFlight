# Production rules/admission methods with deterministic Fusion doubles; not a live transport test.
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$network=Get-Content Assets/Script/NetworkGameManager.cs -Raw
function Method([string]$name,[string]$code=$network) {
 $m=[regex]::Match($code,'(?ms)^    (?:private|public) (?:static )?\w+ '+$name+'\([^{}]*?\)\r?\n    \{.*?^    \}')
 if(!$m.Success){throw "Missing $name"}; $m.Value -replace '^    private ','    public '
}
$source=@'
using System;
using System.Collections.Generic;
using Fusion;
namespace Fusion {
 public struct PlayerRef:IEquatable<PlayerRef> {
  public int RawEncoded; public static PlayerRef None=>default; public static implicit operator PlayerRef(int v)=>new PlayerRef{RawEncoded=v};
  public bool Equals(PlayerRef p)=>RawEncoded==p.RawEncoded;
  public override bool Equals(object o)=>o is PlayerRef p&&Equals(p);
  public override int GetHashCode()=>RawEncoded;
  public static bool operator==(PlayerRef a,PlayerRef b)=>a.Equals(b);
  public static bool operator!=(PlayerRef a,PlayerRef b)=>!a.Equals(b);
 }
 public class NetworkObject:NetObject {
  public PlayerData Data; public object gameObject=>this;
  public T GetComponent<T>() where T:class=>Data as T;
 }
 public class NetworkRunner {
  public bool IsRunning=true; public PlayerRef LocalPlayer=1; public List<PlayerRef> ActivePlayers=new(); public Info SessionInfo=new(); public class Info {public bool IsOpen=true;}
  public int SpawnCalls,FailAt=-1;
  public NetworkObject Spawn(object prefab,Vector3 position,Quaternion rotation,PlayerRef authority,Action<NetworkRunner,NetworkObject> onBeforeSpawned){
   if(++SpawnCalls==FailAt)return null;
   var obj=new NetworkObject();obj.Data=new PlayerData{Object=obj,Runner=this};onBeforeSpawned(this,obj);return obj;
  }
  public void MakeDontDestroyOnLoad(object obj){} public void Despawn(NetworkObject obj){obj.IsValid=false;}
 }
}
public struct Vector3 {public static Vector3 zero=>default;}
public struct Quaternion {public static Quaternion identity=>default;}
public struct PlayerConfig {}
public static class MatchBotSettings {public static PlayerConfig RandomAppearance(IReadOnlyList<PlayerConfig> others)=>default;}
public static class TitleCatalog {public static int ChooseBotTitle(IReadOnlyList<int> used)=>used.Count+1;}
public static class Debug {public static void LogException(Exception error,object context){}}
public class NetObject {public bool IsValid=true,HasStateAuthority=true;}
public enum Camp {A,B}
public class PlayerData {
 public NetObject Object=new(); public NetworkRunner Runner; public bool IsRoomOwner,IsLoadoutInitialized=true;
 public int teamIndex;public Camp camp;public PlayerRef ParticipantRef;public BattleMatchMode RoomMatchMode=BattleMatchMode.TwoVsTwo;
 public bool IsMatchBot;public int EquippedTitleId,CounterSelections;
 public MatchBotRole BotRole;public int BotTeamOrder;
 public void SetMatchBotRole(MatchBotRole role,int order){BotRole=role;BotTeamOrder=order;}
 public PlayerConfig GetPlayerConfig()=>default;
 public void InitializeMatchBot(int slot,int team,PlayerConfig config,int title){IsMatchBot=true;ParticipantRef=BattleParticipants.BotRef(slot);teamIndex=team;EquippedTitleId=title;}
 public void ChooseBotCounters(bool previous){CounterSelections++;}
 public void SetRoomMatchMode(BattleMatchMode mode){RoomMatchMode=BattleMatchRules.Normalize(mode);}
 public static PlayerData FindPerkData(NetworkRunner runner,PlayerRef who)=>NetworkGameManager.Instance.GetPlayerData(who);
}
public static class BattleParticipants {
 public static PlayerRef BotRef(int slot)=>1024+slot;
 public static IEnumerable<PlayerRef> Refs(NetworkRunner runner){
  foreach(var human in runner.ActivePlayers)yield return human;
  foreach(var data in NetworkGameManager.Instance.playerDatas.Values)if(data.IsMatchBot&&data.Object.IsValid)yield return data.ParticipantRef;
 }
}
public class NetworkGameManager {
 public static NetworkGameManager Instance; public NetworkRunner _runner=new();
 public bool IsMatching=true,IsWaitingInCodeRoom=true,IsRoomHost=true,IsTraining,IsDeathMatch,IsRaceMode,IsRaidMode,isGameStarting,IsBattleSceneLoaded,cancelRequested,stopInProgress;
 public BattleMatchMode roomMatchMode=BattleMatchMode.TwoVsTwo;
 public BattleMatchMode CurrentMatchMode=>BattleMatchRules.Normalize(GetPlayerData(_runner.LocalPlayer)?.RoomMatchMode??roomMatchMode);
 public int ConnectedPlayerCount=>_runner.ActivePlayers.Count;
 public int RequiredPlayerCount=>BattleMatchRules.PlayerCount(CurrentMatchMode);
 public bool IsBotMatch=>!IsTraining&&BattleMatchRules.HasBots(CurrentMatchMode);public object playerDataPrefab;
 public Dictionary<PlayerRef,PlayerData> playerDatas=new(); public Dictionary<PlayerRef,int> assignedTeamIndexes=new();
 public PlayerData GetPlayerData(PlayerRef who)=>playerDatas.TryGetValue(who,out var d)?d:null;
 public void SetMatchStatus(string value){} public void UpdateWaitingStatus(){}
'@
foreach($name in @('TrySetRoomMatchMode','TrySetWaitingTeam','NextTeamIndex','RefreshRoomAdmission')){$source+=Method $name}
$source+=Method 'AssignBotRoles' (Get-Content Assets/Script/NetworkGameManager.MatchBots.cs -Raw)
$source+=Method 'FillBotSeats' (Get-Content Assets/Script/NetworkGameManager.MatchBots.cs -Raw)
$source+="`n}`n"+((Get-Content Assets/Script/BattleMatchRules.cs -Raw)-replace '(?m)^using .*;\r?\n','')
$source+=[regex]::Match((Get-Content Assets/Script/PlayerData.cs -Raw),'public enum MagicType \{[^}]+\}').Value
$source+=((Get-Content Assets/Script/MatchBotSettings.cs -Raw) -split '\[CreateAssetMenu')[0] -replace '(?m)^using .*;\r?\n',''
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
  string[] labels={"1vs1","2vs2","3vs3","4vs4","5vs5","1vs1(BOT)","2vs2(BOT)","3vs3(BOT)","4vs4(BOT)","5vs5(BOT)","거대 먀오녀 토벌"};
  Check(BattleMatchRules.ModeOptionCount==labels.Length,"eleven dropdown options");
  for(int i=0;i<BattleMatchRules.ModeOptionCount;i++){
   var mode=BattleMatchRules.FromDropdown(i);
   Check(mode!=BattleMatchMode.Race,"race cannot be selected");
   Check(BattleMatchRules.DropdownIndex(mode)==i,"dropdown round trip");
   Check(BattleMatchRules.Label(mode)==labels[i],"requested dropdown label and order");
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
  // One human, mixed humans, and full human rooms: bots fill only the remaining seats.
  for(int size=1;size<=5;size++)foreach(int humans in new[]{1,Math.Min(3,size*2),size*2}){
   var m=new NetworkGameManager();NetworkGameManager.Instance=m;Join(m,1);
   var mode=BattleMatchRules.FromDropdown(4+size);
   Check(m.TrySetRoomMatchMode(4+size),"select bot mode");
   Check(m.IsBotMatch&&m.RequiredPlayerCount==size*2,"bot capacity");
   Check(BattleMatchRules.IsTeamMode(mode)==(size>1),"bot team classification");
   Check(BattleMatchRules.Normalize(mode)==mode,"bot enum survives normalization");
   for(int id=2;id<=humans;id++)Join(m,id);
   Check(m.FillBotSeats(),"fill bot seats");
   Check(m.playerDatas.Count==size*2&&m._runner.SpawnCalls==size*2-humans,"only missing seats spawned");
   var roster=new List<PlayerData>();BattleMatchRules.CollectRoster(m._runner,roster);
   Check(BattleMatchRules.IsFullBalanced(roster,size*2),"human and bot roster balanced");
   foreach(var bot in roster)if(bot.IsMatchBot){
    Check(bot.CounterSelections==1,"all bots receive loadouts");
    Check(bot.BotRole==MatchBotRoles.ForTeamSlot(size,bot.BotTeamOrder),"bot role assigned before loadout selection");
   }
   Check(m.FillBotSeats()&&m._runner.SpawnCalls==size*2-humans,"repeat fill creates no duplicate bots");
  }
  var failed=new NetworkGameManager();NetworkGameManager.Instance=failed;Join(failed,1);failed.TrySetRoomMatchMode(9);failed._runner.FailAt=5;
  Check(!failed.FillBotSeats()&&failed.playerDatas.Count==1,"partial nine-bot spawn rolls back on failure");
  failed._runner.FailAt=-1;Check(failed.FillBotSeats()&&failed.playerDatas.Count==10,"bot fill can retry after rollback");
  return checks;
 }
}
'@
Add-Type -TypeDefinition $source
"Mode order, admission, team limits, 1v1-5v5 bot fill and rollback checks passed: $([TeamExpansionChecks]::Run())"
