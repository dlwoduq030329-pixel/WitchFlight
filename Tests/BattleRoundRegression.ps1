# Actual round transitions and loadout validation with small Unity/Fusion doubles.
# Does not replace an end-to-end two-client Unity test.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$flag = Get-Content Assets/Script/BattleFlag.cs -Raw
$manager = Get-Content Assets/Script/vibe/BattleManager.cs -Raw
$kill = Get-Content Assets/Script/BattleKillFeed.cs -Raw
$rules = Get-Content Assets/Script/BattleRoundRules.cs -Raw
$enums = [regex]::Match((Get-Content Assets/Script/PlayerData.cs -Raw), 'public enum MagicType\s*\{[^}]+\}').Value +
    "`n" + [regex]::Match($flag, 'public enum BattleStartPhase\s*\{[^}]+\}').Value
function Method([string]$source, [string]$name) {
    $m = [regex]::Match($source, '(?ms)^    (?:public|private) (?:static |override )?[\w<>]+ '+$name+'\([^{}]*?\)\r?\n    \{.*?^    \}')
    if (!$m.Success) { throw "Missing $name" }
    return ($m.Value -replace '^    private ', '    public ') -replace 'public override ', 'public '
}
$canChange = [regex]::Match($flag, '(?ms)^    public bool CanChangeMagic\([^)]*\) =>.*?;').Value
$stubs = @'
using System;
using System.Collections.Generic;
namespace RoundChecks {
public enum PlayerRef {None,A,B}
public class NetworkObject {public bool IsValid=true,HasStateAuthority=true;public PlayerRef InputAuthority=PlayerRef.A;}
public class NetworkRunner {public bool IsServer=true;public float Time;}
public struct TickTimer {
 public float End;public bool IsRunning;public static TickTimer None=>default;
 public static TickTimer CreateFromSeconds(NetworkRunner r,float seconds)=>new(){End=r.Time+seconds,IsRunning=true};
 public bool Expired(NetworkRunner r)=>IsRunning&&r.Time>=End;
}
public class NetworkArray<T> {T[] a=new T[16];public int Length=>a.Length;public T this[int i]=>a[i];public void Set(int i,T v)=>a[i]=v;}
public struct Vector3 {public float x,y;public Vector3(float value){x=value;y=0;}}
public struct Vector2 {public float x,y;public Vector2(float a,float b){x=a;y=b;}}
public class Transform {public Vector3 position;public Vector3 TransformPoint(Vector3 p)=>p;}
public static class Mathf {
 public static int Max(int a,int b)=>Math.Max(a,b);public static float Max(float a,float b)=>Math.Max(a,b);
 public static int Clamp(int a,int b,int c)=>Math.Clamp(a,b,c);public static float Clamp01(float a)=>Math.Clamp(a,0,1);
 public static float Lerp(float a,float b,float t)=>a+(b-a)*Clamp01(t);public static float Pow(float a,float b)=>(float)Math.Pow(a,b);
}
public class Player {public bool IsAlive=true,DiedFromAltitude;public Transform transform=new();}
public class PlayerData {
 public NetworkRunner Runner;public NetworkObject Object=new();public bool IsLoadoutInitialized=true;
 public int MagicChangeRound=1;public MagicType MagicChangeBase1=MagicType.Fire,MagicChangeBase2=MagicType.Ice;
 public MagicType magic1=MagicType.Fire,magic2=MagicType.Ice;
}
public class BattleFlag {
 public NetworkRunner Runner=new();public NetworkObject Object=new();public Transform transform=new();
 public bool IsPrepared,HasEnded,HasStarted,IsDescending,IsOvertime,startRequested;
 public bool AllReady=true;public int PickupTeam;
 public float initialFlagHeight,introSeconds,matchSeconds,magicChangeSeconds;
 public Vector3 WorldPosition,roundFlagPosition,carryOffset;
 public int ExpectedPlayerCount,RoundNumber,AllowedMagicChanges,Team1Wins,Team2Wins,LastRoundWinner,LastCarrierTeam,WinningTeam,CarrierObjectId;
 public const int WinsRequired=2;
 public PlayerRef Carrier,LastCarrier;public BattleStartPhase Phase;public TickTimer MatchTimer,PhaseTimer;
 public HashSet<PlayerRef> readyPlayers=new();public Dictionary<int,int> previousPositions=new();
 public NetworkArray<int> SmokeClouds=new();
 public bool AreAllPlayersReady()=>AllReady;
 public Player GetCarrierPlayer()=>null;
 public void DropCarrier(PlayerRef p,Vector3 v,bool b){Carrier=PlayerRef.None;}
 public void UpdateDescent(){}
 public void CheckPickupAndRecordPositions(){if(PickupTeam>0)LastCarrierTeam=PickupTeam;}
'@
$flagMethods = @('PrepareMatch','RequestStart','ResetPreparation','TickPreparation','BeginMatch','FixedUpdateNetwork',
    'FinishRound','PrepareNextRound','EndSeriesByForfeit','EndSeries','ClearRoundSmoke') | ForEach-Object { Method $flag $_ }
$managerStub = @'
}
public class BattleManager {
 public static BattleManager Instance;
 public NetworkRunner runner;public BattleFlag battleFlag;
 public bool AutoStartWhenReady=true,Connected=true;
 public bool HasValidFlag=>battleFlag!=null&&battleFlag.Object.IsValid;
 public int Changes,Resets,Results;public Dictionary<PlayerRef,PlayerData> playerDatas=new();
 public void startGame()=>battleFlag.RequestStart();
 public void TickRespawns(){}
 public void BeginMagicChange(){Changes++;}
 public void ResetPlayersForNextRound(){Resets++;}
 public void NotifyBattleEnded(int team){Results++;}
 public bool IsConnected(PlayerRef p)=>Connected;
'@
$tests = @'
}
public static class KillAnimation {
'@
$run = @'
}
public static class Tests {
 static int count;static void Check(bool b,string m){if(!b)throw new Exception(m);count++;}
 static BattleFlag New(int limit=2){var f=new BattleFlag();BattleManager.Instance=new(){runner=f.Runner,battleFlag=f};f.PrepareMatch(new Vector3(50),10,2,5,40,limit);return f;}
 static void Start(BattleFlag f){f.FixedUpdateNetwork();Check(f.Phase==BattleStartPhase.Intro,"ready begins VS");f.Runner.Time=f.PhaseTimer.End;f.FixedUpdateNetwork();Check(f.Phase==BattleStartPhase.Countdown,"VS completes into shared countdown");f.Runner.Time=f.PhaseTimer.End;f.FixedUpdateNetwork();Check(f.Phase==BattleStartPhase.Playing,"countdown starts round");}
 static void Win(BattleFlag f,int team){f.LastCarrierTeam=team;f.Runner.Time=f.MatchTimer.End;f.FixedUpdateNetwork();}
 public static int Run(){
  foreach(var outcomes in new[]{new[]{1,1},new[]{2,2},new[]{1,2,1},new[]{2,1,2}}){
   var f=New();Start(f);
   for(int i=0;i<outcomes.Length;i++){
    f.SmokeClouds.Set(0,1);Win(f,outcomes[i]);
    Check(f.SmokeClouds[0]==0,"old round smoke cleared");
    int total=f.Team1Wins+f.Team2Wins;f.FixedUpdateNetwork();Check(f.Team1Wins+f.Team2Wins==total,"result cannot score twice");
    if(i==outcomes.Length-1){
     Check(f.HasEnded&&f.WinningTeam==outcomes[i]&&f.Phase==BattleStartPhase.Ended,"2 wins ends series");
     Check(f.RoundNumber==outcomes.Length&&BattleManager.Instance.Results==1,"correct round count and one result event");
     Check(!f.CanChangeMagic(f.RoundNumber),"no changes after final result");
    }else{
     Check(!f.HasEnded&&f.Phase==BattleStartPhase.Intermission,"nonfinal result opens intermission");
     Check(f.PhaseTimer.End-f.Runner.Time==40&&f.CanChangeMagic(f.RoundNumber),"server owns full 40 second change window");
     f.PrepareNextRound();Check(f.RoundNumber==i+1,"cannot skip change timer");
     f.Runner.Time=f.PhaseTimer.End;Check(!f.CanChangeMagic(f.RoundNumber),"changes close at exact deadline");f.FixedUpdateNetwork();
     Check(f.RoundNumber==i+2&&f.Phase==BattleStartPhase.WaitingForPlayers&&f.WorldPosition.x==50,"reset flag and advance round");
     Check(BattleManager.Instance.Resets==i+1,"respawn players exactly once per round");
     f.AllReady=false;f.FixedUpdateNetwork();Check(f.Phase==BattleStartPhase.WaitingForPlayers,"new round must wait for fresh client readiness");f.AllReady=true;Start(f);
    }
   }
  }
  var overtime=New();Start(overtime);overtime.Runner.Time=overtime.MatchTimer.End;overtime.FixedUpdateNetwork();
  Check(overtime.IsOvertime&&overtime.Phase==BattleStartPhase.Playing,"unclaimed flag extends round instead of scoring a draw");
  overtime.PickupTeam=2;overtime.FixedUpdateNetwork();Check(overtime.Team2Wins==1&&overtime.Phase==BattleStartPhase.Intermission,"first overtime pickup wins");
  var proxy=New();proxy.Object.HasStateAuthority=false;proxy.FinishRound(1);proxy.EndSeriesByForfeit(2);Check(proxy.Team1Wins+proxy.Team2Wins==0&&!proxy.HasEnded,"clients cannot award wins");
  var forfeit=New();forfeit.EndSeriesByForfeit(2);forfeit.EndSeriesByForfeit(1);Check(forfeit.Team2Wins==2&&forfeit.WinningTeam==2&&BattleManager.Instance.Results==1,"disconnect ends once with remaining team winning");
  var limited=New(1);Start(limited);Win(limited,1);var manager=BattleManager.Instance;var data=new PlayerData{Runner=limited.Runner};manager.playerDatas[PlayerRef.A]=data;
  Check(manager.TryChangeRoundMagic(data,1,MagicType.Vision,MagicType.Ice,out _),"one slot may change");
  Check(manager.TryChangeRoundMagic(data,1,MagicType.Thunder,MagicType.Ice,out _),"same slot may be selected again");
  Check(!manager.TryChangeRoundMagic(data,1,MagicType.Thunder,MagicType.Flare,out _)&&data.magic2==MagicType.Ice,"second changed slot rejected without mutation");
  Check(manager.TryChangeRoundMagic(data,1,MagicType.Fire,MagicType.Ice,out _)&&manager.TryChangeRoundMagic(data,1,MagicType.Fire,MagicType.Smoke,out _),"restore baseline then change other slot");
  Check(!manager.TryChangeRoundMagic(data,0,MagicType.Fire,MagicType.Ice,out _),"old round request rejected");
  Check(!manager.TryChangeRoundMagic(data,1,(MagicType)99,MagicType.Ice,out _),"invalid enum rejected");
  var impostor=new PlayerData{Runner=limited.Runner};Check(!manager.TryChangeRoundMagic(impostor,1,MagicType.Fire,MagicType.Ice,out _),"unregistered object cannot change another player");
  limited.AllowedMagicChanges=2;Check(manager.TryChangeRoundMagic(data,1,MagicType.Flare,MagicType.Smoke,out _),"two-slot setting accepts both choices");
  manager.Connected=false;Check(!manager.TryChangeRoundMagic(data,1,MagicType.Fire,MagicType.Ice,out _),"disconnected owner rejected");manager.Connected=true;
  limited.Runner.Time=limited.PhaseTimer.End;Check(!manager.TryChangeRoundMagic(data,1,MagicType.Fire,MagicType.Ice,out _),"late packets cannot equip after deadline");
  var start=KillAnimation.EvaluateAnimation(0,.12f,.75f,.75f,1.2f,.6f);
  var peak=KillAnimation.EvaluateAnimation(.12f,.12f,.75f,.75f,1.2f,.6f);
  var mid=KillAnimation.EvaluateAnimation(.495f,.12f,.75f,.75f,1.2f,.6f);
  var end=KillAnimation.EvaluateAnimation(.88f,.12f,.75f,.75f,1.2f,.6f);
  Check(start.x==.75f&&start.y==1&&peak.x==1.2f&&peak.y==1,"kill card pops to peak while opaque");
  Check(mid.x<peak.x&&mid.x>end.x&&Math.Abs(mid.y-.5f)<.01f,"kill card shrinks and fades simultaneously");
  Check(end.y==0&&Math.Abs(end.x-.6f)<.01f,"kill card ends small and transparent");
  return count;
 }
}}
'@
Add-Type -TypeDefinition ($stubs + $canChange + ($flagMethods -join "`n") + $managerStub +
    (Method $manager 'TryChangeRoundMagic') + $tests + (Method $kill 'EvaluateAnimation') + $run +
    "`nnamespace RoundChecks {`n$enums`n$rules`n}")
