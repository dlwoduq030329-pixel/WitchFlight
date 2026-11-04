# Executes production settings lifecycle, render gating and frame sampling with in-memory doubles.
# Does not change desktop display settings, Unity assets, or the user's saved preferences.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$source = @'
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace UnityEngine {
 public enum RuntimeInitializeLoadType {SubsystemRegistration,BeforeSceneLoad}
 public class RuntimeInitializeOnLoadMethodAttribute:Attribute {public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t){}}
 public enum HideFlags {DontSave} public enum CameraType {Game,SceneView}public enum FullScreenMode {FullScreenWindow,Windowed}
 public class Object {public string name; public HideFlags hideFlags;public bool destroyed;
  public static T Instantiate<T>(T o) where T:Object=>(T)(Object)((UniversalRenderPipelineAsset)(Object)o).Copy();
  public static void Destroy(Object o){if(o!=null)o.destroyed=true;}public static void DontDestroyOnLoad(Object o){}
 }
 public class GameObject:Object {public GameObject(string n){name=n;} public T AddComponent<T>() where T:MonoBehaviour,new(){var t=new T();t.gameObject=this;typeof(T).GetMethod("Awake",BindingFlags.NonPublic|BindingFlags.Instance)?.Invoke(t,null);return t;}}
 public class MonoBehaviour:Object {public GameObject gameObject;}
 public class Camera:Object {public CameraType cameraType;public UniversalAdditionalCameraData data;public bool TryGetComponent<T>(out T value) where T:class{value=data as T;return value!=null;}}
 public static class Mathf {public static float Clamp(float x,float a,float b)=>Math.Clamp(x,a,b);public static int Clamp(int x,int a,int b)=>Math.Clamp(x,a,b);public static float Clamp01(float x)=>Math.Clamp(x,0,1);public static float Max(float a,float b)=>Math.Max(a,b);public static int Max(int a,int b)=>Math.Max(a,b);public static float Min(float a,float b)=>Math.Min(a,b);public static int Min(int a,int b)=>Math.Min(a,b);public static int RoundToInt(float x)=>(int)Math.Round(x);}
 public struct Resolution {public int width,height;}
 public static class Screen {public static int width=1280,height=720;public static bool fullScreen;public static Resolution currentResolution=new(){width=1920,height=1080};public static FullScreenMode mode;
  public static void SetResolution(int w,int h,FullScreenMode m){width=w;height=h;mode=m;fullScreen=m!=FullScreenMode.Windowed;}}
 public static class QualitySettings {public static RenderPipelineAsset renderPipeline;public static float lodBias=2;}
 public static class PlayerPrefs {public static Dictionary<string,int> values=new();public static int saves;public static int GetInt(string k,int d)=>values.TryGetValue(k,out var v)?v:d;public static void SetInt(string k,int v)=>values[k]=v;public static bool HasKey(string k)=>values.ContainsKey(k);public static void Save()=>saves++;}
}
namespace UnityEngine.Rendering {
 public class RenderPipelineAsset:UnityEngine.Object {}
 public struct ScriptableRenderContext {}
 public static class GraphicsSettings {public static RenderPipelineAsset fallback;public static RenderPipelineAsset currentRenderPipeline=>QualitySettings.renderPipeline??fallback;}
 public static class RenderPipelineManager {
  public static event Action<ScriptableRenderContext,List<Camera>> beginContextRendering,endContextRendering;
  public static event Action<ScriptableRenderContext,Camera> beginCameraRendering;
  public static void Begin(List<Camera> cams)=>beginContextRendering?.Invoke(default,cams);
  public static void Camera(Camera cam)=>beginCameraRendering?.Invoke(default,cam);
  public static void End(List<Camera> cams)=>endContextRendering?.Invoke(default,cams);
 }
}
namespace UnityEngine.Rendering.Universal {
 public enum CameraRenderType {Base,Overlay}
 public class UniversalRenderPipelineAsset:RenderPipelineAsset {
  public float renderScale=1,shadowDistance=50;public int mainLightShadowmapResolution=2048,additionalLightsShadowmapResolution=2048,shadowCascadeCount=4,maxAdditionalLightsCount=4;
  public UniversalRenderPipelineAsset Copy()=>(UniversalRenderPipelineAsset)MemberwiseClone();
 }
 public class UniversalAdditionalCameraData:UnityEngine.Object {public bool renderPostProcessing;public CameraRenderType renderType;public List<Camera> cameraStack=new();}
}
'@
$source += "`n" + ((Get-Content Assets/Script/GameGraphicsSettings.cs -Raw) -replace '(?m)^using [^;]+;\r?\n','')
$ui = Get-Content Assets/Script/GraphicsSettingsUI.cs -Raw
$source += "`n" + $ui.Substring($ui.IndexOf('public sealed class FramePerformanceMeter'))
$source += @'
public static class GraphicsChecks {
 static int count;static void Check(bool ok,string message){if(!ok)throw new Exception(message);count++;}
 static void Static(string name)=>typeof(GameGraphicsSettings).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,null);
 static void Shutdown(){var i=typeof(GameGraphicsSettings).GetField("instance",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);typeof(GameGraphicsSettings).GetMethod("OnDestroy",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(i,null);}
 public static int Run(){
  var source=new UniversalRenderPipelineAsset();GraphicsSettings.fallback=source;Static("Initialize");
  var runtime=(UniversalRenderPipelineAsset)QualitySettings.renderPipeline;
  Check(runtime!=source&&runtime!=null,"quality uses a runtime clone");
  Check(GameGraphicsSettings.Quality==0&&runtime.renderScale==1&&runtime.shadowDistance==50,"first launch preserves authored very-high quality");
  float previous=2;for(int i=0;i<4;i++){GameGraphicsSettings.SetQuality(i);Check(runtime.renderScale<previous,"each quality lowers render scale");previous=runtime.renderScale;}
  Check(runtime.shadowDistance==20&&runtime.shadowCascadeCount==1&&runtime.mainLightShadowmapResolution==512,"low preset reduces shadow cost");
  Check(source.renderScale==1&&source.shadowDistance==50&&source.shadowCascadeCount==4,"source pipeline never mutated");
  GameGraphicsSettings.SetQuality(0);Check(runtime.renderScale==1&&runtime.shadowCascadeCount==4&&QualitySettings.lodBias==2,"returning to highest restores baseline");
  GameGraphicsSettings.SetDisplayMode(0);Check(Screen.fullScreen&&Screen.width==1920&&Screen.height==1080,"full screen uses native desktop dimensions");
  GameGraphicsSettings.SetDisplayMode(1);Check(!Screen.fullScreen&&Screen.width==1280&&Screen.height==720,"window mode restores previous window size");
  var game=new Camera{data=new(){renderPostProcessing=true}};
  var overlay=new Camera{data=new(){renderPostProcessing=true,renderType=CameraRenderType.Overlay}};
  var hud=new Camera{data=new(){renderPostProcessing=false}};
  var scene=new Camera{cameraType=CameraType.SceneView,data=new(){renderPostProcessing=true}};
  game.data.cameraStack.Add(overlay);var cams=new List<Camera>{game,hud,scene};
  GameGraphicsSettings.SetPostProcessing(1);RenderPipelineManager.Begin(cams);
  Check(!game.data.renderPostProcessing&&!overlay.data.renderPostProcessing,"disable reaches base and overlay before stack setup");
  Check(!hud.data.renderPostProcessing&&scene.data.renderPostProcessing,"HUD stays off and editor Scene view is untouched");
  RenderPipelineManager.Camera(game);var late=new Camera{data=new(){renderPostProcessing=true}};RenderPipelineManager.Camera(late);
  Check(!late.data.renderPostProcessing,"late/new camera obeys toggle");
  RenderPipelineManager.End(cams);Check(game.data.renderPostProcessing&&overlay.data.renderPostProcessing&&late.data.renderPostProcessing,"render overrides restore authored state after frame");
  Check(!hud.data.renderPostProcessing,"restore never enables originally disabled cameras");
  GameGraphicsSettings.SetPostProcessing(0);RenderPipelineManager.Begin(cams);Check(game.data.renderPostProcessing&&!hud.data.renderPostProcessing,"re-enable respects each camera's original opt-in");RenderPipelineManager.End(cams);
  GameGraphicsSettings.SetQuality(2);GameGraphicsSettings.SetPostProcessing(1);Shutdown();
  Check(QualitySettings.renderPipeline==null&&runtime.destroyed&&QualitySettings.lodBias==2,"shutdown restores pipeline override and global LOD");
  Static("ResetStatics");Static("Initialize");Check(GameGraphicsSettings.Quality==2&&!GameGraphicsSettings.PostProcessing&&GameGraphicsSettings.DisplayMode==1,"all saved preferences reload");
  Check(((UniversalRenderPipelineAsset)QualitySettings.renderPipeline).renderScale==.8f,"saved quality applied without opening settings");
  Shutdown();var meter=new FramePerformanceMeter();bool published=false;
  for(int i=0;i<61;i++)published|=meter.Sample(1f/60);Check(published&&Math.Abs(meter.Fps-60)<.01,"sample reports measured 60 FPS");
  Check(FramePerformanceMeter.State(200)==2&&FramePerformanceMeter.State(200.1f)==1,"200 FPS boundary");
  Check(FramePerformanceMeter.State(500)==1&&FramePerformanceMeter.State(500.1f)==0,"500 FPS boundary");
  Check(FramePerformanceMeter.State(60)==2,"fixed thresholds also apply to capped FPS");
  Check(FramePerformanceMeter.Comfort(100)<FramePerformanceMeter.Comfort(300)&&FramePerformanceMeter.Comfort(300)<FramePerformanceMeter.Comfort(600),"comfort increases with FPS");
  Check(FramePerformanceMeter.Comfort(0)==0&&FramePerformanceMeter.Comfort(10000)==1,"slider is bounded");
  meter.Reset();Check(meter.Fps==0&&!meter.Sample(float.NaN)&&!meter.Sample(0),"reset and invalid samples are safe");
  meter.Sample(1.5f);Check(meter.Fps<1,"real long stalls count as high load");
  return count;
 }
}
'@
Add-Type -TypeDefinition $source
"Graphics settings lifecycle/render/performance checks passed: $([GraphicsChecks]::Run())"
$guid = [regex]::Match((Get-Content Assets/Script/GraphicsSettingsUI.cs.meta -Raw),'guid: (\w+)').Groups[1].Value
foreach ($path in 'Assets/UI/Prefab/SettingUI.prefab','Assets/UI/Prefab/IntroAndRobby.prefab') {
    $yaml = Get-Content $path -Raw
    $blocks = @{}
    foreach ($m in [regex]::Matches($yaml,'(?ms)^--- !u!\d+ &(\d+)\r?\n.*?(?=^--- !u!|\z)')) {
        if ($blocks.ContainsKey($m.Groups[1].Value)) {throw "Duplicate fileID in $path"}
        $blocks[$m.Groups[1].Value] = $m.Value
    }
    $component = @($blocks.Values | Where-Object {$_ -match "guid: $guid,"})
    if ($component.Count -ne 1) {throw "Missing graphics controller in $path"}
    $refs=[regex]::Matches($component[0],'(?:displayModeDropdown|qualityDropdown|postProcessingDropdown|performanceSlider|performanceText): \{fileID: (\d+)')
    if($refs.Count -ne 5){throw "Missing graphics bindings in $path"}
    foreach($ref in $refs){if(!$blocks.ContainsKey($ref.Groups[1].Value)){throw "Broken graphics reference in $path"}}
}
'Both graphics tabs have 3 dropdowns and valid performance slider/text bindings.'
