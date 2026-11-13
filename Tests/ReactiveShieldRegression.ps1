# Executes the actual procedural mesh builder with native-free mesh/vector doubles.
# GPU shader validation is available in Unity: Tools/WitchFlight/Validate Reactive Shield.
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$source=Get-Content Assets/Script/ReactiveShieldVfx.cs -Raw
$method=[regex]::Match($source,'(?ms)^    public static Mesh BuildMesh\(.*?^    \}').Value
if(!$method){throw 'Production mesh builder missing'}
$stubs=@'
using System;
using System.Collections.Generic;
namespace ShieldChecks {
public enum IndexFormat {UInt16,UInt32}
public static class Mathf {
 public static float Sqrt(float x)=>MathF.Sqrt(x);public static float Abs(float x)=>Math.Abs(x);
 public static float Atan2(float a,float b)=>MathF.Atan2(a,b);
 public static int Min(int a,int b)=>Math.Min(a,b);public static int Max(int a,int b)=>Math.Max(a,b);
 public static int Clamp(int x,int a,int b)=>Math.Clamp(x,a,b);public static float Clamp(float x,float a,float b)=>Math.Clamp(x,a,b);
}
public struct Vector3 {
 public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}
 public float sqrMagnitude=>x*x+y*y+z*z;
 public Vector3 normalized=>sqrMagnitude<1e-15f?default:this/MathF.Sqrt(sqrMagnitude);
 public static Vector3 up=>new(0,1,0);public static Vector3 right=>new(1,0,0);public static Vector3 forward=>new(0,0,1);
 public static Vector3 operator+(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
 public static Vector3 operator-(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
 public static Vector3 operator-(Vector3 a)=>a*-1;
 public static Vector3 operator*(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);
 public static Vector3 operator/(Vector3 a,float b)=>a*(1/b);
 public static Vector3 Lerp(Vector3 a,Vector3 b,float t)=>a+(b-a)*t;
 public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
 public static Vector3 Cross(Vector3 a,Vector3 b)=>new(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
}
public struct Vector4 {public float x,y,z,w;public Vector4(float a,float b,float c,float d){x=a;y=b;z=c;w=d;}}
public class Mesh {
 public string name;public IndexFormat indexFormat;
 public List<Vector3> vertices,normals;public List<int> triangles;
 public Dictionary<int,int> uvCounts=new();
 public void SetVertices(List<Vector3> v){vertices=v;}public void SetNormals(List<Vector3> v){normals=v;}
 public void SetUVs<T>(int channel,List<T> v){uvCounts[channel]=v.Count;}
 public void SetTriangles(List<int> t,int submesh){triangles=t;}public void RecalculateBounds(){}
}
public static class Test {
'@
$checks=@'
 public static string Run(){
  int triangles=0;
  for(int level=0;level<=3;level++) foreach(float gap in new[]{0f,.045f,.3f}) foreach(float depth in new[]{0f,.015f,.1f}){
   var mesh=BuildMesh(level,gap,depth);int cells=10*(1<<(2*level))+2;
   int expected=(cells*4-12)*(depth>0?24:3);
   if(mesh.vertices.Count!=expected)throw new Exception("Cell or fragment count incorrect");
   if(mesh.normals.Count!=expected||mesh.triangles.Count!=expected)throw new Exception("Incomplete vertex streams");
   for(int uv=0;uv<4;uv++)if(mesh.uvCounts[uv]!=expected)throw new Exception("Missing shader metadata");
   for(int i=0;i<mesh.triangles.Count;i+=3){
    int a=mesh.triangles[i],b=mesh.triangles[i+1],c=mesh.triangles[i+2];
    var cross=Vector3.Cross(mesh.vertices[b]-mesh.vertices[a],mesh.vertices[c]-mesh.vertices[a]);
    if(!float.IsFinite(cross.sqrMagnitude)||cross.sqrMagnitude<1e-12f)throw new Exception("Degenerate triangle at level "+level);
    if(Vector3.Dot(cross,mesh.normals[a])<=0)throw new Exception("Inverted winding at level "+level);
    triangles++;
   }
  }
  return "PASS: 36 geometry configurations, "+triangles+" triangles; topology, winding and shader vertex streams.";
 }
}}
'@
Add-Type -TypeDefinition ($stubs+$method+$checks)
[ShieldChecks.Test]::Run()
if($source -match 'void Update\('){throw 'Reusable shield must not run a frame polling loop'}
if($source -match 'new Material\('){throw 'Shield must share its authored material'}
foreach($path in @('Assets/Scenes/ReactiveShieldPreview.unity','Assets/VFX/ReactiveShield/ReactiveShield.prefab')){
 $yaml=Get-Content $path -Raw
 $ids=@{};foreach($m in [regex]::Matches($yaml,'(?m)^--- !u!\d+ &(\d+)')){
  if($ids.ContainsKey($m.Groups[1].Value)){throw "Duplicate object ID: $path"};$ids[$m.Groups[1].Value]=$true
 }
 foreach($m in [regex]::Matches($yaml,'\{fileID: (\d+)\}')){
  if($m.Groups[1].Value -ne '0' -and !$ids.ContainsKey($m.Groups[1].Value)){throw "Dangling local reference: $path"}
 }
}
'PASS: independent prefab/scene references and event-driven runtime contract.'