"Round state, loadout authority and kill animation checks passed: $([RoundChecks.Tests]::Run())"

$scene = Get-Content Assets/Scenes/Battle.unity -Raw
$ids = [regex]::Matches($scene,'(?m)^--- !u!\d+ &(\d+)') | ForEach-Object {$_.Groups[1].Value}
if (@($ids | Group-Object | Where-Object Count -gt 1).Count) { throw 'Duplicate scene IDs' }
foreach ($id in 'e4e9841248b04c1f826b6c88951e16ab','bd529f4d63724b49b2e653f746173a17') {
    if (!$scene.Contains($id)) { throw "Missing UI component: $id" }
}
if (!$scene.Contains('magicChangeDurationSeconds: 40') -or !$scene.Contains('maxMagicChangesPerRound: 2')) { throw 'Round settings not saved' }
$mana = Get-Content Assets/Resources/MagicStatTable.asset -Raw
$expected = @(60,36,16,80,20,50,0,0,50,0,24,40)
$entries = [regex]::Matches($mana, '(?ms)^  - magic: (\d+)\r?\n(.*?)(?=^  - magic:|\z)')
foreach ($entry in $entries) {
    $id = [int]$entry.Groups[1].Value
    $cost = [float][regex]::Match($entry.Groups[2].Value, '(?m)^    apCost: ([\d.]+)').Groups[1].Value
    if ($cost -ne $expected[$id-1]) { throw "Incorrect doubled MP cost for spell $id" }
}
if (!$mana.Contains('parryApCost: 16') -or !$mana.Contains('maxApFractionPerSecond: 0.5') -or !$mana.Contains('apPerSecond: 40')) { throw 'Sustained/parry costs not doubled' }
$equipment = Get-Content Assets/Resources/EquipmentStatTable.asset -Raw
$recovery = [regex]::Matches($equipment, 'apRecoveryPerSecond: ([\d.]+)') | ForEach-Object { $_.Groups[1].Value }
if (($recovery -join ',') -ne '5,3,6,9') { throw 'Hat recovery must be halved' }
if ([regex]::Matches($equipment,'boostApCostPerSecond: 40').Count -ne 4) { throw 'All boost costs must double' }
$profile = Get-Content Assets/Script/PlayerData.cs -Raw
if ($profile -notmatch '(?s)RpcSources.InputAuthority, RpcTargets.StateAuthority.*RPC_ChangeRoundMagic' -or
    $flag -notmatch '(?s)RpcSources.StateAuthority, RpcTargets.All, Channel = RpcChannel.Reliable.*RPC_AnnounceKill') { throw 'RPC authority contracts missing' }
