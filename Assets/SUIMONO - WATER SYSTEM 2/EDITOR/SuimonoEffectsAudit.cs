using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;
namespace Suimono.Core {
public static class SuimonoEffectsAudit {
 const string Root="Assets/SUIMONO - WATER SYSTEM 2";
 static string Output => Path.Combine(Path.GetDirectoryName(Application.dataPath),"Temp","SuimonoEffectsAudit");
 static int failures;
 static void Check(bool ok,string name,string detail="") { if(!ok)failures++;Debug.Log("SUIMONO_EFFECT_TEST "+name+" "+(ok?"PASS":"FAIL")+" "+detail); }
 static void Test(string name,Action action){try{action();}catch(Exception e){Check(false,name,e.GetBaseException().Message);}}
 static void Call(object o,string method){o.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(o,null);}
 [MenuItem("Tools/SUIMONO/Audit all effects (Play mode) %#F8")]
 public static void Run(){
  if(!Application.isPlaying){Debug.LogError("Effects audit requires Play mode");return;}
  var module=Object.FindFirstObjectByType<SuimonoModule>();if(module==null)return;
  var pool=module.suimonoModuleLibrary.fxObject;var oldParticles=pool.fxParticles;var oldObjects=pool.fxObjects;var oldClamp=pool.clampIndex;
  var oldCam=module.setCameraComponent;var oldTransform=module.setCamera;
  failures=0;Directory.CreateDirectory(Output);
  var cameraGO=new GameObject("SUIMONO temporary effects audit camera");var cam=cameraGO.AddComponent<Camera>();cam.enabled=false;
  cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.04f,.12f,.2f,1);cam.nearClipPlane=.1f;cam.farClipPlane=100;cam.fieldOfView=45;
  cam.GetUniversalAdditionalCameraData().requiresDepthTexture=true;cam.GetUniversalAdditionalCameraData().requiresColorTexture=true;
  var rt=new RenderTexture(512,512,24,RenderTextureFormat.ARGBHalf);rt.Create();
  try {
   var sources=AssetDatabase.FindAssets("t:Prefab",new[]{Root+"/EFFECTS"}).Select(g=>AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g))).ToList();
   sources.AddRange(AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/PREFABS/SUIMONO_Module.prefab").GetComponentsInChildren<ParticleSystem>(true).Select(p=>p.gameObject));
   foreach(var prefab in sources){
    var instance=Object.Instantiate(prefab,new Vector3(40000,100,40000),Quaternion.identity);instance.name="AUDIT "+prefab.name;
    cameraTools capture=null;
    try {
     Check(instance.GetComponentsInChildren<Transform>(true).All(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)==0),prefab.name+" references");
     var systems=instance.GetComponentsInChildren<ParticleSystem>(true);Check(systems.Length>0,prefab.name+" particleSystem");
     var renderers=instance.GetComponentsInChildren<Renderer>(true);
     // Particle renderers reserve a second material slot for optional trails.
     Check(renderers.All(r=>ValidMaterial(r.sharedMaterial) && (!(r is ParticleSystemRenderer pr) || !pr.GetComponent<ParticleSystem>().trails.enabled || ValidMaterial(pr.trailMaterial))),prefab.name+" materials");
     bool normal=renderers.Any(r=>r.sharedMaterial.shader.name.Contains("NormalBlend"));bool wake=renderers.Any(r=>r.sharedMaterial.shader.name.Contains("WakeMask"));
     foreach(var t in instance.GetComponentsInChildren<Transform>(true))t.gameObject.layer=normal||wake?module.layerScreenFXNum:0;
     foreach(var ps in systems){ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);ps.useAutoRandomSeed=false;ps.randomSeed=123;ps.Play();ps.Emit(12);ps.Simulate(.35f,false,false,true);}
     var bounds=new Bounds(instance.transform.position,Vector3.one);
     foreach(var ps in systems){var buffer=new ParticleSystem.Particle[ps.particleCount];int count=ps.GetParticles(buffer);var main=ps.main;var space=main.simulationSpace==ParticleSystemSimulationSpace.Local?ps.transform:main.customSimulationSpace;
      for(int i=0;i<count;i++){var p=space==null?buffer[i].position:space.TransformPoint(buffer[i].position);bounds.Encapsulate(new Bounds(p,Vector3.one*buffer[i].GetCurrentSize(ps)));}}
     float radius=Mathf.Max(2,bounds.extents.magnitude);cam.transform.position=bounds.center+new Vector3(0,radius*1.8f,-radius*2.2f);cam.transform.LookAt(bounds.center);
     cam.farClipPlane=radius*10;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.04f,.12f,.2f,1);
     cam.cullingMask=1<<instance.layer;
     module.setCameraComponent=cam;module.setCamera=cam.transform;
     if(normal||wake){capture=cameraGO.AddComponent<cameraTools>();capture.cameraType=normal?suiCamToolType.normals:suiCamToolType.wakeEffects;capture.resolution=512;}
     Func<Color[]> render=()=>{if(capture!=null){capture.CameraUpdate();return Read(capture.renderTexDiff);}RenderPipeline.SubmitRenderRequest(cam,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});return Read(rt);};
     foreach(var r in renderers)r.enabled=false;var empty=render();foreach(var r in renderers)r.enabled=true;
     var pixels=render();float delta=Difference(empty,pixels);int pink=pixels.Count(c=>c.r>.8f&&c.b>.8f&&c.g<.05f);
     Check(delta>0.000001f&&pink==0,prefab.name+" rendered",$"delta={delta} magenta={pink} particles={systems.Sum(p=>p.particleCount)} shaders={string.Join(",",renderers.Select(r=>r.sharedMaterial.shader.name))}");
     var ps0=systems[0];pool.fxParticles=new[]{ps0};pool.fxObjects=new[]{ps0.transform};pool.clampIndex=new[]{0};
     Test(prefab.name+" AddFX",()=>{ps0.Clear();module.AddFX(0,instance.transform.position,4,1,0,0,Vector3.up,Color.white);Check(ps0.particleCount==4,prefab.name+" AddFX");});
     foreach(var ps in systems)ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
     module.AddFX(0,instance.transform.position,1,3,0,0,Vector3.zero,Color.white);
     ps0.Simulate(.2f,false,false,true);cam.farClipPlane=100;cam.transform.position=instance.transform.position+new Vector3(0,3,-6);cam.transform.LookAt(instance.transform.position);
     pixels=render();
     var image=new Texture2D(512,512,TextureFormat.RGBA32,false);image.SetPixels(pixels.Select(c=>{c=c.gamma;c.a=1;return c;}).ToArray());image.Apply();File.WriteAllBytes(Path.Combine(Output,prefab.name+".png"),image.EncodeToPNG());Object.DestroyImmediate(image);
     // Boat wake intentionally holds its alpha until 88% of its eight-second life.
     var sample=new ParticleSystem.Particle[ps0.particleCount];int live=ps0.GetParticles(sample);
     float step=live>0?Mathf.Max(.1f,sample[0].startLifetime*.95f-.2f):.8f;
     ps0.Simulate(step,false,false,true);var later=render();Check(Difference(pixels,later)>0.0000001f,prefab.name+" animation");
     Test(prefab.name+" capacity",()=>{ps0.Clear();var main=ps0.main;main.maxParticles=3;module.AddFX(0,instance.transform.position,10,1,0,0,Vector3.zero,Color.white);Check(ps0.particleCount==3,prefab.name+" capacity");});
     Test(prefab.name+" invalidIndex",()=>{module.AddFX(-1,Vector3.zero,1,1,0,0,Vector3.zero,Color.white);module.AddFX(100,Vector3.zero,1,1,0,0,Vector3.zero,Color.white);Check(true,prefab.name+" invalidIndex");});
     Test(prefab.name+" clampGrowth",()=>{ps0.Clear();var main=ps0.main;main.maxParticles=64;pool.clampIndex[0]=1;Call(pool,"ClampSystems");ps0.Emit(30);Call(pool,"ClampSystems");Check(ps0.particleCount==30,prefab.name+" clampGrowth");});
     Test(prefab.name+" localSpace",()=>{ps0.Clear();var main=ps0.main;main.simulationSpace=ParticleSystemSimulationSpace.Local;var position=instance.transform.position+Vector3.up*3;module.AddFX(0,position,1,1,0,0,Vector3.zero,Color.white);var p=new ParticleSystem.Particle[1];ps0.GetParticles(p);Check(Vector3.Distance(ps0.transform.TransformPoint(p[0].position),position)<.01f,prefab.name+" localEmission");pool.clampIndex[0]=1;Call(pool,"ClampSystems");ps0.GetParticles(p);float expected=module.SuimonoGetHeight(position,"surfaceLevel")+.2f;Check(Mathf.Abs(ps0.transform.TransformPoint(p[0].position).y-expected)<.01f,prefab.name+" localClamp");});
     foreach(var ps in systems){ps.Stop(false,ParticleSystemStopBehavior.StopEmitting);ps.Simulate(60,false,false,true);}
     Check(systems.Sum(p=>p.particleCount)==0,prefab.name+" lifetime");
    }catch(Exception e){Check(false,prefab.name,e.GetBaseException().Message);}
    finally {if(capture!=null)Object.DestroyImmediate(capture);Object.DestroyImmediate(instance);}
   }
   Test("emitter stationary/re-enable",()=>{
    var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/PREFABS/fx_object.prefab");var emitterGO=Object.Instantiate(prefab);try{var emitter=emitterGO.GetComponent<fx_EffectObject>();emitter.systemIndex=0;emitter.effectDistance=float.MaxValue;Call(emitter,"Start");emitter.CancelInvoke();emitter.transform.position+=Vector3.right;Call(emitter,"SetUpdate");Check(emitter.currentSpeed>0,"emitter moving");Call(emitter,"SetUpdate");Check(emitter.currentSpeed==0,"emitter stopped");emitter.enabled=false;emitter.enabled=true;Call(emitter,"SetUpdate");Check(emitter.currentSpeed==0,"emitter re-enabled");}finally{Object.DestroyImmediate(emitterGO);}
   });
  }finally{pool.fxParticles=oldParticles;pool.fxObjects=oldObjects;pool.clampIndex=oldClamp;module.setCameraComponent=oldCam;module.setCamera=oldTransform;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(cameraGO);}
  Debug.Log("SUIMONO_EFFECT_AUDIT failures="+failures);
 }
 static bool ValidMaterial(Material m)=>m!=null&&m.shader!=null&&m.shader.isSupported&&m.GetTag("RenderPipeline",false)=="UniversalPipeline";
 static Color[] Read(RenderTexture rt){var previous=RenderTexture.active;var tex=new Texture2D(rt.width,rt.height,TextureFormat.RGBAFloat,false,true);try{RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);tex.Apply();return tex.GetPixels();}finally{RenderTexture.active=previous;Object.DestroyImmediate(tex);}}
 static float Difference(Color[] a,Color[] b){double sum=0;for(int i=0;i<a.Length;i++)sum+=Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b);return (float)(sum/(a.Length*3));}
}}
