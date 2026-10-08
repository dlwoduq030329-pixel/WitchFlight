# Offline geometry and scene wiring checks; Unity Play Mode still needs visual verification.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$source = Get-Content Assets/Script/Camera/SpeedWindLines.cs -Raw
$stubs = @'
using System;
using System.Collections.Generic;
namespace UnityEngine {
    public class AddComponentMenu : Attribute { public AddComponentMenu(string name) {} }
    public class RequireComponent : Attribute { public RequireComponent(Type type) {} }
    public class CanvasRenderer {}
    public static class Time { public static float unscaledDeltaTime = 1f/60f; }
    public struct Vector2 {
        public float x,y;
        public Vector2(float x,float y) {this.x=x;this.y=y;}
        public static Vector2 zero => new Vector2(0,0);
        public Vector2 normalized { get {float length=(float)Math.Sqrt(x*x+y*y);return new Vector2(x/length,y/length);} }
        public static Vector2 operator +(Vector2 a,Vector2 b)=>new Vector2(a.x+b.x,a.y+b.y);
        public static Vector2 operator -(Vector2 a,Vector2 b)=>new Vector2(a.x-b.x,a.y-b.y);
        public static Vector2 operator *(Vector2 a,float b)=>new Vector2(a.x*b,a.y*b);
        public static Vector2 Scale(Vector2 a,Vector2 b)=>new Vector2(a.x*b.x,a.y*b.y);
    }
    public struct Color {public float r,g,b,a;public Color(float r,float g,float b,float a){this.r=r;this.g=g;this.b=b;this.a=a;}}
    public struct Rect {public float width,height;public Vector2 size=>new Vector2(width,height);public Vector2 center=>Vector2.zero;}
    public class RectTransform {public Rect rect=new Rect{width=1920,height=1080};}
    public static class Mathf {
        public const float PI=(float)Math.PI;
        public static float Clamp01(float x)=>Math.Clamp(x,0,1);
        public static int Clamp(int x,int min,int max)=>Math.Clamp(x,min,max);
        public static float Max(float a,float b)=>Math.Max(a,b);
        public static float Min(float a,float b)=>Math.Min(a,b);
        public static float Abs(float x)=>Math.Abs(x);
        public static float Sin(float x)=>(float)Math.Sin(x);
        public static float Cos(float x)=>(float)Math.Cos(x);
        public static int FloorToInt(float x)=>(int)Math.Floor(x);
        public static float Repeat(float x,float length)=>x-(float)Math.Floor(x/length)*length;
        public static float Lerp(float a,float b,float t)=>a+(b-a)*Clamp01(t);
    }
}
namespace UnityEngine.UI {
    using UnityEngine;
    public class MaskableGraphic {
        public bool enabled=true,raycastTarget=true;
        public Color color;
        public RectTransform rectTransform=new RectTransform();
        protected virtual void Awake() {}
        protected virtual void OnPopulateMesh(VertexHelper helper) {}
        protected void SetVerticesDirty() {}
        public void Initialize()=>Awake();
        public void Populate(VertexHelper helper)=>OnPopulateMesh(helper);
    }
    public class VertexHelper {
        public List<Vector2> Positions=new List<Vector2>();
        public List<Color> Colors=new List<Color>();
        public List<int> Indices=new List<int>();
        public int currentVertCount=>Positions.Count;
        public void Clear(){Positions.Clear();Colors.Clear();Indices.Clear();}
        public void AddVert(Vector2 p,Color c,Vector2 uv){Positions.Add(p);Colors.Add(c);}
        public void AddTriangle(int a,int b,int c){Indices.Add(a);Indices.Add(b);Indices.Add(c);}
    }
}
public static class WindChecks {
    private static int count;
    private static void Check(bool valid,string label){if(!valid)throw new Exception(label);count++;}
    public static int Run() {
        var wind=new SpeedWindLines();var mesh=new UnityEngine.UI.VertexHelper();
        var tint=new UnityEngine.Color(.8f,.94f,1,1);
        wind.Initialize();Check(!wind.raycastTarget,"Wind must not intercept gameplay/UI input");
        wind.Populate(mesh);Check(mesh.currentVertCount==0,"No wind before stage presentation");
        wind.SetPresentation(.3f,tint,36,1.6f);wind.Populate(mesh);
        Check(wind.enabled,"Wind enables without a sprite/material assignment");
        Check(mesh.currentVertCount==144&&mesh.Indices.Count==216,"36 lightweight streak quads");
        Check(mesh.Indices.TrueForAll(i=>i>=0&&i<144),"Triangle indices valid");
        Check(mesh.Colors.Exists(c=>c.a>.1f),"Nonzero visible opacity");
        Check(mesh.Colors.TrueForAll(c=>c.a>=0&&c.a<=.3f),"Opacity never exceeds configured strength");
        Check(mesh.Positions.TrueForAll(p=>p.x*p.x/(960*960)+p.y*p.y/(540*540)>.56f),"Central aiming area stays clear");
        for(int i=0;i<mesh.currentVertCount;i+=4) {
            Check(mesh.Colors[i].a==0&&mesh.Colors[i+1].a==0,"Streak tail fades out");
            var side=mesh.Positions[i+2]-mesh.Positions[i+3];
            double width=Math.Sqrt(side.x*side.x+side.y*side.y);
            Check(width>=.999&&width<=6.001,"Streak width respects Inspector limits");
        }
        var previous=mesh.Positions[2];wind.SetPresentation(.3f,tint,36,1.6f);wind.Populate(mesh);
        Check(Math.Abs(previous.x-mesh.Positions[2].x)+Math.Abs(previous.y-mesh.Positions[2].y)>.01,"Wind animates each frame");
        wind.SetPresentation(0,tint,36,1.6f);wind.Populate(mesh);
        Check(!wind.enabled&&mesh.currentVertCount==0,"Leaving stage/menu/death clears wind");
        wind.SetPresentation(.3f,tint,36,1.6f);wind.Populate(mesh);
        Check(wind.enabled&&mesh.currentVertCount==144,"Wind can re-enable after hiding");
        return count;
    }
}
'@
$usings = [regex]::Matches($source, '(?m)^using .*?;') | ForEach-Object { $_.Value }
$body = [regex]::Replace($source, '(?m)^using .*?;\r?\n', '')
Add-Type -TypeDefinition (($usings -join "`n") + "`n" + $stubs + "`n" + $body)
"Wind geometry checks passed: $([WindChecks]::Run())"