if ((Method $manager 'TryChangeRoundMagic') -match 'DataConfig|Backend') { throw 'Round loadout must not overwrite account customization' }
$death = Method $manager 'PlayerKilled'
if ($death.IndexOf('pending.PlayerRef == deadPlayer') -gt $death.IndexOf('AnnounceKill') -or
    $death -notmatch 'killer\.playerName\.ToString\(\)' -or $death -notmatch 'killer\.playerprofile' -or
    $death -notmatch 'victim\.playerName\.ToString\(\)' -or $death -notmatch 'victim\.playerprofile') {
    throw 'Kill notice must reject duplicate deaths and snapshot both names and profile IDs'
}
foreach ($field in 'killPanel','killerNameText','victimNameText','killerProfileImage','victimProfileImage') {
    if ($kill -notmatch ('\[SerializeField\] private \w+ ' + $field + ';')) { throw "Missing custom kill UI field $field" }
}
if ($kill -notmatch 'pending.Count >= 8' -or $kill -notmatch 'Time.unscaledTime' -or
    $kill -notmatch 'profileTable.GetSprite\(id\)') { throw 'Kill queue, unscaled animation or shared profile lookup missing' }
$reset = Method $manager 'ResetPlayersForNextRound'
if ($reset -notmatch 'pendingRespawns.Clear\(\)' -or $reset -notmatch 'runner.Despawn\(old\)' -or
    $reset -notmatch 'SpawnBattlePlayers\(playerPrefab\)') { throw 'Round reset must discard old players and respawn from current loadout' }
