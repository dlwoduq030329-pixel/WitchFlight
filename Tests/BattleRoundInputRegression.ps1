# Offline regression for both custom-panel early returns and generated round UI.
param([switch]$LegacyInput)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$ui = Get-Content Assets/Script/BattleRoundUI.cs -Raw
function Method([string]$name) {
    $match = [regex]::Match($ui, '(?ms)^    private void '+$name+'\([^{}]*?\)\r?\n    \{.*?^    \}')
    if (!$match.Success) {throw "Missing method: $name"}
    $match.Value -replace '^    private ', '    public '
}
$source = if ($LegacyInput) {''} else {"#define ENABLE_INPUT_SYSTEM`n"}
$source += @'
using System;
using System.Collections.Generic;
namespace RoundInputChecks {
public class Transform {public Transform parent;public void SetParent(Transform value,bool worldPositionStays){parent=value;}}
public class Component {public GameObject gameObject;public Transform transform=>gameObject.transform;}
public class GameObject {
 public static int Systems,Modules;
 public Transform transform=new();public bool activeSelf=true;public string name;
 public List<Component> components=new();
 public GameObject(string name,params Type[] types){this.name=name;foreach(var type in types)Add(type);}
 public Component Add(Type type){
  var component=(Component)Activator.CreateInstance(type);component.gameObject=this;components.Add(component);
  if(component is EventSystem system){Systems++;EventSystem.current=system;}
  if(component is BaseInputModule){Modules++;}
  return component;
 }
 public T AddComponent<T>() where T:Component,new()=>(T)Add(typeof(T));
 public T GetComponent<T>() where T:Component=>components.Find(c=>c is T) as T;
}
public class EventSystem:Component {public static EventSystem current;}
public class BaseInputModule:Component {public string authoredAction="original binding";}
public class InputSystemUIInputModule:BaseInputModule {}
public class StandaloneInputModule:BaseInputModule {}
public class CanvasGroup:Component {}
public class TMPText:Component {}
public class Button:Component {}
public class Image:Component {}
public struct Vector2 {public Vector2(float x,float y){}}
public enum MagicType {None=0,Fire=1,Smoke=12}
public class Controller {
 public Transform transform=new();public GameObject magicSwapPanel,finalResultPanel;
 public CanvasGroup generatedResultGroup;
 public TMPText swapCountdownText,roundScoreText,slot1Text,slot2Text,selectedMagicNameText,swapStatusText,resultTitleText,resultScoreText;
 public Image slot1Icon,slot2Icon;public Button equipButton,returnToMainButton;
 public int generatedPanels,buttons;public Button resetMagicButton;public bool resetButtonBound;
 public void BindResetButton(){} public void ToggleRoundReady(){}
 public GameObject Panel(string name){generatedPanels++;return new GameObject(name);}
 public Button Button(Transform parent,string caption,Vector2 position,Vector2 size,Action action){buttons++;return new GameObject(caption).AddComponent<Button>();}
 public TMPText Label(Transform parent,string name,string value,Vector2 position,Vector2 size,float fontSize)=>new();
 public Image Image(Transform parent,Vector2 position,Vector2 size)=>new();
 public string Name(MagicType magic)=>magic.ToString();
 public void Icon(Image image,MagicType magic){}
 public void SelectMagic(int id){} public void SelectSlot(int slot){}
 public void EquipSelectedMagic(){} public void ResetRoundChanges(){} public void ReturnToMain(){}
'@
$source += (Method 'EnsureUIInput') + (Method 'EnsureSwapPanel') + (Method 'EnsureResultPanel')
$source += @'
}
public static class Tests {
 static int count;
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;}
 public static int Run(){
  var custom=new GameObject("MagicSelectUI");var result=new GameObject("Authored result");
  var ui=new Controller{magicSwapPanel=custom,finalResultPanel=result};
  ui.EnsureSwapPanel();var system=EventSystem.current;
  Check(system!=null&&GameObject.Systems==1&&GameObject.Modules==1,"custom swap panel receives EventSystem and one input module");
  Check(ui.magicSwapPanel==custom&&ui.generatedPanels==0&&ui.buttons==0,"custom panel and its buttons are preserved");
  Check(system.transform.parent==ui.transform,"input belongs to always-active controller, not hidden panels");
#if ENABLE_INPUT_SYSTEM
  Check(system.gameObject.GetComponent<InputSystemUIInputModule>()!=null&&system.gameObject.GetComponent<StandaloneInputModule>()==null,"new or both input backends prefer InputSystemUIInputModule");
#else
  Check(system.gameObject.GetComponent<StandaloneInputModule>()!=null&&system.gameObject.GetComponent<InputSystemUIInputModule>()==null,"legacy-only builds retain StandaloneInputModule fallback");
#endif
  for(int i=0;i<120;i++){ui.EnsureSwapPanel();ui.EnsureResultPanel();}
  Check(EventSystem.current==system&&GameObject.Systems==1&&GameObject.Modules==1,"repeated refreshes never duplicate systems or modules");
  Check(ui.finalResultPanel==result&&ui.generatedPanels==0,"custom results also preserve authored UI");
  custom.activeSelf=false;result.activeSelf=false;
  Check(system.gameObject.activeSelf&&system.transform.parent!=custom.transform&&system.transform.parent!=result.transform,"hiding a panel does not hide input system");
  var second=new Controller{magicSwapPanel=new GameObject("Second panel")};second.EnsureSwapPanel();
  Check(EventSystem.current==system&&GameObject.Systems==1,"another controller reuses active scene input");
  var authored=new GameObject("Existing scene EventSystem",typeof(EventSystem));
  var input=authored.AddComponent<InputSystemUIInputModule>();input.authoredAction="custom UI actions";
  int systems=GameObject.Systems,modules=GameObject.Modules;ui.EnsureUIInput();
  Check(EventSystem.current.gameObject==authored&&GameObject.Systems==systems&&GameObject.Modules==modules,"authored EventSystem is reused unchanged");
  Check(input.authoredAction=="custom UI actions"&&authored.transform.parent==null,"existing actions and scene hierarchy are untouched");
  EventSystem.current=null;ui.EnsureResultPanel();
  Check(EventSystem.current!=null&&GameObject.Systems==systems+1,"custom results repair missing input without generating UI");
  EventSystem.current=null;ui.EnsureSwapPanel();
  Check(EventSystem.current!=null&&GameObject.Systems==systems+2,"lost input is restored even when swap panel already exists");
  EventSystem.current=null;var generated=new Controller();generated.EnsureSwapPanel();generated.EnsureResultPanel();
  Check(generated.generatedPanels==2&&generated.buttons==17,"default UI still creates twelve magic, two slot, equip, reset and return buttons");
  Check(EventSystem.current.transform.parent==generated.transform&&GameObject.Systems==systems+3,"generated UI shares one persistent-in-scene input system");
  return count;
 }
}}
'@
Add-Type -TypeDefinition $source
"Round UI input checks passed ($(@('Input System','Legacy')[[int]$LegacyInput.IsPresent])): $([RoundInputChecks.Tests]::Run())"
if ((Method 'Awake') -notmatch 'EnsureUIInput\(\);') {throw 'Battle input must be initialized outside panel creation'}
if ((Method 'EnsureCanvas') -match 'new GameObject\([^\r\n]*EventSystem') {throw 'Canvas generation must not own another input bootstrap'}
if ($ui -notmatch '#if ENABLE_INPUT_SYSTEM\s+using UnityEngine.InputSystem.UI;\s+#endif') {throw 'Input System dependency must be conditional'}
'Startup bootstrap and generated-canvas isolation passed.'
