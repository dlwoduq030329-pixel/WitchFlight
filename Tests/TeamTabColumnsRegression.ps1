# Execute actual team layout and roster mapping; UI rendering still needs Unity.
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
function Method($path,$name){
 $m=[regex]::Match((Get-Content $path -Raw),'(?ms)^    (?:public|private) (?:static )?[\w<>]+ '+$name+'\([^{}]*?\)\r?\n    \{.*?^    \}')
 if(!$m.Success){throw "Missing $name"};$m.Value
}
$source=@'
using System;using System.Collections.Generic;
public struct Vector2 {public float x,y;public Vector2(float x,float y){this.x=x;this.y=y;}}
public struct Vector3 {public float x,y,z;public static Vector3 one=>new(){x=1,y=1,z=1};public static Vector3 operator*(Vector3 v,float s)=>new(){x=v.x*s,y=v.y*s,z=v.z*s};}
public class Mathf {public static float Max(float a,float b)=>Math.Max(a,b);public static float Min(float a,float b)=>Math.Min(a,b);}
public class LayoutElement {public bool ignoreLayout;}
public class GameObject {public T AddComponent<T>() where T:new()=>new T();}
public class Rect {public float width,height;public Vector2 size=>new(width,height);}
public class RectTransform {
 public Rect rect=new();public GameObject gameObject=new();public Vector2 anchorMin,anchorMax,pivot,sizeDelta,anchoredPosition;
 public Vector3 localScale;public T GetComponent<T>() where T:class=>null;
}
public class PlayerData {public int teamIndex,id;}
public class View {
 public PlayerData local;public object[] teamViews=new object[10];public List<PlayerData> comparisonRoster=new();
 private bool Ready(PlayerData d)=>d!=null;
 __ROSTER__
 public PlayerData Get(int i)=>TeamViewData(i);
}
public static class Checks {
 __LAYOUT__
 static int n;static void Check(bool ok,string message){n++;if(!ok)throw new Exception(message);}
 public static string Run(){
  foreach(var size in new[]{new Vector2(1920,1080),new Vector2(1280,720),new Vector2(2560,1080)}){
   var area=new RectTransform{rect=new(){width=size.x,height=size.y}};var rows=new List<RectTransform>();
   for(int i=0;i<10;i++)rows.Add(new(){rect=new(){width=1114,height=197}});
   FitTeamColumns(rows,area);
   for(int i=0;i<10;i++){
    var r=rows[i];float x=r.anchoredPosition.x,w=r.sizeDelta.x*r.localScale.x,h=r.sizeDelta.y*r.localScale.y,y=-r.anchoredPosition.y;
    Check(i<5?x<0:x>0,"Teams split left and right");
    Check(Math.Abs(x)+w/2<=size.x/2&&y+h<=size.y,"Rows fit viewport");
    if(i%5!=4)Check(y+h< -rows[i+1].anchoredPosition.y,"Rows do not overlap vertically");
    if(i<5)Check(x+w/2<rows[i+5].anchoredPosition.x-w/2,"Columns do not overlap");
   }
  }
  foreach(int teamSize in new[]{4,5})foreach(int localTeam in new[]{0,1}){
   var view=new View{local=new(){teamIndex=localTeam,id=99}};view.comparisonRoster.Add(view.local);
   for(int i=0;i<teamSize;i++)view.comparisonRoster.Add(new(){teamIndex=1-localTeam,id=i});
   for(int i=1;i<teamSize;i++)view.comparisonRoster.Add(new(){teamIndex=localTeam,id=10+i});
   Check(view.Get(0)==view.local,"Local player remains first on left");
   for(int i=0;i<5;i++){
    Check(i<teamSize?view.Get(i)?.teamIndex==localTeam:view.Get(i)==null,"Left roster / empty fifth slot");
    Check(i<teamSize?view.Get(i+5)?.teamIndex==1-localTeam:view.Get(i+5)==null,"Right roster / empty fifth slot");
   }
  }
  return n+" team-column geometry and roster checks passed";
 }
}
'@
$source=$source.Replace('__LAYOUT__',(Method Assets/Script/ParticipantUiSlots.cs FitTeamColumns)).Replace('__ROSTER__',(Method Assets/Script/BattlePerkUI.cs TeamViewData))
Add-Type -TypeDefinition $source
[Checks]::Run()
