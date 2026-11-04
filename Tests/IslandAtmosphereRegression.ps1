$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$code=Get-Content Assets/Script/IslandAtmosphere.cs -Raw
$methods=foreach($name in @('CloudPosition','BirdPosition','Variation')){
 $m=[regex]::Match($code,'(?ms)^    (?:public|private) static [^\r\n]+ '+$name+'\([^\r\n]*\)\r?\n    \{.*?^    \}')
 if(!$m.Success){throw "Missing method $name"};$m.Value
}
$source=@'
using System;
public struct Vector2 {public float x,y;public Vector2(float x,float y){this.x=x;this.y=y;}}
public struct Vector3 {
 public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
 public static float Distance(Vector3 a,Vector3 b)=>(float)Math.Sqrt((a.x-b.x)*(a.x-b.x)+(a.y-b.y)*(a.y-b.y)+(a.z-b.z)*(a.z-b.z));
}
public static class Mathf {
 public const float PI=(float)Math.PI;
 public static int Clamp(int v,int a,int b)=>Math.Clamp(v,a,b);
 public static int Max(int a,int b)=>Math.Max(a,b);public static float Max(float a,float b)=>Math.Max(a,b);
 public static float Lerp(float a,float b,float t)=>a+(b-a)*Math.Clamp(t,0,1);
 public static float Sin(float v)=>(float)Math.Sin(v);public static float Cos(float v)=>(float)Math.Cos(v);
}
public static class Layout {
'@
$source+=($methods -join "`n")+"}"
$source+=@'
public static class Checks {
 static int count;static void Check(bool v,string m){if(!v)throw new Exception(m);count++;}
 public static int Run(){
  var radii=new Vector2(440,440);var height=new Vector2(-30,55);
  for(int cloud=0;cloud<36;cloud++) {
   var p=Layout.CloudPosition(cloud,36,45,radii,height,8,173);
   Check(Math.Sqrt(p.x*p.x+p.z*p.z)>430,"Cloud banks leave the central battle space clear");
   Check(p.y>=-35&&p.y<=60,"Cloud height remains within its bank");
   var next=Layout.CloudPosition(cloud,36,45.016f,radii,height,8,173);
   Check(Vector3.Distance(p,next)<.01,"Slow cloud drift, no emission jumps");
  }
  for(int bird=0;bird<18;bird++) {
   var h=new Vector2(65,100);var period=new Vector2(65,95);
   var p=Layout.BirdPosition(bird,18,3,300,235,h,period,3,173);
   Check(Math.Abs(p.x)<280&&Math.Abs(p.z)<280&&p.y>55&&p.y<110,"Bird stays in the intended flight band");
   var next=Layout.BirdPosition(bird,18,3,300.016f,235,h,period,3,173);
   Check(Vector3.Distance(p,next)>.05&&Vector3.Distance(p,next)<.6,"Continuous gliding motion at bounded speed");
   var repeat=Layout.BirdPosition(bird,18,3,300,235,h,period,3,173);
   Check(Vector3.Distance(p,repeat)==0,"Deterministic layout avoids random frame jitter");
  }
  var one=Layout.BirdPosition(0,1,3,1,235,new Vector2(65,100),new Vector2(65,95),3,173);
  Check(!float.IsNaN(one.x),"One-bird configuration remains valid");
  return count;
 }
}
'@
Add-Type -TypeDefinition $source
"Island atmosphere layout/motion checks passed: $([Checks]::Run())"
$guid=[regex]::Match((Get-Content Assets/Script/IslandAtmosphere.cs.meta -Raw),'guid: (\w+)').Groups[1].Value
foreach($name in @('Clouds','Birds')){
 $prefab=Get-Content "Assets/temp/$name.prefab" -Raw
 if(([regex]::Matches($prefab,"m_Script: \{fileID: 11500000, guid: $guid,")).Count -ne 1){throw "Missing controller: $name"}
 if($prefab -notmatch 'birdCount: 18' -or $prefab -notmatch 'cloudCount: 36'){throw "Unexpected visual budget: $name"}
}
$scene=Get-Content Assets/Scenes/Battle.unity -Raw
foreach($id in @('215395746484466643','8347069038054795992')){
 $p='(?m)    - target: \{fileID: '+$id+', guid: [a-f0-9]+, type: 3\}\r?\n      propertyPath: m_LocalPosition\.[xyz]\r?\n      value: 0\r?$'
 if([regex]::Matches($scene,$p).Count -ne 3){throw "Scene root is not centered: $id"}
}
'Cloud/Bird prefab controllers and Battle root positions verified.'
