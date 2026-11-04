# Test production lifecycle/framing with small non-rendering Unity doubles.
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$death=Get-Content Assets/Script/Camera/CameraFollow.Death.cs -Raw
function Method($name) {
 $m=[regex]::Match($death,'(?ms)^    (?:internal|public|private) (?:static )?\w+ '+$name+'\([^{}]*\)\r?\n    \{.*?^    \}')
 if(!$m.Success){throw "Missing $name"};$m.Value.Replace('private ','public ').Replace('internal ','public ')
}
$source=@'
using System;
namespace DeathChecks {
 public static class Mathf {
  public const float Deg2Rad=(float)Math.PI/180;
  public static float Max(float a,float b)=>Math.Max(a,b);public static float Min(float a,float b)=>Math.Min(a,b);
  public static float Abs(float a)=>Math.Abs(a);public static float Clamp(float x,float a,float b)=>Math.Clamp(x,a,b);
  public static float Sin(float a)=>(float)Math.Sin(a);public static float Tan(float a)=>(float)Math.Tan(a);public static float Atan(float a)=>(float)Math.Atan(a);
  public static float SmoothStep(float a,float b,float t){t=Math.Clamp(t,0,1);return a+(b-a)*t*t*(3-2*t);}
 }
 public struct Vector3 {
  public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
  public float sqrMagnitude=>x*x+y*y+z*z;public float magnitude=>(float)Math.Sqrt(sqrMagnitude);public Vector3 normalized=>this*(1/magnitude);
  public static Vector3 operator+(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
  public static Vector3 operator-(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
  public static Vector3 operator*(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);
  public static Vector3 Lerp(Vector3 a,Vector3 b,float t)=>a+(b-a)*Math.Clamp(t,0,1);
  public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
  public static Vector3 Cross(Vector3 a,Vector3 b)=>new(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
 }
 public struct Quaternion {
  public static Quaternion Slerp(Quaternion a,Quaternion b,float t)=>b;
  public static Quaternion LookRotation(Vector3 a,Vector3 b){if(a.sqrMagnitude<.00001f||Vector3.Cross(a,b).sqrMagnitude<.00001f)throw new Exception("Invalid view basis");return new();}
 }
 public struct Bounds {public Vector3 center,extents;}
 public class Transform {
  public Vector3 position=new(0,1,-5),forward=new(0,0,1),up=new(0,1,0);public Quaternion rotation;
  public void SetPositionAndRotation(Vector3 p,Quaternion q){position=p;rotation=q;}
 }
 public class Camera {public float fieldOfView=75,aspect=16f/9;}
 public class Obj {public bool IsValid=true,HasInputAuthority=true;}
 public class Player {public Obj Object=new();public bool IsAlive;}
 public class FallingDefeatVisual {public Bounds CameraBounds=new(){center=new(0,0,0),extents=new(.5f,1,.5f)};}
 public class BattleManager {public static BattleManager Instance=new();public bool IsGameplayActive=true;}
 public static class Time {public static float time;}
 public class Probe {
  public bool isActiveAndEnabled=true,boundaryCameraFrozen=true,boundaryReturnBlending=true,deathObserving,hasFollowAnchor=true;
  public Player targetPlayer;public FallingDefeatVisual deathBody;public Camera viewCamera=new();public Transform transform=new();
  public float deathViewDistance=8,deathViewBlendSeconds=.45f,deathViewStarted;
  public Vector3 deathViewDirection,deathViewUp,deathViewStart;public Quaternion deathViewStartRotation;public int resets;
  void ResetSpeedShake(){resets++;}void ResetTopSpeedKick(){resets++;}
'@
$source+=(Method ObserveFallingDeath)+(Method UpdateDeathObservation)+(Method DeathFramingDistance)+(Method ClearDeathObservation)
$source+=@'
 }
 public static class Tests {
  static int n;static void Check(bool b,string s){if(!b)throw new Exception(s);n++;}
  public static int Run(){
   var p=new Probe();var owner=new Player();var body=new FallingDefeatVisual();p.targetPlayer=owner;
   p.ObserveFallingDeath(new Player(),body);Check(!p.deathObserving,"enemy death cannot take over local view");
   owner.Object.HasInputAuthority=false;p.ObserveFallingDeath(owner,body);Check(!p.deathObserving,"remote authority cannot start view");owner.Object.HasInputAuthority=true;
   owner.IsAlive=true;p.ObserveFallingDeath(owner,body);Check(!p.deathObserving,"living character cannot start death view");owner.IsAlive=false;
   p.ObserveFallingDeath(owner,body);Check(p.deathObserving&&p.deathBody==body&&p.resets==2&&!p.boundaryReturnBlending&&!p.boundaryCameraFrozen,"death starts isolated body tracking and clears camera effects");
   Time.time=1;body.CameraBounds=new(){center=new(0,-20,3),extents=new(.5f,1,.5f)};
   p.UpdateDeathObservation();Check(Math.Abs(p.transform.position.y+20)<.001f,"camera follows actual falling body height");
   owner.Object.IsValid=false;Check(p.UpdateDeathObservation()&&p.deathObserving,"network despawn does not interrupt baked body tracking");
   p.targetPlayer=null;Check(p.UpdateDeathObservation(),"destroyed original player does not interrupt tracking");
   p.deathBody=null;var last=p.transform.position;Check(p.UpdateDeathObservation()&&(p.transform.position-last).sqrMagnitude==0,"expired body holds last view until respawn");
   p.targetPlayer=new Player{IsAlive=true};Check(!p.UpdateDeathObservation()&&!p.deathObserving&&!p.hasFollowAnchor,"revived target exits and resets follow anchor");
   p.targetPlayer=owner;owner.Object.IsValid=true;p.ObserveFallingDeath(owner,body);BattleManager.Instance.IsGameplayActive=false;
   Check(!p.UpdateDeathObservation()&&!p.deathObserving,"round end exits death view");BattleManager.Instance.IsGameplayActive=true;
   p.ObserveFallingDeath(owner,body);p.ClearDeathObservation();Check(!p.deathObserving&&p.deathBody==null,"target reset releases death reference");
   Check(Probe.DeathFramingDistance(1,75,16f/9,8)==8,"normal body uses authored distance");
   float wide=Probe.DeathFramingDistance(5,75,16f/9,2),narrow=Probe.DeathFramingDistance(5,75,.5f,2);
   Check(narrow>wide,"portrait aspect increases distance to keep body in frame");
   Check(Probe.DeathFramingDistance(1,30,1,2)>Probe.DeathFramingDistance(1,90,1,2),"narrow FOV compensates framing");
   return n;
  }
 }
}
'@
Add-Type -TypeDefinition $source
"Death camera lifecycle and framing checks passed: $([DeathChecks.Tests]::Run())"
$follow=Get-Content Assets/Script/Camera/CameraFollow.cs -Raw
foreach($name in @('SetTarget','OnDisable')) {
 if($follow -notmatch ($name+'\([^)]*\)\s*\{\s*ClearDeathObservation\(\)')){throw "Missing cleanup: $name"}
}
if($follow -notmatch 'UpdateCamera\(bool forceSnap\)\s*\{\s*//[^\r\n]*\r?\n\s*if \(deathObserving\) return;'){throw 'Fusion render callback can overwrite death view'}
if($follow -notmatch 'LateUpdate\(\)\s*\{\s*if \(UpdateDeathObservation\(\)\) return;'){throw 'Tracking must continue before original target null check'}
