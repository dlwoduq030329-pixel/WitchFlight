# Execute production dash/ram/evasion intent and live-flight threat selection with Unity/Fusion doubles.
# Physical contact, real turn response and networking still require Unity play testing.
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
function StaticMethod([string]$source,[string]$name) {
 $m=[regex]::Match($source,'(?ms)^    public static [\w<>]+ '+$name+'\([^{}]*?\)\r?\n    \{.*?^    \}')
 if(!$m.Success){throw "Missing $name"};$m.Value
}
$settings=Get-Content Assets/Script/MatchBotSettings.cs -Raw
$section=$settings.Substring($settings.IndexOf('public bool useCombatDash'),$settings.IndexOf('[Header("3~5인')-$settings.IndexOf('public bool useCombatDash'))
$fields=([regex]::Matches($section,'public (?:bool|float|Vector2) \w+[^;]+;') | ForEach-Object Value)-join "`n"
$source=@'
using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;
namespace Fusion {public struct NetworkId {public int Value;}}
namespace UnityEngine {
 public struct Vector2 {public float x,y;public Vector2(float a,float b){x=a;y=b;}}
 public struct Vector3 {
  public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}
  public static Vector3 up=>new(0,1,0);public static Vector3 forward=>new(0,0,1);
  public float sqrMagnitude=>x*x+y*y+z*z;public float magnitude=>(float)Math.Sqrt(sqrMagnitude);
  public Vector3 normalized=>magnitude>.000001f?this*(1/magnitude):default;
  public static Vector3 operator+(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
  public static Vector3 operator-(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
  public static Vector3 operator*(Vector3 a,float f)=>new(a.x*f,a.y*f,a.z*f);
  public static Vector3 operator/(Vector3 a,float f)=>a*(1/f);
  public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
  public static Vector3 Cross(Vector3 a,Vector3 b)=>new(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
  public static float Distance(Vector3 a,Vector3 b)=>(a-b).magnitude;
  public static bool operator==(Vector3 a,Vector3 b)=>(a-b).sqrMagnitude<1e-10f;
  public static bool operator!=(Vector3 a,Vector3 b)=>!(a==b);
  public override bool Equals(object b)=>b is Vector3 v && this==v;
  public override int GetHashCode()=>HashCode.Combine(x,y,z);
 }
 public struct Quaternion {
  System.Numerics.Quaternion value;
  public static Quaternion AngleAxis(float angle,Vector3 axis)=>new(){value=System.Numerics.Quaternion.CreateFromAxisAngle(new(axis.x,axis.y,axis.z),angle*Mathf.Deg2Rad)};
  public static Vector3 operator*(Quaternion q,Vector3 v){var p=System.Numerics.Vector3.Transform(new(v.x,v.y,v.z),q.value);return new(p.X,p.Y,p.Z);}
 }
 public static class Mathf {
  public const float Deg2Rad=(float)(Math.PI/180);
  public static float Max(float a,float b)=>Math.Max(a,b);public static float Abs(float a)=>Math.Abs(a);
  public static float Clamp(float a,float b,float c)=>Math.Clamp(a,b,c);public static float Clamp01(float a)=>Math.Clamp(a,0,1);
  public static float Sqrt(float a)=>(float)Math.Sqrt(a);public static float Pow(float a,float b)=>(float)Math.Pow(a,b);
 }
 public static class Random {
  public static float Sample=.5f;public static int Calls;
  public static float value {get{Calls++;return Sample;}}
  public static float Range(float a,float b){Calls++;return (a+b)*.5f;}
 }
 public class Transform {public Vector3 position,forward=Vector3.forward,right=new(1,0,0);}
}
public class MatchBotSettings { __FIELDS__ public float pickupApproachDistance=14; }
public class FlightCombatTable {public static FlightCombatTable Default=new();public bool enableRamming=true;}
public class NetObject {public bool IsValid=true;public Fusion.NetworkId Id;}
public class Runner {public float DeltaTime=.1f;}
public class Box {public bool clear=true;public Vector3 center;public bool Contains(Vector3 p)=>clear;}
public class MapBoundary {public Box box=new();public bool TryGetBotFlightBounds(out Box b){b=box;return true;}}
public class BattleManager {public static BattleManager Instance=new();public int RoundNumber;public MapBoundary MapBoundary;}
public class Controller {public Box bounds=new();}
public enum PlayerInputButton {Boost}
public struct Buttons {public bool Boost;public void Set(PlayerInputButton b,bool v){Boost=v;}public bool IsSet(PlayerInputButton b)=>Boost;}
public struct NetworkInputData {public Buttons buttons;}
public partial class Player {
 public NetObject Object=new();public Runner Runner=new();public Transform transform=new();
 public Player botTarget;public int TeamIndex=1,DashCharges=3,DeathSequence,PortalExitSequence;
 public bool IsAlive=true,IsSpawnProtected,IsBound,IsHitStunned,IsReturningToMap,hasPendingPortalTeleport,RamCooldown,BurstBoostTimer;
 public float Speed=30,flightMaxSpeed=39,boostMultiplier=1.35f;
 public Vector3 ParryHintVelocity;public Vector3 LockAimPoint=>transform.position+Vector3.up*1.6f;
 public Controller characterController;public Buttons previousButtons;public bool wall,visible=true;public int ClearanceQueries;
 bool TimerIsActive(bool timer)=>timer;
 bool IsBotCombatant(Player p)=>p!=null&&p.Object.IsValid&&p.IsAlive&&p.Runner==Runner;
 bool HasLineOfSight(Player p)=>visible;
 float BotClearance(Vector3 a,Vector3 d,float length){ClearanceQueries++;return wall?0:length;}
 public void Tick(float dt=.1f){Runner.DeltaTime=dt;TickBotMobilityPlan(dt);}
 public bool Ram(MatchBotSettings s,bool allow,out Vector3 point)=>TryBotRamDestination(s,allow,out point);
 public bool Evade(MatchBotSettings s,out Vector3 point,out bool dash)=>TryBotEvasionDestination(s,out point,out dash);
 public bool Dash(MatchBotSettings s,bool avoiding=false,bool pickup=false,int stage=3,bool evading=false,bool evadeDash=false){
  NetworkInputData input=default;ApplyBotCombatDash(s,avoiding,pickup,evading,evadeDash,stage,new(0,0,60),transform.forward,ref input);return input.buttons.Boost;
 }
 public bool HasRam=>botRamTarget!=null;
 public bool HasEvasion=>botEvasionThreat!=null;
}
public class MagicStatTable {
 public static MagicStatTable Default=new();public float homingTurnReferenceSpeed=67.5f,homingTurnSpeedExponent=2;
}
public struct MagicStatEntry {public float projectileSpeed,projectileTurnSpeed;}
public class MagicProjectile {
 public static List<MagicProjectile> active=new();public NetObject Object=new();public Runner Runner;
 public bool initialized=true,HasFlightExhaustion=true,Finished,FlightExhausted,Blocked;
 public int ShooterTeam=2;public Fusion.NetworkId TargetId;public float FlightSpeedFraction=1;
 public Transform transform=new();public MagicStatEntry stats=new(){projectileSpeed=85,projectileTurnSpeed=450};
 public MagicStatTable flightSettings=MagicStatTable.Default;
 public bool IsThreatTo(Player p)=>Object.IsValid&&!Finished&&!FlightExhausted&&p.Runner==Runner&&p.TeamIndex!=ShooterTeam&&TargetId.Equals(p.Object.Id);
 public static float TimeScale=1;static float TimeProjectileMultiplier(Runner r,Vector3 p)=>TimeScale;
 static bool HasBlastSight(Vector3 p,Player observer){foreach(var shot in active)if(shot.transform.position==p&&shot.Blocked)return false;return true;}
 __MAGIC_METHODS__
}
'@
$projectile=Get-Content Assets/Script/MagicProjectile.cs -Raw
$methods=(StaticMethod $projectile HomingAimDelta)+(StaticMethod $projectile FlightTurnSpeed)+(StaticMethod $projectile TryGetBotHomingThreat)
$source=$source.Replace('__FIELDS__',$fields).Replace('__MAGIC_METHODS__',$methods)
foreach($file in 'BotHomingEvasion.cs','Player.BotDash.cs','Player.BotEvasion.cs'){$source+=(Get-Content ('Assets/Script/'+$file) -Raw)-replace '(?m)^using UnityEngine;\r?\n',''}
$source+=@'
public static class BotMobilityChecks {
 static int n;static void Check(bool ok,string name){n++;if(!ok)throw new Exception(name);}
 static Player Pilot(){MagicProjectile.active.Clear();BattleManager.Instance=new();Random.Sample=.5f;var p=new Player();p.Object.Id=new(){Value=1};p.Tick();p.botTarget=new Player{Runner=p.Runner,TeamIndex=2};p.botTarget.Object.Id=new(){Value=2};p.botTarget.transform.position=new(0,0,30);return p;}
 static MagicProjectile Threat(Player p,float fraction=1){var shot=new MagicProjectile{Runner=p.Runner,TargetId=p.Object.Id,FlightSpeedFraction=fraction};shot.Object.Id=new(){Value=40};shot.transform.position=p.LockAimPoint+new Vector3(0,0,-35);MagicProjectile.active.Add(shot);return shot;}
 public static int Run(){
  var s=new MatchBotSettings();var p=Pilot();
  Check(p.Dash(s),"far aligned bot issues Shift");Check(p.DashCharges==3,"planner cannot spend or grant charges directly");
  p.Tick();Check(!p.Dash(s),"no repeated held boost or repeated roll next tick");
  p=Pilot();p.wall=true;Check(!p.Dash(s),"blocked burst path prevents dash");
  p=Pilot();p.DashCharges=0;Check(!p.Dash(s),"no free dash at zero charges");
  p=Pilot();p.BurstBoostTimer=true;Check(!p.Dash(s),"no overlapping burst");
  p=Pilot();p.IsHitStunned=true;Check(!p.Dash(s),"hit stun prevents Shift");
  p=Pilot();p.previousButtons.Boost=true;Check(!p.Dash(s),"release required between presses");
  p=Pilot();Check(!p.Dash(s,evading:true),"weakened-shot turn does not consume boost");
  p=Pilot();Check(p.Dash(s,evading:true,evadeDash:true),"fresh-shot evasion can use normal Shift");
  p=Pilot();Random.Sample=.1f;Check(!p.Dash(s),"late mistake does not press immediately");int calls=Random.Calls;
  p.Tick(.2f);Check(!p.Dash(s)&&Random.Calls==calls,"pending mistake is retained without reroll");
  p.Tick(1);Check(p.Dash(s),"late Shift actually occurs");
  p=Pilot();s.dashUseChance=0;Check(!p.Dash(s),"zero chance skips dash");calls=Random.Calls;p.Tick(.1f);p.Dash(s);Check(Random.Calls==calls,"failed dash waits before retrying");s.dashUseChance=.85f;
  p=Pilot();Check(p.Ram(s,true,out var aim)&&p.HasRam,"near enemy creates committed ram");
  Check(Math.Abs(aim.x)<.01f&&Math.Abs(aim.y)<.01f&&aim.z>30,"ram flies through target at matching body height");
  calls=Random.Calls;p.Tick();p.botTarget.transform.position=new(20,0,30);p.Ram(s,true,out var same);
  Check((aim-same).sqrMagnitude<.001&&calls==Random.Calls,"commit keeps sampled intercept and randomness");
  Check(!p.Ram(s,false,out _)&&!p.HasRam,"flag/health objective immediately cancels ram");
  p=Pilot();Random.Sample=.1f;Check(p.Ram(s,true,out aim)&&Math.Abs(aim.x)>3,"ram mistake visibly misses sideways");
  p=Pilot();p.botTarget.IsSpawnProtected=true;Check(!p.Ram(s,true,out _),"protected target is not rammed");
  p=Pilot();p.visible=false;Check(!p.Ram(s,true,out _),"ram cannot select through a wall");
  p=Pilot();p.Ram(s,true,out _);p.Dash(s,avoiding:true);Check(!p.HasRam,"obstacle avoidance cancels commitment");
  p=Pilot();p.Ram(s,true,out _);p.PortalExitSequence++;p.Tick();Check(!p.HasRam,"portal resets ram plan");
  p=Pilot();p.Ram(s,true,out _);p.DeathSequence++;p.Tick();Check(!p.HasRam,"respawn resets ram plan");
  p=Pilot();p.Ram(s,true,out _);p.Tick(3);Check(!p.Ram(s,true,out _),"missed pass times out instead of chasing forever");
  p=Pilot();var shot=Threat(p,.7f);
  Check(MagicProjectile.TryGetBotHomingThreat(p,100,out var found,out float eta,out float rate,out float initial)&&found==shot,"read live targeted homing state");
  Check(Math.Abs(rate/initial-.49f)<.0001f,"remaining speed drives actual turn ratio");
  Check(Player.CanExploitBotHomingTurn(rate,initial,eta,.65f),"aged weak shot permits turn dodge");
  Check(!Player.CanExploitBotHomingTurn(initial,initial,eta,.65f),"fresh shot is not treated as weak");
  Check(!Player.CanExploitBotHomingTurn(rate,initial,.1f,.65f),"point-blank shot is too late for a leisurely turn");
  shot.TargetId=new(){Value=99};Check(!MagicProjectile.TryGetBotHomingThreat(p,100,out _,out _,out _,out _),"other player's shot ignored");shot.TargetId=p.Object.Id;
  shot.FlightExhausted=true;Check(!MagicProjectile.TryGetBotHomingThreat(p,100,out _,out _,out _,out _),"falling shots ignored");shot.FlightExhausted=false;
  shot.Blocked=true;Check(!MagicProjectile.TryGetBotHomingThreat(p,100,out _,out _,out _,out _),"occluded shot ignored");shot.Blocked=false;
  shot.transform.forward=new(0,0,-1);Check(!MagicProjectile.TryGetBotHomingThreat(p,100,out _,out _,out _,out _),"departing shot ignored");
  p=Pilot();shot=Threat(p,.7f);Check(p.Evade(s,out var route,out bool dash)&&!dash,"weak-shot plan uses turn without dash");
  calls=Random.Calls;p.Tick();Check(p.Evade(s,out _,out _)&&Random.Calls==calls,"active turn keeps decision without reroll");
  p.PortalExitSequence++;p.Tick();Check(!p.HasEvasion,"portal clears evasion history");
  p=Pilot();shot=Threat(p);Check(p.Evade(s,out _,out dash)&&dash,"young agile shot selects dash-assisted cut");
  p=Pilot();shot=Threat(p);p.DashCharges=0;Check(!p.Evade(s,out _,out _),"fresh shot with no charges left to ordinary parry planner");
  p=Pilot();shot=Threat(p,.7f);p.wall=true;Check(!p.Evade(s,out _,out _),"both blocked sides cancel dodge");
  p=Pilot();shot=Threat(p,.7f);Random.Sample=.1f;Check(!p.Evade(s,out _,out _)&&p.HasEvasion,"failed reaction has deliberate delay");
  calls=Random.Calls;p.Tick(.2f);Check(!p.Evade(s,out _,out _)&&Random.Calls==calls,"late reaction not rerolled");
  p.Tick(.3f);Check(p.Evade(s,out route,out _),"late turn executes");
  Check(Vector3.Dot(route.normalized,p.transform.forward)>.8f,"mistake turns too shallowly rather than cancelling incoming damage");
  p=Pilot();shot=Threat(p,.7f);s.evasionUseChance=0;Check(!p.Evade(s,out _,out _),"evasion can be skipped by probability");
  calls=Random.Calls;p.Tick(.1f);p.Evade(s,out _,out _);Check(Random.Calls==calls,"skipped dodge waits before another attempt");
  return n;
 }
}
'@
Add-Type -TypeDefinition $source
"Bot dash, ram, evasion and live threat checks passed: $([BotMobilityChecks]::Run())"
