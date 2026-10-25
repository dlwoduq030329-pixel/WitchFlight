using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;
namespace Suimono.Core {
public static class SuimonoEffectsSceneSetup {
 const string Root="Assets/SUIMONO - WATER SYSTEM 2";
 [MenuItem("Tools/SUIMONO/Place three visible effects %#F7")]
 public static void Setup(){
  if(Application.isPlaying)throw new Exception("Stop Play before placement");
  var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
  var module=Object.FindFirstObjectByType<SuimonoModule>();var camera=Camera.main;
  if(module==null||camera==null)throw new Exception("Active scene requires SUIMONO_Module and MainCamera");
  if(GameObject.Find("SUIMONO Effects Showcase")!=null){Tune(Object.FindFirstObjectByType<SuimonoEffectsShowcase>());EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);return;}
  Directory.CreateDirectory("Temp/SuimonoEffectsShowcase");EditorSceneManager.SaveScene(scene,"Temp/SuimonoEffectsShowcase/before-placement.unity",true);
  var root=new GameObject("SUIMONO Effects Showcase");Undo.RegisterCreatedObjectUndo(root,"Place SUIMONO effects");
  var rig=root.AddComponent<SuimonoEffectsShowcase>();rig.module=module;
  var surface=Object.FindFirstObjectByType<SuimonoObject>();float y=surface==null?0:surface.transform.position.y;
  var pool=module.GetComponent<SuimonoModuleLib>().fxObject;
  Undo.RecordObject(module,"Connect effect rendering");module.enableInteraction=true;module.enableAdvancedDistort=true;
  Undo.RecordObject(pool,"Configure surface clamp");if(pool.clampIndex==null||pool.clampIndex.Length<pool.effectsSystems.Length)Array.Resize(ref pool.clampIndex,pool.effectsSystems.Length);
  rig.waterfall=Emitter(root.transform,pool,"fx_waterfall",new Vector3(-6,y+2,5),10,.12f,1,0);
  rig.boatwake=Emitter(root.transform,pool,"fx_boatwake",new Vector3(0,y,5),4,.35f,1,1);
  rig.ripple=Emitter(root.transform,pool,"fx_rippleNormals",new Vector3(6,y,5),8,1.3f,1,1);
  var controller=GameObject.Find("_CONTROLLER");if(controller!=null){Undo.RecordObject(controller,"Use effects overview camera");controller.SetActive(false);}
  Undo.RecordObjects(new Object[]{camera,camera.transform},"Frame three effects");camera.transform.position=new Vector3(0,y+10,-16);camera.transform.LookAt(new Vector3(0,y,5));camera.fieldOfView=50;
  module.setCamera=camera.transform;module.setCameraComponent=camera;module.setTrack=camera.transform;
  Tune(rig);EditorUtility.SetDirty(module);EditorUtility.SetDirty(pool);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
  Selection.activeGameObject=root;Debug.Log("SUIMONO_SCENE_PLACED "+scene.path);
 }
 static void Tune(SuimonoEffectsShowcase rig){
  Undo.RecordObjects(new Object[]{rig.waterfall,rig.boatwake,rig.ripple,rig.waterfall.GetComponent<fx_EffectObject>(),rig.boatwake.GetComponent<fx_EffectObject>(),rig.ripple.GetComponent<fx_EffectObject>()},"Separate effect previews");
  float y=Object.FindFirstObjectByType<SuimonoObject>().transform.position.y;
  rig.waterfall.position=new Vector3(-8,y+1,5);rig.waterfall.GetComponent<fx_EffectObject>().effectSize=Vector2.one*4;rig.waterfall.GetComponent<fx_EffectObject>().emitSpeed=.25f;
  rig.boatwake.position=new Vector3(0,y,5);rig.boatwake.GetComponent<fx_EffectObject>().effectSize=Vector2.one*1.7f;rig.boatwake.GetComponent<fx_EffectObject>().emitSpeed=.6f;
  rig.ripple.position=new Vector3(7,y,2);rig.ripple.GetComponent<fx_EffectObject>().effectSize=Vector2.one*6;rig.ripple.GetComponent<fx_EffectObject>().emitSpeed=.7f;
  foreach(var tool in Object.FindObjectsByType<cameraTools>(FindObjectsInactive.Include,FindObjectsSortMode.None))if(tool.cameraType==suiCamToolType.normals){Undo.RecordObject(tool,"Sharpen ripple capture");tool.resolution=512;EditorUtility.SetDirty(tool);}
  var cam=rig.module.setCameraComponent;Undo.RecordObjects(new Object[]{cam,cam.transform},"Frame effects");cam.transform.position=new Vector3(0,y+12,-16);cam.transform.LookAt(new Vector3(0,y,5));cam.fieldOfView=50;
 }
 static Transform Emitter(Transform parent,SuimonoModuleFX pool,string effect,Vector3 position,float size,float interval,int count,int clamp){
  int index=Array.FindIndex(pool.effectsSystems,t=>t!=null&&t.name==effect);if(index<0)throw new Exception("Missing registered effect "+effect);
  var go=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/PREFABS/fx_object.prefab"));go.name=effect+" Generator";go.transform.SetParent(parent);go.transform.position=position;
  var fx=go.GetComponent<fx_EffectObject>();fx.fxObject=pool;fx.systemIndex=index+1;fx.actionIndex=1;fx.emitSpeed=interval;fx.emitNum=new Vector2(count,count);fx.effectSize=new Vector2(size,size);fx.effectDistance=500;fx.emitAtWaterLevel=clamp!=0;fx.clampRot=effect=="fx_boatwake";fx.tintCol=Color.white;fx.directionMultiplier=0;
  fx.effectRule=new Sui_FX_Rules[0];fx.effectData=new float[0];fx.ruleIndex=new int[0];fx.resetRule=new Sui_FX_Rules[0];pool.clampIndex[index]=clamp;
  return go.transform;
 }
 [MenuItem("Tools/SUIMONO/Verify placed effects %#F6")]
 public static void Verify(){
  if(!Application.isPlaying)return;var rig=Object.FindFirstObjectByType<SuimonoEffectsShowcase>();var pool=rig.module.suimonoModuleLibrary.fxObject;int failures=0;
  foreach(var fx in rig.GetComponentsInChildren<fx_EffectObject>()){int count=pool.fxParticles[fx.systemIndex-1].particleCount;bool ok=count>0;if(!ok)failures++;Debug.Log("SUIMONO_PLACED_EFFECT "+fx.name+" count="+count+" "+(ok?"PASS":"FAIL"));}
  var cam=rig.module.setCameraComponent;var rt=new RenderTexture(1280,720,24,RenderTextureFormat.ARGBHalf);rt.Create();Directory.CreateDirectory("Temp/SuimonoEffectsShowcase");
  try{RenderPipeline.SubmitRenderRequest(cam,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});var before=Read(rt);Save(before,1280,720,"scene.png");
   var normals=Object.FindObjectsByType<cameraTools>(FindObjectsSortMode.None).First(t=>t.cameraType==suiCamToolType.normals);normals.CameraUpdate();var mask=Read(normals.renderTexDiff);float energy=mask.Sum(c=>c.r+c.g+c.b)/mask.Length;
   var old=Shader.GetGlobalTexture("_suimono_NormalsTex");try{Shader.SetGlobalTexture("_suimono_NormalsTex",Texture2D.blackTexture);RenderPipeline.SubmitRenderRequest(cam,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});var without=Read(rt);double delta=0;for(int i=0;i<before.Length;i++)delta+=Math.Abs(before[i].r-without[i].r)+Math.Abs(before[i].g-without[i].g)+Math.Abs(before[i].b-without[i].b);delta/=before.Length*3;bool ok=energy>.000001&&delta>.000001;if(!ok)failures++;Debug.Log("SUIMONO_PLACED_RIPPLE mask="+energy+" surfaceDelta="+delta+" "+(ok?"PASS":"FAIL"));}finally{Shader.SetGlobalTexture("_suimono_NormalsTex",old);}
  }finally{rt.Release();Object.DestroyImmediate(rt);}Debug.Log("SUIMONO_PLACED_VALIDATION failures="+failures);
 }
 static Color[] Read(RenderTexture rt){var old=RenderTexture.active;var tex=new Texture2D(rt.width,rt.height,TextureFormat.RGBAFloat,false,true);try{RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);return tex.GetPixels();}finally{RenderTexture.active=old;Object.DestroyImmediate(tex);}}
 static void Save(Color[] pixels,int w,int h,string name){var tex=new Texture2D(w,h,TextureFormat.RGBA32,false);tex.SetPixels(pixels.Select(c=>{c=c.gamma;c.a=1;return c;}).ToArray());tex.Apply();File.WriteAllBytes("Temp/SuimonoEffectsShowcase/"+name,tex.EncodeToPNG());Object.DestroyImmediate(tex);}
}}
