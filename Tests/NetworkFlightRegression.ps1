# Offline contract/behaviour checks, using method bodies from the production scripts.
# Run in a fresh PowerShell 7 process: pwsh -NoProfile -File Tests/NetworkFlightRegression.ps1
# Does not emulate Fusion transport, Unity collision resolution, or actual rendered frames.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$playerSource = Get-Content (Join-Path $repo 'Assets/Script/Player.cs') -Raw
function Method([string]$name) {
    $m = [regex]::Match($playerSource, '(?ms)^    private [^\r\n]+\b' + $name + '\([^\r\n]*\)\r?\n    \{.*?^    \}')
    if (!$m.Success) { throw "Missing production method: $name" }
    return $m.Value
}
$methods = @('ProcessInput','ContinueWithoutActions','SelectMagicSlot','UpdateSpeed',
    'GetMovementMultiplier','GetFlagSpeedMultiplier','GetTurnMultiplier','UpdateHeldBoost','TryConsumeMovementAp','RegenerateAp','TimerIsActive') |
    ForEach-Object { Method $_ }
$source = @'
using System;
using NetworkRunner=FlightChecks.RunnerState;
namespace FlightChecks {
public static class Mathf {
 public static int Min(int a,int b)=>Math.Min(a,b); public static int Max(int a,int b)=>Math.Max(a,b);
 public static float Min(float a,float b)=>Math.Min(a,b); public static float Max(float a,float b)=>Math.Max(a,b);
 public static float Abs(float v)=>Math.Abs(v); public static float Clamp(float v,float a,float b)=>Math.Clamp(v,a,b);
 public static float Clamp01(float v)=>Clamp(v,0,1);
 public static float MoveTowards(float a,float b,float max)=>Math.Abs(b-a)<=max?b:a+Math.Sign(b-a)*max;
}
public enum PlayerInputButton {Accelerate,Decelerate,TurnLeft,TurnRight,Lock,Parry,Boost,MagicSlot1,MagicSlot2,MagicSlot3,Menu}
public enum MagicType {None}
public struct NetworkButtons {
 public int Bits; public bool IsSet(PlayerInputButton b)=>(Bits&(1<<(int)b))!=0;
 public bool WasPressed(NetworkButtons old,PlayerInputButton b)=>IsSet(b)&&!old.IsSet(b);
}
public struct NetworkInputData {public NetworkButtons buttons;public bool suppressActions,suppressCombat;}
public enum PerkId {BrakingRune,FirstWind,WindTurning,Tailwind}
public class RunnerState {public float DeltaTime=1f/64f;public float Time;public bool IsForward=true;}
public static class BattleHud {public static int ManaWarnings;public static void ShowInsufficientMana(FlightProbe p){ManaWarnings++;}}
public enum PlayerRef {None,One,Two}
public enum BattleStartPhase {Waiting,Playing,Intermission,Ended}
public class Obj {public bool IsValid=true,HasStateAuthority,HasInputAuthority=true;public PlayerRef InputAuthority=PlayerRef.One;}
public class PerkStatTable {public float flagCarrierSpeedMultiplier=.8f;}
public partial class BattleFlag {
 public static BattleFlag Instance;public Obj Object=new();public RunnerState Runner;
 public BattleStartPhase Phase=BattleStartPhase.Playing;public PlayerRef Carrier,LastCarrier;
}
public struct TickTimer {public bool IsRunning;public float End;public bool Expired(RunnerState r)=>r.Time>=End;}
public class FlightProbe {
 public PerkStatTable Perks=new();
 public float TurnMultiplier()=>GetTurnMultiplier();
 float CommonAmount(PerkId id)=>0;
 public float ItemMultiplier=1;
 float GetItemSpeedMultiplier()=>ItemMultiplier;
 void CancelCombatForPerks(){ClearLockTargetInternal();StopChannel();pendingMagic=MagicType.None;}
 public RunnerState Runner=new RunnerState(); public Obj Object=new Obj();
 public bool SimulatesMovement=true,IsAlive=true,IsHitStunned,IsBoosting,boostNeedsRelease;
 public float Speed,Position,flightMaxSpeed=60,brakeSpeed=20,stageTransitionSpeed=15,boostMultiplier=1.35f;
 public float boostApCostPerSecond=20,NowAp=100,MaxAp=100,ApRecoveryPerSecond=10;
 public float SlowMultiplier=1;
 public TickTimer SlowTimer; public NetworkButtons previousButtons;
 public bool IsBound,IsChanneling,channelNeedsRelease,areaAimArmed;
 public Vec KnockbackVelocity;
 public struct Vec { public static Vec zero => default; }
 public static class Vector3 { public static Vec zero => default; }
 public struct Stats { public bool IsChanneled; }
 public Stats SelectedMagicStats;
 void StopChannel(){IsChanneling=false;}
 public int SpeedStage,CurrentMagicSlot=1,CombatCalls,TurnCalls,KnockbackCalls,LockClears;
 public MagicType pendingMagic;
 void ClearLockTargetInternal(){LockClears++;} void PlayerTurn(NetworkInputData d,NetworkButtons b){TurnCalls++;}
 void ProcessAuthoritativeCombat(NetworkInputData d,NetworkButtons b){CombatCalls++;}
 void ProcessPredictedProjectileInput(NetworkInputData d,NetworkButtons b){}
 bool ShouldShowInsufficientMana(NetworkButtons buttons)=>false;
 void GoForward(){Position+=Speed*Runner.DeltaTime;} void ApplyKnockback(){KnockbackCalls++;}
 public void Step(int bits=0,bool suppressed=false,bool hasInput=true){
  IsBoosting=false;
  if(hasInput) ProcessInput(new NetworkInputData {buttons=new NetworkButtons{Bits=bits},suppressActions=suppressed});
  else ContinueWithoutActions();
  if(!IsBoosting) RegenerateAp(); Runner.Time+=Runner.DeltaTime;
 }
 public FlightProbe Snapshot(){var p=(FlightProbe)MemberwiseClone();p.Runner=new RunnerState{Time=Runner.Time,DeltaTime=Runner.DeltaTime};p.Object=new Obj{HasStateAuthority=Object.HasStateAuthority};return p;}
'@ + ($methods -join "`n") + @'
}
public static class Tests {
 static int n;
 static void Check(bool condition,string label){if(!condition)throw new Exception(label);n++;}
 static bool Near(float a,float b)=>Math.Abs(a-b)<.0001f;
 static int B(PlayerInputButton b)=>1<<(int)b;
 static void Equal(FlightProbe a,FlightProbe b,string label){Check(Near(a.Position,b.Position)&&Near(a.Speed,b.Speed)&&Near(a.NowAp,b.NowAp)&&a.SpeedStage==b.SpeedStage&&a.boostNeedsRelease==b.boostNeedsRelease&&a.previousButtons.Bits==b.previousButtons.Bits,label);}
 public static int Run(){
  int w=B(PlayerInputButton.Accelerate),s=B(PlayerInputButton.Decelerate),boost=B(PlayerInputButton.Boost);
  var p=new FlightProbe();p.Step(w);Check(p.SpeedStage==1&&p.Position>0,"client moves on first local tick");
  for(int i=0;i<12;i++)p.Step(w);Check(p.SpeedStage==1,"held W only increments once");
  p.Step();p.Step(w);p.Step();p.Step(w);p.Step();p.Step(w);Check(p.SpeedStage==3,"speed stage cap");
  Check(p.CombatCalls==0,"client movement never invokes authoritative combat");
  for(int i=0;i<10;i++){p.Step();p.Step(s);}Check(p.SpeedStage==-3,"reverse stage cap");
  p=new FlightProbe{Speed=20,SpeedStage=-3,ApRecoveryPerSecond=0};p.Step();Check(p.Speed>0&&p.Speed<20,"brake before reverse");
  for(int i=0;i<300;i++)p.Step();Check(p.Speed<0,"eventually reverses");
  p=new FlightProbe{Speed=10,SpeedStage=1};p.Step(hasInput:false);Check(p.Position>0,"missing input does not freeze translation");
  p.Step(w|boost,true);Check(p.SpeedStage==1&&!p.IsBoosting&&p.CombatCalls==0,"menu suppresses actions without freezing flight");
  p=new FlightProbe();p.Step(boost);Check(p.IsBoosting&&p.SpeedStage==1&&Near(p.NowAp,99.6875f),"boost predicted mana and initial stage");
  p.Step();Check(!p.IsBoosting&&Near(p.NowAp,99.84375f),"release regenerates mana");
  int manaWarnings=BattleHud.ManaWarnings;
  p=new FlightProbe{NowAp=.1f};p.Step(boost);Check(!p.IsBoosting&&p.boostNeedsRelease,"insufficient mana latches release requirement");
  Check(BattleHud.ManaWarnings==manaWarnings+1,"failed boost produces MP warning");
  for(int i=0;i<12;i++)p.Step(boost);Check(!p.IsBoosting&&p.NowAp>0,"no boost flicker while held and regenerating");
  Check(BattleHud.ManaWarnings==manaWarnings+1,"held exhausted boost does not restart MP warning every tick");
  p.Step();p.Step(boost);Check(p.IsBoosting,"boost resumes after explicit release");
  p=new FlightProbe{IsHitStunned=true,Speed=20,SpeedStage=3};p.Step(boost);Check(!p.IsBoosting&&p.TurnCalls==0&&p.Speed<20&&p.KnockbackCalls==1,"hit stun brakes and preserves knockback path");
  p=new FlightProbe{boostApCostPerSecond=0,NowAp=0};p.Step(boost);Check(p.IsBoosting&&!p.boostNeedsRelease,"zero cost boost works with empty mana");
  p=new FlightProbe{ApRecoveryPerSecond=100,NowAp=99.9f};p.Step();Check(Near(p.NowAp,100),"regen clamps at max");
  p=new FlightProbe{SimulatesMovement=false,NowAp=0,ApRecoveryPerSecond=0};p.Step(boost);Check(!p.IsBoosting&&p.NowAp==0,"non-simulator cannot consume movement mana");
  p=new FlightProbe();p.Step(B(PlayerInputButton.MagicSlot2));Check(p.CurrentMagicSlot==2&&p.CombatCalls==0,"predicted slot selection without spell effects");
  var host=new FlightProbe();host.Object.HasStateAuthority=true;var client=new FlightProbe();
  var history=new int[240];for(int i=0;i<history.Length;i++)history[i]=(i%35==0?w:0)|(i>=20&&i<220?boost:0);
  for(int i=0;i<80;i++){host.Step(history[i]);client.Step(history[i]);}
  Equal(host,client,"host and owner agree before rollback");var snapshot=host.Snapshot();snapshot.Object.HasStateAuthority=false;
  for(int i=80;i<history.Length;i++){host.Step(history[i]);client.Step(history[i]);}
  Equal(host,client,"host and owner agree after identical ticks");
  for(int replay=0;replay<5;replay++){
   var restored=snapshot.Snapshot();for(int i=80;i<history.Length;i++)restored.Step(history[i]);
   Equal(host,restored,"rollback replay does not accumulate speed, edges, or mana: "+replay);
   Check(restored.CombatCalls==snapshot.CombatCalls,"rollback never duplicates attacks: "+replay);
  }
  Check(host.CombatCalls==history.Length,"host still executes combat once per input tick");
  p=new FlightProbe{IsBound=true,Speed=60,SpeedStage=3};p.Step(boost);
  Check(p.Position==0&&p.Speed==0&&!p.IsBoosting,"binding stops translation and boost");
  p.Step(hasInput:false);Check(p.Position==0,"binding also stops missing-input movement");
  p=new FlightProbe{NowAp=50,IsChanneling=true};p.Step();Check(p.NowAp==50,"no mana regen while channeling");
  p=new FlightProbe{SpeedStage=3,SlowMultiplier=.5f,SlowTimer=new TickTimer{IsRunning=true,End=100}};
  for(int i=0;i<300;i++)p.Step();Check(p.Speed<=30.001f,"Ice reduces movement speed");
  foreach(int stage in new[]{-3,-2,-1,1,2,3}) {
   p=new FlightProbe{SpeedStage=stage,boostApCostPerSecond=0};
   BattleFlag.Instance=new(){Runner=p.Runner,Carrier=PlayerRef.One};
   for(int i=0;i<600;i++)p.Step();
   Check(Near(p.Speed,60*stage/3f*.8f)&&p.SpeedStage==stage,"carrier speed is 80 percent without changing stage "+stage);
   Check(Near(p.TurnMultiplier(),1),"carrier penalty does not slow turning "+stage);
   for(int i=0;i<600;i++)p.Step(boost);
   Check(Near(p.Speed,60*stage/3f*1.35f*.8f),"carrier boost has same 20 percent penalty "+stage);
   BattleFlag.Instance.LastCarrier=PlayerRef.One;BattleFlag.Instance.Carrier=PlayerRef.None;
   for(int i=0;i<600;i++)p.Step();
   Check(Near(p.Speed,60*stage/3f),"drop restores speed despite last-carrier record "+stage);
  }
  p=new FlightProbe{SpeedStage=3,SlowMultiplier=.5f,SlowTimer=new TickTimer{IsRunning=true,End=100}};
  BattleFlag.Instance=new(){Runner=p.Runner,Carrier=PlayerRef.One};
  for(int i=0;i<600;i++)p.Step();
  Check(Near(p.Speed,24)&&Near(p.TurnMultiplier(),.5f),"ice and flag multiply movement but only ice affects turning");
  BattleFlag.Instance.Carrier=PlayerRef.Two;
  for(int i=0;i<300;i++)p.Step();Check(Near(p.Speed,30),"opponent holding flag never penalizes local pilot");
  BattleFlag.Instance.Carrier=PlayerRef.One;BattleFlag.Instance.Runner=new();
  Check(!BattleFlag.Instance.IsCarriedBy(p.Runner,PlayerRef.One),"another runner cannot apply carrier effects");
  BattleFlag.Instance.Runner=p.Runner;BattleFlag.Instance.Phase=BattleStartPhase.Intermission;
  Check(!BattleFlag.Instance.IsCarriedBy(p.Runner,PlayerRef.One),"between rounds has no carrier effect");
  BattleFlag.Instance.Phase=BattleStartPhase.Playing;BattleFlag.Instance.Object.IsValid=false;
  Check(!BattleFlag.Instance.IsCarriedBy(p.Runner,PlayerRef.One),"despawned flag cannot apply carrier effects");
  BattleFlag.Instance.Object.IsValid=true;BattleFlag.Instance.Carrier=PlayerRef.None;
  Check(!BattleFlag.Instance.IsCarriedBy(p.Runner,PlayerRef.None),"unowned flag is never an owned match");
  BattleFlag.Instance.Carrier=PlayerRef.One;p.Perks.flagCarrierSpeedMultiplier=.6f;
  for(int i=0;i<300;i++)p.Step();Check(Near(p.Speed,18),"carrier speed multiplier remains editable");
  p=new FlightProbe{SpeedStage=3};BattleFlag.Instance=new(){Runner=p.Runner,Carrier=PlayerRef.One};
  for(int i=0;i<80;i++)p.Step();var carrierSnapshot=p.Snapshot();
  for(int i=0;i<160;i++)p.Step();
  var replayed=carrierSnapshot.Snapshot();BattleFlag.Instance.Runner=replayed.Runner;
  for(int i=0;i<160;i++)replayed.Step();Equal(p,replayed,"carrier speed penalty is stable across snapshot replay");
  BattleFlag.Instance=null;p=new FlightProbe{SpeedStage=3,ItemMultiplier=1.3f};
  for(int i=0;i<500;i++)p.Step();Check(Near(p.Speed,78),"item booster increases stage speed by configured multiplier");
  p.ItemMultiplier=1;for(int i=0;i<120;i++)p.Step();Check(Near(p.Speed,60),"expired booster brakes smoothly to normal stage speed");
  p=new FlightProbe{SpeedStage=0,ItemMultiplier=1.3f};for(int i=0;i<120;i++)p.Step();
  Check(Near(p.Speed,0),"booster never starts movement from zero stage");
  p=new FlightProbe{SpeedStage=-3,ItemMultiplier=1.3f};for(int i=0;i<500;i++)p.Step();
  Check(Near(p.Speed,-78),"booster also respects reverse stage direction");
  p=new FlightProbe{SpeedStage=3,ItemMultiplier=1.3f};BattleFlag.Instance=new(){Runner=p.Runner,Carrier=PlayerRef.One};
  for(int i=0;i<500;i++)p.Step();Check(Near(p.Speed,62.4f),"item bonus retains current-carrier speed penalty");
  return n;
 }
}}
'@
$flagSource=Get-Content (Join-Path $repo 'Assets/Script/BattleFlag.cs') -Raw
$carrierMethod=[regex]::Match($flagSource,'(?ms)^    public bool IsCarriedBy\([^{}]*?\)\r?\n    \{.*?^    \}').Value
if(!$carrierMethod){throw 'Missing shared current-carrier predicate'}
$source+="`nnamespace FlightChecks { public partial class BattleFlag {`n$carrierMethod`n}}"
Add-Type -TypeDefinition $source
"Flight behaviour checks passed: $([FlightChecks.Tests]::Run())"
$state = @('flightMaxSpeed','previousButtons','currentPitch','currentYaw','currentAimTurnSpeed','currentTurnSpeed',
    'turnAccel','returnSpeed','stageTransitionSpeed','brakeSpeed','boostMultiplier','boostApCostPerSecond','boostNeedsRelease',
    'mapReturnStart','mapReturnControl1','mapReturnControl2','mapReturnEnd','mapReturnEntryDirection',
    'mapReturnStartRotation','mapReturnElapsed','mapReturnDuration','mapReturnHeight','mapReturnSpeed','mapReturnSpeedStage')
foreach ($name in $state) {
    if ($playerSource -notmatch ('\[Networked\]\s+private\s+\w+\s+' + $name + '\s*\{\s*get;\s*set;\s*\}')) {
        throw "Rollback-sensitive state is not networked: $name"
    }
}
if ($playerSource -notmatch 'Object.ForceRemoteRenderTimeframe = !SimulatesMovement;') {throw 'Render timeline mismatch'}
if ($playerSource -notmatch 'Object.HasStateAuthority && hasPendingPortalTeleport') {throw 'Predicted teleport side effect'}
if ($playerSource -notmatch 'if \(Object.HasStateAuthority\) UpdatePendingCast\(\);') {throw 'Predicted cast side effect'}
$projectile = Get-Content (Join-Path $repo 'Assets/Script/MagicProjectile.cs') -Raw
if ($playerSource -match 'FindObjectsByType<Player>' -or $projectile -match 'FindObjectsByType<Player>') {throw 'Allocating battle scene scan returned'}
if ($projectile -notmatch 'Object.ForceRemoteRenderTimeframe = !Object.HasStateAuthority;') {throw 'Projectile timeline mismatch'}
"Rollback state and authority/render contract checks passed: $($state.Count + 5)"
