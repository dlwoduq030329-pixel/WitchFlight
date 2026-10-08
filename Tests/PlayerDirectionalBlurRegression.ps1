# Production motion/projection logic under stubs. Actual GPU appearance requires Unity Play Mode.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$source = Get-Content Assets/Script/Camera/PlayerDirectionalBlur.cs -Raw
function Method([string]$name) {
    $m = [regex]::Match($source, '(?ms)^    (?:public|private|internal) (?:static )?[\w<>]+ '+$name+'\([^{}]*?\)\r?\n    \{.*?^    \}')
    if (!$m.Success) { throw "Production method missing: $name" }
    $m.Value -replace '^    (?:private|internal) ', '    public '
}
$gate = [regex]::Match($source,'(?s)internal static bool ShouldBlur\(.*?;').Value.Replace('internal','public')
$stubs = @'
using System;
using System.Collections.Generic;
namespace DirectionalBlurChecks {
 public static class Time {public static float unscaledTime,unscaledDeltaTime=.016f;}
 public static class Mathf {
  public static float Max(float a,float b)=>Math.Max(a,b);public static float Abs(float a)=>Math.Abs(a);public static float Exp(float a)=>(float)Math.Exp(a);
  public static float Clamp(float a,float b,float c)=>Math.Clamp(a,b,c);public static float Clamp01(float a)=>Clamp(a,0,1);
  public static float Lerp(float a,float b,float t)=>a+(b-a)*Clamp01(t);
 }
 public struct Vector2 {
  public float x,y;public Vector2(float x,float y){this.x=x;this.y=y;}public static Vector2 zero=>default;
  public float magnitude=>(float)Math.Sqrt(x*x+y*y);
  public static Vector2 Min(Vector2 a,Vector2 b)=>new(Math.Min(a.x,b.x),Math.Min(a.y,b.y));
  public static Vector2 Max(Vector2 a,Vector2 b)=>new(Math.Max(a.x,b.x),Math.Max(a.y,b.y));
  public static Vector2 Scale(Vector2 a,Vector2 b)=>new(a.x*b.x,a.y*b.y);
 }
 public struct Vector3 {
  public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
  public static Vector3 zero=>default;public float sqrMagnitude=>x*x+y*y+z*z;
  public Vector3 normalized=>sqrMagnitude>.00001f?this/(float)Math.Sqrt(sqrMagnitude):zero;
  public static Vector3 operator+(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
  public static Vector3 operator-(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
  public static Vector3 operator*(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);
  public static Vector3 operator/(Vector3 a,float b)=>new(a.x/b,a.y/b,a.z/b);
  public static Vector3 Scale(Vector3 a,Vector3 b)=>new(a.x*b.x,a.y*b.y,a.z*b.z);
  public static Vector3 Lerp(Vector3 a,Vector3 b,float t)=>a+(b-a)*Mathf.Clamp01(t);
  public static implicit operator Vector2(Vector3 a)=>new(a.x,a.y);
 }
 public struct Vector4 {public float x,y,z,w;public Vector4(float x,float y,float z,float w){this.x=x;this.y=y;this.z=z;this.w=w;}public static Vector4 zero=>default;}
 public struct Bounds {public Vector3 center,extents;}
 public class Transform {public Vector3 position;}
 public class Player {public Transform transform=new();public int CurrentSpeedStage=3;public bool IsBoosting=true;public Vector3 LockAimPoint=>transform.position;}
 public class Renderer {public Player owner;public Bounds bounds=>new(){center=owner.transform.position,extents=new(.5f,.8f,.3f)};}
 public class Source {public Renderer renderer;}
 public class Camera {
  public Vector3 position=new(0,0,-3);public bool orthographic,lookingRight;public float nearClipPlane=.1f;
  public int scaledPixelWidth=1920,scaledPixelHeight=1080;
  public Vector3 WorldToViewportPoint(Vector3 p){var v=p-position;if(lookingRight)v=new(-v.z,v.y,v.x);float scale=orthographic?2:2*v.z;return new(.5f+v.x/scale,.5f+v.y/scale,v.z);}
 }
 public class PlayerDirectionalBlurSettings {public bool enabled=true,requireBoost=true;public float trailDistance=.18f,maximumPixels=24,strength=.45f,preserveBody=.7f,responseSpeed=10;}
 public class Probe {
  public Player owner;public float nextRefresh,weight;public bool hasPosition,ProtectPlayer;public Vector3 previousPosition,velocity;
  public PlayerDirectionalBlurSettings settings;public Vector4 Trail,Anchor,ScreenRect;public List<Source> sources=new();
  public bool HasBlur=>weight>.001f&&settings!=null&&settings.enabled&&settings.trailDistance>0&&settings.maximumPixels>0&&settings.strength>0;
  void ReleaseSources(){sources.Clear();}void RefreshSources(){sources.Add(new(){renderer=new(){owner=owner}});}
  bool IsVisible(Source s,Camera c)=>true;
'@
$methods = @('Update','ResetMotion','Prepare','ProjectionLimit') | ForEach-Object { Method $_ }
$tests = @'
 }
 public static class Tests {
  static int n;static void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
  static Probe Moving(Player p,Vector3 delta,PlayerDirectionalBlurSettings s){var b=new Probe();b.Update(p,true,true,s);p.transform.position+=delta;Time.unscaledTime+=.016f;b.Update(p,true,true,s);return b;}
  public static int Run(){
   foreach(int stage in new[]{-3,-2,-1,0,1,2,3})foreach(bool boost in new[]{false,true})
    Check(Probe.ShouldBlur(true,stage,boost,true)==(stage==3&&boost),"stage/boost gate "+stage+" "+boost);
   Check(Probe.ShouldBlur(true,3,false,false)&&!Probe.ShouldBlur(false,3,true,false),"optional boost and master toggles");
   var settings=new PlayerDirectionalBlurSettings();var p=new Player();var b=new Probe();var c=new Camera();
   b.Update(p,true,true,settings);Check(!b.HasBlur,"new player has no spurious movement vector");
   p.transform.position=new(0,0,.5f);c.position=new(0,0,-2.5f);b.Update(p,true,true,settings);b.Prepare(c);
   Check(b.HasBlur&&b.Trail.z>0&&b.Anchor.z<0,"forward flight creates backward perspective smear even with a following camera");
   Check(Math.Abs(b.Trail.x)<.0001f&&Math.Abs(b.Trail.y)<.0001f,"straight centered flight is radial, not an arbitrary sideways streak");
   Check(b.Trail.w>0&&b.Trail.w<=settings.strength,"strength eases in and remains bounded");
   Check(b.ScreenRect.x<b.ScreenRect.z&&b.ScreenRect.y<b.ScreenRect.w,"valid bounded character screen rectangle");
   var side=new Player();var s=Moving(side,new(1,0,0),settings);c=new Camera{position=new(1,0,-3)};s.Prepare(c);
   Check(s.Trail.x<0&&Math.Abs(s.Trail.z)<.0001,"rightward travel smears only left, not forward");
   var orbit=new Player();var o=Moving(orbit,new(0,0,1),settings);c=new Camera{position=new(-3,0,1),lookingRight=true};o.Prepare(c);
   Check(o.Trail.x>0&&Math.Abs(o.Trail.z)<.0001,"camera rotation reprojects world movement correctly");
   c.orthographic=true;o.Prepare(c);Check(o.Trail.z==0,"orthographic projection never scales the silhouette");
   c.position=orbit.LockAimPoint;o.Prepare(c);Check(o.Trail.w==0,"near-plane/invalid projection disables trail safely");
   var limit=Probe.ProjectionLimit(new(.1f,0),.2f,new(.3f,.3f),new(1920,1080),24);
   var bound=192+.2f*(float)Math.Sqrt(576*576+324*324);Check(limit*bound<=24.001f,"translation plus radial stretch respects pixel cap");
   Check(Probe.ProjectionLimit(default,0,default,new(1920,1080),24)==0,"zero motion projection stays finite");
   float l1=Probe.ProjectionLimit(new(.1f,0),.2f,new(.3f,.3f),new(1920,1080),24);
   float l2=Probe.ProjectionLimit(new(.1f,0),.2f,new(.3f,.3f),new(3840,2160),48);Check(Math.Abs(l1-l2)<.0001,"resolution scaling preserves visual length");
   p.IsBoosting=false;b.Update(p,true,true,settings);Check(!b.HasBlur,"Shift release stops character blur immediately");
   p.IsBoosting=true;p.transform.position+=new Vector3(0,0,1);b.Update(p,true,true,settings);Check(b.HasBlur,"boost restarts while moving");
   b.Update(p,true,true,settings);Check(!b.HasBlur,"stationary player/camera-only orbit does not smear");
   p.transform.position+=new Vector3(0,0,100);b.Update(p,true,true,settings);Check(!b.HasBlur,"teleport resets blur rather than making a huge streak");
   p.transform.position+=new Vector3(0,0,1);b.Update(p,true,true,settings,false);Check(!b.HasBlur&&b.ProtectPlayer,"boundary view preserves background exclusion but disables character blur");
   b.Update(p,false,true,settings);Check(!b.HasBlur&&!b.ProtectPlayer&&!b.hasPosition,"death/menu reset motion and draw state");
   p=new Player();b=Moving(p,new(1,0,0),settings);p.CurrentSpeedStage=2;p.transform.position+=new Vector3(1,0,0);b.Update(p,true,true,settings);Check(!b.HasBlur,"lower stage never leaves a tail");
   return n;
  }
 }
}
'@
Add-Type -TypeDefinition ($stubs + ($methods -join "`n") + $gate + $tests)
"Directional player blur behavior checks passed: $([DirectionalBlurChecks.Tests]::Run())"
$effects = Get-Content Assets/Script/Camera/SpeedCameraEffects.cs -Raw
$feature = Get-Content Assets/Script/Camera/PeripheralSpeedBlurFeature.cs -Raw
$shader = Get-Content Assets/Resources/PeripheralSpeedBlur.shader -Raw
$mask = Get-Content Assets/Resources/PlayerMotionMask.shader -Raw
if ($source -match 'BakeMesh\(|Instantiate\(|new Mesh\b|Runner\.Spawn|RPC_|AddComponent<') { throw 'No pose clones, mesh snapshots or network objects are allowed' }
if ($effects -notmatch 'motionBlur.intensity.Override\(UsesMaskedBackgroundBlur \? 0f' -or
    $effects -notmatch 'playerBlur\?\.Dispose\(\)') { throw 'Double-blur prevention or lifecycle cleanup missing' }
if ($shader -notmatch 'uv - anchor - shift \* t' -or $shader -notmatch '1.0 \+ _PlayerTrail.z \* t' -or
    $shader -notmatch 'PlayerMask\(sampleUV\)' -or $shader -notmatch 'sourceEye \+ _PlayerAnchor.z \* t') { throw 'Directional sampling / isolated silhouette / occlusion protection missing' }
if ($mask -notmatch 'clip\(sceneEye \+ 0.015 - playerEye\)' -or $mask -notmatch '_AlphaMaskMode == 1') { throw 'Mask must preserve scene occlusion and lilToon alpha' }
if ($feature -notmatch 'builder.UseTexture\(data.mask\)' -or $feature -notmatch 'resources.cameraColor = destination' -or
    $feature -notmatch 'BeforeRenderingPostProcessing' -or $feature -notmatch 'ClearFlag.Color, Color.clear') { throw 'RenderGraph/compatibility inputs or clear missing' }
if ($feature.IndexOf('graph.AddRasterRenderPass<MaskData>') -gt $feature.IndexOf('graph.AddRasterRenderPass<PassData>')) { throw 'Mask must precede blur' }
foreach ($old in @('Assets/Script/Camera/PlayerSpeedVisuals.cs','Assets/Resources/PlayerSpeedGhost.shader')) {
    if (Test-Path $old) { throw "Unwanted pose-ghost implementation remains: $old" }
}
$battle = Get-Content Assets/Scenes/Battle.unity -Raw
if ($battle -notmatch 'playerMotionBlur:\s+enabled: 1\s+requireBoost: 1' -or $battle -match 'playerAfterimages:') { throw 'Battle camera must use directional blur, not pose afterimages' }
'Directional sampling, depth/alpha masking, no duplicated characters, no double blur, cleanup and scene contracts passed.'
