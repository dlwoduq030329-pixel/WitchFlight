# Production mode/roster/admission/spawn methods with deterministic Unity/Fusion doubles.
# Not a live four-client transport, render, or collision test.
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$network=Get-Content Assets/Script/NetworkGameManager.cs -Raw
$data=Get-Content Assets/Script/PlayerData.cs -Raw
$battle=Get-Content Assets/Script/vibe/BattleManager.cs -Raw
function Method([string]$s,[string]$name) {
 $m=[regex]::Match($s,'(?ms)^    (?:private|public) (?:static )?\w+ '+$name+'\([^{}]*?\)\r?\n    \{.*?^    \}')
 if(!$m.Success){throw "Missing method $name"}; $m.Value -replace '^    private ','    public '
}
$source=@'
using System;
using System.Collections.Generic;
using Fusion;
namespace Fusion {
 public struct PlayerRef:IEquatable<PlayerRef> {
  public int RawEncoded;public int PlayerId=>RawEncoded;public static PlayerRef None=>default;
  public static implicit operator PlayerRef(int n)=>new(){RawEncoded=n};
  public bool Equals(PlayerRef b)=>RawEncoded==b.RawEncoded;public override bool Equals(object b)=>b is PlayerRef p&&Equals(p);
  public override int GetHashCode()=>RawEncoded;public static bool operator==(PlayerRef a,PlayerRef b)=>a.Equals(b);public static bool operator!=(PlayerRef a,PlayerRef b)=>!a.Equals(b);
 }
 public struct NetworkPrefabRef {}
 public class SessionInfo {public bool IsOpen=true;}
 public class NetworkRunner {
  public bool IsRunning=true,IsServer=true;public PlayerRef LocalPlayer=1;public List<PlayerRef> ActivePlayers=new();public SessionInfo SessionInfo=new();
  public NetworkObject Spawn(NetworkPrefabRef prefab,Vector3 pos,Quaternion rot,PlayerRef who)=>new(){InputAuthority=who,transform=new(){position=pos,rotation=rot}};
  public void SetPlayerObject(PlayerRef who,NetworkObject obj){}
 }
 public class NetworkObject {
  public bool IsValid=true,HasStateAuthority=true,HasInputAuthority=true;public PlayerRef InputAuthority;public Transform transform=new();public Player Pilot=new();
  public T GetComponent<T>() where T:class=>Pilot as T;
 }
}
public static class Mathf {public static float Max(float a,float b)=>Math.Max(a,b);}
public struct Vector3 {
 public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}
 public static Vector3 operator+(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
 public static Vector3 operator*(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);
}
public struct Quaternion {}
public class Transform {public Vector3 position,right=new(1,0,0);public Quaternion rotation;}
public static class Debug {public static void LogError(string s,object c){}}
public enum Camp {A,B}
public class Player {public bool Respawn;public PlayerData data;public void InitPlayer(PlayerData d,bool respawn){data=d;Respawn=respawn;}}
public class PlayerData {
 public static List<PlayerData> PerkPlayers=new();public NetworkRunner Runner;public NetworkObject Object=new();
 public int teamIndex,playerprofile;public bool IsLoadoutInitialized=true,IsRoomOwner;public Camp camp;
 public BattleMatchMode RoomMatchMode=BattleMatchMode.TwoVsTwo;
 public static PlayerData FindPerkData(NetworkRunner r,PlayerRef who)=>PerkPlayers.Find(d=>d.Runner==r&&d.Object.IsValid&&d.Object.InputAuthority==who);
 public Dictionary<PlayerRef,(PlayerData data,int profile,int ownProfile,int team,int ownTeam)> lobbyPreviews=new();
'@
foreach($name in @('SetRoomMatchMode','HasPresentedLobbyOpponent','RPC_ConfirmLobbyProfilePreview','RPC_ClearLobbyProfilePreview','RequestWaitingTeam','RPC_RequestWaitingTeam')){$source+=Method $data $name}
$source+=@'
}
public class NetworkGameManager {
 public static NetworkGameManager Instance;public NetworkRunner _runner;public NetworkRunner Runner=>_runner;
 public BattleMatchMode roomMatchMode=BattleMatchMode.TwoVsTwo;
 public bool IsRandomMatch,IsTraining,IsBattleSceneLoaded,isGameStarting,cancelRequested,stopInProgress;
 public bool IsMatching=true,IsWaitingInCodeRoom=true,IsRoomHost=true;public string Status;
 public Dictionary<PlayerRef,PlayerData> playerDatas=new();public Dictionary<PlayerRef,int> assignedTeamIndexes=new();
 public int RequiredPlayerCount=>IsTraining?1:BattleMatchRules.PlayerCount(CurrentMatchMode);
 public int ConnectedPlayerCount=>_runner?.ActivePlayers.Count??0;
 public PlayerData GetPlayerData(PlayerRef who)=>playerDatas.TryGetValue(who,out var d)?d:null;
 public void SetMatchStatus(string s){Status=s;}public void UpdateWaitingStatus(){}
'@
$mode=[regex]::Match($network,'(?ms)^    public BattleMatchMode CurrentMatchMode\r?\n    \{.*?^    \}').Value
if(!$mode){throw 'Missing replicated room mode getter'};$source+=$mode
foreach($name in @('TrySetRoomMatchMode','TrySetWaitingTeam','NextTeamIndex','RefreshRoomAdmission','AreBattlePlayerDatasReady')){$source+=Method $network $name}
$source+=@'
}
public class FlagProbe {public int Winner;public void ForgetReadyPlayer(PlayerRef p){}public void DropCarrier(PlayerRef p,Vector3 v){}public void EndSeriesByForfeit(int t){Winner=t;}}
public class BattleProbe {
 public NetworkRunner runner;public Dictionary<PlayerRef,PlayerData> playerDatas=new();public Dictionary<PlayerRef,NetworkObject> spawnedPlayers=new();
 public Transform playerSpawnA=new(),playerSpawnB=new(){position=new(100,0,0),right=new(-1,0,0)},playerSpawnA2,playerSpawnB2;
 public float teammateSpawnSpacing=3;public int ExpectedPlayerCount=4;public bool IsBattleEnded;
 public class PendingRespawn {public PlayerRef PlayerRef;}
 public List<PendingRespawn> pendingRespawns=new();public FlagProbe battleFlag=new();
'@
foreach($name in @('SpawnBattlePlayer','IsConnected','PlayerLeft')){$source+=Method $battle $name}
$source+="`n}`n"+((Get-Content Assets/Script/BattleMatchRules.cs -Raw)-replace '(?m)^using .*;\r?\n','')
$source+=@'
public static class Checks {
 static int n;static void Check(bool value,string name){if(!value)throw new Exception(name);n++;}
 static PlayerData Add(NetworkGameManager m,int id,int team){
  var d=new PlayerData{Runner=m.Runner,teamIndex=team,playerprofile=id,IsRoomOwner=id==1};d.Object.InputAuthority=id;
  m.Runner.ActivePlayers.Add(id);m.playerDatas[id]=d;m.assignedTeamIndexes[id]=team;PlayerData.PerkPlayers.Add(d);return d;
 }
 static NetworkGameManager Fresh(){PlayerData.PerkPlayers.Clear();var m=new NetworkGameManager{_runner=new()};NetworkGameManager.Instance=m;return m;}
 public static int Run(){
  var training=Fresh();training.IsTraining=true;
  Check(!training.AreBattlePlayerDatasReady(),"training waits for initialized local data");
  var trainee=Add(training,1,1);
  Check(training.RequiredPlayerCount==1&&training.AreBattlePlayerDatasReady(),"solo training does not wait for an enemy");
  trainee.IsLoadoutInitialized=false;Check(!training.AreBattlePlayerDatasReady(),"training waits for loadout");trainee.IsLoadoutInitialized=true;
  training.Runner.SessionInfo.IsOpen=false;training.RefreshRoomAdmission();Check(!training.Runner.SessionInfo.IsOpen,"training never opens session admission");
  Add(training,2,2);Check(!training.AreBattlePlayerDatasReady(),"training rejects a second participant");
  Check(BattleMatchRules.PlayerCount(BattleMatchMode.OneVsOne)==2&&BattleMatchRules.PlayerCount(BattleMatchMode.TwoVsTwo)==4,"supported sizes");
  var m=Fresh();var host=Add(m,1,1);Check(m.RequiredPlayerCount==4,"new code room defaults to 2v2");
  Check(m.TrySetRoomMatchMode(0)&&host.RoomMatchMode==BattleMatchMode.OneVsOne&&m.RequiredPlayerCount==2,"host changes replicated mode");
  var guest=Add(m,2,2);m.RefreshRoomAdmission();Check(!m.Runner.SessionInfo.IsOpen&&m.AreBattlePlayerDatasReady(),"full 1v1 closes admission and can start");
  m.IsRoomHost=false;Check(!m.TrySetRoomMatchMode(1)&&m.RequiredPlayerCount==2,"guest cannot change mode");
  m.IsRoomHost=true;Check(m.TrySetRoomMatchMode(1)&&m.RequiredPlayerCount==4&&m.Runner.SessionInfo.IsOpen,"host expands full 1v1 without reconnecting");
  Check(!m.AreBattlePlayerDatasReady(),"two participants cannot launch a 2v2");
  var ally=Add(m,3,1);Check(!m.TrySetRoomMatchMode(0)&&m.RequiredPlayerCount==4,"no shrinking under three players");
  var enemy=Add(m,4,2);m.RefreshRoomAdmission();Check(!m.Runner.SessionInfo.IsOpen&&m.AreBattlePlayerDatasReady(),"balanced four-player room ready");
  enemy.IsLoadoutInitialized=false;Check(!m.AreBattlePlayerDatasReady(),"fourth loadout must finish");enemy.IsLoadoutInitialized=true;
  enemy.teamIndex=1;Check(!m.AreBattlePlayerDatasReady(),"3v1 roster cannot launch");enemy.teamIndex=2;
  m.IsWaitingInCodeRoom=false;Check(!m.TrySetRoomMatchMode(0),"cannot change after room phase");m.IsWaitingInCodeRoom=true;
  m.IsRandomMatch=true;host.RoomMatchMode=BattleMatchMode.OneVsOne;Check(m.RequiredPlayerCount==4,"random is always 2v2");m.IsRandomMatch=false;host.RoomMatchMode=BattleMatchMode.TwoVsTwo;
  var client=new NetworkGameManager{_runner=m.Runner,IsRoomHost=false,roomMatchMode=BattleMatchMode.OneVsOne};Check(client.RequiredPlayerCount==4,"client reads host snapshot instead of own inspector default");
  var roster=new List<PlayerData>();m.Runner.ActivePlayers.Reverse();
  for(int id=1;id<=4;id++){
   m.Runner.LocalPlayer=id;BattleMatchRules.CollectRoster(m.Runner,roster);
   Check(roster.Count==4&&roster[0].Object.InputAuthority==(PlayerRef)id,"self first regardless of enumeration "+id);
   Check(roster[1].teamIndex==roster[0].teamIndex&&roster[2].teamIndex!=roster[0].teamIndex&&roster[3].teamIndex!=roster[0].teamIndex,"own team left, enemies right "+id);
   Check(BattleMatchRules.IsFullBalanced(roster,4),"balanced roster "+id);
  }
  m.Runner.LocalPlayer=1;m.Runner.ActivePlayers.Remove(2);m.RefreshRoomAdmission();Check(m.Runner.SessionInfo.IsOpen,"leave reopens queue");
  BattleMatchRules.CollectRoster(m.Runner,roster);Check(roster.Count==3&&!BattleMatchRules.IsFullBalanced(roster,4),"stale departed data not shown or counted");
  m.Runner.ActivePlayers.Remove(4);m.assignedTeamIndexes.Remove(2);m.assignedTeamIndexes.Remove(4);
  Check(m.TrySetRoomMatchMode(0)&&host.teamIndex==1&&ally.teamIndex==2&&m.AreBattlePlayerDatasReady(),"shrink rebalances two same-team survivors");
  m.IsMatching=false;m.IsRoomHost=false;m._runner=null;Check(m.TrySetRoomMatchMode(1)&&m.CurrentMatchMode==BattleMatchMode.TwoVsTwo,"pre-room choice supported");
  Check(!m.TrySetRoomMatchMode(5),"invalid dropdown choice rejected");
  m=Fresh();host=Add(m,1,1);guest=Add(m,2,2);ally=Add(m,3,1);enemy=Add(m,4,2);m.IsRandomMatch=true;
  host.RPC_ConfirmLobbyProfilePreview(3,3,1);Check(host.HasPresentedLobbyOpponent(ally),"teammate preview accepted");
  host.RPC_ConfirmLobbyProfilePreview(2,99,1);Check(!host.HasPresentedLobbyOpponent(guest),"wrong profile acknowledgement rejected");
  host.RPC_ConfirmLobbyProfilePreview(2,2,1);Check(host.HasPresentedLobbyOpponent(guest),"enemy preview accepted");
  ally.playerprofile=8;Check(!host.HasPresentedLobbyOpponent(ally),"profile change invalidates preview");ally.playerprofile=3;
  host.teamIndex=2;Check(!host.HasPresentedLobbyOpponent(ally),"own team change invalidates preview");host.teamIndex=1;
  guest.Object.IsValid=false;Check(!host.HasPresentedLobbyOpponent(guest),"departed object cannot stay acknowledged");guest.Object.IsValid=true;
  host.RPC_ClearLobbyProfilePreview();Check(!host.HasPresentedLobbyOpponent(ally),"hidden UI clears every preview");
  guest.SetRoomMatchMode(BattleMatchMode.OneVsOne);Check(guest.RoomMatchMode==BattleMatchMode.TwoVsTwo,"non-owner cannot set room settings");
  host.Object.HasStateAuthority=false;host.SetRoomMatchMode(BattleMatchMode.OneVsOne);Check(host.RoomMatchMode==BattleMatchMode.TwoVsTwo,"proxy cannot write room settings");host.Object.HasStateAuthority=true;
  var b=new BattleProbe{runner=m.Runner,playerDatas=m.playerDatas};
  foreach(var pair in m.playerDatas)b.SpawnBattlePlayer(pair.Key,pair.Value,default);
  Check(b.spawnedPlayers.Count==4,"all players spawn");
  Check(b.spawnedPlayers[1].transform.position.x==0&&b.spawnedPlayers[3].transform.position.x==3,"team A near same anchor, separated");
  Check(b.spawnedPlayers[2].transform.position.x==100&&b.spawnedPlayers[4].transform.position.x==97,"team B uses its own anchor orientation");
  b.spawnedPlayers.Remove(3);b.SpawnBattlePlayer(3,ally,default,true);Check(b.spawnedPlayers[3].transform.position.x==3&&b.spawnedPlayers[3].Pilot.Respawn,"respawn keeps team placement");
  b.playerSpawnA2=new(){position=new(0,2,8)};b.spawnedPlayers.Remove(3);b.SpawnBattlePlayer(3,ally,default);
  Check(b.spawnedPlayers[3].transform.position.y==2&&b.spawnedPlayers[3].transform.position.z==8,"explicit secondary spawn overrides spacing");
  b.PlayerLeft(3);Check(b.battleFlag.Winner==2,"leaving ally does not award victory to its own teammate");
  m=Fresh();host=Add(m,1,1);guest=Add(m,2,2);
  guest.RequestWaitingTeam(1);
  Check(guest.teamIndex==1&&guest.camp==Camp.A&&m.assignedTeamIndexes[2]==1,"guest owner RPC joins team A and updates reservation/camp");
  Check(m.TrySetRoomMatchMode(1)&&guest.teamIndex==1,"re-selecting same mode preserves manual teams");
  Check(m.NextTeamIndex()==2,"two A players automatically assign new arrivals to B");
  ally=Add(m,3,m.NextTeamIndex());
  Check(ally.teamIndex==2&&!m.TrySetWaitingTeam(ally,1)&&ally.teamIndex==2,"A full: new B player cannot switch to A");
  Check(m.TrySetWaitingTeam(host,1),"current team remains selectable at capacity");
  Check(m.TrySetWaitingTeam(host,2)&&host.camp==Camp.B&&m.NextTeamIndex()==1,"B fills and subsequent arrival goes to A");
  Check(!m.TrySetWaitingTeam(guest,2)&&guest.teamIndex==1,"B capacity is symmetric");
  m=Fresh();host=Add(m,1,1);guest=Add(m,2,2);ally=Add(m,3,2);
  Check(m.TrySetWaitingTeam(guest,1),"first request wins last team A place");
  Check(!m.TrySetWaitingTeam(ally,1)&&ally.teamIndex==2,"second concurrent/stale request rejected without displacing anyone");
  m.Runner.ActivePlayers.Add(4);m.assignedTeamIndexes[4]=2;
  Check(!m.TrySetWaitingTeam(host,2),"joining participant reservation counts before PlayerData exists");
  m.Runner.ActivePlayers.Remove(4);
  Check(m.TrySetWaitingTeam(host,2),"departed reservation no longer blocks a free place");
  m.IsRoomHost=false;Check(!m.TrySetWaitingTeam(guest,2),"client cannot bypass host authority");m.IsRoomHost=true;
  m.IsWaitingInCodeRoom=false;Check(!m.TrySetWaitingTeam(guest,2),"no switching during loading, battle, random or cancelled session");m.IsWaitingInCodeRoom=true;
  host.RoomMatchMode=BattleMatchMode.OneVsOne;Check(!m.TrySetWaitingTeam(guest,2),"1v1 team selection forbidden");host.RoomMatchMode=BattleMatchMode.TwoVsTwo;
  Check(!m.TrySetWaitingTeam(guest,0)&&!m.TrySetWaitingTeam(guest,3),"invalid team IDs rejected");
  var forged=new PlayerData{Runner=m.Runner,teamIndex=1};forged.Object.InputAuthority=2;
  Check(!m.TrySetWaitingTeam(forged,1),"must be registered requester object");
  guest.IsLoadoutInitialized=false;Check(!m.TrySetWaitingTeam(guest,1),"uninitialized requester rejected");guest.IsLoadoutInitialized=true;
  guest.Object.HasStateAuthority=false;Check(!m.TrySetWaitingTeam(guest,1),"proxy PlayerData cannot write team");guest.Object.HasStateAuthority=true;
  guest.Runner=new();Check(!m.TrySetWaitingTeam(guest,1),"foreign runner rejected");guest.Runner=m.Runner;
  m.Runner.ActivePlayers.Remove(2);Check(!m.TrySetWaitingTeam(guest,1),"departed requester rejected");m.Runner.ActivePlayers.Add(2);
  m=Fresh();host=Add(m,1,1);guest=Add(m,2,2);guest.Object.HasInputAuthority=false;guest.RequestWaitingTeam(1);
  Check(guest.teamIndex==2,"local UI cannot request another participant's team");
  guest.Object.HasInputAuthority=true;guest.RequestWaitingTeam(0);Check(guest.teamIndex==2,"owner wrapper filters invalid input");
  return n;
 }
}
'@
Add-Type -TypeDefinition $source
"Team mode, host authority, roster order, readiness, preview and spawn checks passed: $([Checks]::Run())"

$random=Get-Content Assets/Script/RandomMatchUI.cs -Raw
$tab=Get-Content Assets/Script/BattlePerkUI.cs -Raw
$intro=Get-Content Assets/Script/BattleIntroPresentation.cs -Raw
$teams=Get-Content Assets/Script/BattleIntroPresentation.Teams.cs -Raw
$settings=Get-Content Assets/Script/RoomMatchSettingsUI.cs -Raw
foreach($contract in @(
 @($network,'private const int RandomPlayerCount = 4;'),
 @($network,'WitchFlight-Random-2v2-v1'),
 @($network,'ConnectedPlayerCount >= RequiredPlayerCount'),
 @($network,'assignedTeamIndexes.Count >= RequiredPlayerCount'),
 @($settings,'TMP_DefaultControls.CreateDropdown'),
 @($settings,'modeDropdown.interactable = manager.IsRoomHost;'),
 @($settings,'boundDropdown.onValueChanged.AddListener(SelectMode);'),
 @($random,'ShowParticipant(teammateData'),
 @($random,'ShowParticipant(secondOpponentData'),
 @($tab,'Show(teamComparisonRoot,compare && teamBattle);'),
 @($tab,'Input.GetKey(KeyCode.Tab)'),
 @($teams,'phase == BattleStartPhase.WaitingForPlayers && teamSnapshots[0].IsReady && teamSnapshots[1].IsReady'),
 @($teams,'EnsureTeamSnapshot(ref teamSnapshots[0], teamPlayers[0], teamPlayers[1], 0, localPortrait)'),
 @($teams,'EnsureTeamSnapshot(ref teamSnapshots[1], teamPlayers[2], teamPlayers[3], 1, opponentPortrait)'),
 @($intro,'ReleaseSnapshot(ref teamSnapshots[i]);'),
 @($intro,'foreach (PortraitSnapshot snapshot in teamSnapshots) FreezeCompletedSnapshot(snapshot);')
)) {if(!$contract[0].Contains($contract[1])) {throw "Missing integration: $($contract[1])"}}
$main=Get-Content Assets/Scenes/Main.unity -Raw
if(!$main.Contains('roomMatchMode: 4') -or !$main.Contains('component: {fileID: 244481548}') -or !$main.Contains('guid: 045445890bfb427480029a3aca1a8bb8')){throw 'Main scene mode/dropdown controller binding missing'}
'Team matching, dropdown, two-camera team intro and held-Tab integration contracts passed: 17'

foreach ($contract in @(
 @($teams,'new PortraitSnapshot[2]'),
 @($teams,'"PlayerProfile (1)"'),
 @($teams,'"EnemyProfile (1)"'),
 @($teams,'child.name.Trim()'),
 @($teams,'secondVisual.SetParent(snapshot.Stage.transform, false)'),
 @($teams,'snapshot.OwnedMeshes.AddRange(companion.OwnedMeshes)'),
 @($teams,'companion.OwnedMeshes.Clear()'),
 @($teams,'ReleaseSnapshot(ref companion)'),
 @($intro,'if (countdown) EnsureCountdownText();'),
 @($intro,'TMP_Text activeCountdown = countdownText;')
)) {if(!$contract[0].Contains($contract[1])){throw "Missing authored VS integration: $($contract[1])"}}
if($teams.Contains('Generated Team VS UI') -or $teams.Contains('TeamVSPanel')){throw 'Obsolete generated 2v2 panel must not replace authored VSUI'}
if($teams -match 'teamPlayers\[[^]]+\]\.transform\.|first\.transform\.|second\.transform\.|Teleport\('){throw 'Team framing must not move network gameplay actors'}
'Authored VS profiles, two shared cameras, mesh ownership, spawn preservation and common countdown contracts passed.'

$teamUI=Get-Content Assets/Script/RoomMatchSettingsUI.Teams.cs -Raw
$waiting=Get-Content Assets/Script/linkuserinfo.WaitingRoom.cs -Raw
$bridge=Get-Content Assets/Script/linkuserinfo.cs -Raw
foreach($contract in @(
 @($data,'[Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]'),
 @($data,'NetworkGameManager.Instance?.TrySetWaitingTeam(this, team);'),
 @($teamUI,'GetCodeWaitingRoomProfile(local.Object.InputAuthority)'),
 @($teamUI,'rect.SetParent(anchor, false)'),
 @($teamUI,'option.interactable = allowTeamA'),
 @($teamUI,'option.interactable = allowTeamB'),
 @($teamUI,'teamDropdown.onValueChanged.AddListener(SelectTeam);'),
 @($teamUI,'teamDropdown.SetValueWithoutNotify(local.teamIndex == 1 ? 0 : 1);'),
 @($waiting,'waitingRoomPanel.transform.Find("Card0" + (i + 1))'),
 @($waiting,'waitingParticipants.IndexOf(player)'),
 @($bridge,'slot.slotRoot.SetActive(i < 2 || (waiting && manager.RequiredPlayerCount == 4))')
)) {if(!$contract[0].Contains($contract[1])) {throw "Missing team selection integration: $($contract[1])"}}
if($waiting -match 'new GameObject\("Card' -or $teamUI -match 'new GameObject\("Card') {throw 'Reuse existing cards; do not generate a replacement roster'}
$prefab=Get-Content Assets/UI/Prefab/Multi.prefab -Raw
foreach($pair in @(@('6090647181730269947','Card03'),@('6162534207457792003','Card04'))) {
 $block=[regex]::Match($prefab,'(?ms)^--- !u!1 &'+$pair[0]+'\r?\n.*?(?=^--- !u!|\z)').Value
 if(!$block.Contains('m_Name: '+$pair[1]) -or !$block.Contains('m_IsActive: 0')) {throw 'Authored inactive card changed unexpectedly'}
}
'Team selection authority, existing Card03/04 reuse, local-only dropdown and full-team option contracts passed.'
