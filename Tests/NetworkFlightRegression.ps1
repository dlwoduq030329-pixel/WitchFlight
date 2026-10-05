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
    'GetMovementMultiplier','GetTurnMultiplier','UpdateHeldBoost','TryConsumeMovementAp','RegenerateAp','TimerIsActive') |
    ForEach-Object { Method $_ }
$source = @'
using System;
namespace FlightChecks {
public static class Mathf {
 public static int Min(int a,int b)=>Math.Min(a,b); public static int Max(int a,int b)=>Math.Max(a,b);
 public static float Min(float a,float b)=>Math.Min(a,b); public static float Max(float a,float b)=>Math.Max(a,b);
 public static float Abs(float v)=>Math.Abs(v); public static float Clamp(float v,float a,float b)=>Math.Clamp(v,a,b);
 public static float MoveTowards(float a,float b,float max)=>Math.Abs(b-a)<=max?b:a+Math.Sign(b-a)*max;
}
public enum PlayerInputButton {Accelerate,Decelerate,TurnLeft,TurnRight,Lock,Parry,Boost,MagicSlot1,MagicSlot2,MagicSlot3,Menu}
public enum MagicType {None}
public struct NetworkButtons {
 public int Bits; public bool IsSet(PlayerInputButton b)=>(Bits&(1<<(int)b))!=0;
 public bool WasPressed(NetworkButtons old,PlayerInputButton b)=>IsSet(b)&&!old.IsSet(b);
}
public struct NetworkInputData {public NetworkButtons buttons;public bool suppressActions;}
public class RunnerState {public float DeltaTime=1f/64f;public float Time;}
public class Obj {public bool HasStateAuthority;}
public struct TickTimer {public bool IsRunning;public float End;public bool Expired(RunnerState r)=>r.Time>=End;}
public class FlightProbe {
 public RunnerState Runner=new RunnerState(); public Obj Object=new Obj();
 public bool SimulatesMovement=true,IsAlive=true,IsHitStunned,IsBoosting,boostNeedsRelease;
 public float Speed,Position,flightMaxSpeed=60,brakeSpeed=20,stageTransitionSpeed=15,boostMultiplier=1.35f;
 public float boostApCostPerSecond=20,NowAp=100,MaxAp=100,ApRecoveryPerSecond=10;
 public float WindMultiplier=1,SlowMultiplier=1;
 public TickTimer WindTimer,SlowTimer; public NetworkButtons previousButtons;
 public int SpeedStage,CurrentMagicSlot=1,CombatCalls,TurnCalls,KnockbackCalls,LockClears;
 public MagicType pendingMagic;
 void ClearLockTargetInternal(){LockClears++;} void PlayerTurn(NetworkInputData d,NetworkButtons b){TurnCalls++;}
 void ProcessAuthoritativeCombat(NetworkInputData d,NetworkButtons b){CombatCalls++;}
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
  p=new FlightProbe{NowAp=.1f};p.Step(boost);Check(!p.IsBoosting&&p.boostNeedsRelease,"insufficient mana latches release requirement");
  for(int i=0;i<12;i++)p.Step(boost);Check(!p.IsBoosting&&p.NowAp>0,"no boost flicker while held and regenerating");
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
  return n;
 }
}}
'@
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
