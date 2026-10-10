# Tests the actual emitter follow/stop/cleanup methods without entering Unity Play Mode.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$visual = Get-Content Assets/Script/UtilityMagicVisual.cs -Raw
function Method([string]$name) {
    $m = [regex]::Match($visual, '(?ms)^    private [\w<>]+ '+$name+'\([^{}]*?\)\r?\n    \{.*?^    \}')
    if (!$m.Success) { throw "Missing $name" }
    return $m.Value -replace '^    private ', '    public '
}
$stubs = @'
using System;
namespace FlareVisualChecks {
public struct Vector3 {
 public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}
 public static Vector3 operator+(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
 public static Vector3 operator-(Vector3 a)=>new(-a.x,-a.y,-a.z);
 public static Vector3 operator*(Vector3 a,float n)=>new(a.x*n,a.y*n,a.z*n);
}
public struct Quaternion {public Vector3 forward;public static Quaternion LookRotation(Vector3 v)=>new(){forward=v};}
public class Transform {
 public Vector3 position,forward=new(0,0,1);public Quaternion rotation;
 public void SetPositionAndRotation(Vector3 p,Quaternion q){position=p;rotation=q;}
}
public static class Time {public static float time;}
public class NetworkObject {public bool IsValid=true;}
public class Player {public bool isActiveAndEnabled=true,IsAlive=true;public NetworkObject Object=new();public Transform transform=new();public Vector3 LockAimPoint;}
public class GameObject {public bool destroyed;}
public enum ParticleSystemStopBehavior {StopEmitting}
public class ParticleSystem {
 public bool alive=true;public int stops;
 public bool IsAlive(bool children)=>alive;
 public void Stop(bool children,ParticleSystemStopBehavior mode){stops++;}
}
public class UtilityMagicVisual {
 public Player flareOwner;
 public ParticleSystem[] flareParticles;
 public float flareBackOffset=.7f,flareEmissionUntil=3,flareCleanupAt;
 public bool isFlare=true,flareStopped;
 public Transform transform=new();public GameObject gameObject=new();
 public static void Destroy(GameObject o){o.destroyed=true;}
'@
$tests = @'
}
public static class Tests {
 static int count;static void Check(bool b,string message){if(!b)throw new Exception(message);count++;}
 static UtilityMagicVisual View()=>new(){flareOwner=new Player(),flareParticles=new[]{new ParticleSystem()}};
 public static int Run(){
  Time.time=0;var v=View();var owner=v.flareOwner;owner.LockAimPoint=new(10,5,0);v.LateUpdate();
  Check(v.transform.position.x==10&&Math.Abs(v.transform.position.z+.7f)<.001f,"emitter stays behind caster body");
  Check(v.transform.rotation.forward.z==-1,"emission faces rear");
  owner.LockAimPoint=new(20,5,0);owner.transform.forward=new(1,0,0);Time.time=2.9f;v.LateUpdate();
  Check(Math.Abs(v.transform.position.x-19.3f)<.001f&&v.transform.rotation.forward.x==-1,"emitter follows movement and rotation");
  Check(!v.flareStopped&&v.flareParticles[0].stops==0,"emits throughout selected duration");
  Time.time=3;v.LateUpdate();
  Check(v.flareStopped&&v.flareOwner==null&&v.flareParticles[0].stops==1,"deadline stops emission once and releases caster");
  owner.LockAimPoint=new(100,5,0);Time.time=3.1f;v.LateUpdate();
  Check(!v.gameObject.destroyed&&Math.Abs(v.transform.position.x-19.3f)<.001f,"tail remains behind while particles fade");
  v.flareParticles[0].alive=false;v.LateUpdate();Check(v.gameObject.destroyed,"cleanup after last particle dies");
  Time.time=0;v=View();v.flareOwner.Object.IsValid=false;v.LateUpdate();Check(v.flareStopped,"despawn stops emitter safely");
  v=View();v.flareOwner.IsAlive=false;v.LateUpdate();Check(v.flareStopped,"death stops emitter");
  v=View();v.flareOwner.isActiveAndEnabled=false;v.LateUpdate();Check(v.flareStopped,"disabled caster stops emitter");
  v=View();v.flareOwner=null;v.LateUpdate();Check(v.flareStopped,"destroyed caster stops emitter");
  v=View();v.flareParticles=null;Time.time=3;v.LateUpdate();v.LateUpdate();Check(v.gameObject.destroyed,"non-particle authored VFX ends on deadline");
  Time.time=0;v=View();v.StopFlareEmission();Time.time=8;v.LateUpdate();Check(v.gameObject.destroyed,"supplied infinite sub-emitters cannot leak a VFX root");
  v=View();v.isFlare=false;Time.time=100;v.LateUpdate();Check(!v.gameObject.destroyed&&!v.flareStopped,"smoke lifetime remains owned by BattleFlag");
  return count;
 }
}}
'@
Add-Type -TypeDefinition ($stubs + (Method 'LateUpdate') + (Method 'StopFlareEmission') + (Method 'HasLiveFlareParticles') + $tests)
"Flare emitter lifecycle checks passed: $([FlareVisualChecks.Tests]::Run())"
foreach ($token in 'emission.rateOverTime = 40f', 'main.loop = true', 'main.simulationSpace = ParticleSystemSimulationSpace.World') {
    if (!$visual.Contains($token)) { throw "Continuous world-space flare configuration missing: $token" }
}
$presentation = Get-Content Assets/Script/CombatPresentation.cs -Raw
if (!$presentation.Contains('UtilityMagicVisual.PlayFlare(owner, origin, end, stats)')) { throw 'Flare must receive its caster.' }
'Continuous emission, world-space trail and caster binding contracts passed: 4'
