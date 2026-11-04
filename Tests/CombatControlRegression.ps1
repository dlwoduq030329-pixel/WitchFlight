# Production math / bone-pose logic with small Unity stubs; no server or live Play session.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$player = Get-Content Assets/Script/Player.cs -Raw
$projectile = Get-Content Assets/Script/MagicProjectile.cs -Raw
$hud = Get-Content Assets/Script/BattleHud.cs -Raw
$head = Get-Content Assets/Script/PlayerHeadLook.cs -Raw
$damageIndicator = Get-Content Assets/UI/Scripts/DamageIndicator.cs -Raw
function Method([string]$source, [string]$name) {
    $match = [regex]::Match($source, '(?ms)^    (?:private|public) static [\w<>]+ ' + $name + '\([^{}]*?\)\r?\n    \{.*?^    \}')
    if (!$match.Success) { throw "Missing production method: $name" }
    $match.Value -replace '^    private ', '    public '
}
$spriteMethod = [regex]::Match($hud, '(?ms)^    private static Sprite SelectWarningSprite\([^;]+;').Value -replace '^    private ', '    public '
$stubs = @'
using System;
using System.Collections.Generic;
using System.Reflection;
namespace UnityEngine {
 public class SerializeField:Attribute{}
 public class TooltipAttribute:Attribute{public TooltipAttribute(string x){}}
 public class RangeAttribute:Attribute{public RangeAttribute(float a,float b){}}
 public class MinAttribute:Attribute{public MinAttribute(float x){}}
 public class Sprite{}
 public enum HumanBodyBones{Head}
 public class Avatar{public bool isValid=true;}
 public class Animator{public bool isHuman=true;public Avatar avatar=new();public Transform Head; public Transform GetBoneTransform(HumanBodyBones b)=>Head;}
 public static class Mathf {
  public const float Rad2Deg=180f/(float)Math.PI,Deg2Rad=(float)Math.PI/180;
  public static float Clamp(float x,float a,float b)=>Math.Clamp(x,a,b);
  public static float Max(float a,float b)=>Math.Max(a,b);
  public static float Abs(float a)=>Math.Abs(a);
  public static float Sqrt(float a)=>(float)Math.Sqrt(a);
  public static float Atan2(float a,float b)=>(float)Math.Atan2(a,b);
  public static float Round(float a)=>(float)Math.Round(a);
  public static float Exp(float a)=>(float)Math.Exp(a);
 }
 public struct Vector2 {
  public float x,y;public Vector2(float x,float y){this.x=x;this.y=y;}
  public static Vector2 zero=>default;public static Vector2 up=>new(0,1);
  public float sqrMagnitude=>x*x+y*y;
  public void Normalize(){float l=(float)Math.Sqrt(sqrMagnitude);if(l>0){x/=l;y/=l;}}
  public static Vector2 operator+(Vector2 a,Vector2 b)=>new(a.x+b.x,a.y+b.y);
  public static Vector2 Scale(Vector2 a,Vector2 b)=>new(a.x*b.x,a.y*b.y);
  public static Vector2 Lerp(Vector2 a,Vector2 b,float t)=>new(a.x+(b.x-a.x)*Math.Clamp(t,0,1),a.y+(b.y-a.y)*Math.Clamp(t,0,1));
 }
 public struct Vector3 {
  public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
  public static Vector3 zero=>default;public static Vector3 forward=>new(0,0,1);
  public float sqrMagnitude=>x*x+y*y+z*z;
  public Vector3 normalized=>sqrMagnitude>.000001f?this*(1f/(float)Math.Sqrt(sqrMagnitude)):zero;
  public static Vector3 operator*(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);
  public static Vector3 operator-(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
  public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
  public static Vector3 RotateTowards(Vector3 a,Vector3 b,float radians,float unused){
   float angle=(float)Math.Acos(Math.Clamp(Dot(a.normalized,b.normalized),-1,1));
   if(angle<.00001f||radians>=angle)return b;
   float t=radians/angle;return (a*(1-t)-b*(-t)).normalized;
  }
 }
 public struct Quaternion {
  public System.Numerics.Quaternion q;
  public static Quaternion identity=>new(){q=System.Numerics.Quaternion.Identity};
  public static Quaternion Euler(float x,float y,float z)=>new(){q=System.Numerics.Quaternion.CreateFromYawPitchRoll(y*Mathf.Deg2Rad,x*Mathf.Deg2Rad,z*Mathf.Deg2Rad)};
  public static Quaternion Inverse(Quaternion a)=>new(){q=System.Numerics.Quaternion.Inverse(a.q)};
  public static Quaternion operator*(Quaternion a,Quaternion b)=>new(){q=a.q*b.q};
  public static Vector3 operator*(Quaternion a,Vector3 b){var v=System.Numerics.Vector3.Transform(new(b.x,b.y,b.z),a.q);return new(v.X,v.Y,v.Z);}
 }
 public struct Vector4 {
  public float x,y,z,w;public Vector4(float x,float y,float z,float w){this.x=x;this.y=y;this.z=z;this.w=w;}
 }
 public struct Matrix4x4 {
  public float scaleX,scaleY;public bool ortho;
  public static Matrix4x4 Perspective(float verticalFov,float aspect){float y=1f/(float)Math.Tan(verticalFov*Mathf.Deg2Rad*.5f);return new(){scaleX=y/aspect,scaleY=y};}
  public static Vector4 operator*(Matrix4x4 m,Vector4 v)=>new(v.x*m.scaleX,v.y*m.scaleY,0,m.ortho?1:-v.z);
 }
 public struct Rect {
  public float x,y,width,height;public Rect(float x,float y,float w,float h){this.x=x;this.y=y;width=w;height=h;}
  public Vector2 center=>new(x+width/2,y+height/2);
 }
 public class Transform {
  public Transform parent;public string name;public int childCount;public Transform[] bones=Array.Empty<Transform>();
  public Quaternion localRotation=Quaternion.identity;
  public Quaternion rotation {get=>parent==null?localRotation:parent.rotation*localRotation;set=>localRotation=parent==null?value:Quaternion.Inverse(parent.rotation)*value;}
  public Vector3 InverseTransformDirection(Vector3 d)=>Quaternion.Inverse(rotation)*d;
  public bool IsChildOf(Transform body){for(var p=parent;p!=null;p=p.parent)if(p==body)return true;return false;}
  public T[] GetComponentsInChildren<T>(bool includeInactive)=>(T[])(object)bones;
 }
}
'@
$probe = "namespace ControlChecks { using UnityEngine; public static class MathProbe {`n" +
    (Method $projectile 'ResolveLaunchDirection') + "`n" + (Method $projectile 'ResolveHomingDirection') + "`n" +
    (Method $hud 'GetThreatViewDirection') + "`n" + (Method $hud 'GetThreatScreenPoint') + "`n" +
    (Method $hud 'IsInsideThreatViewport') + "`n" +
    (Method $damageIndicator 'CalculateFlightDirection') + "`n" + $spriteMethod + "`n}}"
$checks = @'
namespace ControlChecks {using UnityEngine;
public static class Tests {
 static int n;static void Check(bool ok,string name){if(!ok)throw new Exception(name);n++;}
 static bool Near(float a,float b)=>Math.Abs(a-b)<.005f;
 static bool Same(Vector3 a,Vector3 b)=>(a-b).sqrMagnitude<.0001f;
 static bool SameQ(Quaternion a,Quaternion b)=>Math.Abs(System.Numerics.Quaternion.Dot(a.q,b.q))>.99999f;
 static void Set(object obj,string key,object value)=>obj.GetType().GetField(key,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(obj,value);
 public static int Run(){
  Vector3 forward=Vector3.forward,side=new(10,0,.1f);
  Check(Same(MathProbe.ResolveLaunchDirection(true,forward,forward,side),side.normalized),"Lock launches directly sideways, not nose-forward");
  Check(Same(MathProbe.ResolveLaunchDirection(false,forward,forward,side),forward),"Non-lock / mine launch unchanged");
  Check(Same(MathProbe.ResolveLaunchDirection(true,forward,forward,Vector3.zero),forward),"Overlapping target has valid fallback");
  Check(Same(MathProbe.ResolveLaunchDirection(false,Vector3.zero,forward,side),forward),"Zero aim has valid fallback");
  Check(Same(MathProbe.ResolveHomingDirection(true,forward,side,0,.02f),side.normalized),"Lock tracks directly even with zero turn speed");
  Check(Same(MathProbe.ResolveHomingDirection(true,forward,new(-10,3,4),100,.02f),new Vector3(-10,3,4).normalized),"Moving target re-aims directly");
  Check(Same(MathProbe.ResolveHomingDirection(false,forward,side,0,.02f),forward),"Non-lock zero turn speed unchanged");
  Check(Same(MathProbe.ResolveHomingDirection(true,forward,Vector3.zero,100,.02f),forward),"Zero target delta preserves direction");
  var rect=new Rect(100,50,1000,600);
  var right=MathProbe.GetThreatScreenPoint(new(1,0,0),rect,50);
  Check(Near(right.x,1050)&&Near(right.y,350),"Right threat respects camera pixel rectangle");
  var left=MathProbe.GetThreatScreenPoint(new(-1,0,0),rect,50);Check(Near(left.x,150),"Left threat");
  var front=MathProbe.GetThreatScreenPoint(forward,rect,50);Check(Near(front.x,600)&&Near(front.y,600),"Dead-ahead threat at top");
  var back=MathProbe.GetThreatScreenPoint(new(0,0,-1),rect,50);Check(Near(back.x,600)&&Near(back.y,100),"Directly behind at bottom");
  var backRight=MathProbe.GetThreatScreenPoint(new(1,0,-1),rect,50);Check(backRight.x>600&&backRight.y<350,"Back-right has distinct lower-right bearing");
  var tiny=MathProbe.GetThreatScreenPoint(new(1,1,1),new Rect(0,0,40,20),64);Check(Near(tiny.x,20)&&Near(tiny.y,10),"Small viewport clamps safely");
  Vector3 fixedThreat=new(0,0,100),cameraPosition=Vector3.zero;
  Vector3 initialView=MathProbe.GetThreatViewDirection(cameraPosition,Quaternion.identity,fixedThreat);
  Vector2 initialPoint=MathProbe.GetThreatScreenPoint(initialView,rect,50);
  Check(Near(initialPoint.x,600)&&Near(initialPoint.y,600),"Camera-front threat starts at top");
  Vector3 rightTurnView=MathProbe.GetThreatViewDirection(cameraPosition,Quaternion.Euler(0,90,0),fixedThreat);
  Vector2 rightTurnPoint=MathProbe.GetThreatScreenPoint(rightTurnView,rect,50);
  Check(rightTurnView.x<0&&Near(rightTurnPoint.x,150),"Turning camera right moves the same stationary threat left without rotating the player");
  Vector3 leftTurnView=MathProbe.GetThreatViewDirection(cameraPosition,Quaternion.Euler(0,-90,0),fixedThreat);
  Check(leftTurnView.x>0&&Near(MathProbe.GetThreatScreenPoint(leftTurnView,rect,50).x,1050),"Turning camera left moves threat right");
  Vector3 rearView=MathProbe.GetThreatViewDirection(cameraPosition,Quaternion.Euler(0,180,0),fixedThreat);
  Check(rearView.z<0&&Near(MathProbe.GetThreatScreenPoint(rearView,rect,50).y,100),"Half-turn moves previously front threat to rear arc");
  Check(MathProbe.GetThreatViewDirection(cameraPosition,Quaternion.Euler(-45,0,0),fixedThreat).y<0,"Looking upward places level threat below camera gaze");
  Check(MathProbe.GetThreatViewDirection(cameraPosition,Quaternion.Euler(45,0,0),fixedThreat).y>0,"Looking downward places level threat above camera gaze");
  Vector3 offsetView=MathProbe.GetThreatViewDirection(new(10,2,-5),Quaternion.identity,new(0,2,20));
  Check(Same(offsetView,new(-10,0,25)),"Third-person camera position, not player aim point, is the warning origin");
  Check(MathProbe.GetThreatViewDirection(new(0,0,10),Quaternion.identity,new(0,0,5)).z<0,"Threat between camera and player uses camera-side front/back");
  Check(MathProbe.GetThreatViewDirection(cameraPosition,Quaternion.Euler(0,0,90),new(10,0,0)).y<0,"Camera roll is reflected in warning screen axes");
  var projection=Matrix4x4.Perspective(60,16f/9f);
  Check(MathProbe.IsInsideThreatViewport(initialView,projection,.1f,1000),"Front projectile is inside view and gets no warning");
  Check(!MathProbe.IsInsideThreatViewport(rearView,projection,.1f,1000),"Behind-camera projectile remains a warning candidate");
  Check(MathProbe.IsInsideThreatViewport(new(9,0,10),projection,.1f,1000)&&!MathProbe.IsInsideThreatViewport(new(11,0,10),projection,.1f,1000),"Horizontal camera viewport edges respected");
  Check(MathProbe.IsInsideThreatViewport(new(0,5,10),projection,.1f,1000)&&!MathProbe.IsInsideThreatViewport(new(0,6,10),projection,.1f,1000),"Vertical camera viewport edges respected");
  Check(!MathProbe.IsInsideThreatViewport(new(-11,0,10),projection,.1f,1000)&&!MathProbe.IsInsideThreatViewport(new(0,-6,10),projection,.1f,1000),"Left and bottom offscreen candidates retained");
  Check(!MathProbe.IsInsideThreatViewport(new(0,0,.05f),projection,.1f,1000)&&!MathProbe.IsInsideThreatViewport(new(0,0,1100),projection,.1f,1000),"Clipped projectiles are not treated as visible");
  Check(MathProbe.IsInsideThreatViewport(new(15,0,10),Matrix4x4.Perspective(90,16f/9f),.1f,1000),"FOV widening updates visibility");
  Check(!MathProbe.IsInsideThreatViewport(new(15,0,10),Matrix4x4.Perspective(90,1),.1f,1000),"Aspect ratio changes update visibility");
  Check(!MathProbe.IsInsideThreatViewport(rightTurnView,projection,.1f,1000)&&MathProbe.IsInsideThreatViewport(initialView,projection,.1f,1000),"Camera turning toggles same stationary projectile between warning and hidden");
  Check(MathProbe.IsInsideThreatViewport(new(4,4,10),new Matrix4x4{scaleX=.2f,scaleY=.2f,ortho=true},.1f,1000),"Orthographic projection also supported");
  Check(!MathProbe.IsInsideThreatViewport(new(float.NaN,0,10),projection,.1f,1000),"Invalid projected coordinate is not classified visible");
  var a=new Sprite();var b=new Sprite();
  Check(MathProbe.SelectWarningSprite(a,b,true)==a&&MathProbe.SelectWarningSprite(a,b,false)==b,"Two sprites alternate");
  Check(MathProbe.SelectWarningSprite(a,null,false)==a&&MathProbe.SelectWarningSprite(null,b,true)==b,"Either single sprite works");
  Check(MathProbe.SelectWarningSprite(null,null,true)==null,"No sprites selects procedural fallback");
  Check(Near(MathProbe.CalculateFlightDirection(new(0,0,1)),0),"Damage directly ahead at top");
  Check(Near(MathProbe.CalculateFlightDirection(new(1,0,0)),90),"Damage from right");
  Check(Near(MathProbe.CalculateFlightDirection(new(-1,0,0)),-90),"Damage from left");
  Check(Near(Math.Abs(MathProbe.CalculateFlightDirection(new(0,0,-1))),180),"Damage behind at bottom");
  Check(Near(MathProbe.CalculateFlightDirection(new(0,1,0)),0),"Damage overhead at top");
  Check(Near(Math.Abs(MathProbe.CalculateFlightDirection(new(0,-1,0))),180),"Damage below at bottom");
  var body=new Transform();var bone=new Transform{parent=body,name="Head",childCount=4};var hips=new Transform{parent=body};
  var look=new PlayerHeadLook();Set(look,"headBone",bone);
  var angles=look.GetAngles(body,new Vector3(1,1,1).normalized);Check(Near(angles.x,45)&&Near(angles.y,-35.5f),"Yaw/pitch relative to flight body");
  Check(Near(look.GetAngles(body,new(1,0,0)).x,70),"Yaw limited to 70 degrees");
  Check(Near(look.GetAngles(body,new(0,1,0)).y,-40),"Pitch limited to 40 degrees");
  Check(Near(look.GetAngles(body,new(float.NaN,0,1)).x,0),"NaN input rejected");
  Check(Near(look.GetAngles(body,new(float.PositiveInfinity,0,1)).x,0),"Infinite input rejected");
  Check(Near(look.GetAngles(body,new(0,0,999)).x,0),"Invalid magnitude rejected");
  body.rotation=Quaternion.Euler(20,90,35);
  Check(Near(look.GetAngles(body,body.rotation*forward).x,0),"Rolled flight uses local body axes");
  Quaternion baseHead=Quaternion.Euler(4,0,0);bone.localRotation=baseHead;
  Quaternion bodyBefore=body.rotation,hipsBefore=hips.rotation;
  for(int i=0;i<300;i++){
   look.Apply(body,null,new Vector2(50,10),true,1f/60);look.RestorePose();
   if(!SameQ(bone.localRotation,baseHead))throw new Exception("Head offset accumulates with culled animator");
  }
  Check(SameQ(body.rotation,bodyBefore)&&SameQ(hips.rotation,hipsBefore),"Head does not rotate body or hips");
  Check(SameQ(bone.localRotation,baseHead),"Head animation restored over 300 frames");
  look.Apply(body,null,new Vector2(50,10),true,1);Check(!SameQ(bone.localRotation,baseHead),"Head visibly changes");
  look.Reset();Check(SameQ(bone.localRotation,baseHead),"Disable/despawn restores pose");
  look.Apply(body,null,new Vector2(50,10),false,1);Check(SameQ(bone.localRotation,baseHead),"Intro/dead state leaves animation alone");
  Set(look,"headBone",body);look.Apply(body,null,new Vector2(50,10),true,1);Check(SameQ(body.rotation,bodyBefore),"Invalid head reference cannot rotate player root");
  return n;
 }
}}
'@
$headBody = [regex]::Replace($head, '(?m)^using .*?;\r?\n', '')
Add-Type -TypeDefinition ("using UnityEngine;`n" + $stubs + $headBody + $probe + $checks)
"Combat control checks passed: $([ControlChecks.Tests]::Run())"
foreach ($expected in '[Networked] private Vector2 HeadLookAngles', 'headLook.RestorePose();',
    'headLook.Apply(transform, animator, angles, canLook, Time.deltaTime);',
    'HeadLookAngles = headLook.GetAngles(transform, data.aimDirection);', 'MagicProjectile.ResolveLaunchDirection(stats.requiresTarget,') {
    if (!$player.Contains($expected)) { throw "Missing Player integration: $expected" }
}
foreach ($expected in 'nearest = shot;', 'out MagicProjectile nearest', 'if (!HasBlastSight(shot.transform.position, observer)) continue;',
    'shot.Finished', 'shot.ShooterTeam == observer.TeamIndex') {
    if (!$projectile.Contains($expected)) { throw "Missing threat filter: $expected" }
}
'Network head angles, root-pose isolation, projectile direction and warning integration checks passed.'
foreach ($expected in 'RpcTargets.InputAuthority, Channel = RpcChannel.Reliable',
    'RPC_ReceivedDamageDirection(source.LockAimPoint);', 'BattleHud.ShowReceivedDamage(this, attackerPositionAtHit);') {
    if (!$player.Contains($expected)) { throw "Missing authoritative victim-only hit snapshot: $expected" }
}
foreach ($expected in 'slot.damagePosition = position;', 'slot.damagePosition - origin', 'SetReceiver(Transform camera, Transform receiver)') {
    if (!$damageIndicator.Contains($expected)) { throw "Missing frozen position / receiver binding: $expected" }
}
foreach ($expected in 'victim != Player.LocalPlayer', 'damageIndicator.TriggerDamageSense(attackerPositionAtHit, instance.receivedDamageColor)') {
    if (!$hud.Contains($expected)) { throw "Missing local damage presentation: $expected" }
}
'Hit direction uses a server snapshot, victim-only reliable event and frozen pooled world position.'
if (!$hud.Contains('GetThreatViewDirection(worldCamera.transform.position, worldCamera.transform.rotation,') -or
    !$hud.Contains('if (show) PlaceThreatIndicator();')) { throw 'Warning must use the current camera pose and refresh after camera rendering' }
if ($hud.Contains('InverseTransformDirection(incomingThreat.transform.position - owner.LockAimPoint)')) { throw 'Warning still uses the character origin' }
foreach ($element in 'warning.rectTransform','warningGlow.rectTransform','warningFrame.rectTransform','parryCueRing') {
    if (!$hud.Contains(('PlaceScreen(' + $element + ', point)'))) { throw "Warning element has a different bearing: $element" }
}
'Camera-origin/rotation warning and shared icon/halo/frame/parry-ring positioning checked.'
if (!$hud.Contains('warningLeadSeconds, offscreenThreatFilter)') -or !$hud.Contains('if (!IsOffscreenThreat(incomingThreat))')) { throw 'Offscreen filter missing in selection or render-time placement' }
if (!$projectile.Contains('if (candidateFilter != null && !candidateFilter(shot)) continue;')) { throw 'Visible shots can hide offscreen candidates' }
'Offscreen-only selection and render-time visibility guard checked.'
