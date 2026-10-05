# Offline lifecycle checks against the production CombatTransientEffect class.
# Unity rendering is stubbed; verify the actual visuals separately in Play Mode.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$effect = Get-Content (Join-Path $repo 'Assets/Script/CombatTransientEffect.cs') -Raw
$stubs = @'
using System;
using System.Collections.Generic;
using System.Reflection;
namespace UnityEngine {
 public class Object {
  public bool destroyed;
  static bool Dead(Object o)=>ReferenceEquals(o,null)||o.destroyed||(o is Component c&&!ReferenceEquals(c.gameObject,null)&&c.gameObject.destroyed);
  public static bool operator==(Object a,Object b)=>(Dead(a)&&Dead(b))||ReferenceEquals(a,b);
  public static bool operator!=(Object a,Object b)=>!(a==b);
  public override bool Equals(object o)=>ReferenceEquals(this,o); public override int GetHashCode()=>base.GetHashCode();
  public static void Destroy(Object o){
   if(o==null)return;
   if(o is GameObject g){foreach(var c in g.components)c.GetType().GetMethod("OnDestroy",BindingFlags.NonPublic|BindingFlags.Instance)?.Invoke(c,null);}
   o.destroyed=true;
  }
 }
 public class Component:Object {public GameObject gameObject;public Transform transform=>gameObject.transform;}
 public class MonoBehaviour:Component {}
 public class Transform {public Vector3 position,localScale;public void SetPositionAndRotation(Vector3 p,Quaternion q){position=p;}}
 public class GameObject:Object {
  public static List<GameObject> All=new();public List<Component> components=new();public string name;public int layer;
  public bool activeSelf=true; public Transform transform=new();public GameObject(string n=""){name=n;All.Add(this);}
  public void SetActive(bool b){activeSelf=b;}
  public T AddComponent<T>() where T:Component,new(){var c=new T{gameObject=this};components.Add(c);return c;}
  public T GetComponent<T>() where T:Component{foreach(var c in components)if(c is T t&&!t.destroyed)return t;return null;}
  public static GameObject CreatePrimitive(PrimitiveType p){var g=new GameObject();g.AddComponent<Collider>();g.AddComponent<Renderer>();return g;}
 }
 public enum PrimitiveType {Sphere}
 public class Collider:Component {public bool enabled=true;}
 public class Renderer:Component {public Material sharedMaterial;public Rendering.ShadowCastingMode shadowCastingMode;}
 public class LineRenderer:Renderer {public int positionCount;public bool useWorldSpace;public float startWidth,endWidth;public Vector3[] points=new Vector3[2];public void SetPosition(int i,Vector3 p){points[i]=p;}}
 public class Material:Object {public Color color;}
 public class Mesh:Object {}
 public struct Vector3 {
  public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}
  public static Vector3 zero=>new(0,0,0);public static Vector3 one=>new(1,1,1);
  public static Vector3 operator*(Vector3 v,float n)=>new(v.x*n,v.y*n,v.z*n);
  public static Vector3 Lerp(Vector3 a,Vector3 b,float t)=>new(a.x+(b.x-a.x)*t,a.y+(b.y-a.y)*t,a.z+(b.z-a.z)*t);
 }
 public struct Quaternion {public static Quaternion identity=>default;}
 public struct Color {public float r,g,b,a;public Color(float x,float y,float z,float w){r=x;g=y;b=z;a=w;}public static Color white=>new(1,1,1,1);}
 public static class Time {public static float unscaledTime;}
 public static class Mathf {public static float Max(float a,float b)=>Math.Max(a,b);public static float Min(float a,float b)=>Math.Min(a,b);public static float Clamp01(float a)=>Math.Clamp(a,0,1);}
 namespace Rendering {public enum ShadowCastingMode {Off}}
}
namespace EffectChecks {
 public static class CombatPresentation {public static int CreatedMaterials;public static UnityEngine.Material CreateEffectMaterial(UnityEngine.Color c){CreatedMaterials++;return new UnityEngine.Material{color=c};}}
}
'@
$tests = @'
namespace EffectChecks { using UnityEngine;
public static class Tests {
 static int n;static void Check(bool v,string label){if(!v)throw new Exception(label);n++;}
 static void Tick(CombatTransientEffect e)=>typeof(CombatTransientEffect).GetMethod("Update",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(e,null);
 static GameObject Last()=>GameObject.All[GameObject.All.Count-1];
 static bool Near(float a,float b)=>Math.Abs(a-b)<.001f;
 public static int Run(){
  var red=new Color(1,0,0,.8f);Time.unscaledTime=0;
  CombatTransientEffect.PlayPulse(new Vector3(1,2,3),red,2,1);var g=Last();var e=g.GetComponent<CombatTransientEffect>();var mat=g.GetComponent<Renderer>().sharedMaterial;
  Check(g.layer==2&&g.GetComponent<Collider>()==null,"pulse collider destroyed and layer preserved");
  Check(g.activeSelf&&Near(g.transform.localScale.x,.08f),"pulse initial size");
  Time.unscaledTime=.5f;Tick(e);Check(Near(mat.color.a,.4f),"pulse fade");
  Time.unscaledTime=1;Tick(e);Check(!g.activeSelf&&!g.destroyed&&!mat.destroyed,"expiry returns pulse and retains material");
  int count=GameObject.All.Count;CombatTransientEffect.PlayPulse(Vector3.zero,new Color(0,1,0,1),3,2);
  Check(GameObject.All.Count==count&&g.activeSelf,"pulse object reused");Check(CombatPresentation.CreatedMaterials==1,"pulse material reused");
  Check(mat.color.g==1&&mat.color.a==1&&Near(g.transform.localScale.x,.08f),"reuse restores color alpha and scale");
  Time.unscaledTime=2;Tick(e);Check(Near(mat.color.a,.5f),"reuse lifetime restarted");
  CombatTransientEffect.PlayBeam(Vector3.zero,new Vector3(4,5,6),red);var beam=Last();var be=beam.GetComponent<CombatTransientEffect>();var line=beam.GetComponent<LineRenderer>();
  Check(beam!=g&&line.positionCount==2&&line.useWorldSpace,"beam pool separated");
  Check(Near(line.startWidth,.12f)&&Near(line.endWidth,.04f)&&line.points[1].z==6,"beam geometry preserved");
  Time.unscaledTime=2.2f;Tick(be);Check(!beam.activeSelf,"beam expires to pool");
  count=GameObject.All.Count;CombatTransientEffect.PlayBeam(new Vector3(7,8,9),Vector3.zero,new Color(0,0,1,1));
  Check(GameObject.All.Count==count&&line.points[0].x==7&&line.points[1].z==0&&line.sharedMaterial.color.a==1,"beam endpoints/color reset on reuse");
  int cap=(int)typeof(CombatTransientEffect).GetField("MaxRetainedPerKind",BindingFlags.NonPublic|BindingFlags.Static).GetRawConstantValue();
  for(int i=0;i<cap+8;i++)CombatTransientEffect.PlayPulse(Vector3.zero,red,2,1);
  Time.unscaledTime=20;
  foreach(var go in GameObject.All.ToArray()){var fx=go.GetComponent<CombatTransientEffect>();if(go!=null&&go.activeSelf&&fx!=null)Tick(fx);}
  var pool=(System.Collections.ICollection)typeof(CombatTransientEffect).GetField("pulses",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
  Check(pool.Count==cap,"pool retention bounded after burst");
  int destroyed=0;foreach(var go in GameObject.All)if(go.destroyed)destroyed++;Check(destroyed>=8,"overflow effects destroyed");
  foreach(var go in GameObject.All.ToArray())Object.Destroy(go);
  CombatTransientEffect.PlayPulse(Vector3.zero,red,2,1);Check(Last().activeSelf&&!Last().destroyed,"destroyed scene references not reused");
  var decoy=new GameObject();var df=decoy.AddComponent<CombatTransientEffect>();var dm=new Material{color=red};var mesh=new Mesh();
  df.Initialize(.5f,Vector3.one,Vector3.one,new[]{dm},new[]{mesh});Time.unscaledTime=21;Tick(df);
  Check(decoy.destroyed&&dm.destroyed&&mesh.destroyed,"nonpooled decoy releases owned resources");
  return n;
 }
}}
'@
Add-Type -TypeDefinition ($stubs + "`nnamespace EffectChecks {`n" + $effect + "`n}`n" + $tests)
"Effect pool lifecycle checks passed: $([EffectChecks.Tests]::Run())"
