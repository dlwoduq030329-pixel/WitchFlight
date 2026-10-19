# Production portal methods with deterministic physics/Fusion doubles.
# Does not replace Unity collision geometry or two-client Play Mode testing.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$player = Get-Content Assets/Script/Player.cs -Raw
$portal = Get-Content Assets/Script/Portal.cs -Raw
$camera = Get-Content Assets/Script/Camera/CameraFollow.cs -Raw
function Method([string]$source, [string]$name) {
    $m = [regex]::Match($source, '(?ms)^    (?:private|public) (?:static )?\w+ ' + $name + '\([^{}]*?\)\r?\n    \{.*?^    \}')
    if (!$m.Success) { throw "Missing method: $name" }
    $m.Value -replace '^    private ', '    public '
}
$source = @'
using System;
using V = System.Numerics.Vector3;
using Q = System.Numerics.Quaternion;
namespace PortalChecks {
 public struct Vector3 {
  public V v; public float x=>v.X; public float y=>v.Y; public float z=>v.Z;
  public Vector3(float x,float y,float z){v=new V(x,y,z);} public Vector3(V value){v=value;}
  public static Vector3 zero=>new(0,0,0); public static Vector3 forward=>new(0,0,1);
  public static Vector3 operator+(Vector3 a,Vector3 b)=>new(a.v+b.v);
  public static Vector3 operator-(Vector3 a,Vector3 b)=>new(a.v-b.v);
  public static Vector3 operator*(Vector3 a,float b)=>new(a.v*b);
 }
 public struct Quaternion {
  public Q q; public Vector3 eulerAngles; public static Quaternion identity=>new(){q=Q.Identity};
  public static Quaternion Euler(float x,float y,float z)=>new(){q=Q.CreateFromYawPitchRoll(y*MathF.PI/180,x*MathF.PI/180,z*MathF.PI/180),eulerAngles=new(x,y,z)};
  public static Quaternion Inverse(Quaternion q)=>new(){q=Q.Inverse(q.q)};
  public static Quaternion operator*(Quaternion a,Quaternion b)=>new(){q=a.q*b.q};
  public static Vector3 operator*(Quaternion q,Vector3 v)=>new(V.Transform(v.v,q.q));
 }
 public static class Mathf {public static float Max(float a,float b)=>Math.Max(a,b);public static float Abs(float f)=>Math.Abs(f);public static int Abs(int i)=>Math.Abs(i);}
 public class Transform {
  public Vector3 position; public Quaternion rotation=Quaternion.identity; public Transform parent;
  public bool IsChildOf(Transform t)=>this==t || (parent!=null && parent.IsChildOf(t));
 }
 public class GameObject {public int layer;}
 public class Collider {public Transform transform=new();public GameObject gameObject=new();public object owner;public bool ignored,trigger;public T GetComponentInParent<T>() where T:class=>owner as T;}
 public class CharacterController:Collider {public bool enabled=true;}
 public class MagicProjectile {}
 public enum QueryTriggerInteraction {Ignore}
 public static class Physics {public static bool GetIgnoreLayerCollision(int a,int b)=>b==8;public static bool GetIgnoreCollision(Collider a,Collider b)=>b.ignored;}
 public class PhysicsScene {
  public Collider[] hits=Array.Empty<Collider>();public Vector3 a,b;public float radius;public int queries;
  public int OverlapCapsule(Vector3 x,Vector3 y,float r,Collider[] result,int mask,QueryTriggerInteraction interaction){
   a=x;b=y;radius=r;queries++;int n=0;foreach(var h in hits){if(h!=null&&h.trigger)continue;if(n==result.Length)break;result[n++]=h;}return n;
  }
 }
 public class NetworkRunner {public bool IsRunning=true;public float Time;public PhysicsScene scene=new();public PhysicsScene GetPhysicsScene()=>scene;}
 public class NetworkObject {public bool IsValid=true,HasStateAuthority=true,HasInputAuthority=true;}
 public struct TickTimer {
  public bool active;public float end;public bool ExpiredOrNotRunning(NetworkRunner r)=>!active||r.Time>=end;
  public static TickTimer CreateFromSeconds(NetworkRunner r,float s)=>new(){active=true,end=r.Time+s};
 }
 public class NetworkTransform {public Vector3 position;public Quaternion rotation;public int teleports;public bool Throw;public void Teleport(Vector3 p,Quaternion r){if(Throw)throw new Exception("test");position=p;rotation=r;teleports++;}}
 public class BattleManager {public static BattleManager Instance=new();public bool IsGameplayActive=true;}
 public static class Debug {public static void LogWarning(string s,object context){}}
 public static class BattleFlag {
  public static void GetCapsule(Player p,out Vector3 c,out Vector3 h,out float r){c=p.transform.position+p.transform.rotation*new Vector3(0,.6f,0);h=p.transform.rotation*new Vector3(0,.38f,0);r=.22f;}
 }
 public class Player {
  public NetworkObject Object=new();public NetworkRunner Runner=new();public NetworkTransform portalNetworkTransform=new();
  public Transform transform=new();public GameObject gameObject=new();public CharacterController characterController=new();
  public bool IsAlive=true,IsReturningToMap,hasPendingPortalTeleport,controllerNeedsRenderReset=true;
  public Vector3 pendingPortalPosition,KnockbackVelocity;public Quaternion pendingPortalRotation,PortalExitRotation;
  public TickTimer portalReentryTimer;public int PortalExitSequence,SpeedStage=3;public float Speed=45,currentPitch,currentYaw,currentAimTurnSpeed=10,currentTurnSpeed=20;
  public Collider[] portalExitOverlaps=new Collider[64];public T GetComponent<T>() where T:class=>portalNetworkTransform as T;
'@
$queue = [regex]::Match($player, '(?ms)^    public bool TryQueuePortalTeleport\(Vector3 destination, Quaternion exitRotation, float reentryDelay\)\r?\n    \{.*?^    \}').Value
if (!$queue) { throw 'Missing exit rotation queue' }
$source += $queue
foreach ($method in @('CanOccupyPortalExit','ApplyPortalTeleport','NormalizePitch')) { $source += Method $player $method }
$source += @'
 }
 public class Portal {public Transform destination=new(),exitDirection;public float exitForwardOffset=1.5f;
'@
$source += Method $portal 'GetExitPose'
$source += @'
 }
 public class CameraFollow {
  public Player targetPlayer;public int observedPortalExitSequence,shakeResets,kickResets;
  public Quaternion mouseAimRotation;public float mouseOrbitYaw=30;
  public bool boundaryCameraFrozen=true,boundaryReturnBlending=true,hasFollowAnchor=true;
  void ResetSpeedShake(){shakeResets++;}void ResetTopSpeedKick(){kickResets++;}
'@
$source += Method $camera 'SynchronizePortalExit'
$source += @'
 }
 public static class Tests {
  static int n;static void Check(bool b,string name){if(!b)throw new Exception(name);n++;}
  static bool Near(float a,float b)=>Math.Abs(a-b)<.0001f;
  static bool Near(Vector3 a,Vector3 b)=>V.Distance(a.v,b.v)<.0001f;
  static bool Same(Quaternion a,Quaternion b)=>Math.Abs(Q.Dot(a.q,b.q))>.9999f;
  public static int Run(){
   var r=Quaternion.Euler(0,90,0);var dest=new Vector3(100,20,30);var portal=new Portal();
   portal.destination.position=dest;portal.destination.rotation=r;
   portal.GetExitPose(out var pos,out var rot);
   Check(Near(pos,new Vector3(101.5f,20,30))&&Same(rot,r),"destination +Z and 1.5m default");
   portal.exitDirection=new(){rotation=Quaternion.Euler(0,180,0),position=new(999,999,999)};
   portal.GetExitPose(out pos,out rot);
   Check(Near(pos,new Vector3(100,20,28.5f))&&Same(rot,portal.exitDirection.rotation),"override uses rotation only, not position");
   portal.exitForwardOffset=-3;portal.GetExitPose(out pos,out rot);Check(Near(pos,dest),"offset cannot point backwards");
   foreach(int stage in new[]{-3,-2,-1,0,1,2,3}) {
    var p=new Player{SpeedStage=stage,Speed=stage*15,KnockbackVelocity=new(1,2,3)};
    Check(p.TryQueuePortalTeleport(dest,r,.5f),"queue accepted "+stage);
    Check(p.portalNetworkTransform.teleports==0&&p.hasPendingPortalTeleport,"callback only queues");
    Check(!p.TryQueuePortalTeleport(dest,r,.5f),"duplicate request rejected");p.ApplyPortalTeleport();
    Check(p.portalNetworkTransform.teleports==1&&Near(p.portalNetworkTransform.position,dest)&&Same(p.portalNetworkTransform.rotation,r),"network position and rotation together");
    Check(p.SpeedStage==Math.Abs(stage)&&Near(p.Speed,Math.Abs(stage*15)),"reverse exits forward without extra acceleration");
    Check(Near(p.KnockbackVelocity,Vector3.zero)&&p.currentAimTurnSpeed==0&&p.currentTurnSpeed==0,"old wall impulse and turn rate cleared");
    Check(p.currentYaw==90&&p.currentPitch==0&&p.PortalExitSequence==1&&Same(p.PortalExitRotation,r),"legacy heading and replicated camera event");
    Check(p.characterController.enabled&&!p.controllerNeedsRenderReset&&!p.hasPendingPortalTeleport,"controller restored");
    Check(!p.TryQueuePortalTeleport(dest,r,.5f),"reentry delay enforced");p.Runner.Time=1;
    Check(p.TryQueuePortalTeleport(dest,r,.5f),"reentry expires");
   }
   var blocked=new Player();blocked.Runner.scene.hits=new[]{new Collider()};
   Check(!blocked.TryQueuePortalTeleport(dest,r,.5f)&&!blocked.portalReentryTimer.active&&!blocked.hasPendingPortalTeleport,"blocked exit consumes no request/cooldown");
   var race=new Player();race.TryQueuePortalTeleport(dest,r,.5f);race.Runner.scene.hits=new[]{new Collider()};race.ApplyPortalTeleport();
   Check(race.portalNetworkTransform.teleports==0&&race.PortalExitSequence==0&&!race.hasPendingPortalTeleport,"moving obstacle checked again at tick");
   var filtered=new Player();filtered.Runner.scene.hits=new[]{new Collider{trigger=true},new Collider{owner=new MagicProjectile()},new Collider{owner=new Player()},new Collider{ignored=true},new Collider{gameObject=new(){layer=8}},new Collider{transform=new(){parent=filtered.transform}}};
   Check(filtered.CanOccupyPortalExit(dest,r),"triggers, projectiles, players, ignored pairs and self do not block");
   filtered.Runner.scene.hits=new Collider[64];Check(!filtered.CanOccupyPortalExit(dest,r),"full query buffer fails safe");
   var shape=new Player();shape.transform.position=new(12,8,3);shape.transform.rotation=Quaternion.Euler(12,130,30);
   shape.CanOccupyPortalExit(dest,Quaternion.Euler(0,0,90));var scene=shape.Runner.scene;
   Check(Near((scene.a+scene.b)*.5f,dest+new Vector3(-.6f,0,0)),"rotated capsule center, not old pose");
   Check(Near(scene.b-scene.a,new Vector3(-.76f,0,0))&&Near(scene.radius,.27f),"rotated capsule segment and wall margin");
   for(int reason=0;reason<7;reason++){
    var p=new Player();switch(reason){case 0:p.Object.HasStateAuthority=false;break;case 1:p.Object.IsValid=false;break;case 2:p.Runner.IsRunning=false;break;case 3:p.IsAlive=false;break;case 4:p.IsReturningToMap=true;break;case 5:BattleManager.Instance.IsGameplayActive=false;break;case 6:p.portalNetworkTransform=null;break;}
    Check(!p.TryQueuePortalTeleport(dest,r,.5f),"authority/lifecycle guard "+reason);BattleManager.Instance.IsGameplayActive=true;
   }
   var disabled=new Player();disabled.characterController.enabled=false;disabled.TryQueuePortalTeleport(dest,r,.5f);disabled.ApplyPortalTeleport();Check(!disabled.characterController.enabled,"does not enable intentionally disabled controller");
   var error=new Player();error.TryQueuePortalTeleport(dest,r,.5f);error.portalNetworkTransform.Throw=true;
   try{error.ApplyPortalTeleport();}catch(Exception){}Check(error.characterController.enabled,"controller restored even on teleport error");
   var owner=new Player{PortalExitSequence=1,PortalExitRotation=r};var cam=new CameraFollow{targetPlayer=owner};cam.SynchronizePortalExit();
   Check(Same(cam.mouseAimRotation,r)&&cam.observedPortalExitSequence==1&&cam.mouseOrbitYaw==0,"owner camera adopts replicated exit heading");
   Check(!cam.hasFollowAnchor&&!cam.boundaryCameraFrozen&&!cam.boundaryReturnBlending&&cam.shakeResets==1&&cam.kickResets==1,"short teleports also snap and clear presentation history");
   cam.mouseAimRotation=Quaternion.Euler(0,110,0);cam.SynchronizePortalExit();Check(cam.shakeResets==1&&Same(cam.mouseAimRotation,Quaternion.Euler(0,110,0)),"ordinary mouse aim not overwritten every frame");
   owner.PortalExitSequence++;owner.PortalExitRotation=Quaternion.Euler(0,180,0);cam.SynchronizePortalExit();Check(cam.shakeResets==2&&Same(cam.mouseAimRotation,owner.PortalExitRotation),"second portal processed once");
   owner.Object.HasInputAuthority=false;owner.PortalExitSequence++;cam.SynchronizePortalExit();Check(cam.observedPortalExitSequence==2,"remote observer cannot rotate local camera");
   return n;
  }
 }
}
'@
Add-Type -TypeDefinition $source
"Portal direction, queue, clearance and camera checks passed: $([PortalChecks.Tests]::Run())"

