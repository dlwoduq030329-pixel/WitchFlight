# Production formation controller with lightweight visual stubs; no live Unity render.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$source = (Get-Content Assets/Script/ProjectileFormationVisual.cs -Raw) -replace '^using UnityEngine;\s*', ''
$player = Get-Content Assets/Script/Player.cs -Raw
$origin = [regex]::Match($player, 'public Vector3 MagicCastPosition =>[^;]+;').Value
if (!$origin) { throw 'Missing cast root fallback property' }
$stubs = @'
using System;
using System.Collections.Generic;
namespace UnityEngine {
 public class Object {public string name;public bool destroyed;public static void Destroy(Object obj){if(obj!=null)obj.destroyed=true;}}
 public class Material:Object {public static int Count;public Material(){Count++;}}
 public struct Color {public float r,g,b,a;public Color(float r,float g,float b,float a){this.r=r;this.g=g;this.b=b;this.a=a;}}
 public static class Time {public static float time;}
 public static class Mathf {
  public const float PI=(float)Math.PI;
  public static float Max(float a,float b)=>Math.Max(a,b);public static int Max(int a,int b)=>Math.Max(a,b);
  public static float Clamp(float v,float a,float b)=>Math.Clamp(v,a,b);public static int Clamp(int v,int a,int b)=>Math.Clamp(v,a,b);
  public static float Clamp01(float v)=>Math.Clamp(v,0,1);public static float Lerp(float a,float b,float t)=>a+(b-a)*Clamp01(t);
  public static float SmoothStep(float a,float b,float t){t=Clamp01(t);return a+(b-a)*t*t*(3-2*t);}
  public static float Cos(float a)=>(float)Math.Cos(a);public static float Sin(float a)=>(float)Math.Sin(a);
 }
 public struct Vector3 {
  public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
  public static Vector3 one=>new(1,1,1);public static Vector3 forward=>new(0,0,1);
  public float sqrMagnitude=>x*x+y*y+z*z;
  public static Vector3 operator+(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
  public static Vector3 operator-(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
  public static Vector3 operator*(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);
  public static Vector3 operator*(float b,Vector3 a)=>a*b;
  public static Vector3 Lerp(Vector3 a,Vector3 b,float t)=>a+(b-a)*Mathf.Clamp01(t);
 }
 public struct Quaternion {public static Quaternion LookRotation(Vector3 d)=>default;public static Quaternion operator*(Quaternion a,Quaternion b)=>default;public static Vector3 operator*(Quaternion a,Vector3 b)=>b;}
 public class Transform {
  public GameObject gameObject;public Vector3 position,localScale=Vector3.one,forward=Vector3.forward;public Quaternion rotation;public Transform parent;
  public void SetParent(Transform p,bool world){parent=p;}public void SetPositionAndRotation(Vector3 p,Quaternion r){position=p;rotation=r;}
 }
 public class Collider:Object {public bool enabled=true;}
 public class Rigidbody:Object {public bool detectCollisions=true,isKinematic,useGravity=true;}
 public class ParticleSystem:Object {public class Module {public bool enabled=true;}public Module collision=new(),trigger=new();}
 public class Renderer:Object {public Material sharedMaterial;}
 public class TrailRenderer:Renderer {public float time,startWidth,endWidth;public Color startColor,endColor;}
 public enum PrimitiveType {Sphere}
 public class GameObject:Object {
  public static int Primitives,Trails;public Transform transform;public bool activeSelf=true;private readonly Dictionary<Type,object> components=new();
  public GameObject(string name=""){this.name=name;transform=new(){gameObject=this};}
  public static GameObject CreatePrimitive(PrimitiveType type){Primitives++;var g=new GameObject();g.AddComponent<Collider>();g.AddComponent<Renderer>();return g;}
  public T AddComponent<T>() where T:new(){var c=new T();components[typeof(T)]=c;if(c is TrailRenderer)Trails++;return c;}
  public T GetComponent<T>() where T:class=>components.TryGetValue(typeof(T),out var c)?c as T:null;
  public T[] GetComponentsInChildren<T>(bool inactive) where T:class=>GetComponent<T>() is T c?new[]{c}:Array.Empty<T>();
  public void SetActive(bool active){activeSelf=active;}
 }
}
namespace FormationChecks {
 using UnityEngine;
 using Object=UnityEngine.Object;
 public struct MagicStatEntry {public float projectileSpeed;public int magic;}
 public class MagicStatTable {
  public int projectileSatelliteCount=6;public GameObject projectileSatelliteVfxPrefab;
  public float projectileCoreVisualScale=1.8f,projectileSatelliteVisualScale=.35f,projectileSatelliteBackDistance=2,projectileSatelliteBackSeconds=.18f;
  public float projectileSatelliteSpeedMultiplier=2.5f,projectileSatelliteSpread=1,projectileSatelliteLeadDistance=3,projectileMergeStartDistance=18,projectileMergeEndDistance=3;
 }
 public class Player {public Transform transform=new GameObject().transform;public Vector3 LockAimPoint=new(0,.8f,0);}
 public class ProjectilePredictionView {
  public Transform transform=new GameObject().transform;public int Sweeps;public bool Block;
  public Vector3 ClampSatelliteMotion(Vector3 from,Vector3 to,out bool hit){Sweeps++;hit=Block;return hit?from:to;}
 }
 public static class CombatPresentation {
  public static int Authored;
  public static Color MagicColor(int magic)=>new(1,.2f,.1f,1);
  public static Material CreateEffectMaterial(Color c)=>new();
  public static GameObject InstantiateVfx(GameObject prefab,Vector3 p,Quaternion q,Transform parent){
   if(prefab==null)return null;Authored++;var view=new GameObject();view.AddComponent<Collider>();view.AddComponent<Rigidbody>();view.AddComponent<ParticleSystem>();view.transform.SetParent(parent,false);view.transform.SetPositionAndRotation(p,q);return view;
  }
 }
 public class OriginProbe {public Transform magicCastRoot;public Vector3 LockAimPoint=new(2,3,4);
'@
$tests = @'
public static class Tests {
 static int n;static void Check(bool ok,string msg){if(!ok)throw new Exception(msg);n++;}
 static bool Near(float a,float b)=>Math.Abs(a-b)<.001f;
 static T Field<T>(object obj,string name)=>(T)obj.GetType().GetField(name,System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(obj);
 public static int Run(){
  var origin=new OriginProbe();Check(Near(origin.MagicCastPosition.y,3)&&Near(origin.MagicCastPosition.z,4),"unassigned root uses body aim point");
  origin.magicCastRoot=new GameObject().transform;origin.magicCastRoot.position=new(7,8,9);Check(Near(origin.MagicCastPosition.x,7)&&Near(origin.MagicCastPosition.z,9),"assigned root uses its world position");
  origin.magicCastRoot.position=new(-2,10,30);Check(Near(origin.MagicCastPosition.z,30),"moving root changes origin immediately");
  origin.magicCastRoot=null;Check(Near(origin.MagicCastPosition.x,2),"clearing reference restores body origin");
  var initial=new Vector3(0,0,0);var retreat=new Vector3(0,0,-2);var lead=new Vector3(0,0,30);var f=Vector3.forward;
  Check(Near(ProjectileFormationVisual.FlightPosition(0,.18f,.2f,initial,retreat,f,lead,f,2).z,0),"birth starts at body/back position");
  Check(Near(ProjectileFormationVisual.FlightPosition(.09f,.18f,.2f,initial,retreat,f,lead,f,2).z,-1),"first phase truly moves backwards before turning");
  Check(Near(ProjectileFormationVisual.FlightPosition(.18f,.18f,.2f,initial,retreat,f,lead,f,2).z,-2),"backward travel reaches configured distance");
  Check(ProjectileFormationVisual.FlightPosition(.18001f,.18f,.2f,initial,retreat,f,lead,f,2).z<-2,"turn begins with backward tangent, no snap forward");
  Check(Near(ProjectileFormationVisual.FlightPosition(.38f,.18f,.2f,initial,retreat,f,lead,f,2).z,30),"curved turn catches up to the leading formation");
  Check(Near(ProjectileFormationVisual.FlightPosition(1,.18f,.2f,initial,retreat,f,lead,f,2).z,30),"after catchup satellites maintain a forward lead");
  Check(ProjectileFormationVisual.CatchupSeconds(75,3,2,3,.18f)<ProjectileFormationVisual.CatchupSeconds(75,1.5f,2,3,.18f),"higher speed multiplier overtakes sooner");
  Check(!float.IsNaN(ProjectileFormationVisual.CatchupSeconds(0,0,0,0,0)),"invalid speed settings stay finite");
  Check(Near(ProjectileFormationVisual.MergeProgress(100,18,3),0)&&Near(ProjectileFormationVisual.MergeProgress(18,18,3),0),"far from target satellites remain separate");
  Check(Near(ProjectileFormationVisual.MergeProgress(10.5f,18,3),.5f),"halfway through merge distance converges halfway");
  Check(Near(ProjectileFormationVisual.MergeProgress(3,18,3),1)&&Near(ProjectileFormationVisual.MergeProgress(0,18,3),1),"near target merge completes");
  Check(!float.IsNaN(ProjectileFormationVisual.MergeProgress(3,0,10)),"reversed merge thresholds are safely clamped");
  Vector3 sum=default;for(int i=0;i<6;i++){var v=ProjectileFormationVisual.RingOffset(i,6);sum+=v;Check(Near(v.sqrMagnitude,1),"satellite ring unit offset "+i);}
  Check(sum.sqrMagnitude<.00001f,"six launch directions distribute evenly around the back");
  var host=new ProjectilePredictionView();var owner=new Player();var core=new GameObject().transform;var table=new MagicStatTable();
  Time.time=0;int materials=Material.Count,primitives=GameObject.Primitives;
  var visual=new ProjectileFormationVisual(host,core,owner,new(){projectileSpeed=75},table);
  var satellites=Field<Transform[]>(visual,"satellites");var root=Field<GameObject>(visual,"root");var material=Field<Material>(visual,"ownedMaterial");
  Check(satellites.Length==6&&GameObject.Primitives-primitives==6,"one core gets six local fallback satellites");
  Check(Material.Count-materials==1,"six satellites share one material");
  Check(Near(core.localScale.x,1.8f)&&Near(satellites[0].localScale.x,.105f),"core is larger and satellites use editable scale");
  foreach(var satellite in satellites)Check(!satellite.gameObject.GetComponent<Collider>().enabled,"cosmetic satellite collider disabled");
  Time.time=.09f;visual.Update(new(0,.8f,10),f,100);
  Check(Near(satellites[0].position.z,-1.15f),"backward phase is anchored in world space, not dragged forward by fast core");
  Time.time=.8f;visual.Update(new(0,.8f,40),f,100);Check(Near(satellites[0].position.z,43),"small satellites overtake core after the turn");
  visual.Update(new(0,.8f,40),f,10.5f);Check(Near(satellites[0].position.z,41.5f)&&Near(satellites[0].localScale.x,.0525f),"approaching target brings satellites inward and shrinks them");
  visual.Update(new(0,.8f,40),f,3);Check(!root.activeSelf,"completed merge hides all small satellites");
  visual.Update(new(0,.8f,40),f,100);Check(!root.activeSelf,"merged satellites do not reappear when target retreats");
  visual.Dispose();Check(root.destroyed&&material.destroyed,"despawn releases formation and owned material");
  Check(Near(core.localScale.x,1),"disabling formation restores original core scale");
  host.Block=true;Time.time=0;visual=new ProjectileFormationVisual(host,new GameObject().transform,owner,new(){projectileSpeed=75},table);
  Time.time=.09f;visual.Update(new(0,0,10),f,100);satellites=Field<Transform[]>(visual,"satellites");
  Check(Array.TrueForAll(satellites,s=>s.gameObject.activeSelf)&&host.Sweeps==0,"small satellites ignore obstacles and never query collision sweeps");
  Check(Near(satellites[0].position.z,-1.15f),"obstacles cannot stop the backward launch arc");
  Time.time=.8f;visual.Update(new(0,.8f,40),f,100);Check(Near(satellites[0].position.z,43),"small satellites pass through obstacles and retain their forward lead");
  visual.Update(new(0,.8f,40),f,10.5f);Check(Near(satellites[0].position.z,41.5f),"collision-free satellites still converge toward the core");
  root=Field<GameObject>(visual,"root");visual.SetVisible(false);Check(!root.activeSelf,"core impact immediately hides satellite group");visual.Dispose();
  table.projectileSatelliteCount=100;table.projectileSatelliteVfxPrefab=new GameObject();primitives=GameObject.Primitives;
  visual=new ProjectileFormationVisual(host,new GameObject().transform,owner,new(){projectileSpeed=75},table);
  satellites=Field<Transform[]>(visual,"satellites");
  Check(satellites.Length==12&&GameObject.Primitives==primitives&&CombatPresentation.Authored==12,"authored VFX honored and count bounded");
  foreach(var satellite in satellites){var v=satellite.gameObject;var body=v.GetComponent<Rigidbody>();var ps=v.GetComponent<ParticleSystem>();
   Check(!v.GetComponent<Collider>().enabled&&!body.detectCollisions&&body.isKinematic&&!body.useGravity,"authored satellite cannot collide or fall under physics");
   Check(!ps.collision.enabled&&!ps.trigger.enabled,"authored satellite particles cannot collide or trigger");}
  visual.Dispose();
  return n;
 }
}
}
'@
Add-Type -TypeDefinition ($stubs + $origin + "}`n" + $source + $tests)
"Magic cast root / satellite behavior checks passed: $([FormationChecks.Tests]::Run())"

foreach ($file in @('Assets/Ch/ChPrefab.prefab','Assets/Prefab/MagicDummy_Immortal.prefab','Assets/Prefab/MagicDummy_LockOnAttacker.prefab')) {
    $yaml = Get-Content $file -Raw
    $anchors = [regex]::Matches($yaml, '(?m)^--- !u!\d+ &(-?\d+)') | ForEach-Object { $_.Groups[1].Value }
    if (($anchors | Group-Object | Where-Object Count -gt 1).Count -gt 0) { throw "Duplicate prefab anchor: $file" }
    if ([regex]::Matches($yaml,'m_Name: MagicCastRoot\r?$',[System.Text.RegularExpressions.RegexOptions]::Multiline).Count -ne 1) { throw "Missing/duplicate root: $file" }
    if ($yaml -notmatch 'magicCastRoot: \{fileID: 862026100900000002\}') { throw "Player reference missing: $file" }
    if ($yaml -notmatch '(?m)^  - \{fileID: 862026100900000002\}') { throw "Root not in hierarchy: $file" }
    $rootBlock = [regex]::Match($yaml,'(?ms)^--- !u!4 &862026100900000002\r?\n.*?(?=^---|\z)').Value
    $parent = if ($file -like '*ChPrefab*') {'5553512542147760588'} else {'1017679280241420192'}
    if ($rootBlock -notmatch ('m_Father: \{fileID: '+$parent+'\}')) { throw "Cast root must be a direct Player child: $file" }
}
$prediction = Get-Content Assets/Script/ProjectilePredictionView.cs -Raw
$presentation = Get-Content Assets/Script/CombatPresentation.cs -Raw
if ($player -notmatch 'Vector3 origin = MagicCastPosition;' -or $prediction -notmatch 'Create\(entry, player.MagicCastPosition' -or
    $prediction -notmatch 'transform.position = owner.MagicCastPosition;' -or
    $presentation -notmatch 'channelVisual.SetEndpoints\(owner.MagicCastPosition, end\)') { throw 'An attack origin path still bypasses the root' }
if ($prediction -notmatch 'if \(formation == null\)\s+formation = new ProjectileFormationVisual' -or
    $prediction -notmatch 'formation\?\.Dispose\(\)' -or $prediction -notmatch 'stats.effect == MagicEffectKind.Mine') { throw 'Formation lifetime / mine exclusion missing' }
if ($source -match 'Runner\.Spawn|ReceiveMagicHit\(|RPC_|TakeDamage\(') { throw 'Cosmetic satellites must not spawn gameplay or apply damage' }
if ($source -match 'ClampSatelliteMotion|Physics\.|blocked\[' -or $prediction -match 'ClampSatelliteMotion') { throw 'Satellite collision sweep must be removed' }
if ($source -notmatch 'GetComponentsInChildren<Collider>\(true\)' -or $source -notmatch 'GetComponentsInChildren<ParticleSystem>\(true\)') { throw 'Inactive child VFX must also be collision-free' }
if ($prediction -notmatch 'Vector3 next = Sweep\(transform.position, transform.position \+ direction \* step' -or
    $prediction -notmatch 'Physics.SphereCastNonAlloc') { throw 'Core projectile collision prediction must remain intact' }
"Three prefab root bindings and launch/prediction/beam/cosmetic-only contracts passed."
