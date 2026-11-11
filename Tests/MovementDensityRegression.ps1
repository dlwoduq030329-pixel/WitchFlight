# Runs the production density calculation and XLSX reader without Unity or Excel.
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$stubs=@'
using System;
namespace UnityEngine {
 public struct Vector3 {
  public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
  public static Vector3 operator-(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
  public static Vector3 Lerp(Vector3 a,Vector3 b,float t)=>new(a.x+(b.x-a.x)*t,a.y+(b.y-a.y)*t,a.z+(b.z-a.z)*t);
 }
 public struct Vector3Int:IEquatable<Vector3Int> {
  public int x,y,z;public Vector3Int(int x,int y,int z){this.x=x;this.y=y;this.z=z;}
  public bool Equals(Vector3Int b)=>x==b.x&&y==b.y&&z==b.z;
  public override bool Equals(object b)=>b is Vector3Int v&&Equals(v);
  public override int GetHashCode()=>HashCode.Combine(x,y,z);
  public static Vector3Int Min(Vector3Int a,Vector3Int b)=>new(Math.Min(a.x,b.x),Math.Min(a.y,b.y),Math.Min(a.z,b.z));
  public static Vector3Int Max(Vector3Int a,Vector3Int b)=>new(Math.Max(a.x,b.x),Math.Max(a.y,b.y),Math.Max(a.z,b.z));
 }
 public static class Mathf {
  public static float Clamp(float x,float a,float b)=>Math.Clamp(x,a,b);
  public static int Clamp(int x,int a,int b)=>Math.Clamp(x,a,b);
  public static float Min(float a,float b)=>Math.Min(a,b);
  public static float Max(float a,float b)=>Math.Max(a,b);
  public static float Max(float a,float b,float c)=>Math.Max(a,Math.Max(b,c));
  public static float Abs(float x)=>Math.Abs(x);
  public static int FloorToInt(float x)=>(int)Math.Floor(x);
  public static int CeilToInt(float x)=>(int)Math.Ceiling(x);
 }
}
'@
$model=(Get-Content Assets/Script/BattleMovementLog.cs -Raw).Split('public static class BattleMovementLogFiles')[0]
$code=$stubs+"`nnamespace DensityChecks {`n"+$model+"`n}`n"
foreach($file in 'BattleMovementDensity','BattleMovementSpreadsheet') {
 $code+="`nnamespace DensityChecks {`n"+(Get-Content "Assets/Script/$file.cs" -Raw)+"`n}`n"
}
$code+=@'
namespace DensityChecks {
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using UnityEngine;
public static class Tests {
 static int checks;
 static void Check(bool value,string label){if(!value)throw new Exception(label);checks++;}
 static bool Near(double a,double b)=>Math.Abs(a-b)<.00001;
 static BattleMovementPoint P(float time,float x,float y,int segment=0,bool alive=true,int round=1)
  =>new(){time=time,position=new Vector3(x,y,0),segment=segment,alive=alive,round=round};
 static BattleMovementLog Log(string id,params BattleMovementPoint[] points)=>new(){matchId=id,scenePath="map",duration=points.Last().time,sampleInterval=.25f,
  players=new(){new(){participant=1,points=points.ToList()}}};
 static BattleMovementDensity.Result Build(BattleMovementLog[] logs,bool human=true,bool bot=true,int round=0,CancellationToken token=default)
  =>BattleMovementDensity.Build(logs,20,10,human,bot,round,"map",token);
 public static int Run(){
  var low=Log("one",P(0,0,5),P(1,0,5),P(2,0,5));
  var high=Log("two",P(0,0,25),P(1,0,25),P(2,0,25));
  var result=Build(new[]{low,high,low});
  Check(result.seconds.Count==2&&result.altitudeSeconds.Count==2,"same X/Z at different altitudes stays separate");
  Check(result.matches==2&&result.players==2&&result.duplicates==2,"duplicate exports do not add weight");
  Check(Near(result.totalSeconds,4)&&Near(result.altitudeSeconds[0],2)&&Near(result.altitudeSeconds[2],2),"dwell seconds and height shares");
  var fast=Log("fast",Enumerable.Range(0,9).Select(i=>P(i*.25f,0,5)).ToArray());
  Check(Near(Build(new[]{fast}).totalSeconds,Build(new[]{low}).totalSeconds),"sampling frequency does not skew dwell density");
  var crossing=Build(new[]{Log("cross",P(0,0,5),P(1,100,5))});
  Check(crossing.seconds.Count==5&&Near(crossing.totalSeconds,1),"fast continuous flight distributes time over crossed cells");
  var portal=Build(new[]{Log("portal",P(0,0,5),P(1,100,5,1))});
  Check(portal.seconds.Count==1&&Near(portal.totalSeconds,.25),"portal does not heat the route between endpoints");
  var gap=Build(new[]{Log("gap",P(0,0,5),P(10,100,5))});
  Check(gap.seconds.Count==1&&Near(gap.totalSeconds,.25),"missing ten seconds are not counted as camping or traversal");
  var dead=Build(new[]{Log("dead",P(0,0,5,alive:false),P(1,100,5,alive:false))});
  Check(dead.samples==0&&dead.seconds.Count==0,"dead characters excluded");
  high.players[0].bot=true;
  Check(Near(Build(new[]{low,high},true,false).totalSeconds,2)&&Near(Build(new[]{low,high},false,true).totalSeconds,2),"human and bot filters");
  Check(Build(new[]{low},round:2).seconds.Count==0,"round filter");
  high.scenePath="other map";
  Check(Build(new[]{low,high}).skippedMaps==1,"different maps are not superimposed");
  var below=Build(new[]{Log("negative",P(0,-1,-1),P(1,-1,-1))});
  Check(below.seconds.ContainsKey(new Vector3Int(-1,-1,0)),"negative world coordinates use floor bins");
  using(var cancel=new CancellationTokenSource()){
   cancel.Cancel();bool canceled=false;try{Build(new[]{low},token:cancel.Token);}catch(OperationCanceledException){canceled=true;}
   Check(canceled,"cancellation stops aggregation");
  }
  string path=Path.Combine(Path.GetTempPath(),"movement-density-"+Guid.NewGuid().ToString("N")+".xlsx");
  try {
   using(var file=File.Create(path))using(var zip=new ZipArchive(file,ZipArchiveMode.Create)){
    void Entry(string name,string text){using(var writer=new StreamWriter(zip.CreateEntry(name).Open()))writer.Write(text);}
    Entry("xl/sharedStrings.xml","<sst xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><si><t>H4sIFirstPayload</t></si></sst>");
    Entry("xl/worksheets/sheet1.xml","<worksheet xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><sheetData><row><c t='s'><v>0</v></c></row></sheetData></worksheet>");
    Entry("xl/worksheets/sheet2.xml","<worksheet xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><sheetData><row><c t='inlineStr'><is><r><t>H4sI</t></r><r><t>SecondPayload</t></r></is></c><c t='str'><v>ThirdPayload</v></c></row></sheetData></worksheet>");
   }
   var cells=BattleMovementSpreadsheet.Cells(path).ToArray();
   Check(cells.Length==3&&cells[0]=="H4sIFirstPayload"&&cells[1]=="H4sISecondPayload"&&cells[2]=="ThirdPayload","all XLSX sheets, shared strings, rich inline strings and cached values");
  }finally{if(File.Exists(path))File.Delete(path);}
  return checks;
 }
}
}
'@
Add-Type -TypeDefinition $code
Write-Output ("Movement density / XLSX checks passed: {0}" -f [DensityChecks.Tests]::Run())