$scene = Get-Content Assets/Scenes/Battle.unity -Raw
$blocks = @{}
foreach ($match in [regex]::Matches($scene, '(?ms)^--- !u!\d+ &(\d+)[^\r\n]*\r?\n.*?(?=^--- !u!|\z)')) {
    $id = $match.Groups[1].Value
    if ($blocks.ContainsKey($id)) { throw "Duplicate scene object ID: $id" }
    $blocks[$id] = $match.Value
}
if ($scene -notmatch 'windLines: \{fileID: 810900103\}' -or $scene -notmatch 'showWindLines: 1') { throw 'Camera must enable and reference wind graphic' }
if ($blocks['810900103'] -notmatch 'guid: 7e663825228c4254bc3101f7e71c7cd6' -or $blocks['810900103'] -notmatch 'm_RaycastTarget: 0') { throw 'Wind graphic script/input configuration incorrect' }
if ($blocks['810900102'] -notmatch '^--- !u!222' -or $blocks['810900100'] -notmatch 'm_IsActive: 1') { throw 'Active wind GameObject requires CanvasRenderer' }
if ($blocks['810900101'] -notmatch 'm_AnchorMax: \{x: 1, y: 1\}' -or $blocks['810900101'] -notmatch 'm_SizeDelta: \{x: 0, y: 0\}' -or $blocks['810900101'] -notmatch 'm_Father: \{fileID: 2072900282\}') { throw 'Wind must stretch to BattleCanvas' }
if ($blocks['2072900282'] -notmatch '(?s)m_Children:\s*- \{fileID: 810900101\}\s*- \{fileID: 2027782579\}') { throw 'Wind must precede HUD so HUD stays on top' }
if ($blocks['810900104'] -notmatch '^--- !u!223' -or $blocks['810900104'] -notmatch 'm_OverrideSorting: 0') { throw 'Wind needs an isolated Canvas with inherited draw order' }
'Battle wind scene wiring checks passed.'
