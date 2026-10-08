# Exercises the actual TraceMagic method with recording physics stubs.
# Unity Play Mode is still required to validate scene colliders and visual feel.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$player = Get-Content Assets/Script/Player.cs -Raw
$trace = @('TraceMagic','GetDotAimDirection','ResolveDotCastDirection','IsFiniteDirection') | ForEach-Object {
    $method = [regex]::Match($player, '(?ms)^    (?:private|public) (?:static )?\w+ ' + $_ + '\([^{}]*?\)\r?\n    \{.*?^    \}').Value
    if (!$method) { throw "Missing $_" }
    $method -replace '^    private ', '    public '
}
$enum = [regex]::Match((Get-Content Assets/Script/PlayerData.cs -Raw), 'public enum MagicType\s*\{[^}]+\}').Value
$stubs = @'
using System;
namespace HitscanChecks {
public static class Mathf { public static float Max(float a,float b)=>Math.Max(a,b); }
public struct MagicStatEntry { public MagicType magic; public float range,hitscanRadius,projectileRadius,projectileSpeed; }
public struct Vector3 {
 public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
 public static Vector3 zero=>default;
 public float sqrMagnitude=>x*x+y*y+z*z;
 public Vector3 normalized=>sqrMagnitude>.0000001f?this*(1f/(float)Math.Sqrt(sqrMagnitude)):zero;
 public static Vector3 operator+(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
 public static Vector3 operator-(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
 public static Vector3 operator*(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);
}
public struct Ray {public Vector3 origin,direction;public Ray(Vector3 o,Vector3 d){origin=o;direction=d;}}
public class Transform {
 public Transform parent;
 public bool IsChildOf(Transform owner){for(var t=this;t!=null;t=t.parent)if(t==owner)return true;return false;}
}
public class MagicProjectile {}
public class Collider {
 public Transform transform=new();public Player player;public MagicProjectile projectile;
 public T GetComponentInParent<T>() where T:class=>typeof(T)==typeof(Player)?player as T:projectile as T;
}
public struct RaycastHit {public Collider collider;public float distance;public Transform transform=>collider?.transform;}
public enum QueryTriggerInteraction {Ignore}
public static class Physics {
 public static RaycastHit[] Hits=Array.Empty<RaycastHit>();
 public static int Rays,Spheres,AllRays,AllSpheres,LastMask;
 public static float LastRadius,LastRange;public static QueryTriggerInteraction LastTriggers;
 public static Vector3 LastOrigin,LastDirection;
 public static void Reset(params RaycastHit[] hits){Hits=hits;Rays=Spheres=AllRays=AllSpheres=0;LastRadius=0;}
 static int Copy(RaycastHit[] results,float range,int mask,QueryTriggerInteraction triggers){
  LastRange=range;LastMask=mask;LastTriggers=triggers;int count=Math.Min(results.Length,Hits.Length);Array.Copy(Hits,results,count);return count;
 }
 public static int SphereCastNonAlloc(Vector3 o,float r,Vector3 d,RaycastHit[] hits,float range,int mask,QueryTriggerInteraction triggers){LastOrigin=o;LastDirection=d;Spheres++;LastRadius=r;return Copy(hits,range,mask,triggers);}
 public static int RaycastNonAlloc(Vector3 o,Vector3 d,RaycastHit[] hits,float range,int mask,QueryTriggerInteraction triggers){LastOrigin=o;LastDirection=d;Rays++;return Copy(hits,range,mask,triggers);}
 public static RaycastHit[] SphereCastAll(Vector3 o,float r,Vector3 d,float range,int mask,QueryTriggerInteraction triggers){AllSpheres++;LastRadius=r;return Hits;}
 public static RaycastHit[] RaycastAll(Vector3 o,Vector3 d,float range,int mask,QueryTriggerInteraction triggers){AllRays++;return Hits;}
}
public class Player {
 public Transform transform=new();public RaycastHit[] instantShotHits=new RaycastHit[32];public int lockObstructionMask=123;
 public Vector3 MagicCastPosition=new(2,3,4);
 public MagicType Magic1=MagicType.Dark,Magic2=MagicType.Vision;
 public MagicStatEntry GetMagicStats(MagicType magic)=>new(){range=magic==MagicType.Vision?260:180};
'@
$checks = @'
}
public static class Tests {
 static int n;static void Check(bool ok,string message){if(!ok)throw new Exception(message);n++;}
 static bool Near(float a,float b)=>Math.Abs(a-b)<.0001f;
 static RaycastHit Hit(Collider collider,float distance)=>new(){collider=collider,distance=distance};
 public static int Run(){
  var p=new Player();var target=new Player();var targetCollider=new Collider{player=target};var wall=new Collider();
  var stats=new MagicStatEntry{range=100,hitscanRadius=.3f,projectileRadius=9};
  Vector3 origin=new(2,3,4),forward=new(0,0,1);
  Physics.Reset(Hit(targetCollider,10));var end=p.TraceMagic(stats,origin,forward,out var victim);
  Check(Physics.Spheres==1&&Physics.Rays==0&&Near(Physics.LastRadius,.3f),"positive hitscan radius uses SphereCastNonAlloc independently of projectile radius");
  Check(victim==target&&Near(end.z,13.98f),"sphere hit selects victim and ends the visual at contact distance");
  Check(Physics.LastMask==123&&Physics.LastTriggers==QueryTriggerInteraction.Ignore&&Near(Physics.LastRange,100),"layer mask, trigger filtering and spell range preserved");
  stats.hitscanRadius=.75f;Physics.Reset();p.TraceMagic(stats,origin,forward,out victim);
  Check(Near(Physics.LastRadius,.75f),"edited radius is applied on the next cast");
  stats.hitscanRadius=0;Physics.Reset(Hit(targetCollider,10));p.TraceMagic(stats,origin,forward,out victim);
  Check(Physics.Rays==1&&Physics.Spheres==0&&victim==target,"zero radius restores Raycast, never zero-radius SphereCast");
  stats.hitscanRadius=-1;Physics.Reset();p.TraceMagic(stats,origin,forward,out victim);
  Check(Physics.Rays==1&&Physics.Spheres==0,"negative runtime radius clamps to ray");
  stats.hitscanRadius=.3f;Physics.Reset(Hit(targetCollider,10),Hit(wall,5));end=p.TraceMagic(stats,origin,forward,out victim);
  Check(victim==null&&Near(end.z,8.98f),"near wall blocks target despite unsorted results");
  Physics.Reset(Hit(wall,20),Hit(targetCollider,10));p.TraceMagic(stats,origin,forward,out victim);
  Check(victim==target,"target before wall is hit");
  var self=new Collider{player=p,transform=new Transform{parent=p.transform}};
  var shot=new Collider{projectile=new MagicProjectile()};
  Physics.Reset(Hit(self,0),Hit(shot,1),Hit(targetCollider,10));p.TraceMagic(stats,origin,forward,out victim);
  Check(victim==target,"self and projectile colliders do not block hitscan");
  Physics.Reset(default,Hit(targetCollider,10));p.TraceMagic(stats,origin,forward,out victim);
  Check(victim==target,"invalid collider results skipped");
  Physics.Reset();end=p.TraceMagic(stats,origin,forward,out victim);
  Check(victim==null&&Near(end.x,2)&&Near(end.y,3)&&Near(end.z,104),"miss visual ends at max range");
  Physics.Reset(Hit(targetCollider,101));p.TraceMagic(stats,origin,forward,out victim);
  Check(victim==null,"out of range result cannot be a target");
  var other=new Player();Physics.Reset(Hit(new Collider{player=other},30),Hit(targetCollider,10));p.TraceMagic(stats,origin,forward,out victim);
  Check(victim==target,"only nearest character is selected");
  var crowded=new RaycastHit[33];for(int i=0;i<32;i++)crowded[i]=Hit(targetCollider,10+i);crowded[32]=Hit(wall,2);
  Physics.Reset(crowded);end=p.TraceMagic(stats,origin,forward,out victim);
  Check(Physics.AllSpheres==1&&victim==null&&Near(end.z,5.98f),"saturated sphere buffer includes omitted near wall via full fallback");
  stats.hitscanRadius=0;Physics.Reset(crowded);p.TraceMagic(stats,origin,forward,out victim);
  Check(Physics.AllRays==1&&victim==null,"zero-radius fallback preserves wall occlusion");
  stats.range=0;Physics.Reset();p.TraceMagic(stats,origin,forward,out victim);
  Check(Near(Physics.LastRange,.1f),"invalid range remains safely clamped");
  var dotDirection=new Vector3(1,.25f,0).normalized;
  foreach(var magic in new[]{MagicType.Vision,MagicType.Dark}) {
   stats.magic=magic;stats.projectileSpeed=0;
   var direction=Player.ResolveDotCastDirection(stats,dotDirection,forward);
   Check((direction-dotDirection).sqrMagnitude<.00001f,"Dot can fire sideways even before the body turns: "+magic);
   Check(Near(Player.ResolveDotCastDirection(stats,dotDirection*1.5f,forward).sqrMagnitude,1),"Valid input direction normalized: "+magic);
   foreach(var invalid in new[]{Vector3.zero,new Vector3(float.NaN,0,1),new Vector3(0,float.PositiveInfinity,0),new Vector3(0,0,100)})
    Check((Player.ResolveDotCastDirection(stats,invalid,forward)-forward).sqrMagnitude==0,"Invalid/missing Dot input safely uses existing direction: "+magic);
  }
  foreach(MagicType magic in Enum.GetValues(typeof(MagicType))) {
   if(magic==MagicType.Vision||magic==MagicType.Dark)continue;
   stats.magic=magic;
   Check((Player.ResolveDotCastDirection(stats,dotDirection,forward)-forward).sqrMagnitude==0,"Other spells retain their launch direction: "+magic);
  }
  stats.magic=MagicType.Vision;stats.projectileSpeed=75;
  Check((Player.ResolveDotCastDirection(stats,dotDirection,forward)-forward).sqrMagnitude==0,"Flying projectile behavior unchanged");
  var cameraRay=new Ray(new Vector3(4,5,-2),forward);
  Physics.Reset(Hit(self,1),Hit(targetCollider,12));
  var aim=p.GetDotAimDirection(cameraRay);
  var expected=(cameraRay.origin+forward*11.98f-p.MagicCastPosition).normalized;
  Check((aim-expected).sqrMagnitude<.00001f,"Physical root converges on Dot target, accounting for shoulder offset");
  Check(Physics.Rays==1&&Physics.Spheres==0,"Picking uses a thin camera ray, not the damaging SphereCast");
  Check(Near(Physics.LastRange,260),"Both slot ranges support same-tick slot switching");
  Physics.Reset();aim=p.GetDotAimDirection(cameraRay);
  expected=(cameraRay.origin+forward*260-p.MagicCastPosition).normalized;
  Check((aim-expected).sqrMagnitude<.00001f,"Empty Dot aims toward far point without forcing body forward");
  Physics.Reset();Check(p.GetDotAimDirection(new Ray(default,Vector3.zero)).sqrMagnitude==0&&Physics.Rays==0,"Missing ray does not query physics");
  stats=new MagicStatEntry{magic=MagicType.Dark,range=180,hitscanRadius=.3f};
  var shotDirection=Player.ResolveDotCastDirection(stats,dotDirection,forward);
  Physics.Reset(Hit(targetCollider,20),Hit(wall,2));
  p.TraceMagic(stats,p.MagicCastPosition,shotDirection,out victim);
  Check(victim==null&&Physics.Spheres==1,"Body-to-Dot shot still stops at a near wall");
  Check((Physics.LastOrigin-p.MagicCastPosition).sqrMagnitude==0&&(Physics.LastDirection-dotDirection).sqrMagnitude<.00001f,"Authoritative sweep uses physical origin and transmitted Dot direction");
  return n;
 }
}}
'@
Add-Type -TypeDefinition ($stubs + ($trace -join "`n") + $checks + "namespace HitscanChecks {" + $enum + "}")
"Hitscan query behavior checks passed: $([HitscanChecks.Tests]::Run())"

$asset = Get-Content Assets/Resources/MagicStatTable.asset -Raw
foreach ($id in @(3,4,7,10)) {
    $entry = [regex]::Match($asset, '(?ms)^  - magic: ' + $id + '\r?\n.*?(?=^  - magic: |\z)').Value
    # Designer-tuned radii (for example Vision = 1) must not be reset to the old default.
    $radius = [regex]::Match($entry, '(?m)^    hitscanRadius: ([^\r\n]+)')
    if (!$radius.Success) { throw "Missing saved radius for magic $id" }
    $value = [float]::Parse($radius.Groups[1].Value, [System.Globalization.CultureInfo]::InvariantCulture)
    if (![float]::IsFinite($value) -or $value -lt 0) { throw "Invalid saved radius for magic $id" }
}
if ($player -notmatch 'else end = TraceMagic\(stats, MagicCastPosition, transform.forward, out target\)') {
    throw 'Straight channel must use the same configurable trace'
}
$targeting = Get-Content Assets/Script/enemyLockOn.cs -Raw
if ([regex]::Matches($targeting, 'owner\.CanAcquireMagicTarget\(\)').Count -ne 2) {
    throw 'Both local input selection and candidate checks must use shared cooldown eligibility'
}
"Saved radii, channel trace and local targeting contracts passed: 6"
$manager = Get-Content Assets/Script/NetworkGameManager.cs -Raw
$input = Get-Content Assets/Script/NetworkInputData.cs -Raw
$hud = Get-Content Assets/Script/BattleHud.cs -Raw
foreach ($expected in 'castAimDirection = data.magicAimDirection;', 'pendingAimDirection = castAimDirection;',
    'castAimDirection = pendingAimDirection;', 'direction = ResolveDotCastDirection(stats, castAimDirection, direction);',
    'RPC_PresentCast(stats.magic, origin, end, 0f);') {
    if (!$player.Contains($expected)) { throw "Missing authoritative Dot integration: $expected" }
}
if (!$input.Contains('public Vector3 magicAimDirection;') -or
    !$manager.Contains('data.magicAimDirection = local.GetDotAimDirection(BattleHud.GetMagicAimRay(inputCamera));')) { throw 'Dot direction must be sent in owner input, not read from host camera' }
$aimRay = [regex]::Match($hud, '(?ms)^    public static Ray GetMagicAimRay\([^{}]*?\)\r?\n    \{.*?^    \}').Value
if (!$aimRay.Contains('instance.desiredAimMarker') -or !$aimRay.Contains('camera.ScreenPointToRay(screenPoint)') -or $aimRay.Contains('forwardAimMarker')) { throw 'Aim must use assigned Dot, not moving forward circle' }
$uiStubs = @'
namespace DotUiChecks {
using System;
public struct Vector3 {
 public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
 public float sqrMagnitude=>x*x+y*y+z*z;
 public Vector3 normalized {get {float length=(float)Math.Sqrt(sqrMagnitude);return length>0?new(x/length,y/length,z/length):default;}}
 public static Vector3 operator+(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
}
public struct Ray {public Vector3 origin,direction;public Ray(Vector3 o,Vector3 d){origin=o;direction=d;}}
public struct Vector2 {public float x,y;public Vector2(float x,float y){this.x=x;this.y=y;}public static implicit operator Vector3(Vector2 p)=>new(p.x,p.y,0);}
public struct Rect {public Vector2 center;}
public class Camera {
 public Rect pixelRect=new(){center=new(960,540)};public Vector2 sampledPoint;
 public Ray ScreenPointToRay(Vector2 point){sampledPoint=point;return new Ray(default,new Vector3((point.x-960)/540,(point.y-540)/540,1).normalized);}
}
public enum RenderMode {ScreenSpaceOverlay,ScreenSpaceCamera,WorldSpace}
public class Canvas {public Canvas rootCanvas;public RenderMode renderMode;public Camera worldCamera;}
public class GameObject {public bool activeInHierarchy=true;}
public class RectTransform {
 public GameObject gameObject=new();public Rect rect;public Vector3 position;public Canvas canvas;
 public T GetComponentInParent<T>() where T:class=>canvas as T;
 public Vector3 TransformPoint(Vector3 point)=>position+point;
}
public static class RectTransformUtility {
 public static Camera LastCamera;
 public static Vector2 WorldToScreenPoint(Camera camera,Vector3 point){LastCamera=camera;return new(point.x,point.y);}
}
public class BattleHud {
 public static BattleHud instance;public bool isActiveAndEnabled=true;public RectTransform desiredAimMarker;
'@
$uiChecks = @'
}
public static class Tests {
 static int n;static void Check(bool ok,string name){if(!ok)throw new Exception(name);n++;}
 public static int Run(){
  var camera=new Camera();var ray=BattleHud.GetMagicAimRay(camera);
  Check(camera.sampledPoint.x==960&&camera.sampledPoint.y==540&&ray.direction.z==1,"Missing HUD falls back to viewport center");
  BattleHud.instance=new();BattleHud.GetMagicAimRay(camera);
  Check(camera.sampledPoint.x==960&&camera.sampledPoint.y==540,"Missing Dot falls back to center");
  var root=new Canvas{renderMode=RenderMode.ScreenSpaceOverlay,worldCamera=new Camera()};root.rootCanvas=root;
  var dot=new RectTransform{position=new Vector3(800,400,0),rect=new Rect{center=new(10,20)},canvas=root};
  BattleHud.instance.desiredAimMarker=dot;ray=BattleHud.GetMagicAimRay(camera);
  Check(camera.sampledPoint.x==810&&camera.sampledPoint.y==420,"Assigned Dot rect center is sampled, not its pivot or mouse position");
  Check(ray.direction.x<0&&ray.direction.y<0,"Off-center Dot projects to corresponding world direction");
  Check(RectTransformUtility.LastCamera==null,"Overlay Dot uses screen coordinates");
  root.renderMode=RenderMode.ScreenSpaceCamera;dot.canvas=new Canvas{rootCanvas=root};BattleHud.GetMagicAimRay(camera);
  Check(RectTransformUtility.LastCamera==root.worldCamera,"Nested Canvas resolves root UI camera");
  dot.gameObject.activeInHierarchy=false;BattleHud.GetMagicAimRay(camera);
  Check(camera.sampledPoint.x==960,"Hidden Dot falls back safely");
  dot.gameObject.activeInHierarchy=true;BattleHud.instance.isActiveAndEnabled=false;BattleHud.GetMagicAimRay(camera);
  Check(camera.sampledPoint.x==960,"Disabled HUD is not used by network input");
  Check(BattleHud.GetMagicAimRay(null).direction.sqrMagnitude==0,"Missing camera returns no aim");
  return n;
 }
}}
'@
Add-Type -TypeDefinition ($uiStubs + $aimRay + $uiChecks)
"Dot UI projection checks passed: $([DotUiChecks.Tests]::Run())"
$scene = Get-Content Assets/Scenes/Battle.unity -Raw
$prefab = Get-Content Assets/UI/Prefab/BattleUI.prefab -Raw
if ($scene -notmatch 'desiredAimMarker: \{fileID: 2027782585\}' -or
    $scene -notmatch '(?s)&2027782585 stripped\s+RectTransform:\s+m_CorrespondingSourceObject: \{fileID: 8323471176851699074' -or
    $prefab -notmatch '(?ms)component: \{fileID: 8323471176851699074\}(?:(?!^---).)*m_Name: Dot') { throw 'Battle Dot reference changed; verify binding' }
'Dot input, launch, VFX endpoint and Battle scene binding contracts passed.'
