# Production UI callbacks, with two settings panels sharing volume state.
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$source=@'
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
namespace UnityEngine {
 public class MonoBehaviour {public Transform transform=new();}
 public class SerializeField:Attribute {} public class DisallowMultipleComponent:Attribute {}
 public class Transform {
  public Transform parent=>this;public Transform Find(string s)=>null;
  public T GetComponent<T>() where T:class=>null;
  public T GetComponentInChildren<T>(bool b) where T:class=>null;
  public T[] GetComponentsInChildren<T>(bool b)=>Array.Empty<T>();
 }
 public static class Mathf {public static float Clamp01(float v)=>Math.Clamp(v,0,1);public static int RoundToInt(float v)=>(int)Math.Round(v);}
 public static class PlayerPrefs {public static void Save(){}}
}
namespace TMPro {public class TMP_Text {public string name,text;}}
namespace UnityEngine.UI {
 public class FloatEvent {public event Action<float> Changed;public void AddListener(Action<float> a)=>Changed+=a;public void RemoveListener(Action<float> a)=>Changed-=a;public void Invoke(float v)=>Changed?.Invoke(v);}
 public class Slider {public Transform transform=new();public float minValue,maxValue,value;public bool wholeNumbers;public FloatEvent onValueChanged=new();public void SetValueWithoutNotify(float v)=>value=v;}
}
public static class GameAudioSettings {
 public static event Action Changed;
 public static float Master=1,BGM=1,SFX=1,Voice=1;
 public static void SetMaster(float v){Master=v;Changed?.Invoke();}
 public static void SetBGM(float v){BGM=v;Changed?.Invoke();}
 public static void SetSFX(float v){SFX=v;Changed?.Invoke();}
 public static void SetVoice(float v){Voice=v;Changed?.Invoke();}
}
'@
$source+=(Get-Content Assets/Script/SoundSettingsUI.cs -Raw)-replace '(?m)^using [^;]+;\r?\n',''
$source+=@'
public static class SoundUIChecks {
 static int count;static void Check(bool value,string name){if(!value)throw new Exception(name);count++;}
 static void Call(object o,string n)=>o.GetType().GetMethod(n,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(o,null);
 static void Field(object o,string n,object v)=>o.GetType().GetField(n,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(o,v);
 static SoundSettingsUI Make(out Slider[] sliders,out TMP_Text[] texts){
  var ui=new SoundSettingsUI();sliders=new Slider[4];texts=new TMP_Text[4];
  string[] names={"master","bgm","sfx","voice"};
  for(int i=0;i<4;i++){sliders[i]=new();texts[i]=new();Field(ui,names[i]+"Slider",sliders[i]);Field(ui,names[i]+"Text",texts[i]);}
  Call(ui,"Awake");Call(ui,"OnEnable");return ui;
 }
 public static int Run(){
  var main=Make(out var sliders,out var texts);var battle=Make(out var otherSliders,out var otherTexts);
  for(int i=0;i<4;i++){
   Check(texts[i].text=="100%","initial percentage");
   sliders[i].onValueChanged.Invoke(.37f);
   Check(texts[i].text=="37%"&&otherTexts[i].text=="37%"&&otherSliders[i].value==.37f,"drag updates both open panels");
   sliders[i].onValueChanged.Invoke(0);Check(texts[i].text=="0%","mute displays zero");
   sliders[i].onValueChanged.Invoke(1);Check(texts[i].text=="100%","maximum displays 100 percent");
  }
  Call(battle,"OnDisable");sliders[1].onValueChanged.Invoke(.12f);Call(battle,"OnEnable");
  Check(otherTexts[1].text=="12%"&&otherSliders[1].value==.12f,"reopened panel reads current state");
  Call(main,"OnDisable");Call(main,"OnDestroy");Call(battle,"OnDisable");Call(battle,"OnDestroy");
  return count;
 }
}
'@
Add-Type -TypeDefinition $source
"Sound UI percentage/synchronization checks passed: $([SoundUIChecks]::Run())"
