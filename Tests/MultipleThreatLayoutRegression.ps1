# Production overlap placement, independent of Unity rendering.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$text=Get-Content Assets/Script/BattleHud.ThreatWarnings.cs -Raw
$method=[regex]::Match($text,'(?ms)^    private static Vector2 SeparateThreatPoint\([^{}]*?\)\r?\n    \{.*?^    \}').Value.Replace('private static','public static')
if(!$method){throw 'Missing placement method'}
$source=@'
using System;
using System.Collections.Generic;
namespace MultipleThreatChecks {
public struct Vector2 {
 public float x,y;public Vector2(float x,float y){this.x=x;this.y=y;}public float sqrMagnitude=>x*x+y*y;
 public static Vector2 operator+(Vector2 a,Vector2 b)=>new(a.x+b.x,a.y+b.y);
 public static Vector2 operator-(Vector2 a,Vector2 b)=>new(a.x-b.x,a.y-b.y);
}
public struct Rect {public float x,y,width,height;public Vector2 center=>new(x+width/2,y+height/2);}
public static class Mathf {public static float Max(float a,float b)=>Math.Max(a,b);public static float Min(float a,float b)=>Math.Min(a,b);public static float Cos(float a)=>(float)Math.Cos(a);public static float Sin(float a)=>(float)Math.Sin(a);public static float Atan2(float a,float b)=>(float)Math.Atan2(a,b);}
public static class Checks {
'@
$source+=$method
$source+=@'
 public static int Run(){
  int checks=0;
  foreach(var screen in new[]{new Rect{width=1920,height=1080},new Rect{x=200,y=100,width=960,height=540}}){
   var points=new List<Vector2>();var desired=new Vector2(screen.center.x,screen.y+60);
   for(int i=0;i<8;i++){
    var p=SeparateThreatPoint(desired,screen,60,55,points);
    if(p.x<screen.x+59||p.x>screen.x+screen.width-59||p.y<screen.y+59||p.y>screen.y+screen.height-59)throw new Exception("warning outside safe viewport");checks++;
    foreach(var used in points){if((p-used).sqrMagnitude<55*55-.01f)throw new Exception("coincident incoming directions overlap");checks++;}
    var again=SeparateThreatPoint(desired,screen,60,55,points);
    if((again-p).sqrMagnitude>.001f)throw new Exception("layout is not deterministic");checks++;
    points.Add(p);
   }
  }
  return checks;
 }
}
}
'@
Add-Type -TypeDefinition $source
"Multiple threat layout checks passed: $([MultipleThreatChecks.Checks]::Run())"
