# Offline geometry/dependency regression. This does not replace a Unity Play Mode visual check.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$graphic = Get-Content Assets/Script/CombatFeedbackGraphic.cs -Raw
$hud = Get-Content Assets/Script/BattleHud.cs -Raw
$stubs = @'
using System;
using System.Collections.Generic;
namespace UnityEngine {
    public class RequireComponent : Attribute {
        public Type Required;
        public RequireComponent(Type required) { Required = required; }
    }
    public class CanvasRenderer {}
    public struct Vector2 {
        public float x,y;
        public Vector2(float x,float y) { this.x=x; this.y=y; }
        public static Vector2 zero => new Vector2(0,0);
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x+b.x,a.y+b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x-b.x,a.y-b.y);
        public static Vector2 operator *(Vector2 a,float b) => new Vector2(a.x*b,a.y*b);
        public static Vector2 Scale(Vector2 a,Vector2 b) => new Vector2(a.x*b.x,a.y*b.y);
    }
    public struct Vector4 {public float x,y,z,w;public Vector4(float x,float y,float z,float w){this.x=x;this.y=y;this.z=z;this.w=w;}public float this[int index]=>index switch{0=>x,1=>y,2=>z,_=>w};}
    public struct Color { public float r,g,b,a; public Color(float r,float g,float b,float a){this.r=r;this.g=g;this.b=b;this.a=a;} }
    public struct Rect {
        public float width,height;
        public Vector2 center => Vector2.zero;
        public Vector2 size => new Vector2(width,height);
    }
    public class RectTransform { public Rect rect = new Rect{width=100,height=100}; }
    public static class Mathf {
        public const float PI = (float)Math.PI;
        public const float Deg2Rad = PI / 180f;
        public static float Min(float a,float b)=>Math.Min(a,b);
        public static float Max(float a,float b)=>Math.Max(a,b);
        public static float Cos(float a)=>(float)Math.Cos(a);
        public static float Sin(float a)=>(float)Math.Sin(a);
    }
}
namespace UnityEngine.UI {
    using UnityEngine;
    public class MaskableGraphic {
        public Color color = new Color(1,1,1,1);
        public RectTransform rectTransform = new RectTransform();
        protected virtual void OnPopulateMesh(VertexHelper helper) {}
        public void Populate(VertexHelper helper) => OnPopulateMesh(helper);
    }
    public class VertexHelper {
        public int currentVertCount => Positions.Count;
        public List<Vector2> Positions = new List<Vector2>();
        public List<Color> Colors = new List<Color>();
        public List<int> Indices = new List<int>();
        public void Clear(){Positions.Clear();Colors.Clear();Indices.Clear();}
        public void AddVert(Vector2 p,Color c,Vector2 uv){Positions.Add(p);Colors.Add(c);}
        public void AddTriangle(int a,int b,int c){Indices.Add(a);Indices.Add(b);Indices.Add(c);}
    }
}
'@
$checks = @'
namespace FeedbackChecks {
    using System;
    using UnityEngine;
    using UnityEngine.UI;
    public static class RunChecks {
        private static int count;
        private static void Check(bool valid,string name){if(!valid)throw new Exception(name);count++;}
        public static int Run(){
            var required=(RequireComponent)Attribute.GetCustomAttribute(typeof(CombatFeedbackGraphic),typeof(RequireComponent));
            Check(required != null && required.Required == typeof(CanvasRenderer),"Custom graphic must require its renderer");
            var graphic=new CombatFeedbackGraphic(); var vertices=new VertexHelper();
            graphic.Populate(vertices);
            Check(vertices.Positions.Count==128,"Ring has both edges for 64 segments");
            Check(vertices.Indices.Count==384,"Ring has 128 triangles");
            Check(vertices.Colors.TrueForAll(c=>c.a==1),"Ring vertices are visible");
            Check(vertices.Positions.TrueForAll(p=>p.x*p.x+p.y*p.y>2000),"Ring center stays transparent");
            Check(vertices.Indices.TrueForAll(i=>i>=0&&i<128),"Ring mesh indices valid");
            graphic.Edges=true;graphic.color=new Color(1,0,0,.28f);graphic.Populate(vertices);
            Check(vertices.Positions.Count==8&&vertices.Indices.Count==24,"Warning draws four edge quads");
            for(int i=0;i<8;i+=2)Check(vertices.Colors[i].a==.28f&&vertices.Colors[i+1].a==0,"Warning gradient has opaque outer and clear inner edge");
            Check(vertices.Indices.TrueForAll(i=>i>=0&&i<8),"Warning mesh indices valid");
            graphic.Exclamation=true;graphic.Populate(vertices);
            Check(vertices.Positions.Count==8&&vertices.Indices.Count==12,"Fallback exclamation draws stem and dot");
            Check(vertices.Colors.TrueForAll(c=>c.a==.28f),"Exclamation is visible without a font or sprite");
            Check(vertices.Indices.TrueForAll(i=>i>=0&&i<8),"Exclamation mesh indices valid");
            graphic.Glow=true;graphic.Populate(vertices);
            Check(vertices.Positions.Count==49&&vertices.Indices.Count==144,"Halo uses a radial triangle fan");
            Check(vertices.Colors[0].a==.28f,"Halo center is visible");
            Check(vertices.Colors.GetRange(1,48).TrueForAll(c=>c.a==0),"Halo fades to transparent at its edge");
            Check(vertices.Indices.TrueForAll(i=>i>=0&&i<49),"Halo mesh indices valid");
            graphic.CornerFrame=true;graphic.Populate(vertices);
            Check(vertices.Positions.Count==25&&vertices.Indices.Count==72,"HUD badge uses a clipped-corner fill and thin outline");
            Check(vertices.Colors[0].a>0&&vertices.Colors[0].a<.28f,"Badge background stays translucent");
            Check(vertices.Indices.TrueForAll(i=>i>=0&&i<25),"Badge mesh indices valid");
            graphic.HitMarker=true;graphic.color=new Color(1,1,1,1);graphic.Populate(vertices);
            Check(vertices.Positions.Count==16&&vertices.Indices.Count==24,"Hit marker has exactly four diamond quads");
            Check(vertices.Indices.TrueForAll(i=>i>=0&&i<16),"Diamond triangle indices are valid");
            float[] expectedAngles={-30,-120,30,120};
            for(int i=0;i<4;i++){
                Vector2 outer=vertices.Positions[i*4],inner=vertices.Positions[i*4+2];
                Vector2 center=(outer+inner)*.5f;
                float angle=(float)(Math.Atan2(center.y,center.x)*180/Math.PI);
                Check(Math.Abs(angle-expectedAngles[i])<.001,"Diamond direction matches requested angle "+expectedAngles[i]);
                Check(Math.Abs(Math.Sqrt(center.x*center.x+center.y*center.y)-28)<.001,"Diamond center preserves gap around dot");
                Vector2 length=outer-inner,width=vertices.Positions[i*4+1]-vertices.Positions[i*4+3];
                Check(Math.Abs(Math.Sqrt(length.x*length.x+length.y*length.y)-18)<.001&&Math.Abs(Math.Sqrt(width.x*width.x+width.y*width.y)-3)<.001,"Each marker is a thin radial diamond");
            }
            Check(vertices.Positions.TrueForAll(p=>p.x*p.x+p.y*p.y>300),"Hit marker keeps center dot unobscured");
            graphic.Flag=true;graphic.Populate(vertices);
            Check(vertices.Positions.Count==7&&vertices.Indices.Count==9,"Fallback flag has a pole and triangular pennant");
            Check(vertices.Indices.TrueForAll(i=>i>=0&&i<7),"Flag triangle indices valid");
            Check(vertices.Colors.TrueForAll(c=>c.a==1),"Flag visible without assigned sprite or font");
            return count;
        }
    }
}
'@
# Place production usings before all type declarations.
$usingLines = [regex]::Matches($graphic,'(?m)^using .*?;') | ForEach-Object { $_.Value }
$body = [regex]::Replace($graphic,'(?m)^using .*?;\r?\n','')
Add-Type -TypeDefinition (($usingLines -join [Environment]::NewLine) + [Environment]::NewLine + $stubs + $body + $checks)
"Feedback graphic checks passed: $([FeedbackChecks.RunChecks]::Run())"
$factories = [regex]::Matches($hud, 'new GameObject\([^;]+typeof\(CombatFeedbackGraphic\)[^;]*;')
if ($factories.Count -ne 4) { throw 'Expected ring, frame, halo and hit marker creation paths' }
foreach ($factory in $factories) {
    if ($factory.Value.IndexOf('typeof(CanvasRenderer)') -lt 0 -or
        $factory.Value.IndexOf('typeof(CanvasRenderer)') -gt $factory.Value.IndexOf('typeof(CombatFeedbackGraphic)')) {
        throw 'CanvasRenderer must exist before the Graphic is enabled'
    }
}
'All HUD creation paths include CanvasRenderer before CombatFeedbackGraphic.'
foreach ($field in 'lockChargeSprite','lockCompleteSprite') {
    if ($hud -notmatch ('\[SerializeField\] private Sprite ' + $field + ';') -or
        $hud -notmatch ('CreateFeedbackRing\([^;]*, ' + $field + '\)')) { throw "Missing sprite binding: $field" }
}
foreach ($expected in 'sprite != null ? typeof(Image) : typeof(CombatFeedbackGraphic)',
    'icon.sprite = sprite;', 'icon.preserveAspect = true;',
    'if (lockChargeMarker == null)', 'if (lockMarker == null)') {
    if (!$hud.Contains($expected)) { throw "Missing authored UI/fallback rule: $expected" }
}
'Lock sprites, aspect ratio, generated fallback and authored marker priority checks passed.'
foreach ($expected in '[SerializeField] private Sprite warningSpriteA;', '[SerializeField] private Sprite warningSpriteB;',
    'generatedWarning.text = "!";', 'warningFrame.CornerFrame = true;', 'if (feedbackFont != null) text.font = feedbackFont;',
    'SelectWarningSprite(warningSpriteA, warningSpriteB, firstFrame)',
    'Mathf.Lerp(warningFarFrequency, warningNearFrequency, threatStrength)') {
    if (!$hud.Contains($expected)) { throw "Missing directional warning rule: $expected" }
}
if ($hud.Contains('generatedWarning.Edges = true;')) { throw 'Directional warning must not generate the old red edges' }
'A/B sprite fields, exclamation fallback and distance-based blink frequency checks passed.'
$scene = Get-Content Assets/Scenes/Battle.unity -Raw
foreach ($setting in 'warningIconSize: 96', 'warningGlowScale: 1.8', 'warningFarFrequency: 1.5', 'warningNearFrequency: 8') {
    if (!$scene.Contains($setting)) { throw "Battle scene has stale warning setting: $setting" }
}
'Large warning / halo / faster blink values are applied in the actual Battle scene.'
