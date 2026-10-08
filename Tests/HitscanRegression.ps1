# Exercises the actual TraceMagic method with recording physics stubs.
# Unity Play Mode is still required to validate scene colliders and visual feel.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$player = Get-Content Assets/Script/Player.cs -Raw
$trace = [regex]::Match($player, '(?ms)^    private Vector3 TraceMagic\([^{}]*?\)\r?\n    \{.*?^    \}').Value -replace '^    private ', '    public '
if (!$trace) { throw 'Missing TraceMagic' }
$stubs = @'
using System;
namespace HitscanChecks {
public static class Mathf { public static float Max(float a,float b)=>Math.Max(a,b); }
public struct MagicStatEntry { public float range,hitscanRadius,projectileRadius; }
public struct Vector3 {
 public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
 public static Vector3 operator+(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
 public static Vector3 operator*(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);
}
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
 public static void Reset(params RaycastHit[] hits){Hits=hits;Rays=Spheres=AllRays=AllSpheres=0;LastRadius=0;}
 static int Copy(RaycastHit[] results,float range,int mask,QueryTriggerInteraction triggers){
  LastRange=range;LastMask=mask;LastTriggers=triggers;int count=Math.Min(results.Length,Hits.Length);Array.Copy(Hits,results,count);return count;
 }
 public static int SphereCastNonAlloc(Vector3 o,float r,Vector3 d,RaycastHit[] hits,float range,int mask,QueryTriggerInteraction triggers){Spheres++;LastRadius=r;return Copy(hits,range,mask,triggers);}
 public static int RaycastNonAlloc(Vector3 o,Vector3 d,RaycastHit[] hits,float range,int mask,QueryTriggerInteraction triggers){Rays++;return Copy(hits,range,mask,triggers);}
 public static RaycastHit[] SphereCastAll(Vector3 o,float r,Vector3 d,float range,int mask,QueryTriggerInteraction triggers){AllSpheres++;LastRadius=r;return Hits;}
 public static RaycastHit[] RaycastAll(Vector3 o,Vector3 d,float range,int mask,QueryTriggerInteraction triggers){AllRays++;return Hits;}
}
public class Player {
 public Transform transform=new();public RaycastHit[] instantShotHits=new RaycastHit[32];public int lockObstructionMask=123;
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
  return n;
 }
}}
'@
Add-Type -TypeDefinition ($stubs + $trace + $checks)
"Hitscan query behavior checks passed: $([HitscanChecks.Tests]::Run())"

$asset = Get-Content Assets/Resources/MagicStatTable.asset -Raw
foreach ($id in @(3,4,7,10)) {
    $entry = [regex]::Match($asset, '(?ms)^  - magic: ' + $id + '\r?\n.*?(?=^  - magic: |\z)').Value
    if ($entry -notmatch 'hitscanRadius: 0\.3') { throw "Missing saved radius for magic $id" }
}
if ($player -notmatch 'else end = TraceMagic\(stats, MagicCastPosition, transform.forward, out target\)') {
    throw 'Straight channel must use the same configurable trace'
}
$targeting = Get-Content Assets/Script/enemyLockOn.cs -Raw
if ([regex]::Matches($targeting, 'owner\.CanAcquireMagicTarget\(\)').Count -ne 2) {
    throw 'Both local input selection and candidate checks must use shared cooldown eligibility'
}
"Saved radii, channel trace and local targeting contracts passed: 6"
