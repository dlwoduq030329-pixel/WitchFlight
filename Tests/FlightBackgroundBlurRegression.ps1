# Exercise the production render-frame history, including pause, cuts and repeated renders.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$source = Get-Content Assets/Script/Camera/SpeedCameraEffects.cs -Raw
$methods = foreach ($name in @('ResetFlightMotion', 'PrepareFlightMotion', 'ContinuousFlightDisplacement')) {
    $match = [regex]::Match($source, '(?ms)^    (?:private|internal) (?:static )?[\w<>]+ '+$name+'\([^{}]*?\)\r?\n    \{.*?^    \}')
    if (!$match.Success) { throw "Missing production method: $name" }
    $match.Value -replace '^    (?:private|internal) ', '    public '
}
$stub = @'
using System;
namespace FlightBlurChecks {
 public static class Time { public static int frameCount; }
 public struct Vector3 {
  public float x,y,z; public Vector3(float a,float b,float c){x=a;y=b;z=c;}
  public float sqrMagnitude=>x*x+y*y+z*z; public static Vector3 zero=>default;
  public static Vector3 operator-(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
 }
 public struct Vector4 {
  public float x,y,z,w; public Vector4(float a,float b,float c,float d){x=a;y=b;z=c;w=d;}
  public static Vector4 zero=>default;
 }
 public class Transform { public Vector3 position; }
 public class Obj { public bool IsValid=true,HasInputAuthority=true; }
 public class Player {
  public static Player LocalPlayer; public Obj Object=new(); public bool IsAlive=true,IsReturningToMap;
  public Transform transform=new();
 }
 public class BattleManager {public static BattleManager Instance=new(); public bool IsGameplayActive=true;}
 public static class CombatPresentation {public static bool MenuOpen;}
 public class Probe {
  public Player flightMotionPlayer; public Vector3 previousFlightPosition;
  public int flightMotionFrame=-1; public bool flightMotionOnly=true; public Vector4 FlightMotion;
'@
$test = @'
 }
 public static class Tests {
  static int count;
  static void Check(bool ok,string why){if(!ok)throw new Exception(why);count++;}
  static bool Zero(Probe p)=>p.FlightMotion.x==0&&p.FlightMotion.y==0&&p.FlightMotion.z==0;
  public static int Run(){
   var p=new Probe();var player=Player.LocalPlayer=new Player();
   Time.frameCount=1;p.PrepareFlightMotion();Check(Zero(p)&&p.FlightMotion.w==1,"first render has no history smear");
   Time.frameCount++;player.transform.position=new(0,0,1);p.PrepareFlightMotion();
   Check(p.FlightMotion.z==1,"forward flight retains displacement");
   player.transform.position=new(0,0,2);p.PrepareFlightMotion();Check(p.FlightMotion.z==1,"repeated render cannot consume history twice");
   Time.frameCount++;p.PrepareFlightMotion();Check(p.FlightMotion.z==1,"next render continues from last rendered sample");
   Time.frameCount++;p.PrepareFlightMotion();Check(Zero(p),"stationary player generates no blur, regardless of mouse orbit");
   Time.frameCount++;player.transform.position=new(1,2,2);p.PrepareFlightMotion();
   Check(p.FlightMotion.x==1&&p.FlightMotion.y==2&&p.FlightMotion.z==0,"side and vertical flight retained");
   Time.frameCount++;player.transform.position=new(1,2,102);p.PrepareFlightMotion();Check(Zero(p),"teleport cannot smear scenery");
   Time.frameCount+=2;player.transform.position=new(1,2,103);p.PrepareFlightMotion();Check(Zero(p),"render gap discards stale history");
   Time.frameCount++;player.transform.position=new(1,2,104);p.PrepareFlightMotion();Check(p.FlightMotion.z==1,"flight resumes after gap");
   CombatPresentation.MenuOpen=true;Time.frameCount++;p.PrepareFlightMotion();Check(Zero(p),"menu clears travel");
   CombatPresentation.MenuOpen=false;Time.frameCount++;player.transform.position=new(1,2,108);p.PrepareFlightMotion();Check(Zero(p),"menu close starts fresh");
   player.IsAlive=false;Time.frameCount++;p.PrepareFlightMotion();Check(Zero(p),"death clears travel");
   player.IsAlive=true;Time.frameCount++;player.transform.position=new(1,2,110);p.PrepareFlightMotion();Check(Zero(p),"respawn starts fresh");
   Player.LocalPlayer=new Player();Time.frameCount++;p.PrepareFlightMotion();Check(Zero(p),"new player does not inherit old travel");
   Player.LocalPlayer.IsReturningToMap=true;Time.frameCount++;p.PrepareFlightMotion();Check(Zero(p),"boundary return has no blur");
   p.flightMotionOnly=false;Time.frameCount++;p.PrepareFlightMotion();Check(p.FlightMotion.w==0,"legacy motion vector path remains selectable");
   p.ResetFlightMotion();Check(p.flightMotionFrame==-1&&p.flightMotionPlayer==null&&Zero(p),"disable clears lifetime state");
   return count;
  }
 }
}
'@
Add-Type -TypeDefinition ($stub + ($methods -join "`n") + $test)
"Flight background blur checks passed: $([FlightBlurChecks.Tests]::Run())"
$feature = Get-Content Assets/Script/Camera/PeripheralSpeedBlurFeature.cs -Raw
if ($feature -notmatch 'if \(data.needsMotion\) builder.UseTexture\(data.motion\)' -or
    $feature -notmatch 'effects.NeedsBackgroundMotionVectors\s+\? ScriptableRenderPassInput.Motion') {
    throw 'Translation-only rendering must not request the object motion-vector pass'
}
'RenderGraph and legacy motion-vector fallback checked.'
