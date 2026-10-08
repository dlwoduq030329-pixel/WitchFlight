# Execute the production threat-selection method; camera projection is covered by CombatControlRegression.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$source = Get-Content Assets/Script/MagicProjectile.cs -Raw
$method = [regex]::Match($source, '(?ms)^    public static bool TryGetIncomingThreat\(Player observer, float range, out float distance, out MagicProjectile nearest,.*?^    \}').Value
if (!$method) { throw 'Missing production threat selection' }
$stubs = @'
using System;
using System.Collections.Generic;
namespace ThreatChecks {
public struct Vector3 {public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}public float sqrMagnitude=>x*x+y*y+z*z;public static Vector3 operator-(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;}
public static class Mathf {public static float Sqrt(float x)=>(float)Math.Sqrt(x);}
public class Transform {public Vector3 position,forward;}
public class NetworkObject {public bool IsValid=true;}
public class Player {public NetworkObject Object=new();public object Runner=new();public Vector3 LockAimPoint;public int TeamIndex=1;}
public class MagicProjectile {
 public static HashSet<MagicProjectile> active=new();public NetworkObject Object=new();public object Runner;public Transform transform=new();public bool Finished,IsMine,Settled,InView,Timed=true;public int ShooterTeam=2;public float Seconds;
 public static HashSet<float> blockedPositions=new();public static int SightChecks;
 public bool TryGetImpactTime(Player observer,out float seconds){seconds=Seconds;return Timed;}
 public static bool HasBlastSight(Vector3 position,Player observer){SightChecks++;return !blockedPositions.Contains(position.x);}
'@
$checks = @'
}
public static class Tests {
 static int n;static void Check(bool ok,string message){if(!ok)throw new Exception(message);n++;}
 static MagicProjectile Shot(Player observer,float distance,float seconds,bool visible){var s=new MagicProjectile{Runner=observer.Runner,Seconds=seconds,InView=visible};s.transform.position=new(distance,0,0);s.transform.forward=new(-1,0,0);MagicProjectile.active.Add(s);return s;}
 public static int Run(){
  var observer=new Player();Predicate<MagicProjectile> offscreen=s=>!s.InView;
  var front=Shot(observer,10,.1f,true);var rear=Shot(observer,25,.5f,false);
  Check(MagicProjectile.TryGetIncomingThreat(observer,90,out _,out var selected,1.5f,offscreen)&&selected==rear,"visible imminent shot cannot mask an offscreen threat");
  Check(MagicProjectile.SightChecks==1,"on-screen candidate skipped before visibility raycasts");
  Check(MagicProjectile.TryGetIncomingThreat(observer,90,out _,out selected,1.5f)&&selected==front,"unfiltered callers retain previous query behavior");
  rear.InView=true;Check(!MagicProjectile.TryGetIncomingThreat(observer,90,out _,out selected,1.5f,offscreen)&&selected==null,"all projectiles visible means no direction warning");
  front.InView=false;Check(MagicProjectile.TryGetIncomingThreat(observer,90,out _,out selected,1.5f,offscreen)&&selected==front,"camera turning away makes that projectile eligible");
  rear.InView=false;front.InView=true;MagicProjectile.blockedPositions.Add(25);Check(!MagicProjectile.TryGetIncomingThreat(observer,90,out _,out _,1.5f,offscreen),"wall-occluded rear threat stays excluded");
  MagicProjectile.blockedPositions.Clear();rear.ShooterTeam=1;Check(!MagicProjectile.TryGetIncomingThreat(observer,90,out _,out _,1.5f,offscreen),"friendly shot stays excluded");
  rear.ShooterTeam=2;rear.Finished=true;Check(!MagicProjectile.TryGetIncomingThreat(observer,90,out _,out _,1.5f,offscreen),"finished shot stays excluded");
  rear.Finished=false;rear.transform.forward=new(1,0,0);Check(!MagicProjectile.TryGetIncomingThreat(observer,90,out _,out _,1.5f,offscreen),"departing shot stays excluded");
  rear.transform.forward=new(-1,0,0);rear.transform.position=new(120,0,0);rear.Seconds=1;Check(MagicProjectile.TryGetIncomingThreat(observer,90,out _,out selected,1.5f,offscreen)&&selected==rear,"early ETA warning outside distance still works");
  rear.Seconds=2;Check(!MagicProjectile.TryGetIncomingThreat(observer,90,out _,out _,1.5f,offscreen),"distant non-imminent threat still excluded");
  return n;
 }
}}
'@
Add-Type -TypeDefinition ($stubs + $method + $checks)
"Offscreen threat selection checks passed: $([ThreatChecks.Tests]::Run())"
