# Exercise the production route search with a deterministic inflated-box collision oracle.
# Unity's scene colliders and rendered path still require a Play Mode check.
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$source=Get-Content Assets/Script/TrainingManaRoute.cs -Raw
$methods=@('Replan','AddNode','SegmentDistance') | ForEach-Object {
 $m=[regex]::Match($source,'(?ms)^    (?:private|internal) (?:static )?[\w<>]+ '+$_+'\([^{}]*\)\r?\n    \{.*?^    \}')
 if(!$m.Success){throw "Missing route method $_"};$m.Value
}
$stubs=@'
#pragma warning disable 0414,0649
using System;
using System.Collections.Generic;
namespace ManaRouteChecks {
public static class Time{public static float unscaledTime;}
public static class Mathf {
 public static float Max(float a,float b)=>Math.Max(a,b);
 public static float Clamp01(float v)=>Math.Clamp(v,0,1);
}
public struct Vector3 {
 public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}
 public float sqrMagnitude=>x*x+y*y+z*z;
 public static Vector3 operator+(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
 public static Vector3 operator-(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
 public static Vector3 operator*(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);
 public static Vector3 Scale(Vector3 a,Vector3 b)=>new(a.x*b.x,a.y*b.y,a.z*b.z);
 public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
 public static float Distance(Vector3 a,Vector3 b)=>MathF.Sqrt((a-b).sqrMagnitude);
}
public struct Bounds {
 public Vector3 center,extents;
 public void Expand(float amount){extents+=new Vector3(amount*.5f,amount*.5f,amount*.5f);}
 public bool Contains(Vector3 p)=>Math.Abs(p.x-center.x)<=extents.x && Math.Abs(p.y-center.y)<=extents.y && Math.Abs(p.z-center.z)<=extents.z;
}
public class Collider {public Bounds bounds;}
public class Line {public int positionCount;public bool enabled;public void SetPosition(int i,Vector3 p){}}
public class Route {
 readonly List<Vector3> route=new(),nodes=new();readonly List<float> costs=new();readonly List<int> parents=new();
 readonly HashSet<int> closed=new();readonly HashSet<Collider> expanded=new();
 readonly List<Collider> obstacles=new();
 Vector3 plannedFrom,destination,exitPoint=default;Bounds flightBounds;
 bool bounded,mustLeaveFirst=false;float nextRouteCheck,clearance=2;int waypoint,queriesRemaining;
 const int NodeLimit=64;Line line=new();
 bool Clear(Vector3 start,Vector3 end,out Collider blocker){
  blocker=null;if(queriesRemaining--<=0)return false;
  foreach(var box in obstacles){var b=box.bounds;b.Expand(clearance*2);
   float t0=0,t1=1;var delta=end-start;
   bool Axis(float p,float d,float c,float e){
    if(Math.Abs(d)<1e-7f)return p>=c-e && p<=c+e;
    float a=(c-e-p)/d,z=(c+e-p)/d;if(a>z)(a,z)=(z,a);
    t0=Math.Max(t0,a);t1=Math.Min(t1,z);return t0<=t1;
   }
   if(Axis(start.x,delta.x,b.center.x,b.extents.x)&&Axis(start.y,delta.y,b.center.y,b.extents.y)&&Axis(start.z,delta.z,b.center.z,b.extents.z)){
    blocker=box;return false;
   }
  }return true;
 }
'@
$checks=@'
 static void Check(bool value,string name){if(!value)throw new Exception(name);}
 public static string Run(){
  var p=new Route{destination=new Vector3(100,0,0)};p.Replan(default);
  Check(p.route.Count==2 && p.line.enabled,"clear route must connect start and goal");
  p.obstacles.Add(new Collider{bounds=new Bounds{center=new Vector3(50,0,0),extents=new Vector3(8,8,8)}});
  p.Replan(default);Check(p.route.Count>2,"blocking building must produce intermediate waypoints");
  for(int i=1;i<p.route.Count;i++){p.queriesRemaining=160;Check(p.Clear(p.route[i-1],p.route[i],out _),"every drawn segment must clear the obstacle");}
  p.bounded=true;p.flightBounds=new Bounds{center=new Vector3(50,0,0),extents=new Vector3(60,30,30)};
  p.Replan(default);Check(p.route.Count>2,"detour fits inside playable volume");
  p.flightBounds=new Bounds{center=new Vector3(50,0,0),extents=new Vector3(60,2,2)};
  p.Replan(default);Check(p.route.Count==0 && !p.line.enabled,"impossible path must not draw through a wall");
  p.bounded=true;p.flightBounds=new Bounds{center=new Vector3(50,0,0),extents=new Vector3(20,2,2)};
  p.Replan(default);Check(p.nodes.Count>0,"starting outside bounds cannot produce an index exception");
  p=new Route();p.Replan(default);Check(p.route.Count==2,"already at destination remains safe");
  Check(Math.Abs(SegmentDistance(new Vector3(5,4,0),default,new Vector3(10,0,0))-4)<.001,"off-route distance uses segment projection");
  Check(Math.Abs(SegmentDistance(new Vector3(20,0,0),default,new Vector3(10,0,0))-10)<.001,"projection clamps at waypoint ends");
  return "PASS: direct route, obstacle detour, bounds, unreachable target and off-route distance.";
 }
}}
'@
Add-Type -TypeDefinition ($stubs+($methods -join "`n")+$checks)
[ManaRouteChecks.Route]::Run()
if(([regex]::Matches($source,'FindObjectsByType')).Count -ne 1){throw 'Zone inventory must be acquired only once'}
$asset=Get-Content Assets/Resources/CatCompanionTable.asset -Raw
if($asset -notmatch '(?s)id: mana_booster_recovery\s+highlight: 0\s+waitForTargetClick: 0\s+practice: 9'){throw 'Recovery lesson not wired to appended enum'}
'PASS: single zone discovery and practice data binding.'


