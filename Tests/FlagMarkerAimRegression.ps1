# Offline checks execute the production screen-space avoidance calculation.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$hud = Get-Content Assets/Script/BattleHud.cs -Raw
function Method([string]$name) {
    $match = [regex]::Match($hud, '(?ms)^    private (?:static )?[\w<>]+ '+$name+'\([^{}]*?\)\r?\n    \{.*?^    \}')
    if (!$match.Success) { throw "Missing method $name" }
    $match.Value -replace '^    private ', '    public '
}
$source = @'
using System;
namespace FlagMarkerChecks {
public struct Vector2 {
 public float x,y;public Vector2(float x,float y){this.x=x;this.y=y;}
 public static Vector2 zero=>default;public float sqrMagnitude=>x*x+y*y;
 public static Vector2 operator-(Vector2 a,Vector2 b)=>new(a.x-b.x,a.y-b.y);
 public static Vector2 operator+(Vector2 a,Vector2 b)=>new(a.x+b.x,a.y+b.y);
}
public struct Rect {
 public float x,y,width,height;
 public Rect(float x,float y,float w,float h){this.x=x;this.y=y;width=w;height=h;}
 public float xMin=>x;public float yMin=>y;public float xMax=>x+width;public float yMax=>y+height;
 public Vector2 position{get=>new(x,y);set{x=value.x;y=value.y;}}
 public bool Overlaps(Rect b)=>xMax>b.xMin&&xMin<b.xMax&&yMax>b.yMin&&yMin<b.yMax;
 public static Rect MinMaxRect(float x,float y,float maxX,float maxY)=>new(x,y,maxX-x,maxY-y);
}
public static class Mathf {
 public static float Clamp(float v,float min,float max)=>Math.Clamp(v,min,max);
 public static float Min(float a,float b)=>Math.Min(a,b);public static float Max(float a,float b)=>Math.Max(a,b);
}
public static class Layout {
'@
$source += (Method 'UnionScreenRects') + "`n" + (Method 'TryOffsetFlagMarker')
$source += @'
}
public static class Tests {
 static int count;
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;}
 static void Clear(Rect marker,Rect aim,Rect screen,string name){
  Check(Layout.TryOffsetFlagMarker(marker,aim,screen,out var offset),name+": has room");
  marker.position+=offset;
  Check(!marker.Overlaps(aim),name+": aim not covered");
  Check(marker.xMin>=screen.xMin&&marker.xMax<=screen.xMax&&marker.yMin>=screen.yMin&&marker.yMax<=screen.yMax,name+": stays on screen");
 }
 public static int Run(){
  Rect screen=new(0,0,1920,1080),dot=new(930,510,60,60);
  Rect marker=new(400,300,260,100);
  Check(Layout.TryOffsetFlagMarker(marker,dot,screen,out var offset)&&offset.sqrMagnitude==0,"unobstructed marker remains exactly at target");
  Clear(new(830,490,260,100),dot,screen,"centered flag icon and label");
  Clear(new(830,560,260,100),dot,screen,"label-only overlap");
  Clear(new(-80,20,260,100),dot,screen,"partially offscreen flag");
  Rect crosshair=new(700,430,100,100);
  Rect both=Layout.UnionScreenRects(dot,crosshair);
  Check(both.xMin==700&&both.xMax==990&&both.yMin==430&&both.yMax==570,"protects both Dot and moving crosshair");
  Clear(new(750,420,260,100),both,screen,"separated aiming markers");
  Clear(new(950,640,180,70),new(1010,680,80,80),new(500,300,700,500),"offset camera viewport");
  Clear(new(850,0,260,100),new(930,0,60,60),screen,"bottom edge chooses another side");
  Clear(new(850,980,260,100),new(930,1020,60,60),screen,"top edge chooses another side");
  Check(!Layout.TryOffsetFlagMarker(new(0,0,260,100),dot,new(0,0,150,90),out _),"oversized marker hides instead of covering aim");
  Check(!Layout.TryOffsetFlagMarker(new(900,500,260,100),screen,screen,out _),"no free region hides instead of covering aim");
  Clear(new(840,460,240,120),new(900,480,120,120),screen,"custom scaled marker rect");
  for(int x=-200;x<=2000;x+=100)for(int y=-100;y<=1200;y+=100)
   Clear(new(x,y,260,100),both,screen,"position sweep");
  return count;
 }
}}
'@
Add-Type -TypeDefinition $source
"Flag/Dot/crosshair layout checks passed: $([FlagMarkerChecks.Tests]::Run())"

$bind = Method 'BindFlightAim'
if ($bind.IndexOf('UpdateFlagCarrierMarker();') -lt $bind.IndexOf('PlaceStableAim(')) {throw 'Flag must avoid this frame, not the previous frame aiming rectangles'}
$update = Method 'UpdateFlagCarrierMarker'
foreach ($token in 'GetScreenRect(flagCarrierDistanceText.rectTransform)', 'GetScreenRect(flagCarrierMarker)',
    'GetFlagMarkerClearArea()', 'TryOffsetFlagMarker(', 'point + offset', 'textPoint + offset') {
    if (!$update.Contains($token)) {throw "Missing layout contract: $token"}
}
$area = Method 'GetFlagMarkerClearArea'
foreach ($token in 'desiredAimMarker', 'forwardAimMarker', 'reticle.transform', 'flagMarkerAimPadding') {
    if (!$area.Contains($token)) {throw "Missing protected aiming UI: $token"}
}
$bounds = Method 'GetScreenRect'
if (!$bounds.Contains('rootCanvas') -or !$bounds.Contains('GetWorldCorners')) {throw 'Use actual scaled screen bounds and correct canvas camera'}
'Current-frame placement, icon+label footprint and authored Canvas contracts passed.'
