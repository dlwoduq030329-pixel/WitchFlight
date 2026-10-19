# Exercise the real mesh/pulse code without opening Unity or changing the running scene.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$border = Get-Content (Join-Path $repo 'Assets/Script/PerkAvailableBorder.cs') -Raw
$source = @'
using System;
using System.Collections.Generic;
namespace UnityEngine {
 public class AddComponentMenu:Attribute {public AddComponentMenu(string s){}}
 public class RequireComponent:Attribute {public RequireComponent(Type t){}}
 public class Header:Attribute {public Header(string s){}}
 public class Tooltip:Attribute {public Tooltip(string s){}}
 public class SerializeField:Attribute {}
 public class Min:Attribute {public Min(float f){}}
 public class RangeAttribute:Attribute {public RangeAttribute(float a,float b){}}
 public class CanvasRenderer {}
 public static class Application {public static bool isPlaying=true;}
 public static class Time {public static float unscaledTime;}
 public static class Mathf {
  public const float PI=(float)Math.PI;
  public static float Max(float a,float b)=>Math.Max(a,b);
  public static float Clamp01(float v)=>Math.Clamp(v,0,1);
  public static float Cos(float v)=>(float)Math.Cos(v);
  public static float Repeat(float t,float length)=>t-(float)Math.Floor(t/length)*length;
  public static float Lerp(float a,float b,float t)=>a+(b-a)*Clamp01(t);
 }
 public struct Vector2 {public static Vector2 zero=>default;}
 public struct Vector3 {public float x,y,z;public Vector3(float x,float y,float z=0){this.x=x;this.y=y;this.z=z;}}
 public struct Color {public float r,g,b,a;public Color(float r,float g,float b,float a){this.r=r;this.g=g;this.b=b;this.a=a;}}
 public struct Rect {public float xMin,yMin,width,height;public float xMax=>xMin+width;public float yMax=>yMin+height;}
 public class RectTransform {public Rect rect=new(){width=340,height=33};}
}
namespace UnityEngine.UI {
 using UnityEngine;
 public class MaskableGraphic {
  public RectTransform rectTransform=new();public bool raycastTarget=true;public Color color=new(.25f,.86f,.84f,.9f);
  protected virtual void OnEnable(){} protected virtual void OnPopulateMesh(VertexHelper vh){}
  public void Activate()=>OnEnable();public void Bake(VertexHelper vh)=>OnPopulateMesh(vh);protected void SetVerticesDirty(){}
 }
 public class VertexHelper {
  public List<Vector3> vertices=new();public List<Color> colors=new();public List<int> indices=new();
  public int currentVertCount=>vertices.Count;
  public void Clear(){vertices.Clear();colors.Clear();indices.Clear();}
  public void AddVert(Vector3 v,Color c,Vector2 uv){vertices.Add(v);colors.Add(c);}
  public void AddTriangle(int a,int b,int c){indices.Add(a);indices.Add(b);indices.Add(c);}
 }
}
'@
$source += $border -replace '(?m)^using [^;]+;\r?\n',''
# Production code keeps its Unity namespaces; place the imports at the top for Add-Type.
$source = "using UnityEngine;`nusing UnityEngine.UI;`n" + $source
$source += @'
public static class BorderChecks {
 static int checks;static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
 static bool Near(float a,float b)=>Math.Abs(a-b)<.0001f;
 public static int Run(){
  Check(Near(PerkAvailableBorder.PulseAlpha(0,1.2f,.25f),1),"starts bright");
  Check(Near(PerkAvailableBorder.PulseAlpha(.6f,1.2f,.25f),.25f),"midpoint dims");
  Check(Near(PerkAvailableBorder.PulseAlpha(1.2f,1.2f,.25f),1),"pulse repeats instead of holding a final image");
  Check(Near(PerkAvailableBorder.PulseAlpha(-1,0,2),1),"pulse clamps invalid period and alpha bounds");
  var border=new PerkAvailableBorder();Time.unscaledTime=10;border.Activate();
  Check(!border.raycastTarget,"notification does not intercept input");
  var vh=new VertexHelper();
  foreach(var size in new[]{new Vector3(340,33),new Vector3(339,33),new Vector3(1024,100),new Vector3(1,1)}){
   border.rectTransform.rect=new Rect{xMin=-size.x/2,yMin=-size.y/2,width=size.x,height=size.y};
   foreach(float elapsed in new[]{0f,.3f,.6f,1.2f,50f}){
    Time.unscaledTime=10+elapsed;border.Bake(vh);var r=border.rectTransform.rect;
    Check(vh.vertices.Count==16&&vh.indices.Count==24,"exactly four border strips");
    for(int q=0;q<4;q++){
     var a=vh.vertices[q*4];var b=vh.vertices[q*4+2];
     Check(b.x>a.x&&b.y>a.y,"non-degenerate strip");
     Check(b.x<=r.xMin||a.x>=r.xMax||b.y<=r.yMin||a.y>=r.yMax,"entire strip outside HP interior at every pulse phase");
    }
    foreach(var c in vh.colors)Check(c.a>=.225f-.0001f&&c.a<=.9001f,"only border alpha pulses");
   }
  }
  Time.unscaledTime=100;border.Activate();border.Bake(vh);Check(Near(vh.colors[0].a,.9f),"re-enable restarts pulse");
  border.rectTransform.rect=new Rect();border.Bake(vh);Check(vh.vertices.Count==0,"zero size generates no geometry");
  return checks;
 }
}
'@
Add-Type -TypeDefinition $source
"Perk border geometry/pulse checks passed: $([BorderChecks]::Run())"
$scene = Get-Content (Join-Path $repo 'Assets/Scenes/Battle.unity') -Raw
$view = [regex]::Match($scene,'(?ms)^--- !u!114 &1895097777\r?\n.*?(?=^--- !u!|\z)').Value
$animator = [regex]::Match($scene,'(?ms)^--- !u!95 &1895097776\r?\n.*?(?=^--- !u!|\z)').Value
if ($view -notmatch 'guid: 472d336b0c454faeb660b8df54c4372b' -or $view -match 'm_Sprite:' -or
    $view -notmatch 'm_RaycastTarget: 0' -or $animator -notmatch 'm_Enabled: 0') {
    throw 'Battle notification must use the empty-center graphic with the old filled animation disabled'
}
if ($scene -notmatch 'benefitAnimation: \{fileID: 1895097774\}') {throw 'Existing notification lifecycle binding was lost'}
'Battle scene border binding, disabled filled animation and unchanged notification root passed.'
