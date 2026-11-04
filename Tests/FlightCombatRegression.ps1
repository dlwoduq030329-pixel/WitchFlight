# Run production ram / cobra state machines with deterministic geometry and authority doubles.
# Live CharacterController contacts, Fusion weaving and rendered VFX need Unity play checks.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$combat = Get-Content Assets/Script/Player.FlightCombat.cs -Raw
$flag = Get-Content Assets/Script/BattleFlag.cs -Raw
$segment = [regex]::Match($flag, '(?ms)^    internal static float SegmentDistanceSquared\([^{}]+\)\r?\n    \{.*?^    \}').Value
if (!$segment) { throw 'Missing actual capsule distance calculation' }
$source = @'
using System;
using System.Collections.Generic;
using N = System.Numerics;
namespace FlightCombatChecks {
 public sealed class NetworkedAttribute:Attribute {}
 public struct Vector3 {
  public N.Vector3 v; public Vector3(float x,float y,float z){v=new(x,y,z);} public Vector3(N.Vector3 n){v=n;}
  public float x=>v.X;public float y=>v.Y;public float z=>v.Z;public float magnitude=>v.Length();public float sqrMagnitude=>v.LengthSquared();
  public Vector3 normalized=>magnitude>0.00001f?new(N.Vector3.Normalize(v)):zero;
  public static Vector3 zero=>new(0,0,0);public static Vector3 up=>new(0,1,0);public static Vector3 forward=>new(0,0,1);
  public static Vector3 operator+(Vector3 a,Vector3 b)=>new(a.v+b.v);public static Vector3 operator-(Vector3 a,Vector3 b)=>new(a.v-b.v);
  public static Vector3 operator-(Vector3 a)=>new(-a.v);public static Vector3 operator*(Vector3 a,float b)=>new(a.v*b);public static Vector3 operator/(Vector3 a,float b)=>new(a.v/b);
  public static Vector3 Lerp(Vector3 a,Vector3 b,float t)=>new(N.Vector3.Lerp(a.v,b.v,t));public static float Dot(Vector3 a,Vector3 b)=>N.Vector3.Dot(a.v,b.v);
 }
 public struct Quaternion {
  public N.Quaternion q;public Quaternion(N.Quaternion n){q=n;}public static Quaternion identity=>new(N.Quaternion.Identity);
  public Vector3 eulerAngles=>Vector3.zero;
  public static Quaternion Euler(float x,float y,float z)=>new(N.Quaternion.CreateFromYawPitchRoll(y*MathF.PI/180,x*MathF.PI/180,z*MathF.PI/180));
  public static Quaternion operator*(Quaternion a,Quaternion b)=>new(a.q*b.q);public static Vector3 operator*(Quaternion a,Vector3 b)=>new(N.Vector3.Transform(b.v,a.q));
 }
 public class Transform {public Vector3 position;public Quaternion rotation=Quaternion.identity;public Vector3 forward=>rotation*Vector3.forward;}
 public static class Mathf {
  public const float PI=MathF.PI; public static float Sin(float x)=>MathF.Sin(x);public static float Max(float a,float b)=>Math.Max(a,b);
  public static float Clamp01(float x)=>Math.Clamp(x,0,1);public static int Clamp(int x,int a,int b)=>Math.Clamp(x,a,b);public static int CeilToInt(float x)=>(int)Math.Ceiling(x);
  public static float SmoothStep(float a,float b,float t){t=Clamp01(t);return a+(b-a)*t*t*(3-2*t);}
 }
 public class RunnerState {public float Time;public float DeltaTime=.02f;}
 public class NetObject {public bool IsValid=true,HasStateAuthority=true;public int InputAuthority,Id;}
 public struct TickTimer {public bool active;public float end;public static TickTimer None=>default;public float? RemainingTime(RunnerState r)=>active?Math.Max(0,end-r.Time):null;public static TickTimer CreateFromSeconds(RunnerState r,float t)=>new(){active=true,end=r.Time+t};}
 public enum MagicType {None,Fire} public enum BattleSound {BodyRam,Cobra}
 public class BattleManager {public static BattleManager Instance=new();public bool IsGameplayActive=true;}
 public class FlightCombatTable {
  public static FlightCombatTable Default=new();public bool enableRamming=true,enableCobra=true;
  public float minimumRamSpeed=12,ramDamage=30,ramCooldown=1,ramKnockback=7,ramStun=.15f;
  public float cobraMinimumSpeedRatio=.9f,cobraSeconds=.8f,cobraCooldown=4,cobraPitch=80,cobraLift=2,cobraBackwardSpeed=6;
 }
 public class Controller {public bool enabled;public Transform target;public void Move(Vector3 v){target.position+=v;}}
 public partial class BattleFlag {
  public static BattleFlag Instance;public RunnerState Runner;public List<int> Drops=new();
  public void DropCarrier(int id,Vector3 at,bool descend){Drops.Add(id);}
  public static void GetCapsule(Player p,out Vector3 center,out Vector3 half,out float radius){center=p.transform.position;half=p.Half;radius=.3f;}
'@
$source += $segment + "`n }`n"
$source += @'
 public partial class Player {
  public static HashSet<Player> ActiveCombatants=new();public NetObject Object=new();public RunnerState Runner;
  public Transform transform=new();public Vector3 Half=new(0,.6f,0);public Controller characterController;
  public bool IsAlive=true,IsReturningToMap,IsBound,IsHitStunned,IsBoosting,boostNeedsRelease,areaAimArmed,Visible=true;
  public int TeamIndex=1,SpeedStage=3,DamageCalls,RamSounds,CobraSounds,ChannelStops,LockClears;
  public float Speed=30,Hp=100,flightMaxSpeed=30,FlagMultiplier=1,SlowMultiplier=1,currentPitch,currentYaw,currentAimTurnSpeed,currentTurnSpeed;
  public MagicType pendingMagic=MagicType.Fire;public bool LastParry;
  bool TimerIsActive(TickTimer t)=>t.active&&t.end>Runner.Time;
  float GetMovementMultiplier()=>SlowMultiplier;float GetFlagSpeedMultiplier()=>FlagMultiplier;float GetItemSpeedMultiplier()=>1;
  bool HasLineOfSight(Player p)=>Visible;
  void StopChannel(){ChannelStops++;}void ClearLockTargetInternal(){LockClears++;}
  float NormalizePitch(float p)=>p;Vector3 ConstrainMapMovement(Vector3 v)=>v;
  public void TakeDamage(float amount,int attacker,MagicType magic,bool parry,float knock,float stun,int id){Hp-=amount;DamageCalls++;LastParry=parry;if(Hp<=0)IsAlive=false;}
  void RPC_PlayBattleSound(BattleSound s){if(s==BattleSound.BodyRam)RamSounds++;else CobraSounds++;}
  public void Ram(Vector3 start,Vector3 end,Vector3 velocity)=>TryRam(start,end,velocity);
  public bool StartCobra()=>TryStartCobra();public bool CobraTick()=>TickCobra();
 }
'@
$source += ($combat -replace '(?m)^using [^;]+;\r?\n','')
$source += @'
 public static class Tests {
  static int n;static void Check(bool ok,string label){if(!ok)throw new Exception(label);n++;}
  static bool Near(float a,float b)=>Math.Abs(a-b)<.001f;
  static (Player,Player) Pair(){
   BattleManager.Instance=new();FlightCombatTable.Default=new();var runner=new RunnerState();
   var a=new Player{Runner=runner};var b=new Player{Runner=runner,TeamIndex=2,Speed=0};
   a.Object.InputAuthority=1;a.Object.Id=1;b.Object.InputAuthority=2;b.Object.Id=2;
   a.transform.position=new(0,0,.5f);b.transform.position=new(0,0,1);
   Player.ActiveCombatants=new(){a,b};BattleFlag.Instance=new(){Runner=runner};return(a,b);
  }
  static void Hit(Player a)=>a.Ram(new(0,0,0),a.transform.position,new(0,0,30));
  public static int Run(){
   var (a,b)=Pair();Hit(a);
   Check(a.DamageCalls==0&&b.DamageCalls==1&&b.Hp==70,"fast rammer damages stationary victim only");
   Check(!b.LastParry&&a.RamSounds==1&&BattleFlag.Instance.Drops.Count==1&&BattleFlag.Instance.Drops[0]==2,"ram cannot be parried and drops victim flag");
   Hit(a);Check(b.DamageCalls==1,"same contact cannot repeat damage within cooldown");
   a.Runner.Time=1.01f;Hit(a);Check(b.DamageCalls==2,"later fresh ram can damage again");
   (a,b)=Pair();b.Speed=30;b.transform.rotation=Quaternion.Euler(0,180,0);a.Hp=b.Hp=20;Hit(a);
   Check(!a.IsAlive&&!b.IsAlive&&a.DamageCalls==1&&b.DamageCalls==1,"lethal head-on collision applies both outcomes");
   Check(BattleFlag.Instance.Drops.Count==2,"head-on drops both flags before resolving deaths");
   (a,b)=Pair();b.Speed=30;Hit(a);Check(a.DamageCalls==0&&b.DamageCalls==1,"same direction is not a counter-ram");
   (a,b)=Pair();a.Ram(Vector3.zero,a.transform.position,new(0,0,5));Check(b.DamageCalls==0,"slow contact has no ram damage");
   (a,b)=Pair();b.TeamIndex=1;Hit(a);Check(b.DamageCalls==0,"teammates never ram");
   (a,b)=Pair();a.Object.HasStateAuthority=false;Hit(a);Check(b.DamageCalls==0&&BattleFlag.Instance.Drops.Count==0,"predicted owner cannot deal damage/drop flags");
   (a,b)=Pair();a.Visible=false;Hit(a);Check(b.DamageCalls==0,"intervening wall prevents damage");
   (a,b)=Pair();b.Runner=new();Hit(a);Check(b.DamageCalls==0,"other runner cannot receive damage");
   (a,b)=Pair();b.transform.position=new(5,0,1);Hit(a);Check(b.DamageCalls==0,"missed capsule does not damage");
   (a,b)=Pair();a.transform.position=new(0,0,4);b.transform.position=new(0,0,2);Hit(a);Check(b.DamageCalls==1,"sweep catches contact crossed between ticks");
   (a,b)=Pair();a.Half=new(0,-.6f,0);b.Half=new(.6f,0,0);Hit(a);Check(b.DamageCalls==1,"inverted and horizontal capsules still collide");
   (a,b)=Pair();BattleManager.Instance.IsGameplayActive=false;Hit(a);Check(b.DamageCalls==0,"VS and intermission do not ram");
   (a,b)=Pair();a.IsReturningToMap=true;Hit(a);Check(b.DamageCalls==0,"automatic boundary return is protected");
   (a,b)=Pair();Check(a.StartCobra()&&a.CobraActive&&a.SpeedStage==0&&a.boostNeedsRelease,"top-speed deceleration arms cobra and stops sustained boost");
   Check(a.CobraSounds==1&&a.ChannelStops==1&&a.LockClears==1&&a.pendingMagic==MagicType.None,"cobra cancels attacks and plays one sound");
   Check(!a.StartCobra(),"holding/repeated input cannot retrigger active cobra");
   float peak=0;for(int i=0;i<45;i++){a.CobraTick();peak=Math.Max(peak,a.transform.position.y);a.Runner.Time+=a.Runner.DeltaTime;}
   Check(!a.CobraActive&&a.Speed==0&&a.transform.rotation.forwardForTest().z>.99f,"cobra finishes at rest and restores heading");
   Check(peak>1.8f&&peak<=2.01f,"cobra rises by configured two metres");
   Check(a.transform.position.y<.1f,"cobra arc returns close to its original height");
   a.SpeedStage=3;a.Speed=30;Check(!a.StartCobra(),"cooldown blocks another immediate cobra");
   a.Runner.Time=5;Check(a.StartCobra(),"cobra becomes available after cooldown");
   a.IsBound=true;a.CobraTick();Check(!a.CobraActive&&a.Speed==0,"binding interrupts cobra");
   (a,b)=Pair();a.Object.HasStateAuthority=false;Check(a.StartCobra()&&a.CobraSounds==0&&a.ChannelStops==0,"owner predicts motion without duplicating RPC/combat");
   foreach(int stage in new[]{-3,-2,-1,0,1,2}){(a,b)=Pair();a.SpeedStage=stage;Check(!a.StartCobra(),"only stage three can cobra "+stage);}
   (a,b)=Pair();a.Speed=15;Check(!a.StartCobra(),"selected high stage is not enough without actual speed");
   (a,b)=Pair();a.FlagMultiplier=.8f;a.Speed=24;Check(a.StartCobra(),"flag holder can cobra at its actual top cruise speed");
   (a,b)=Pair();a.SlowMultiplier=.4f;a.Speed=12;Check(a.StartCobra(),"slowed pilot still uses the current achievable cruise speed");
   (a,b)=Pair();a.StartCobra();FlightCombatTable.Default=null;a.CobraTick();Check(a.CobraActive,"active manoeuvre tolerates unavailable settings");
   Check(Math.Abs(Player.CobraSpeedFactor(.5f)-.5f)<.0001f,"cobra retains half its speed at midpoint");
   Check(Player.CobraSpeedFactor(.9f)>0&&Player.CobraSpeedFactor(1)==0,"cobra only finishes decelerating at the end");
   Check(Player.CobraSpeedFactor(0)-Player.CobraSpeedFactor(.001f)<.00001f,"cobra starts braking without an abrupt speed change");
   Check(Player.CobraTravelSpeed(0,30,6)==30,"cobra preserves entry momentum");
   Check(Player.CobraTravelSpeed(.5f,30,6)<0,"upright cobra slides backward");
   Check(Math.Abs(Player.CobraTravelSpeed(1,30,6))<.0001f,"backward slide settles at zero");
   Check(Math.Abs(Player.CobraTravelSpeed(.45f,30,6)-Player.CobraTravelSpeed(.45001f,30,6))<.01f,"direction change is continuous");
   return n;
  }
  static Vector3 forwardForTest(this Quaternion q)=>q*Vector3.forward;
 }
}
'@
Add-Type -TypeDefinition $source
"Flight combat behaviour checks passed: $([FlightCombatChecks.Tests]::Run())"

$audio = Get-Content Assets/Resources/BattleAudioTable.asset -Raw
$common = $audio.Substring(0,$audio.IndexOf('  magicSounds:'))
if ([regex]::Matches($common,'(?m)^      clip: \{fileID: 8300000, guid: [a-f0-9]{32}, type: 3\}').Count -ne 43) {throw 'Common cues are not all valid assigned clips'}
foreach($block in [regex]::Matches($audio.Substring($audio.IndexOf('  magicSounds:')),'(?ms)^  - label:.*?(?=^  - label:|\z)')) {
 foreach($stage in @('lockComplete','cast','impact')) {
  if($block.Value -notmatch "${stage}:\r?\n      clip: \{fileID: 8300000, guid: [a-f0-9]{32}, type: 3\}"){throw "Missing $stage audio"}
 }
}
if($audio -match '\$\d') {throw 'Invalid generated YAML substitution'}
"Sound configuration verified: 43 common cues and lock/cast/impact for all 12 spells."
