# Production HUD re-enable and slot particle paths with lightweight Unity doubles.
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$dash=Get-Content Assets/Script/DashChargeHud.cs -Raw
$magic=Get-Content Assets/Script/BattleMagicUI.cs -Raw
function Method([string]$source,[string]$name) {
 $m=[regex]::Match($source,'(?ms)^    private void '+$name+'\([^{}]*?\)\r?\n    \{.*?^    \}')
 if(!$m.Success){throw "Missing $name"};$m.Value -replace '^    private ','    public '
}
$source=@'
using System;
using System.Collections.Generic;
namespace UiReuseChecks {
public struct Color {public float r;public Color(float value){r=value;}}
public class Transform {
 public GameObject gameObject;public Transform parent;public Graphic graphic;
 public bool IsChildOf(Transform other){for(var p=this;p!=null;p=p.parent)if(p==other)return true;return false;}
 public T GetComponent<T>() where T:class=>graphic as T;
}
public class GameObject {
 public Transform transform;public bool activeSelf=true;public List<ParticleSystem> particles=new();public Action enabled;
 public GameObject(){transform=new(){gameObject=this};}
 public bool activeInHierarchy=>activeSelf&&(transform.parent==null||transform.parent.gameObject.activeInHierarchy);
 public void SetActive(bool value){bool changed=activeSelf!=value;activeSelf=value;if(changed&&value)enabled?.Invoke();}
 public void GetComponentsInChildren<T>(bool includeInactive,List<T> result) where T:class {
  result.Clear();foreach(var p in particles)if(includeInactive||p.gameObject.activeInHierarchy)result.Add(p as T);
 }
}
public class Graphic {public Color color;}
public class Slider {public Transform fillRect;public Transform transform=new GameObject().transform;}
public class Player {}
public enum ParticleSystemStopBehavior {StopEmittingAndClear}
public class ParticleSystem {
 public GameObject gameObject=new();public int plays,stops;
 public void Play(bool children){plays++;}public void Stop(bool children,ParticleSystemStopBehavior behavior){stops++;}
}
public class Dash {
 public Slider[] segments=new Slider[3];public Graphic[] fillGraphics;public Color[] fillColors;public float[] flashStarted;
 public Player previousPlayer;public int previousCharges,previousCapacity;
'@ + (Method $dash 'OnEnable') + (Method $dash 'ClearRechargeFlashes') + @'
}
public class Magic {
 public Transform transform=new GameObject().transform;
 public Slider magic1CooldownSlider,magic2CooldownSlider;
 private readonly List<ParticleSystem> selectedFxParticles=new();
'@ + (Method $magic 'SetSelectedFx') + @'
}
public static class Checks {
 static int n;static void Check(bool ok,string m){if(!ok)throw new Exception(m);n++;}
 public static int Run(){
  var dash=new Dash();var original=new Graphic{color=new(.3f)};
  dash.segments[0]=new(){fillRect=new(){graphic=original}};dash.OnEnable();
  var graphics=dash.fillGraphics;var colors=dash.fillColors;var times=dash.flashStarted;
  Check(graphics.Length==3&&graphics[0]==original&&graphics[1]==null,"authored graphics and empty slots bind");
  dash.flashStarted[0]=1;original.color=new(1);dash.ClearRechargeFlashes();
  Check(original.color.r==.3f&&dash.flashStarted[0]==-1,"hiding restores pre-flash tint");
  for(int i=0;i<100;i++)dash.OnEnable();
  Check(ReferenceEquals(graphics,dash.fillGraphics)&&ReferenceEquals(colors,dash.fillColors)&&ReferenceEquals(times,dash.flashStarted),"repeated HUD activation reuses all buffers");
  var replacement=new Graphic{color=new(.8f)};dash.segments[0].fillRect.graphic=replacement;dash.OnEnable();
  Check(dash.fillGraphics[0]==replacement&&dash.fillColors[0].r==.8f,"replaced graphics and edited tint rebind");
  Check(dash.previousPlayer==null&&dash.previousCharges==-1&&dash.previousCapacity==-1,"activation still resets presentation tracking");
  dash.segments=new Slider[5];dash.OnEnable();
  Check(dash.fillGraphics.Length==5&&dash.fillColors.Length==5&&dash.flashStarted.Length==5,"capacity edits resize buffers safely");
  dash.segments=Array.Empty<Slider>();dash.OnEnable();dash.ClearRechargeFlashes();Check(dash.fillGraphics.Length==0,"empty authored list safe");
  var magic=new Magic();var fx=new GameObject();fx.SetActive(false);
  var visible=new ParticleSystem();var inactive=new ParticleSystem();inactive.gameObject.SetActive(false);
  fx.particles.Add(visible);fx.particles.Add(inactive);
  magic.SetSelectedFx(fx,true);Check(fx.activeSelf&&visible.plays==1&&inactive.plays==0,"only active particles play on selection");
  magic.SetSelectedFx(fx,true);Check(visible.plays==1,"unchanged selection does not restart particles");
  magic.SetSelectedFx(fx,false);Check(!fx.activeSelf&&visible.stops==1&&inactive.stops==1,"deselection clears inactive particles too");
  var added=new ParticleSystem();fx.enabled=()=>fx.particles.Add(added);
  magic.SetSelectedFx(fx,true);Check(added.plays==1,"particles added on activation are discovered before playback");
  magic.SetSelectedFx(fx,false);Check(added.stops==1,"new particles also clear on deselection");
  magic.SetSelectedFx(magic.transform.gameObject,false);Check(magic.transform.gameObject.activeSelf,"misassigned controller cannot hide itself");
  magic.magic1CooldownSlider=new();magic.magic1CooldownSlider.transform.parent=fx.transform;fx.SetActive(true);
  magic.SetSelectedFx(fx,false);Check(fx.activeSelf,"misassigned slider ancestor stays active");
  magic.SetSelectedFx(null,false);Check(true,"missing optional FX safe");
  return n;
 }
}}
'@
Add-Type -TypeDefinition $source
"UI reuse and activation checks passed: $([UiReuseChecks.Checks]::Run())"
