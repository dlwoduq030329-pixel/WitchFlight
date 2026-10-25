# Real preferences and capture code with in-memory Unity doubles. No local PlayerPrefs or live game changes.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$source = @'
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
namespace UnityEngine {
 public enum KeyCode {None=0,Tab=9,Return=13,Escape=27,Space=32,Alpha0=48,Alpha1,Alpha2,Alpha3,Alpha4,Alpha5,Alpha6,Alpha7,Alpha8,Alpha9,A=97,D=100,E=101,Q=113,S=115,T=116,W=119,LeftShift=304,RightShift,LeftControl,RightControl,LeftAlt,RightAlt,Mouse0=323,Mouse1,Mouse2,Mouse3,Mouse4,JoystickButton0=330}
 public enum RuntimeInitializeLoadType {SubsystemRegistration}
 public class RuntimeInitializeOnLoadMethodAttribute:Attribute {public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t){}}
 public class SerializeField:Attribute {} public class DisallowMultipleComponent:Attribute {}
 public class MonoBehaviour {public Transform transform=new Transform();}
 public class Transform {public Transform Find(string p)=>null;public T GetComponent<T>() where T:class=>null;}
 public static class Mathf {public static float Clamp(float v,float a,float b)=>Math.Clamp(v,a,b);public static float Round(float v)=>(float)Math.Round(v);public static int RoundToInt(float v)=>(int)Math.Round(v);}
 public static class Time {public static int frameCount;}
 public static class Input {
  public static HashSet<KeyCode> held=new(),down=new();public static bool anyKey=>held.Count>0;
  public static bool GetKey(KeyCode k)=>held.Contains(k);public static bool GetKeyDown(KeyCode k)=>down.Contains(k);
 }
 public static class PlayerPrefs {
  public static Dictionary<string,float> floats=new();public static Dictionary<string,int> ints=new();public static int saves;
  public static float GetFloat(string k,float d)=>floats.TryGetValue(k,out var v)?v:d;
  public static int GetInt(string k,int d)=>ints.TryGetValue(k,out var v)?v:d;
  public static void SetFloat(string k,float v)=>floats[k]=v;public static void SetInt(string k,int v)=>ints[k]=v;public static void Save()=>saves++;
 }
}
namespace TMPro {public class TMP_Text {public string text;}}
namespace UnityEngine.Events {
 public delegate void UnityAction();
 public class ClickEvent {public event UnityAction action;public void AddListener(UnityAction a)=>action+=a;public void RemoveListener(UnityAction a)=>action-=a;public void Invoke()=>action?.Invoke();}
 public class FloatEvent {public event Action<float> action;public void AddListener(Action<float> a)=>action+=a;public void RemoveListener(Action<float> a)=>action-=a;public void Invoke(float v)=>action?.Invoke(v);}
}
namespace UnityEngine.EventSystems {public class EventSystem {public static EventSystem current=new();public void SetSelectedGameObject(object o){}}}
namespace UnityEngine.UI {
 public struct Navigation {public enum Mode {None}public Mode mode;}
 public class Button {public bool interactable=true;public Navigation navigation;public ClickEvent onClick=new();public T GetComponentInChildren<T>(bool b) where T:class=>null;}
 public class Slider {public float minValue,maxValue,value;public bool wholeNumbers,interactable=true;public Navigation navigation;public FloatEvent onValueChanged=new();public void SetValueWithoutNotify(float v)=>value=v;}
}
'@
foreach ($path in 'Assets/Script/ControlSettings.cs','Assets/Script/ControlSettingsUI.cs') {
    $source += "`n" + ((Get-Content $path -Raw) -replace '(?m)^using [^;]+;\r?\n','')
}
$source += @'
public static class ControlChecks {
 static int count;static void Check(bool b,string message){if(!b)throw new Exception(message);count++;}
 static void Call(object o,string name)=>o.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(o,null);
 static void Field(object o,string name,object v)=>o.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(o,v);
 static void Reload()=>typeof(ControlSettings).GetMethod("ResetCache",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,null);
 static void Press(KeyCode k){Input.held.Clear();Input.down.Clear();Input.held.Add(k);Input.down.Add(k);Time.frameCount++;}
 static void Release(){Input.held.Clear();Input.down.Clear();Time.frameCount++;}
 public static int Run(){
  Check(ControlSettings.Sensitivity==50&&ControlSettings.SensitivityMultiplier==1,"50 is existing sensitivity");
  ControlSettings.SetSensitivity(25);Check(ControlSettings.SensitivityMultiplier==.5f,"25 is half sensitivity");
  ControlSettings.SetSensitivity(100);Check(ControlSettings.SensitivityMultiplier==2,"100 is double sensitivity");
  ControlSettings.SetSensitivity(-20);Check(ControlSettings.Sensitivity==1,"avoid disabling camera by zero sensitivity");
  ControlSettings.SetSensitivity(float.NaN);Check(ControlSettings.Sensitivity==1,"ignore invalid sensitivity");
  ControlSettings.SetSensitivity(50);string error;
  Check(ControlSettings.TryRebind(ControlAction.MagicSlot1,KeyCode.Alpha2,out error),"slot swap accepted");
  Check(ControlSettings.GetKey(ControlAction.MagicSlot1)==KeyCode.Alpha2&&ControlSettings.GetKey(ControlAction.MagicSlot2)==KeyCode.Alpha1,"both sides of slot swap updated");
  Check(ControlSettings.TryRebind(ControlAction.LockOn,KeyCode.E,out error),"lock key remapped");
  Check(ControlSettings.GetKey(ControlAction.MagicUse)==KeyCode.E,"cast key always follows lock key");
  Check(ControlSettings.TryRebind(ControlAction.MagicUse,KeyCode.Mouse1,out error),"cast label can remap the shared binding");
  Check(ControlSettings.GetKey(ControlAction.LockOn)==KeyCode.Mouse1&&ControlSettings.GetKey(ControlAction.Parry)==KeyCode.E,"shared binding swaps with parry atomically");
  Check(ControlSettings.TryRebind(ControlAction.Parry,KeyCode.Mouse1,out error),"swap into shared binding");
  Check(ControlSettings.GetKey(ControlAction.LockOn)==KeyCode.E&&ControlSettings.GetKey(ControlAction.MagicUse)==KeyCode.E,"both shared actions move together when displaced");
  foreach(var k in new[]{KeyCode.Escape,KeyCode.Tab,KeyCode.Space,KeyCode.T,KeyCode.S,KeyCode.Alpha3,KeyCode.None,KeyCode.JoystickButton0})
   Check(!ControlSettings.TryRebind(ControlAction.Accelerate,k,out error),"fixed action key rejected: "+k);
  Press(KeyCode.RightShift);Check(ControlSettings.Held(ControlAction.Boost)&&ControlSettings.Down(ControlAction.Boost),"both shift keys retain boost");Release();
  Check(ControlSettings.TryRebind(ControlAction.Boost,KeyCode.Q,out error),"boost remaps");
  Press(KeyCode.RightShift);Check(!ControlSettings.Held(ControlAction.Boost),"old shift no longer boosts");
  Press(KeyCode.Q);Check(ControlSettings.Held(ControlAction.Boost),"new boost key active");Release();
  Reload();Check(ControlSettings.Sensitivity==50&&ControlSettings.GetKey(ControlAction.Boost)==KeyCode.Q,"settings reload from saved values");
  var ui=new ControlSettingsUI();var slider=new Slider();var label=new TMP_Text();
  var rows=new ControlSettingsUI.BindingRow[9];for(int i=0;i<9;i++)rows[i]=new(){action=(ControlAction)i,button=new(),keyText=new()};
  Field(ui,"bindings",rows);Field(ui,"sensitivitySlider",slider);Field(ui,"sensitivityText",label);Call(ui,"Awake");Call(ui,"OnEnable");
  Check(slider.value==.5f&&label.text=="50","authored UI loads default 50");
  Check(rows[0].keyText.text=="2"&&rows[3].keyText.text=="Q","all button labels reflect preferences");
  slider.onValueChanged.Invoke(.75f);Check(ControlSettings.Sensitivity==75&&label.text=="75","slider applies and displays 75");
  Press(KeyCode.Mouse0);rows[0].button.onClick.Invoke();Call(ui,"Update");
  Check(ControlSettings.GetKey(ControlAction.MagicSlot1)==KeyCode.Alpha2,"opening click is ignored");
  Release();Call(ui,"Update");Press(KeyCode.D);Call(ui,"Update");
  Check(rows[0].keyText.text=="D"&&rows[6].keyText.text=="2","capture applies key and refreshes swapped label");
  Check(rows[0].button.interactable&&slider.interactable,"controls restored after capture");Release();
  rows[0].button.onClick.Invoke();Press(KeyCode.Escape);Call(ui,"Update");
  Check(rows[0].keyText.text=="D"&&ControlSettings.GetKey(ControlAction.MagicSlot1)==KeyCode.D,"Escape cancels without modifying binding");Release();
  rows[7].button.onClick.Invoke();Press(KeyCode.Mouse2);Call(ui,"Update");
  Check(rows[7].keyText.text=="휠 클릭"&&rows[8].keyText.text=="휠 클릭","mouse binding updates both lock and cast labels");Release();
  rows[1].button.onClick.Invoke();Call(ui,"OnDisable");Press(KeyCode.W);Call(ui,"Update");
  Check(ControlSettings.GetKey(ControlAction.MagicSlot2)==KeyCode.Alpha1,"closing tab cancels pending capture");Release();
  Call(ui,"OnEnable");Check(label.text=="75","reopening tab restores current settings");Call(ui,"OnDisable");Call(ui,"OnDestroy");
  return count;
 }
}
'@
Add-Type -TypeDefinition $source
"Control preferences and rebind UI checks passed: $([ControlChecks]::Run())"

