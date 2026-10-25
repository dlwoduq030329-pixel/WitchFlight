# Exercise production envelopes, speed sampling and lifecycle without running Unity.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$source = Get-Content Assets/Script/Camera/FlightSpeedPresentation.cs -Raw
$animation = Get-Content Assets/Script/PlayerBroomAnimation.cs -Raw
function Method([string]$text, [string]$name) {
    $match = [regex]::Match($text, '(?ms)^    (?:internal|public) (?:static )?\w+ ' + $name + '\([^{}]*?\)\r?\n    \{.*?^    \}')
    if (!$match.Success) { throw "Missing production method: $name" }
    $match.Value.Replace('internal static', 'public static')
}
$stub = @'
using System;
namespace FlightChecks {
public static class Mathf {
 public static float Clamp(float x,float a,float b)=>Math.Clamp(x,a,b);
 public static int Clamp(int x,int a,int b)=>Math.Clamp(x,a,b);
 public static float Clamp01(float x)=>Clamp(x,0,1);
 public static float Max(float a,float b)=>Math.Max(a,b);public static float Min(float a,float b)=>Math.Min(a,b);
 public static float Abs(float x)=>Math.Abs(x);public static float Exp(float x)=>(float)Math.Exp(x);public static float Pow(float a,float b)=>(float)Math.Pow(a,b);
 public static float Lerp(float a,float b,float t)=>a+(b-a)*Clamp01(t);
 public static float InverseLerp(float a,float b,float x)=>a==b?0:Clamp01((x-a)/(b-a));
 public static float SmoothStep(float a,float b,float t){t=Clamp01(t);return a+(b-a)*t*t*(3-2*t);}
}
public struct Vector3 {
 public float x,y,z;public Vector3(float z){x=y=0;this.z=z;}
 public float sqrMagnitude=>x*x+y*y+z*z;
 public static Vector3 operator-(Vector3 a,Vector3 b)=>new Vector3(a.z-b.z);
 public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
}
public class Transform {public Vector3 position;public Vector3 forward=new(1);}
public class Player {public Transform transform=new();public float CurrentSpeed,ForwardCruiseSpeedRatio;public int CurrentSpeedStage=3,PortalExitSequence;public bool IsBoosting;}
public static class Time {public static float time,deltaTime=.02f,timeScale=1;}
public class AudioSource {public int Stops;public void Stop(){Stops++;}}
public class Probe {
 public bool enabled=true,initialized,previousBoost,nearSurface;
 public Player observedPlayer;public int portal;
 public float previousRatio,pulseAge=100,nextSample,nextPass,suppressAccelerationUntil,speed,boost,acceleration,proximity,passAge=100;
 public float response=5,accelerationSeconds=.65f;public Vector3 previousPosition;
 public AudioSource windAudio=new(),passAudio=new();
 public int samples,audioUpdates;
 void SampleScenery(Player p,Transform c){samples++;}
 void UpdateAudio(Transform c,float dt){audioUpdates++;}
'@
$methods = @('ActualStageWeight','Pulse','PassWeight','Update','Reset') | ForEach-Object { Method $source $_ }
$pose = @'
}
public class Pose {
 public float fastPoseEnterSpeedRatio=.72f,fastPoseExitSpeedRatio=.55f;public bool fastPose;
'@ + (Method $animation 'UseFastPose') + '}'
$tests = @'
public static class Tests {
 static int n;static void Check(bool ok,string label){if(!ok)throw new Exception(label);n++;}
 static void Tick(Probe p,Player player,float distance=.0f){Time.time+=Time.deltaTime;player.transform.position=new(player.transform.position.z+distance);p.Update(player,new Transform(),true);}
 public static int Run(){
  Check(Probe.ActualStageWeight(-3,1)==0 && Probe.ActualStageWeight(0,1)==0,"reverse/zero FOV");
  Check(Probe.ActualStageWeight(3,0)==0 && Probe.ActualStageWeight(3,.5f)==.5f,"stationary and partial acceleration");
  Check(Probe.ActualStageWeight(1,1)==1,"stage envelope capped");
  for(int i=0;i<=100;i++){float x=Probe.Pulse(i*.01f,.65f);Check(x>=0&&x<=1,"bounded impulse");}
  Check(Probe.Pulse(0,.65f)==0 && Probe.Pulse(1,.65f)==0,"no impulse discontinuity or sustained pulse");
  Check(Probe.PassWeight(1,10,1,1)==0,"head-on wall is not fly-by");
  Check(Probe.PassWeight(1,10,0,0)==0 && Probe.PassWeight(11,10,0,1)==0,"stationary/distant scenery silent");
  Check(Probe.PassWeight(1,10,0,1)>Probe.PassWeight(8,10,0,1),"near scenery stronger");
  var p=new Probe();var pilot=new Player{CurrentSpeed=100,ForwardCruiseSpeedRatio=1};
  for(int i=0;i<60;i++)Tick(p,pilot);Check(p.speed==0&&p.acceleration==0,"motor says 100 but blocked player stays quiet");
  for(int i=0;i<120;i++)Tick(p,pilot,2);Check(p.speed>.99f&&p.acceleration==0,"cruise settles without sustained impulse");
  pilot.IsBoosting=true;for(int i=0;i<6;i++)Tick(p,pilot,2);Check(p.acceleration>.9f,"boost onset pulse");
  for(int i=0;i<60;i++)Tick(p,pilot,2);Check(p.acceleration==0&&p.boost>.99f,"held boost cannot retrigger impulse");
  pilot.PortalExitSequence++;Tick(p,pilot,200);Check(p.speed==0&&p.acceleration==0,"portal clears stale motion and pulse");
  for(int i=0;i<30;i++)Tick(p,pilot,2);Check(p.acceleration==0,"portal does not fake another boost start");
  p.Update(null,new Transform(),false);Check(!p.initialized&&p.speed==0&&p.boost==0&&p.windAudio.Stops>0,"menu/death resets and stops sound");
  Tick(p,pilot,2);Check(p.acceleration==0,"resume held boost neutral");
  int updates=p.audioUpdates;Time.timeScale=0;Tick(p,pilot);Check(!p.initialized&&p.audioUpdates==updates,"pause stops presentation");Time.timeScale=1;
  for(int i=0;i<60;i++)Tick(p,pilot,2);pilot.CurrentSpeedStage=-3;for(int i=0;i<120;i++)Tick(p,pilot,-2);Check(p.speed<.001f&&p.boost<.001f,"reverse clears forward speed effects");
  pilot.CurrentSpeedStage=3;Tick(p,pilot,2);Time.deltaTime=.5f;Tick(p,pilot,2);Check(!p.initialized,"long stall discards stale sample");Time.deltaTime=.02f;
  p.enabled=false;Tick(p,pilot);Check(p.speed==0&&!p.initialized,"Inspector disable clears state");
  var pose=new Pose();Check(!pose.UseFastPose(3,0),"stage selection alone does not crouch");
  Check(pose.UseFastPose(3,.8f),"accelerating to cruising speed enters fast pose");
  Check(pose.UseFastPose(3,.65f),"hysteresis avoids pose chatter");
  Check(!pose.UseFastPose(3,.4f),"slowed pilot returns to normal pose");
  Check(!pose.UseFastPose(2,1),"downshift exits fast pose");
  return n;
 }
}}
'@
Add-Type -TypeDefinition ($stub + ($methods -join "`n") + $pose + $tests)
"Flight sensation checks passed: $([FlightChecks.Tests]::Run())"
if ($source -match 'Physics\.RaycastAll|FindObjects|\[Rpc|transform\.position\s*=') { throw 'Presentation must be allocation-bounded and read-only to gameplay' }
foreach ($guard in @('QueryTriggerInteraction.Ignore','collider.attachedRigidbody != null','GetComponentInParent<Player>','GetComponentInParent<MagicProjectile>')) {
    if (!$source.Contains($guard)) { throw "Missing scenery filter: $guard" }
}
"Read-only gameplay, trigger/player/projectile exclusions verified."
