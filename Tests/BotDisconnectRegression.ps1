# Production departure/replacement/readiness methods with Fusion doubles, not a live transport test.
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
function Method([string]$file,[string]$name) {
 $code=Get-Content $file -Raw
 $m=[regex]::Match($code,'(?ms)^    (?:private|public) (?:static )?\w+ '+$name+'\([^{}]*?\)\r?\n    \{.*?^    \}')
 if(!$m.Success){throw "Missing $name"}; $m.Value -replace '^    private ','    public '
}
$source=@'
using System;
using System.Collections.Generic;
using System.Linq;
public struct PlayerRef:IEquatable<PlayerRef> {
 public int RawEncoded; public static PlayerRef None=>default;
 public static implicit operator PlayerRef(int v)=>new PlayerRef{RawEncoded=v};
 public bool Equals(PlayerRef p)=>RawEncoded==p.RawEncoded;
 public override bool Equals(object o)=>o is PlayerRef p&&Equals(p);
 public override int GetHashCode()=>RawEncoded;
 public static bool operator==(PlayerRef a,PlayerRef b)=>a.Equals(b);
 public static bool operator!=(PlayerRef a,PlayerRef b)=>!a.Equals(b);
}
public struct Vector3 { public static Vector3 zero=>default; }
public struct Quaternion { public static Quaternion identity=>default; }
public struct PlayerConfig {}
public enum MatchBotRole {Objective,Support,Fighter}
public enum BattleStartPhase {WaitingForPlayers,Intro,Playing,Intermission}
public class Transform {public Vector3 position;}
public class NetworkObject {
 public bool IsValid=true; public object gameObject=>this; public Transform transform=new();
 public PlayerData Data;
 public T GetComponent<T>() where T:class=>Data as T;
}
public class NetworkRunner {
 public bool IsRunning=true,IsServer=true,FailSpawn;
 public List<PlayerRef> ActivePlayers=new(); public int Spawns;
 public NetworkObject Spawn(object prefab,Vector3 pos,Quaternion rot,PlayerRef authority,Action<NetworkRunner,NetworkObject> onBeforeSpawned){
  if(FailSpawn)return null; Spawns++;var obj=new NetworkObject();
  obj.Data=new PlayerData{Object=obj,Runner=this};onBeforeSpawned(this,obj);return obj;
 }
 public void Despawn(NetworkObject obj){obj.IsValid=false;}
 public void MakeDontDestroyOnLoad(object obj){}
}
public class PlayerData {
 public NetworkObject Object=new();public NetworkRunner Runner;
 public int teamIndex,EquippedTitleId,BotTeamOrder,CounterSelections,MagicChangeRound;
 public PlayerRef ParticipantRef;public bool IsMatchBot,RoundSwapReady;
 public MatchBotRole BotRole;
 public PlayerConfig GetPlayerConfig()=>default;
 public void InitializeMatchBot(int slot,int team,PlayerConfig config,int title){
  IsMatchBot=true;ParticipantRef=BattleParticipants.BotRef(slot);teamIndex=team;EquippedTitleId=title;
 }
 public void SetMatchBotRole(MatchBotRole role,int order){BotRole=role;BotTeamOrder=order;}
 public void ChooseBotCounters(bool previous){CounterSelections++;}
 public void ResetMatchPerks(){} public void ResetBattleStats(){}
 public void BeginRoundMagicChange(int round){MagicChangeRound=round;RoundSwapReady=false;}
 public static PlayerData FindPerkData(NetworkRunner runner,PlayerRef who)=>NetworkGameManager.Instance.playerDatas.GetValueOrDefault(who);
}
public static class MatchBotSettings {public static PlayerConfig RandomAppearance(IReadOnlyList<PlayerConfig> values)=>default;}
public static class TitleCatalog {public static int ChooseBotTitle(IReadOnlyList<int> titles)=>titles.Count+1;}
public static class Debug {
 public static void Log(object msg,object context){} public static void LogWarning(object msg,object context){}
 public static void LogException(Exception e,object context){}
}
public static class BattleParticipants {
 public static PlayerRef BotRef(int slot)=>1024+slot;
 public static IEnumerable<PlayerRef> Refs(NetworkRunner r){
  foreach(var h in r.ActivePlayers)yield return h;
  foreach(var d in NetworkGameManager.Instance.playerDatas.Values)if(d.IsMatchBot&&d.Object.IsValid)yield return d.ParticipantRef;
 }
 public static bool TryGetPlayerObject(NetworkRunner r,PlayerRef p,out NetworkObject obj)=>NetworkGameManager.Instance.spawnedPlayers.TryGetValue(p,out obj);
}
public class BattleFlag {
 public NetworkRunner Runner;public HashSet<PlayerRef> readyPlayers=new();public int ExpectedPlayerCount=10;
 public bool HasEnded;public int WinningTeam,DropCalls;
 public void ForgetReadyPlayer(PlayerRef p){readyPlayers.Remove(p);}
 public void DropCarrier(PlayerRef p,Vector3 pos){DropCalls++;}
 public void EndSeriesByForfeit(int team){HasEnded=true;WinningTeam=team;}
'@
$source+=Method Assets/Script/BattleFlag.cs AreAllPlayersReady
$source+=@'
}
public class BattleManager {
 public static BattleManager Instance;public NetworkRunner runner;public bool isInitialized=true;
 public Dictionary<PlayerRef,PlayerData> playerDatas;
 public Dictionary<PlayerRef,NetworkObject> spawnedPlayers;
 public BattleFlag battleFlag;public int ExpectedPlayerCount=>battleFlag.ExpectedPlayerCount;
 public bool IsBattleEnded=>battleFlag.HasEnded;public BattleStartPhase Phase;
 public int RoundNumber=2;public object playerPrefab;
 public class Pending {public PlayerRef PlayerRef;}
 public List<Pending> pendingRespawns=new();
 public void SpawnBattlePlayer(PlayerRef p,PlayerData d,object prefab){if(!spawnedPlayers.ContainsKey(p))spawnedPlayers[p]=new NetworkObject();}
 public bool TrySetRoundReady(PlayerData d,int round,bool ready){d.RoundSwapReady=ready;return true;}
'@
foreach($name in @('PlayerLeft','SpawnReplacementBot','IsConnected')) {
 $source+=Method Assets/Script/vibe/BattleManager.cs $name
}
$source+=@'
}
public class NetworkGameManager {
 public static NetworkGameManager Instance;public NetworkRunner _runner=new();
 public bool cancelRequested,IsMigratingDeathMatch,stopInProgress,IsBotMatch=true,isGameStarting=true;
 public bool IsRoomHost=>_runner.IsServer&&_runner.IsRunning;
 public int RequiredPlayerCount=10;public int ConnectedPlayerCount=>_runner.ActivePlayers.Count;
 public string CurrentMatchMode=>IsBotMatch?"FiveVsFiveBot":"FiveVsFive";public string Status;
 public float randomPlayersReadyAt;public object playerDataPrefab;
 public Dictionary<PlayerRef,PlayerData> playerDatas=new();
 public Dictionary<PlayerRef,NetworkObject> spawnedPlayers=new();
 public Dictionary<PlayerRef,int> assignedTeamIndexes=new();
 public void UpdateWaitingStatus(){} public void DespawnRobbyPlayer(NetworkRunner r,PlayerRef p){}
 public void SetMatchStatus(string s){Status=s;}
'@
$source+=Method Assets/Script/NetworkGameManager.cs OnPlayerLeft
$source+=Method Assets/Script/NetworkGameManager.MatchBots.cs ReplaceDepartedHumanWithBot
$source+=@'
}
public static class BotDisconnectChecks {
 static int checks;
 static void Check(bool pass,string message){if(!pass)throw new Exception(message);checks++;}
 static NetworkGameManager Setup(int size=5,int clientTeam=2) {
  var n=new NetworkGameManager{RequiredPlayerCount=size*2};NetworkGameManager.Instance=n;
  var f=new BattleFlag{Runner=n._runner,ExpectedPlayerCount=size*2};
  BattleManager.Instance=new BattleManager{runner=n._runner,playerDatas=n.playerDatas,spawnedPlayers=n.spawnedPlayers,battleFlag=f};
  for(int i=1;i<=2;i++){
   int team=i==1?1:clientTeam;var d=new PlayerData{ParticipantRef=i,teamIndex=team,Runner=n._runner};
   n.playerDatas[i]=d;n.assignedTeamIndexes[i]=team;n._runner.ActivePlayers.Add(i);n.spawnedPlayers[i]=new();f.readyPlayers.Add(i);
  }
  int slot=1;
  for(int team=1;team<=2;team++){
   while(n.playerDatas.Values.Count(d=>d.teamIndex==team)<size){
    var p=BattleParticipants.BotRef(slot++);var d=new PlayerData{ParticipantRef=p,teamIndex=team,IsMatchBot=true,Runner=n._runner,BotRole=MatchBotRole.Support,CounterSelections=1};
    n.playerDatas[p]=d;n.spawnedPlayers[p]=new();
   }
  }
  return n;
 }
 static void Leave(NetworkGameManager n,int who){n._runner.ActivePlayers.Remove(who);n.OnPlayerLeft(n._runner,who);}
 public static int Run(){
  foreach(int size in new[]{1,2,3,4,5})foreach(var phase in new[]{BattleStartPhase.WaitingForPlayers,BattleStartPhase.Intro,BattleStartPhase.Playing,BattleStartPhase.Intermission}){
   var n=Setup(size);var b=BattleManager.Instance;b.Phase=phase;var old=n.spawnedPlayers[2];
   var bots=n.playerDatas.Values.Where(d=>d.IsMatchBot).ToArray();
   b.pendingRespawns.Add(new BattleManager.Pending{PlayerRef=2});Leave(n,2);
   Check(!b.IsBattleEnded,"bot match must not forfeit on human departure");
   Check(n.playerDatas.Count==size*2&&n.playerDatas.Values.Count(d=>d.teamIndex==2)==size,"restore roster/team size");
   Check(!old.IsValid&&!n.spawnedPlayers.ContainsKey(2)&&!n.assignedTeamIndexes.ContainsKey(2),"remove departing actor and reservation");
   Check(b.pendingRespawns.Count==0&&b.battleFlag.DropCalls==1,"clear respawn and drop carried flag");
   Check(b.battleFlag.AreAllPlayersReady(),"bot fills required scene-ready roster");
   Check(bots.All(d=>d.BotRole==MatchBotRole.Support&&d.CounterSelections==1),"existing roles/loadouts preserved");
   var replacement=n.playerDatas.Values.First(d=>d.IsMatchBot&&!bots.Contains(d));
   Check(replacement.BotRole==MatchBotRole.Objective&&replacement.CounterSelections==1,"replacement has objective role and attack loadout");
   if(phase==BattleStartPhase.Intermission)Check(replacement.RoundSwapReady&&replacement.MagicChangeRound==2,"intermission replacement is ready");
   n.OnPlayerLeft(n._runner,2);Check(n._runner.Spawns==1&&!b.IsBattleEnded,"duplicate leave must not add bots/end match");
  }
  var same=Setup(5,1);Leave(same,2);Check(same.playerDatas.Values.Count(d=>d.teamIndex==1)==5,"same-team guests replaced on their own team");
  var unknown=Setup();unknown.IsBotMatch=false;unknown.OnPlayerLeft(unknown._runner,99);Check(!BattleManager.Instance.IsBattleEnded,"unknown/rejected connection cannot forfeit");
  var normal=Setup();normal.IsBotMatch=false;Leave(normal,2);Check(BattleManager.Instance.IsBattleEnded&&normal._runner.Spawns==0,"ordinary match policy unchanged");
  var waiting=Setup();waiting.isGameStarting=false;BattleManager.Instance=null;Leave(waiting,2);Check(waiting._runner.Spawns==0,"no replacement in waiting room");
  var loading=Setup();BattleManager.Instance=null;Leave(loading,2);Check(loading.playerDatas.Count==10,"replacement survives before battle manager creation");
  var preinit=Setup();BattleManager.Instance.isInitialized=false;Leave(preinit,2);Check(preinit.playerDatas.Count==10&&preinit.spawnedPlayers.Count==9,"initialization will spawn replacement once");
  var finished=Setup();BattleManager.Instance.battleFlag.HasEnded=true;Leave(finished,2);Check(finished._runner.Spawns==0,"no replacement after result");
  var fail=Setup();fail._runner.FailSpawn=true;Leave(fail,2);Check(fail.playerDatas.Count==9&&fail.Status!=null&&!BattleManager.Instance.IsBattleEnded,"failed replacement is reported without fabricated win");
  var client=Setup();client._runner.IsServer=false;Leave(client,2);Check(client.playerDatas.Count==10,"clients cannot mutate roster");
  var stale=Setup();stale.OnPlayerLeft(new NetworkRunner(),2);Check(stale.playerDatas.Count==10,"ignore old runner callbacks");
  return checks;
 }
}
'@
Add-Type -TypeDefinition $source
"Bot disconnect, roster, preparation and ordinary-mode regression checks passed: $([BotDisconnectChecks]::Run())"
