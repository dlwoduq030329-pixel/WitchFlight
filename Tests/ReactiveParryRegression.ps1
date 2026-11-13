# Execute production impact/end methods with native-free presentation doubles.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$source = Get-Content Assets/Script/CombatMagicVisual.cs -Raw
function Method($name) {
 $match = [regex]::Match($source, '(?ms)^    public void '+$name+'\([^{}]*?\)\r?\n    \{.*?^    \}')
 if (!$match.Success) { throw "Missing $name" }; $match.Value
}
$code = @'
using System;
public struct Vector3 {
 public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
 public float sqrMagnitude=>x*x+y*y+z*z;
 public Vector3 normalized=>this*(1f/(float)Math.Sqrt(sqrMagnitude));
 public static Vector3 operator+(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
 public static Vector3 operator*(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);
}
public struct Quaternion {public static Quaternion identity=>new();}
public struct Color {public Color(float r,float g,float b,float a){}}
public static class Mathf {public const float PI=(float)Math.PI;public static float Max(float a,float b)=>Math.Max(a,b);public static float Cos(float f)=>(float)Math.Cos(f);public static float Sin(float f)=>(float)Math.Sin(f);}
public static class Time {public static float unscaledTime;}
public class Transform {public Vector3 position;public void SetPositionAndRotation(Vector3 p,Quaternion r){position=p;}}
public class Player {public Vector3 LockAimPoint=new(10,20,30);}
public class ReactiveShieldVfx {public int hits;public Vector3 point;public float RecoverySeconds=1.15f;public void ReportHit(Vector3 p){hits++;point=p;}}
public class LineRenderer {public bool enabled=true;public object gameObject;public void SetPosition(int i,Vector3 p){}}
public enum ParticleSystemStopBehavior {StopEmitting}
public class ParticleSystem {public void Stop(bool b,ParticleSystemStopBehavior s){}}
public static class RearThreatCamera {public static void ExcludeLocalEffect(object o,Player p){}}
public static class CombatTransientEffect {public static int calls;public static void PlayPulse(Vector3 v,Color c,float a,float b,Player p){calls++;}}
public class Harness {
 public ReactiveShieldVfx reactiveShield=new();public Player follow=new();public Transform transform=new();
 public Vector3 shieldFacing=new(0,0,1);public float shieldRadius=2.2f,shieldOffset,shieldPulseUntil;
 public float reactiveHitUntil,reactiveEndAt,expires,duration=.12f;
 public bool heldShield=true,shieldEnding;public object ownedMaterial;
 public LineRenderer shieldImpactRing,shieldRim;
 LineRenderer CreateShieldLine(string name,int count)=>new();
 T[] GetComponentsInChildren<T>()=>Array.Empty<T>();
 __METHODS__
}
public static class Checks {
 static int count;static void Check(bool ok,string message){count++;if(!ok)throw new Exception(message);}
 static bool Near(float a,float b)=>Math.Abs(a-b)<.0001f;
 public static int Run(){
  var v=new Harness();Time.unscaledTime=3;
  Check(v.reactiveShield.hits==0,"Starting parry does not fake a hit");
  v.ParrySuccess(new(0,0,-1),new(1,.5f,-2));
  Check(v.reactiveShield.hits==1&&Near(v.reactiveShield.point.x,11)&&Near(v.reactiveShield.point.y,20.5f)&&Near(v.reactiveShield.point.z,28),"Replicated relative contact follows rendered player position");
  Check(v.shieldImpactRing==null&&CombatTransientEffect.calls==0,"Reactive impact replaces legacy ring/pulse");
  Check(Near(v.expires,4.15f),"Keep hit reconstruction alive after short block window");
  v.EndParry();Check(v.shieldEnding&&Near(v.reactiveEndAt,3)&&Near(v.expires,4.15f),"Ending block preserves hit tail");
  Time.unscaledTime=3.1f;v.EndParry();Check(Near(v.reactiveEndAt,3),"Repeated end cannot restart fade");
  v.ParrySuccess(new(-1,0,0));Check(v.reactiveShield.hits==2&&Near(v.reactiveShield.point.x,7.8f),"Missing precise contact falls back to incoming direction");
  Check(Near(v.expires,4.25f),"Late confirmed hit extends its own visual tail");
  v=new Harness();v.EndParry();Check(Near(v.expires,3.22f),"Missed parry only uses short fade, no artificial hit tail");
  return count;
 }
}
'@
$code = $code.Replace('__METHODS__', ((Method 'ParrySuccess') + (Method 'EndParry')))
Add-Type -TypeDefinition $code
"Reactive parry impact/lifetime checks passed: $([Checks]::Run())"
foreach ($path in 'Assets/Ch/ChPrefab.prefab','Assets/Raid/RaidPlayer.prefab') {
 $prefab=Get-Content $path -Raw
 if (!$prefab.Contains('reactiveParryShieldVfxPrefab: {fileID: 100, guid: f099d2207feb4f1fbca3719372d74d86, type: 3}')) {throw "Missing reactive shield: $path"}
 if (!$prefab.Contains('parryShieldVfxPrefab: {fileID: 1070897446116272, guid: 189038a8fc10a2946b97658479b710ff, type: 3}')) {throw "Orbit shield was replaced: $path"}
}