$inputSource = Get-Content Assets/Script/NetworkInputData.cs -Raw
$manager = Get-Content Assets/Script/NetworkGameManager.cs -Raw
if ($player -notmatch '\[Networked\] public int PortalExitSequence' -or $player -notmatch '\[Networked\] public Quaternion PortalExitRotation') { throw 'Exit snapshot must replicate' }
$turn = Method $player 'PlayerTurn'
if ($turn.IndexOf('if (data.portalExitSequence != PortalExitSequence) return;') -lt 0 -or $turn.IndexOf('if (data.portalExitSequence') -gt $turn.IndexOf('if (data.steerToAim')) { throw 'Stale packets can steer back into wall' }
if ($inputSource -notmatch 'public int portalExitSequence;' -or $manager -notmatch 'data.portalExitSequence = local.PortalExitSequence;' -or $manager -notmatch 'follow.SynchronizePortalExit\(\);') { throw 'Camera/input acknowledgement missing' }
if ($portal -notmatch 'TryQueuePortalTeleport\(exitPosition, exitRotation, reentryDelay\)') { throw 'Portal must submit both position and rotation' }
if ($player -notmatch 'Object.HasStateAuthority && hasPendingPortalTeleport') { throw 'Only authority may apply portal teleport' }
'Portal networking integration contracts passed: 5'
