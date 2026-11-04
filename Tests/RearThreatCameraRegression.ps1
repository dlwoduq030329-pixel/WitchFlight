# Production selection, visibility and concave mask triangulation without Unity rendering.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$rear = Get-Content Assets/Script/Camera/RearThreatCamera.cs -Raw
$bubble = Get-Content Assets/Script/Camera/ThreatBubbleGraphic.cs -Raw
function Method($text,$name) {
 $m=[regex]::Match($text,'(?ms)^    (?:internal|private) (?:static )?[\w\[\]]+ '+$name+'\([^{}]*\)\r?\n    \{.*?^    \}')
 if(!$m.Success){throw "Missing method $name"}; $m.Value.Replace('private ','public ').Replace('internal ','public ')
}
$priority = [regex]::Match($rear,'(?m)^    private static int ThreatPriority[^\r\n]+').Value
$cross = [regex]::Match($bubble,'(?ms)^    private static float Cross\([^;]+;').Value
$source=@'
using System;using System.Collections.Generic;
namespace RearChecks {
 public static class Mathf {
  public static int Clamp(int v,int lo,int hi)=>Math.Min(hi,Math.Max(lo,v));
  public static float Max(float a,float b)=>Math.Max(a,b);
  public static float Floor(float v)=>(float)Math.Floor(v);
 }
 public struct Vector3 {
  public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
  public float sqrMagnitude=>x*x+y*y+z*z;
  public float magnitude=>(float)Math.Sqrt(sqrMagnitude);
  public Vector3 normalized=>magnitude>0?new(x/magnitude,y/magnitude,z/magnitude):new();
  public static Vector3 operator-(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
  public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
 }
 public struct Vector2 {
  public float x,y;public Vector2(float x,float y){this.x=x;this.y=y;}
  public static Vector2 operator*(float s,Vector2 v)=>new(s*v.x,s*v.y);
  public static Vector2 operator+(Vector2 a,Vector2 b)=>new(a.x+b.x,a.y+b.y);
 }
 public class Transform {public Transform parent;public bool IsChildOf(Transform root)=>this==root||(parent!=null&&parent.IsChildOf(root));}
 public class GameObject {public bool activeSelf;public Transform transform=new();public List<Renderer> visuals=new();public void SetActive(bool b){activeSelf=b;}
  public void GetComponentsInChildren<T>(bool inactive,List<T> result){foreach(var r in visuals)if(r is T item)result.Add(item);}}
 public class Renderer {public bool enabled=true,forceRenderingOff;}
 public class RawImage {public bool enabled;}
 public class Player {}
 public class MagicProjectile {}
 public class Collider {
  public object owner;
  public T GetComponentInParent<T>() where T:class =>owner as T;
 }
 public struct RaycastHit {public Collider collider;}
 public enum QueryTriggerInteraction {Ignore}
 public static class Physics {
  public static Collider[] overlaps=Array.Empty<Collider>();public static int overlapQueries;
  public static RaycastHit[] hits=Array.Empty<RaycastHit>();
  public static int OverlapSphereNonAlloc(Vector3 o,float r,Collider[] buffer,int mask,QueryTriggerInteraction q){overlapQueries++;int n=Math.Min(buffer.Length,overlaps.Length);Array.Copy(overlaps,buffer,n);return n;}
  public static Collider[] OverlapSphere(Vector3 o,float r,int mask,QueryTriggerInteraction q)=>overlaps;
  public static int RaycastNonAlloc(Vector3 o,Vector3 d,RaycastHit[] buffer,float distance,int mask,QueryTriggerInteraction q){int n=Math.Min(buffer.Length,hits.Length);Array.Copy(hits,buffer,n);return n;}
  public static RaycastHit[] RaycastAll(Vector3 o,Vector3 d,float distance,int mask,QueryTriggerInteraction q)=>hits;
 }
 public class Threat {public int side;public bool locking,projectile;public float distanceSquared;public Vector3 point;}
 public class View {public bool visible;public float nextFrame=5,reveal=1;public RawImage image=new();public GameObject root=new();}
 public class Probe {
  public Transform transform=new();public List<Threat> candidates=new(),threatPool=new();
  public List<Renderer> renderers=new(),temporarilyHidden=new(),localVisuals=new();
  public bool sightOriginChecked,sightOriginBlocked;public Vector3 sightOrigin;
  public RaycastHit[] sightHits=new RaycastHit[32];public Collider[] sightOverlaps=new Collider[16];
'@
$source += $priority + (Method $rear 'RearSector') + (Method $rear 'NextRenderTime') + (Method $rear 'SelectThreats') + (Method $rear 'Nearest') + (Method $rear 'SetVisible')
$source += (Method $rear 'CollectRenderers') + (Method $rear 'HideCollectedRenderers') + (Method $rear 'RestoreLocalMagic')
$source += (Method $rear 'AddThreat')
$source += (Method $rear 'IsSightBlocker') + (Method $rear 'IsSightOriginBlocked') + (Method $rear 'HasClearSight')
$source += (Method $bubble 'BuildOutline') + (Method $bubble 'Curve') + $cross + (Method $bubble 'Triangulate')
$source+=@'
 }
 public static class Tests {
  static int n;static void Check(bool ok,string label){if(!ok)throw new Exception(label);n++;}
  public static int Run(){
   var pool=new Probe();pool.AddThreat(-1,new(1,2,3),49,true,false);
   var first=pool.candidates[0];pool.candidates.Clear();
   pool.AddThreat(1,new(4,5,6),25,false,true);
   Check(ReferenceEquals(first,pool.candidates[0]),"Reuse threat object across frames");
   Check(first.side==1&&!first.locking&&first.projectile&&first.point.x==4&&first.distanceSquared==25,"Pooled threat resets all previous enemy fields");
   pool.AddThreat(-1,new(),1,true,false);
   Check(!ReferenceEquals(pool.candidates[0],pool.candidates[1]),"Simultaneous left and right threats remain independent");
   long before=GC.GetAllocatedBytesForCurrentThread();
   for(int frame=0;frame<10000;frame++){pool.candidates.Clear();pool.AddThreat(-1,new(),49,false,true);pool.AddThreat(1,new(),25,true,false);}
   Check(GC.GetAllocatedBytesForCurrentThread()==before,"No threat allocations after warm-up across 10000 frames");
   var sight=new Probe();var wall=new Collider();var actor=new Collider{owner=new Player()};var missile=new Collider{owner=new MagicProjectile()};
   Check(sight.HasClearSight(new(),new(0,0,49)),"unobstructed enemy visible");
   int originQueries=Physics.overlapQueries;
   sight.HasClearSight(new(),new(-30,0,-40));sight.HasClearSight(new(),new(30,0,-40));
   Check(Physics.overlapQueries==originQueries,"Many enemies and shots share one camera-origin overlap query");
   sight.HasClearSight(new(1,0,0),new(30,0,-40));
   Check(Physics.overlapQueries==originQueries+1,"Changed camera origin is never stale");
   Physics.hits=new[]{new RaycastHit{collider=wall}};
   Check(!sight.HasClearSight(new(),new(0,0,49)),"building blocks preview activation");
   Physics.hits=new[]{new RaycastHit{collider=actor},new RaycastHit{collider=missile}};
   Check(sight.HasClearSight(new(),new(0,0,49)),"actors and spells do not falsely block sight");
   Physics.overlaps=new[]{wall};sight.sightOriginChecked=false;
   Check(!sight.HasClearSight(new(),new(0,0,49)),"camera embedded in wall rejected");
   Physics.overlaps=Array.Empty<Collider>();sight.sightOriginChecked=false;Physics.hits=new RaycastHit[33];
   for(int i=0;i<32;i++)Physics.hits[i]=new RaycastHit{collider=actor};Physics.hits[32]=new RaycastHit{collider=wall};
   Check(!sight.HasClearSight(new(),new(0,0,49)),"full hit buffer cannot hide a wall");
   Physics.hits=Array.Empty<RaycastHit>();Physics.overlaps=new Collider[17];sight.sightOriginChecked=false;
   for(int i=0;i<16;i++)Physics.overlaps[i]=actor;Physics.overlaps[16]=wall;
   Check(!sight.HasClearSight(new(),new(0,0,49)),"full overlap buffer cannot hide a wall");
   Physics.overlaps=Array.Empty<Collider>();sight.sightOriginChecked=false;
   Check(sight.HasClearSight(new(),new(0,0,49)),"Next detection pass observes a removed wall");
   Check(Probe.RearSector(new(-10,0,-10))==-1,"rear-left routes left");
   Check(Probe.RearSector(new(10,0,-10))==1,"rear-right routes right");
   Check(Probe.RearSector(new(-100,0,1))==0,"entire front-left excluded even outside main camera FOV");
   Check(Probe.RearSector(new(100,0,1))==0,"entire front-right excluded");
   Check(Probe.RearSector(new(0,0,-10))==1,"exact rear seam appears once on right");
   Check(Probe.RearSector(new(-10,0,0))==0&&Probe.RearSector(new(10,0,0))==0,"exact lateral boundary excluded");
   Check(Probe.RearSector(new(-10,100,-10))==-1,"airborne rear-left");
   Check(Probe.RearSector(new(10,-100,-10))==1,"below rear-right");
   Check(Probe.RearSector(new(0,0,0))==0,"coincident point excluded");
   // Rotate the viewing axes while leaving the world target fixed at (0,0,-10).
   Check(Probe.RearSector(new(10,0,-.01f))==1,"camera turned left: target on right rear");
   Check(Probe.RearSector(new(-10,0,-.01f))==-1,"camera turned right: target on left rear");
   Check(Probe.RearSector(new(0,0,10))==0,"camera turned around: previous rear is now front");
   foreach(int gameFps in new[]{30,60,90,144}) {
    float deadline=0; int count=0;
    for(int frame=0;frame<gameFps;frame++) {
     float now=frame/(float)gameFps;
     if(now+.0001f<deadline)continue;
     count++;deadline=Probe.NextRenderTime(now,deadline,60);
     Check(deadline>now,"no catch-up loop after slow frame");
    }
    Check(Math.Abs(count-Math.Min(gameFps,60))<=1,"60Hz cadence maintained at "+gameFps+" game FPS");
   }
   var rendering=new Probe();var effect=new GameObject();var own=new Renderer();var hidden=new Renderer{forceRenderingOff=true};var disabled=new Renderer{enabled=false};var world=new Renderer();
   effect.visuals.Add(own);effect.visuals.Add(hidden);effect.visuals.Add(disabled);
   rendering.CollectRenderers(effect);rendering.CollectRenderers(effect);
   try {rendering.HideCollectedRenderers();rendering.HideCollectedRenderers();
    Check(own.forceRenderingOff,"own magic hidden in secondary camera");
    Check(!world.forceRenderingOff&&!disabled.forceRenderingOff,"environment/enemy and disabled renderers untouched");
    throw new InvalidOperationException("simulated rendering failure");
   } catch(InvalidOperationException) {} finally {rendering.RestoreLocalMagic();}
   Check(!own.forceRenderingOff&&hidden.forceRenderingOff,"restores main-camera visibility while preserving existing concealment");
   Check(rendering.temporarilyHidden.Count==0,"render state cleared after failure");
   own.enabled=false;hidden.forceRenderingOff=false;rendering.HideCollectedRenderers();
   Check(!own.forceRenderingOff&&hidden.forceRenderingOff,"Second view checks current visibility rather than cached flags");
   rendering.RestoreLocalMagic();
   Check(!hidden.forceRenderingOff,"Second view restores its own snapshot");
   var p=new Probe();var v=new View();p.SetVisible(v,true);
   Check(v.image.enabled&&v.root.activeSelf&&v.visible&&v.nextFrame==0&&v.reveal==0,"open forces render and shows root");
   v.reveal=.5f;v.nextFrame=10;p.SetVisible(v,true);
   Check(v.reveal==.5f&&v.nextFrame==10,"open animation not restarted each frame");
   p.SetVisible(v,false);Check(!v.image.enabled&&!v.root.activeSelf&&!v.visible,"hide root and image together");
   v.root.activeSelf=true;p.transform.parent=v.root.transform;p.SetVisible(v,false);
   Check(v.root.activeSelf,"bad UI assignment never disables camera ancestor");
   v.image=null;p.SetVisible(v,false);Check(!v.visible,"destroyed image does not throw during teardown");
   p.SelectThreats(out var a,out var b);Check(a==null&&b==null,"no enemies no views");
   var rear=new Threat{side=-1,distanceSquared=100};
   var lockEnemy=new Threat{side=1,locking=true,distanceSquared=40000};
   var otherRear=new Threat{side=-1,distanceSquared=25};
   p.candidates.Add(rear);p.SelectThreats(out a,out b);Check(a==rear&&b==null,"single rear enemy never duplicated");
   p.candidates.Add(lockEnemy);p.SelectThreats(out a,out b);Check(a==rear&&b==lockEnemy,"locking enemy has independent view beyond rear radius");
   p.candidates.Add(otherRear);p.SelectThreats(out a,out b);Check(a==otherRear&&b==lockEnemy,"nearest rear left and lock priority right, maximum two");
   p.candidates.Remove(lockEnemy);p.SelectThreats(out a,out b);Check(a==otherRear&&b==null,"two left rear enemies only open the left view");
   otherRear.locking=true;p.candidates.Remove(rear);p.SelectThreats(out a,out b);Check(a==otherRear&&b==null,"one rear locking enemy only one view");
   p.candidates.Clear();p.candidates.Add(lockEnemy);p.SelectThreats(out a,out b);Check(a==null&&b==lockEnemy,"right locking threat never uses empty left view");
   p.candidates.Clear();var shot=new Threat{side=-1,projectile=true,distanceSquared=400};p.candidates.Add(shot);
   p.SelectThreats(out a,out b);Check(a==shot&&b==null,"shot keeps left view open after lock ends or caster dies");
   shot.side=1;p.SelectThreats(out a,out b);Check(a==null&&b==shot,"shot crossing rear seam transfers to right view");
   p.candidates.Add(lockEnemy);p.SelectThreats(out a,out b);Check(a==null&&b==shot,"incoming shot caption prioritized in shared sector");
   p.candidates.Clear();p.SelectThreats(out a,out b);Check(a==null&&b==null,"no actor or incoming projectile closes both views");
   var outline=Probe.BuildOutline();var tris=Probe.Triangulate(outline);
   Check(tris.Length==(outline.Length-2)*3,"all concave bubble ears triangulated");
   double area=0,meshArea=0;
   for(int i=0;i<outline.Length;i++){var x=outline[i];var y=outline[(i+1)%outline.Length];area+=x.x*y.y-x.y*y.x;}
   for(int i=0;i<tris.Length;i+=3){var x=outline[tris[i]];var y=outline[tris[i+1]];var z=outline[tris[i+2]];meshArea+=Math.Abs((y.x-x.x)*(z.y-x.y)-(y.y-x.y)*(z.x-x.x));}
   Check(Math.Abs(Math.Abs(area)-meshArea)<.00001,"mask mesh area matches concave silhouette without filling notches");
   return n;
  }
 }
}
'@
Add-Type -TypeDefinition $source
"Rear view and mask checks passed: $([RearChecks.Tests]::Run())"
if($rear -match 'main\.transform\.(SetPositionAndRotation|rotation\s*=|position\s*=)'){throw 'Main aim camera modified'}
foreach($required in @('enemy.IsHiddenBySmokeFor(local)','enemy.TeamIndex == local.TeamIndex','enemy.HasLockTarget(local.Object)','main.transform.InverseTransformDirection(delta)','shot.TryGetImpactTime(local, out _)','shot.Finished','shot.ShooterTeam == local.TeamIndex')) {
 if(!$rear.Contains($required)){throw "Missing filtering/framing: $required"}
}
if((Get-Content Assets/Resources/FlightCombatTable.asset -Raw) -notmatch 'rearViewDistance: 100'){throw 'Expected default rear range 100m'}
if($rear.Contains('local.transform.forward') -or $rear.Contains('WorldToViewportPoint')){throw 'Sectors must use camera axes and front/rear half-planes'}
if(!$rear.Contains('Camera.HorizontalToVerticalFieldOfView')){throw 'Sector camera must preserve horizontal coverage when UI aspect changes'}
$flightAsset = Get-Content Assets/Resources/FlightCombatTable.asset -Raw
foreach ($field in @('rearViewRect', 'rightThreatViewRect')) {
 $rectBlock = [regex]::Match($flightAsset, '(?m)^  ' + $field + ':\r?\n(?:    [^\r\n]+\r?\n)+').Value
 if ($rectBlock -notmatch 'serializedVersion: 2') { throw "$field must use Unity Rect version 2; unversioned x/y/width/height silently loads as zero size" }
 foreach ($dimension in @('width', 'height')) {
  $value = [regex]::Match($rectBlock, $dimension + ': ([0-9.]+)').Groups[1].Value
  if (!$value -or [double]::Parse($value, [cultureinfo]::InvariantCulture) -le 0) { throw "$field has no drawable $dimension" }
 }
}
"Both serialized rear viewport Rects are versioned and have positive area."
if($rear.Contains('enemy.LockProgress >')){throw 'Targeted attacker must not wait for charge progress'}
if(!$rear.Contains('(settings.rearViewEnabled || settings.showLockingThreats)')){throw 'Lock warning must work independently of rear detection toggle'}
if(!$rear.Contains('!enemy.SelectedMagicStats.TargetsAllies')){throw 'Healing targets must never trigger lock warnings'}
if(!$rear.Contains('local.TeamIndex > 0')){throw 'Wait for local team assignment before detecting enemies'}
