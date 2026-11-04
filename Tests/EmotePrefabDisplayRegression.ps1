$ErrorActionPreference = 'Stop'
$source = Get-Content (Join-Path (Split-Path $PSScriptRoot -Parent) 'Assets/Script/BattleEmoteUI.cs') -Raw
function Method([string]$name) {
 $m=[regex]::Match($source,'(?ms)^    private (?:static )?(?:void|bool) '+$name+'\([^{}]*?\)\r?\n    \{.*?^    \}')
 if(!$m.Success){throw "Missing $name"}
 $m.Value -replace '^    private ', '    public '
}
$test=@'
using System;
using System.Collections.Generic;
public static class Time {public static float unscaledTime;}
public static class Mathf {
 public static float Max(float a,float b)=>Math.Max(a,b);public static float Min(float a,float b)=>Math.Min(a,b);
 public static float Clamp01(float a)=>Math.Clamp(a,0,1);public static float Lerp(float a,float b,float t)=>a+(b-a)*t;
 public static float Abs(float a)=>Math.Abs(a);
}
public struct AnimatorStateInfo {public float length,speed,speedMultiplier;}
public class Animator {
 public int layerCount=2,PlayedLayer=-1,PlayedHash;public float speed=1,PlayedTime=-1;
 public Dictionary<int,AnimatorStateInfo>[] states={new(),new()};
 public static int StringToHash(string name)=>name.GetHashCode();
 public string GetLayerName(int layer)=>layer==0?"Base Layer":"Face";
 public bool HasState(int layer,int hash)=>states[layer].ContainsKey(hash);
 public void Play(int hash,int layer,float time){PlayedHash=hash;PlayedLayer=layer;PlayedTime=time;}
 public void Update(float delta){}
 public AnimatorStateInfo GetCurrentAnimatorStateInfo(int layer)=>states[layer][PlayedHash];
}
public struct Vector3 {public float x;public static Vector3 operator*(Vector3 v,float f)=>new(){x=v.x*f};}
public class Transform {public Vector3 localScale;}
public class GameObject {public bool active,Destroyed;public Transform transform=new();public void SetActive(bool v){active=v;}}
public class CanvasGroup {public float alpha;}
public class EmoteDisplayView {
 public GameObject root=new(),prefabInstance;public bool usesPrefab;public float lifetime,startedAt;
 public Vector3 baseScale=new(){x=1};public CanvasGroup group=new();
}
public class DisplayDriver {
 float displaySeconds=2.5f,fadeSeconds=.35f,popSeconds=.12f;
 static void Show(GameObject root,bool value){root.SetActive(value);}
 static void ApplyPosition(EmoteDisplayView view){}
 static void Destroy(GameObject root){root.Destroyed=true;}
'@ + (Method 'ClearDisplayPrefab') + (Method 'AnimateDisplay') + (Method 'TryPlayNamedAnimator') + @'
}
public static class Tests {
 static int count;static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;}
 public static int Run(){
  var driver=new DisplayDriver();var instance=new GameObject();
  var view=new EmoteDisplayView{usesPrefab=true,lifetime=1,prefabInstance=instance};
  Time.unscaledTime=.8f;driver.AnimateDisplay(view,true);
  Check(view.root.active&&view.group.alpha==1&&view.root.transform.localScale.x==1,"authored prefab animation is not prematurely faded or scaled");
  Time.unscaledTime=1;driver.AnimateDisplay(view,true);
  Check(!view.root.active&&instance.Destroyed&&view.prefabInstance==null,"clip duration expires and destroys the instance");
  Time.unscaledTime=1.2f;driver.AnimateDisplay(view,true);
  Check(!view.root.active,"short prefab cannot reappear during the longer sprite fallback duration");
  view=new EmoteDisplayView();Time.unscaledTime=2.4f;driver.AnimateDisplay(view,true);
  Check(view.root.active&&view.group.alpha>0&&view.group.alpha<1,"sprite-only emote retains original fade");
  Time.unscaledTime=2.5f;driver.AnimateDisplay(view,true);Check(!view.root.active,"sprite expires normally");
  instance=new GameObject();view=new EmoteDisplayView{usesPrefab=true,lifetime=10,prefabInstance=instance};
  Time.unscaledTime=.1f;driver.AnimateDisplay(view,false);
  Check(!view.root.active&&instance.Destroyed,"leaving gameplay cleans up authored prefab");
  var animator=new Animator();int happy=Animator.StringToHash("Base Layer.Happy");
  animator.states[0][happy]=new(){length=2,speed=1,speedMultiplier=1};
  Check(DisplayDriver.TryPlayNamedAnimator(animator,"Happy",out float duration)&&animator.PlayedHash==happy&&animator.PlayedTime==0&&duration==2,"short name resolves full layer path and restarts selected state");
  animator.speed=2;
  Check(DisplayDriver.TryPlayNamedAnimator(animator,"Base Layer.Happy",out duration)&&duration==1,"full path and playback speed determine selected lifetime");
  int sad=Animator.StringToHash("Face.Sad");animator.states[1][sad]=new(){length=3,speed=1,speedMultiplier=1};
  Check(DisplayDriver.TryPlayNamedAnimator(animator,"Sad",out duration)&&animator.PlayedLayer==1&&duration==1.5f,"named state can be resolved on another layer");
  int previous=animator.PlayedHash;
  Check(!DisplayDriver.TryPlayNamedAnimator(animator,"Missing",out duration)&&animator.PlayedHash==previous,"unknown state never plays an unrelated animation");
  return count;
 }
}
'@
Add-Type -TypeDefinition $test
"Emote prefab lifecycle checks passed: $([Tests]::Run())"
