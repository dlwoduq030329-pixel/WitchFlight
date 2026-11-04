$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$hud=Get-Content (Join-Path $repo 'Assets/Script/BattleHud.cs') -Raw
function Method($name) {
 $m=[regex]::Match($hud,'(?ms)^    private bool '+$name+'\([^{}]*\)\r?\n    \{.*?^    \}')
 if(!$m.Success){throw "Missing $name"}; $m.Value.Replace('private bool','public bool')
}
$source=@'
using System;
public enum FindObjectsInactive {Include} public enum FindObjectsSortMode {None}
public enum AnimatorUpdateMode {UnscaledTime}
public class GameObject {public int scene;public bool activeInHierarchy=true;public void SetActive(bool value){activeInHierarchy=value;}}
public class Animator {
 public bool enabled=true;public bool isActiveAndEnabled=>enabled;public AnimatorUpdateMode updateMode;
 public int plays;public string state;public float normalizedTime;
 public Animator GetCurrentAnimatorStateInfo(int layer)=>this;
 public void Play(string name,int layer,float at){state=name;normalizedTime=at;plays++;}
 public void Update(float delta){}
}
namespace Lovatto.Crosshair {
 public class bl_MarkerRig { public string Name,AnimationName;public GameObject content=new();public Animator animator=new(); }
 public class bl_HitMarker {
  public GameObject gameObject=new();public string name="Hit Marker (Overwatch)";public bl_MarkerRig[] markers;
  public void OnHit(string key){foreach(var rig in markers)rig.content.SetActive(rig.Name==key);}
  public void SetActiveChild(bool active){foreach(var rig in markers)rig.content.SetActive(active);}
 }
}
public class Hud {
 public GameObject gameObject=new();public Lovatto.Crosshair.bl_HitMarker animatedHitMarker;
 public Lovatto.Crosshair.bl_MarkerRig playingHitRig;public bool playingLethalMarker;
 public static T[] FindObjectsByType<T>(FindObjectsInactive inactive,FindObjectsSortMode mode)=>Array.Empty<T>();
 public void ClearHitMarker(){animatedHitMarker?.SetActiveChild(false);playingHitRig=null;playingLethalMarker=false;}
'@+(Method 'PlayAnimatedHitMarker')+(Method 'AnimatedHitIsPlaying')+@'
}
public static class MarkerChecks {
 static int count;static void Check(bool b,string msg){if(!b)throw new Exception(msg);count++;}
 public static int Run(){
  var hit=new Lovatto.Crosshair.bl_MarkerRig{Name="default",AnimationName="Hit6"};
  var kill=new Lovatto.Crosshair.bl_MarkerRig{Name="letal",AnimationName="AuthoredKill"};
  var h=new Hud{animatedHitMarker=new(){markers=new[]{hit,kill}}};
  Check(h.PlayAnimatedHitMarker(false)&&hit.content.activeInHierarchy&&!kill.content.activeInHierarchy,"Normal hit enables only Marker");
  Check(hit.animator.state=="Hit6"&&hit.animator.normalizedTime==0,"Uses authored state from frame zero");
  hit.animator.normalizedTime=.8f;h.PlayAnimatedHitMarker(false);
  Check(hit.animator.plays==2&&hit.animator.normalizedTime==0,"Repeated hit restarts during playback");
  h.PlayAnimatedHitMarker(true);
  Check(kill.content.activeInHierarchy&&!hit.content.activeInHierarchy,"Kill replaces normal marker");
  Check(kill.animator.state=="AuthoredKill","Kill uses its own AnimationName");
  h.PlayAnimatedHitMarker(false);Check(hit.animator.plays==2&&kill.content.activeInHierarchy,"Normal hit cannot interrupt kill");
  h.PlayAnimatedHitMarker(true);Check(kill.animator.plays==2,"Consecutive kills restart kill animation");
  kill.animator.normalizedTime=1;Check(!h.AnimatedHitIsPlaying(),"Animation completion is recognized");
  h.PlayAnimatedHitMarker(false);Check(hit.content.activeInHierarchy&&!kill.content.activeInHierarchy,"New normal hit allowed after kill ends");
  hit.animator.enabled=false;h.PlayAnimatedHitMarker(false);Check(hit.animator.enabled,"Assigned animator is enabled for replay");
  h.ClearHitMarker();Check(!h.AnimatedHitIsPlaying()&&!hit.content.activeInHierarchy&&!kill.content.activeInHierarchy,"Clear hides both branches");
  h.animatedHitMarker=null;Check(!h.PlayAnimatedHitMarker(false),"Missing authored UI returns to fallback");
  return count;
 }
}
'@
Add-Type -TypeDefinition $source
"Animated hit/kill marker checks passed: $([MarkerChecks]::Run())"