'Saved round UI, mana balance and RPC contracts passed.'

# Exercise the production HUD binding without a living Player or a gameplay-only UI phase.
$hud = Get-Content Assets/Script/BattleHud.cs -Raw
$hudStubs = @'
using System;
namespace RoundHudChecks {
public class NetworkObject {public bool IsValid=true,HasInputAuthority=true;}
public class PlayerData {public static PlayerData Local;public NetworkObject Object=new();public bool IsLoadoutInitialized=true;public int teamIndex;}
public class BattleFlag {public static BattleFlag Instance;public NetworkObject Object=new();public int Team1Wins,Team2Wins;}
public class TextMeshProUGUI {string value="placeholder";public int Writes;public string text {get=>value;set{this.value=value;Writes++;}}}
public class Hud {
 public TextMeshProUGUI myRoundWinsText=new(),enemyRoundWinsText=new();
'@
$hudTests = @'
}
public static class Tests {
 static int checks;static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
 public static int Run(){
  var hud=new Hud();hud.BindRoundScores();Check(hud.myRoundWinsText.text=="0"&&hud.enemyRoundWinsText.text=="0","initial HUD contains digits only");
  PlayerData.Local=new(){teamIndex=1};BattleFlag.Instance=new(){Team1Wins=1,Team2Wins=0};
  hud.BindRoundScores();Check(hud.myRoundWinsText.text=="1"&&hud.enemyRoundWinsText.text=="0","team 1 sees its own first win");
  int writes=hud.myRoundWinsText.Writes+hud.enemyRoundWinsText.Writes;
  for(int i=0;i<120;i++)hud.BindRoundScores();
  Check(writes==hud.myRoundWinsText.Writes+hud.enemyRoundWinsText.Writes,"unchanged score does not rewrite TMP labels");
  PlayerData.Local.teamIndex=2;hud.BindRoundScores();Check(hud.myRoundWinsText.text=="0"&&hud.enemyRoundWinsText.text=="1","team 2 sees reversed local-relative score");
  BattleFlag.Instance.Team1Wins=2;hud.BindRoundScores();Check(hud.myRoundWinsText.text=="0"&&hud.enemyRoundWinsText.text=="2","opponent final win updates without a character instance");
  PlayerData.Local.teamIndex=1;BattleFlag.Instance.Team2Wins=1;hud.BindRoundScores();Check(hud.myRoundWinsText.text=="2"&&hud.enemyRoundWinsText.text=="1","2 to 1 final score remains numeric");
  Check(BattleFlag.Instance.Team1Wins==2&&BattleFlag.Instance.Team2Wins==1,"HUD never mutates authoritative scores");
  PlayerData.Local.teamIndex=0;hud.BindRoundScores();Check(hud.myRoundWinsText.text=="0"&&hud.enemyRoundWinsText.text=="0","unassigned team cannot show the wrong side");
  PlayerData.Local.teamIndex=1;PlayerData.Local.Object.HasInputAuthority=false;hud.BindRoundScores();Check(hud.myRoundWinsText.text=="0","nonlocal data cannot be presented as mine");
  PlayerData.Local.Object.HasInputAuthority=true;PlayerData.Local.Object.IsValid=false;hud.BindRoundScores();Check(hud.myRoundWinsText.text=="0","invalid data is safe");
  PlayerData.Local.Object.IsValid=true;PlayerData.Local.IsLoadoutInitialized=false;hud.BindRoundScores();Check(hud.myRoundWinsText.text=="0","uninitialized data is safe");
  PlayerData.Local.IsLoadoutInitialized=true;BattleFlag.Instance.Object.IsValid=false;hud.BindRoundScores();Check(hud.myRoundWinsText.text=="0","despawned flag is safe");
  hud.myRoundWinsText=null;hud.enemyRoundWinsText=null;hud.BindRoundScores();Check(true,"optional unassigned UI fields are safe");
  return checks;
 }
}}
'@
Add-Type -TypeDefinition ($hudStubs + (Method $hud 'BindRoundScores') + (Method $hud 'SetRoundWinsText') + $hudTests)
"Local/opponent numeric round HUD checks passed: $([RoundHudChecks.Tests]::Run())"
foreach ($field in 'myRoundWinsText','enemyRoundWinsText') {
    if ($hud -notmatch ('\[SerializeField\] private TextMeshProUGUI ' + $field + ';') -or !$scene.Contains($field + ':')) {
        throw "Missing serialized score UI field $field"
    }
}
if ($hud.IndexOf('BindRoundScores();') -gt $hud.IndexOf('if (!show)')) { throw 'Round scores must refresh before non-gameplay early return' }
