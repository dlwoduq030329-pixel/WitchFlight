# Production bearing and distance math; Unity canvas appearance still needs play-mode review.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$hud = Get-Content Assets/Script/BattleHud.cs -Raw
$orbit = Get-Content Assets/Script/BattleHud.ThreatOrbit.cs -Raw
$bearing = [regex]::Match($hud, '(?ms)^    private static Vector2 GetThreatScreenPoint\([^{}]*?\)\r?\n    \{.*?^    \}').Value
$fraction = [regex]::Match($orbit, '(?ms)^    private static float ThreatDistanceFraction\([^;]+;').Value
if (!$bearing -or !$fraction) { throw 'Missing production warning math' }
$code = @'
using System;
namespace OrbitChecks {
public struct Vector2 {
 public float x,y;public Vector2(float x,float y){this.x=x;this.y=y;}
 public static Vector2 up=>new(0,1); public float sqrMagnitude=>x*x+y*y;
 public void Normalize(){float n=(float)Math.Sqrt(sqrMagnitude);x/=n;y/=n;}
 public static Vector2 Scale(Vector2 a,Vector2 b)=>new(a.x*b.x,a.y*b.y);
 public static Vector2 operator+(Vector2 a,Vector2 b)=>new(a.x+b.x,a.y+b.y);
 public static Vector2 operator-(Vector2 a,Vector2 b)=>new(a.x-b.x,a.y-b.y);
}
public struct Vector3 {public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}}
public struct Rect {public float x,y,width,height;public Vector2 center=>new(x+width/2,y+height/2);}
public static class Mathf {public static float Abs(float x)=>Math.Abs(x);public static float Max(float a,float b)=>Math.Max(a,b);public static float Clamp01(float x)=>Math.Clamp(x,0,1);}
public static class Check {
'@
$code += $bearing + $fraction
$code += @'
 static int n;static void Assert(bool ok,string name){n++;if(!ok)throw new Exception(name);}
 public static int Run(){
  foreach(var center in new[]{new Vector2(960,540),new Vector2(730,410)}){
   var orbit=new Rect{x=center.x-110,y=center.y-110,width=220,height=220};
   Assert(GetThreatScreenPoint(new(0,0,-1),orbit,0).y==center.y-110,"Rear threat below aim");
   Assert(GetThreatScreenPoint(new(0,0,1),orbit,0).y==center.y+110,"Front threat above aim");
   Assert(GetThreatScreenPoint(new(1,0,0),orbit,0).x==center.x+110,"Right threat follows moving aim");
   Assert(GetThreatScreenPoint(new(-1,0,0),orbit,0).x==center.x-110,"Left threat follows moving aim");
   for(int i=0;i<360;i+=10){
    float r=(float)(i*Math.PI/180);var p=GetThreatScreenPoint(new((float)Math.Cos(r),(float)Math.Sin(r),1),orbit,0);
    Assert(Math.Abs((p-center).sqrMagnitude-12100)<.1f,"Warning stays on compact orbit");
   }
  }
  Assert(ThreatDistanceFraction(90,90)==1,"Far distance full bar");
  Assert(ThreatDistanceFraction(45,90)==.5f,"Half distance half bar");
  Assert(ThreatDistanceFraction(0,90)==0,"At player both halves empty");
  Assert(ThreatDistanceFraction(200,90)==1,"Early ETA warning saturates outside distance range");
  Assert(ThreatDistanceFraction(-1,90)==0,"Negative distance clamps");
  Assert(!float.IsNaN(ThreatDistanceFraction(0,0)),"Invalid range does not divide by zero");
  float previous=1;for(int d=89;d>=0;d--){float f=ThreatDistanceFraction(d,90);Assert(f<=previous,"Approach only shrinks the bars");previous=f;}
  return n;
 }
}}
'@
Add-Type -TypeDefinition $code
"Threat orbit/distance checks passed: $([OrbitChecks.Check]::Run())"
if ($orbit -notmatch 'Slider.Direction.RightToLeft : Slider.Direction.LeftToRight') { throw 'Fill origins must face the center gap' }
if ($orbit -match 'FindObjects|GameObject.Find') { throw 'Orbit layout must not scan the scene' }
