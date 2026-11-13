$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$production=Get-Content Assets/Script/Camera/DashRefractionPulse.cs -Raw
$stubs=@'
using System;
namespace UnityEngine {
 public class TooltipAttribute:Attribute {public TooltipAttribute(string s){}}
 public class MinAttribute:Attribute {public MinAttribute(float f){}}
 public class RangeAttribute:Attribute {public RangeAttribute(float a,float b){}}
 public static class Time {public static float timeScale=1,unscaledDeltaTime=.016f,unscaledTime;}
 public static class Mathf {
  public static float Max(float a,float b)=>Math.Max(a,b);
  public static float Clamp(float a,float b,float c)=>Math.Clamp(a,b,c);
  public static float SmoothStep(float a,float b,float t){t=Math.Clamp(t,0,1);return a+(b-a)*t*t*(3-2*t);}
 }
 public struct Vector2 {public float x,y;public Vector2(float a,float b){x=a;y=b;}}
 public struct Vector3 {public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}}
 public struct Vector4 {public float x,y,z,w;public static Vector4 zero=>default;public Vector4(float a,float b,float c,float d){x=a;y=b;z=c;w=d;}}
 public class Camera {public float nearClipPlane=.1f,aspect=16f/9;public Vector3 projected=new(.5f,.4f,5);public int projections;public Vector3 WorldToViewportPoint(Vector3 p){projections++;return projected;}}
}
public class Player {public bool IsBoosting;public int PortalExitSequence;public UnityEngine.Vector3 LockAimPoint=>default;}
'@
$tests=@'
public static class DashRefractionChecks {
 static int count;static void Check(bool v,string s){if(!v)throw new Exception(s);count++;}
 public static int Run(){
  var pulse=new DashRefractionPulse();var player=new Player();var camera=new UnityEngine.Camera();
  UnityEngine.Time.unscaledTime=0;pulse.Update(player,camera,true);
  UnityEngine.Time.unscaledTime=.04f;pulse.Update(player,camera,true);
  Check(!pulse.Active && camera.projections==0,"windup and normal cruise cannot trigger refraction");
  player.IsBoosting=true;UnityEngine.Time.unscaledTime=.08f;pulse.Update(player,camera,true);
  Check(!pulse.Active && camera.projections==1,"release captures center once, begins continuously");
  UnityEngine.Time.unscaledTime=.135f;pulse.Update(player,camera,true);
  Check(pulse.Active && Math.Abs(pulse.Ring.z-.13f)<.0001f,"ring expands after actual acceleration");
  Check(pulse.Ring.x==.5f && pulse.Ring.y==.4f && pulse.Shape.z==camera.aspect,"viewport center and aspect sent to shader");
  float radius=pulse.Ring.z;camera.projected=new(.8f,.2f,5);
  UnityEngine.Time.unscaledTime=.3f;pulse.Update(player,camera,true);
  Check(pulse.Ring.z>radius && pulse.Ring.x==.5f && camera.projections==1,"wave expands without drifting with the camera");
  UnityEngine.Time.unscaledTime=.7f;pulse.Update(player,camera,true);
  Check(!pulse.Active,"wave expires even while boost remains held");
  UnityEngine.Time.unscaledTime=1;pulse.Update(player,camera,true);
  Check(!pulse.Active && camera.projections==1,"holding boost cannot restart wave");
  player.IsBoosting=false;pulse.Update(player,camera,true);player.IsBoosting=true;pulse.Update(player,camera,true);
  UnityEngine.Time.unscaledTime=1.1f;pulse.Update(player,camera,true);Check(pulse.Active,"new dash retriggers");
  player.PortalExitSequence++;pulse.Update(player,camera,true);Check(!pulse.Active,"portal clears active wave");
  pulse.Update(player,camera,false);pulse.Update(player,camera,true);Check(!pulse.Active,"returning from menu mid-boost does not pulse");
  player.IsBoosting=false;pulse.Update(player,camera,true);camera.projected=new(.5f,.5f,-1);
  player.IsBoosting=true;pulse.Update(player,camera,true);UnityEngine.Time.unscaledTime=1.2f;pulse.Update(player,camera,true);
  Check(!pulse.Active,"behind-camera player cannot create inverted wave");
  pulse.Reset();camera.projected=new(.5f,.4f,5);player.IsBoosting=false;pulse.Update(player,camera,true);
  player.IsBoosting=true;pulse.Update(player,camera,true);UnityEngine.Time.unscaledTime=1.3f;pulse.Update(player,camera,true);
  Check(pulse.Active,"pulse valid before pause");UnityEngine.Time.timeScale=0;pulse.Update(player,camera,true);
  Check(!pulse.Active,"pause clears refraction");UnityEngine.Time.timeScale=1;
  for(int i=0;i<=100;i++){float e=DashRefractionPulse.Envelope(i/100f);Check(e>=0 && e<=1,"bounded fade");}
  Check(DashRefractionPulse.Envelope(0)==0 && DashRefractionPulse.Envelope(1)==0,"no hard start/end amplitude");
  return count;
 }
}
'@
Add-Type -TypeDefinition ($production+"`n"+$stubs.Replace('using System;','')+$tests)
"Dash refraction behavior: $([DashRefractionChecks]::Run()) checks passed"
$feature=Get-Content Assets/Script/Camera/PeripheralSpeedBlurFeature.cs -Raw
$shader=Get-Content Assets/Resources/PeripheralSpeedBlur.shader -Raw
if($feature -notmatch 'd\.dashRing, d\.dashShape' -or $feature -notmatch 'effects\.DashRefractionRing, effects\.DashRefractionShape'){
 throw 'Refraction must be wired in both RenderGraph and compatibility paths'
}
foreach($required in @('sin\(band \* PI','center.y = 1.0 - center.y','float2\(aspect, 1\)','PlayerClearArea\(refracted\)')){
 if($shader -notmatch $required){throw "Missing shader safeguard: $required"}
}
'Both renderer paths, sinusoidal ripple, aspect/UV correction and character exclusion verified.'
