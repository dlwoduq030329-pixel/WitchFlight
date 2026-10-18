# Execute the production wall-hit handler with deterministic contact/authority doubles.
# Does not simulate Unity CharacterController geometry or live Fusion rollback.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$player = Get-Content Assets/Script/Player.cs -Raw
function Method([string]$name) {
    $match = [regex]::Match($player, '(?ms)^    private [^\r\n]+\b' + $name + '\([^\r\n]*\)\r?\n    \{.*?^    \}')
    if (!$match.Success) { throw "Missing production method: $name" }
    $match.Value -replace '\bprivate\b', 'public'
}
$source = @'
using System;
namespace WallChecks {
 public static class Mathf {
  public static float Abs(float value)=>Math.Abs(value);
  public static int Abs(int value)=>Math.Abs(value);
  public static float Max(float a,float b)=>Math.Max(a,b);
 }
 public struct Vector3 {
  public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
  public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
  public static Vector3 ProjectOnPlane(Vector3 v,Vector3 n){float d=Dot(v,n)/Dot(n,n);return new(v.x-n.x*d,v.y-n.y*d,v.z-n.z*d);}
 }
 public class Collider {
  public object Owner;
  public T GetComponentInParent<T>() where T:class=>Owner as T;
 }
 public class ControllerColliderHit {
  public Collider collider=new();public Vector3 normal=new(0,0,-1),moveDirection=new(0,0,1);
 }
 public class MagicProjectile {}
 public class NetworkObject {public bool IsValid=true,HasStateAuthority=true;}
 public class Runner {public float Time;}
 public enum PlayerRef {None}
 public struct TickTimer {
  public bool IsRunning;public float End;
  public bool Expired(Runner runner)=>runner.Time>=End;
  public static TickTimer CreateFromSeconds(Runner runner,float seconds)=>new(){IsRunning=true,End=runner.Time+seconds};
 }
 public class BattleManager {public static BattleManager Instance=new();public bool IsGameplayActive=true;}
 public class Player {
  public NetworkObject Object=new();public Runner Runner=new();
  public bool SimulatesMovement=true,IsAlive=true,IsReturningToMap,IsBoosting,boostNeedsRelease;
  public int SpeedStage=3,DamageCalls;public float Speed=37.5f,Hp=100;
  public float wallDamageSpeedThreshold=30,wallDamage=20,wallDamageCooldown=.5f;
  public Vector3 KnockbackVelocity;public TickTimer WallDamageTimer;
  public void TakeDamage(float damage,PlayerRef attacker){Hp-=damage;DamageCalls++;}
'@
$source += (Method 'OnControllerColliderHit') + (Method 'TimerIsActive')
$source += @'
 }
 public static class Tests {
  static int count;
  static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;}
  static bool Near(float a,float b)=>Math.Abs(a-b)<.0001f;
  public static int Run(){
   var wall=new ControllerColliderHit();
   foreach(int stage in new[]{-2,-1,0,1,2}) foreach(bool boost in new[]{false,true}) {
    // Even residual/boosted speed exceeding the old threshold is safe below stage 3.
    var p=new Player{SpeedStage=stage,Speed=stage<0?-60:60,IsBoosting=boost,KnockbackVelocity=new(2,0,3)};
    float speed=p.Speed;p.OnControllerColliderHit(wall);
    Check(p.SpeedStage==stage&&Near(p.Speed,speed)&&p.IsBoosting==boost&&!p.boostNeedsRelease,"lower stage preserves movement state: "+stage+", boost="+boost);
    Check(p.Hp==100&&p.DamageCalls==0&&!p.WallDamageTimer.IsRunning&&p.KnockbackVelocity.z==3,"lower stage has no crash penalty: "+stage);
   }
   foreach(int stage in new[]{-3,3}) foreach(float speed in new[]{12f,22.5f,30f,37.5f,60f}) {
    var p=new Player{SpeedStage=stage,Speed=Math.Sign(stage)*speed,IsBoosting=true,KnockbackVelocity=new(2,0,3)};
    p.OnControllerColliderHit(wall);
    Check(p.SpeedStage==0&&p.Speed==0&&!p.IsBoosting&&p.boostNeedsRelease,"max stage stops independently of broom/slow/flag absolute speed: "+stage+", "+speed);
    Check(p.Hp==80&&p.DamageCalls==1&&p.WallDamageTimer.IsRunning,"max stage damages exactly once: "+stage+", "+speed);
    Check(p.KnockbackVelocity.z==0&&p.KnockbackVelocity.x==2,"remove only knockback into wall");
    p.OnControllerColliderHit(wall);p.Runner.Time=10;p.OnControllerColliderHit(wall);
    Check(p.Hp==80&&p.DamageCalls==1,"continued contact at stopped stage cannot repeatedly damage");
   }
   var host=new Player{Speed=22.5f,IsBoosting=true};var owner=new Player{Speed=22.5f,IsBoosting=true};owner.Object.HasStateAuthority=false;
   host.OnControllerColliderHit(wall);owner.OnControllerColliderHit(wall);
   Check(host.SpeedStage==owner.SpeedStage&&host.Speed==owner.Speed&&host.boostNeedsRelease==owner.boostNeedsRelease,"owner prediction and host stop identically");
   Check(host.DamageCalls==1&&owner.DamageCalls==0,"only authority applies damage");
   host.SpeedStage=3;host.Speed=30;host.OnControllerColliderHit(wall);
   Check(host.SpeedStage==0&&host.Speed==0&&host.DamageCalls==1,"cooldown suppresses damage but still stops new high-stage impact");
   host.Runner.Time=1;host.SpeedStage=3;host.Speed=30;host.OnControllerColliderHit(wall);
   Check(host.DamageCalls==2,"new high-stage crash can damage after cooldown");
   var configured=new Player{wallDamage=7,wallDamageCooldown=2};configured.OnControllerColliderHit(wall);
   Check(configured.Hp==93&&Near(configured.WallDamageTimer.End,2),"existing configurable damage/cooldown retained");
   Action<Player,ControllerColliderHit>[] ignored={
    (p,h)=>p.SimulatesMovement=false,(p,h)=>p.IsAlive=false,(p,h)=>p.IsReturningToMap=true,
    (p,h)=>p.Object=null,(p,h)=>p.Object.IsValid=false,(p,h)=>h.collider=null,
    (p,h)=>h.collider.Owner=new Player(),(p,h)=>h.collider.Owner=new MagicProjectile(),
    (p,h)=>h.normal=new(0,1,0),(p,h)=>h.normal=new(0,-1,0),
    (p,h)=>h.moveDirection=new(1,0,0),(p,h)=>h.moveDirection=new(0,0,-1),
    (p,h)=>BattleManager.Instance.IsGameplayActive=false,(p,h)=>BattleManager.Instance=null
   };
   foreach(var setup in ignored){
    var p=new Player();var hit=new ControllerColliderHit();BattleManager.Instance=new();setup(p,hit);p.OnControllerColliderHit(hit);
    Check(p.SpeedStage==3&&p.Speed==37.5f&&p.Hp==100,"irrelevant contact/state is ignored");
   }
   BattleManager.Instance=new();return count;
  }
 }
}
'@
Add-Type -TypeDefinition $source
"Wall collision behavior checks passed: $([WallChecks.Tests]::Run())"