$guid = [regex]::Match((Get-Content Assets/Script/ControlSettingsUI.cs.meta -Raw),'guid: (\w+)').Groups[1].Value
foreach ($path in 'Assets/UI/Prefab/SettingUI.prefab','Assets/UI/Prefab/IntroAndRobby.prefab') {
    $yaml = Get-Content $path -Raw
    $blocks = @{}
    foreach ($m in [regex]::Matches($yaml,'(?ms)^--- !u!\d+ &(\d+)\r?\n.*?(?=^--- !u!|\z)')) {
        if ($blocks.ContainsKey($m.Groups[1].Value)) {throw "Duplicate fileID in $path"}
        $blocks[$m.Groups[1].Value] = $m.Value
    }
    $component = @($blocks.Values | Where-Object {$_ -match "guid: $guid,"})
    if ($component.Count -ne 1 -or ([regex]::Matches($component[0],'  - action:')).Count -ne 9) {throw "Missing binding rows in $path"}
    foreach ($m in [regex]::Matches($component[0],'(?:sensitivitySlider|sensitivityText|button|keyText): \{fileID: (\d+)')) {
        if (!$blocks.ContainsKey($m.Groups[1].Value)) {throw "Broken UI reference in $path"}
    }
}
'Both settings prefabs have all 9 binding rows and valid slider/text references.'
