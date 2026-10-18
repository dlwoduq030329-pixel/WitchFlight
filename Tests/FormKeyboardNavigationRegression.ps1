# Execute the complete production helper with keyboard/TMP/EventSystem test doubles.
# No login requests are sent; native Button.Submit is emulated explicitly below.
param([switch]$InputSystem)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$stubs = @'
#define ENABLE_LEGACY_INPUT_MANAGER
#pragma warning disable 0649
using System;
using System.Collections.Generic;
using System.Reflection;
namespace UnityEngine {
 public class SerializeField:Attribute{} public class DisallowMultipleComponent:Attribute{}
 public class TooltipAttribute:Attribute{public TooltipAttribute(string s){}}
 public class DefaultExecutionOrder:Attribute{public DefaultExecutionOrder(int n){}}
 public class GameObject {
  public bool activeSelf=true;public Transform transform;public object Component;
  public GameObject(){transform=new Transform(this);}
  public bool activeInHierarchy=>activeSelf&&(transform.parent==null||transform.parent.gameObject.activeInHierarchy);
  public T GetComponent<T>() where T:class=>Component as T;
 }
 public class Transform {
  public GameObject gameObject;public Transform parent;public Transform(GameObject owner){gameObject=owner;}
  public bool IsChildOf(Transform root)=>this==root||(parent!=null&&parent.IsChildOf(root));
 }
 public class MonoBehaviour {
  public GameObject gameObject=new();public Transform transform=>gameObject.transform;public bool isActiveAndEnabled=true;
  public MonoBehaviour(){gameObject.Component=this;}
 }
 public static class Time {public static int frameCount;}
 public enum KeyCode {Tab,LeftShift,RightShift,Return,KeypadEnter}
 public static class Input {
  public static HashSet<KeyCode> Down=new(),Held=new();public static string compositionString="";
  public static bool GetKeyDown(KeyCode key)=>Down.Contains(key);public static bool GetKey(KeyCode key)=>Held.Contains(key);
 }
}
namespace UnityEngine.Events {
 public class UnityEvent {
  public event Action Listeners;public void AddListener(Action a){Listeners+=a;}public void Invoke(){Listeners?.Invoke();}
 }
 public class UnityEvent<T> {
  public event Action<T> Listeners;public int Count=>Listeners?.GetInvocationList().Length??0;
  public void AddListener(Action<T> a){Listeners+=a;}public void RemoveListener(Action<T> a){Listeners-=a;}
  public void Invoke(T arg){Listeners?.Invoke(arg);}
 }
}
namespace UnityEngine.UI {
 public class Selectable:UnityEngine.MonoBehaviour {public bool interactable=true;public bool IsInteractable()=>interactable;}
 public class Button:Selectable {public UnityEngine.Events.UnityEvent onClick=new();}
}
namespace TMPro {
 public class TMP_InputField:UnityEngine.UI.Selectable {
  public bool readOnly;public int Activations;public UnityEngine.Events.UnityEvent<string> onSubmit=new();
  public void ActivateInputField(){Activations++;}
 }
}
namespace UnityEngine.EventSystems {
 public class EventSystem {
  public static EventSystem current=new();public UnityEngine.GameObject currentSelectedGameObject;
  public void SetSelectedGameObject(UnityEngine.GameObject obj){currentSelectedGameObject=obj;}
 }
}
namespace UnityEngine.InputSystem.LowLevel {
 public struct IMECompositionString {public int Count;}
}
namespace UnityEngine.InputSystem {
 public class KeyControl {
  public UnityEngine.KeyCode Key;
  public bool wasPressedThisFrame=>UnityEngine.Input.GetKeyDown(Key);
  public bool isPressed=>UnityEngine.Input.GetKey(Key);
 }
 public class Keyboard {
  public static Keyboard current=new();
  public KeyControl tabKey=new(){Key=UnityEngine.KeyCode.Tab},leftShiftKey=new(){Key=UnityEngine.KeyCode.LeftShift},rightShiftKey=new(){Key=UnityEngine.KeyCode.RightShift};
  public KeyControl enterKey=new(){Key=UnityEngine.KeyCode.Return},numpadEnterKey=new(){Key=UnityEngine.KeyCode.KeypadEnter};
  public event Action<LowLevel.IMECompositionString> onIMECompositionChange;
  public int CompositionListeners=>onIMECompositionChange?.GetInvocationList().Length??0;
  public void Compose(int count){onIMECompositionChange?.Invoke(new(){Count=count});}
 }
}
'@
$production=Get-Content Assets/Script/login/FormKeyboardNavigation.cs -Raw
# Keep the whole implementation; put its usings at the start of a dedicated namespace.
$tests=@'
namespace FormKeyChecks {
 using UnityEngine;using UnityEngine.UI;using UnityEngine.EventSystems;using TMPro;
 public static class Tests {
  static int count;static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;}
  static void Set(object o,string name,object v)=>o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(o,v);
  static void Call(object o,string name)=>o.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(o,null);
  static FormKeyboardNavigation Form(int priority,Button confirm,params Selectable[] order){
   var form=new FormKeyboardNavigation();foreach(var control in order)if(control!=null)control.transform.parent=form.transform;
   Set(form,"priority",priority);Set(form,"confirmButton",confirm);Set(form,"tabOrder",order);Call(form,"OnEnable");return form;
  }
  static void Frame(params KeyCode[] keys){Time.frameCount++;Input.Down=new(keys);Input.Held.Clear();}
  static void Step(FormKeyboardNavigation form){Call(form,"Update");Call(form,"LateUpdate");}
  static bool Selected(Selectable control)=>EventSystem.current.currentSelectedGameObject==control.gameObject;
  static void Close(FormKeyboardNavigation form){Call(form,"OnDisable");form.isActiveAndEnabled=false;}
  public static int Run(){
   var id=new TMP_InputField();var pw=new TMP_InputField();var login=new Button();int submits=0,designerEvents=0;
   login.onClick.AddListener(()=>submits++);login.onClick.AddListener(()=>designerEvents++);
   var form=Form(0,login,id,pw,login);
   Step(form);Check(EventSystem.current.currentSelectedGameObject==null,"opening key cannot focus/submit newly opened form in same frame");
   Frame();Step(form);Check(Selected(id)&&id.Activations==1,"initial focus enters first field");
   Frame(KeyCode.Tab);Step(form);Check(Selected(pw)&&pw.Activations==1,"Tab enters password");
   Frame(KeyCode.Tab);Step(form);Check(Selected(login),"Tab enters confirm button");
   Frame(KeyCode.Return);Call(form,"Update");login.onClick.Invoke();Call(form,"LateUpdate");
   Check(submits==1&&designerEvents==1,"native selected button fires once, preserving all click events");
   Frame(KeyCode.Tab);Step(form);Check(Selected(id),"Tab wraps to first field");
   Frame(KeyCode.Tab);Input.Held.Add(KeyCode.LeftShift);Step(form);Check(Selected(login),"Shift Tab wraps backwards");
   Frame(KeyCode.Tab);Input.Held.Add(KeyCode.RightShift);Step(form);Check(Selected(pw),"right Shift Tab enters previous field");
   Frame(KeyCode.Return);Call(form,"Update");pw.onSubmit.Invoke("");Call(form,"LateUpdate");
   Check(submits==2,"input submit and polling do not duplicate confirm");
   Frame(KeyCode.KeypadEnter);Step(form);Check(submits==3,"numpad Enter also confirms");
   Frame();pw.onSubmit.Invoke("");Step(form);Check(submits==3,"no held-key/end-edit/Escape submit");
   login.interactable=false;Frame(KeyCode.Return);Call(form,"Update");pw.onSubmit.Invoke("");Call(form,"LateUpdate");
   Check(submits==3,"disabled confirmation cannot bypass validation");
   Frame(KeyCode.Tab);Step(form);Check(Selected(id),"disabled button skipped");
   pw.readOnly=true;Frame(KeyCode.Tab);Step(form);Check(Selected(id),"read-only field skipped");pw.readOnly=false;
   pw.gameObject.activeSelf=false;Frame(KeyCode.Tab);Step(form);Check(Selected(id),"hidden controls skipped");pw.gameObject.activeSelf=true;
   login.interactable=true;Input.compositionString="한";Frame(KeyCode.Return);Call(form,"Update");id.onSubmit.Invoke("");Call(form,"LateUpdate");
   Check(submits==3,"IME composition Enter does not submit");
   Input.compositionString="";Frame(KeyCode.Return);Step(form);Check(submits==3,"IME commit frame protected");
   Frame();Step(form);Frame(KeyCode.Return);Step(form);Check(submits==4,"separate Enter after composition confirms");
   var sid=new TMP_InputField();var duplicate=new Button();var spw=new TMP_InputField();var repeat=new TMP_InputField();var register=new Button();
   int signups=0,duplicateChecks=0;register.onClick.AddListener(()=>signups++);duplicate.onClick.AddListener(()=>duplicateChecks++);
   var signup=Form(10,register,sid,duplicate,spw,repeat,register);
   id.onSubmit.Invoke("");Step(signup);Check(submits==4&&signups==0,"opening signup cannot submit underlying login or consume opening Enter");
   Frame();Step(form);Step(signup);Check(Selected(sid),"signup priority owns keyboard focus");
   Frame(KeyCode.Tab);Step(form);Step(signup);Check(Selected(duplicate),"duplicate-ID button included in Tab order");
   Frame(KeyCode.Return);Call(signup,"Update");duplicate.onClick.Invoke();Call(signup,"LateUpdate");Step(form);
   Check(duplicateChecks==1&&signups==0&&submits==4,"Enter on duplicate check does not submit signup/login");
   Frame(KeyCode.Tab);Step(signup);Check(Selected(spw),"duplicate check to password");
   Frame(KeyCode.Tab);Step(signup);Check(Selected(repeat),"password to password confirmation");
   Frame(KeyCode.Return);Call(signup,"Update");repeat.onSubmit.Invoke("");Call(signup,"LateUpdate");
   Check(signups==1&&submits==4,"signup Enter only triggers its own form");
   Close(signup);Check(repeat.onSubmit.Count==0,"close removes only helper listeners");
   Frame(KeyCode.Tab);Step(form);Check(Selected(id),"login usable again after closing signup");
   int existing=0;id.onSubmit.AddListener(_=>existing++);Close(form);
   Check(id.onSubmit.Count==1,"designer input submit listeners retained");
   form.isActiveAndEnabled=true;Call(form,"OnEnable");Frame(KeyCode.Tab);Step(form);
   Check(Selected(id),"first Tab after reopen advances once, not twice");
   Frame(KeyCode.Return);Call(form,"Update");id.onSubmit.Invoke("");Call(form,"LateUpdate");
   Check(submits==5&&existing==1&&id.onSubmit.Count==2,"reenable does not duplicate listeners");
   EventSystem.current.SetSelectedGameObject(login.gameObject);Frame(KeyCode.Return);Call(form,"Update");
   login.onClick.Invoke();EventSystem.current.SetSelectedGameObject(null);Call(form,"LateUpdate");
   Check(submits==6,"button clearing selection during click still submits only once");
#if ENABLE_INPUT_SYSTEM
   EventSystem.current.SetSelectedGameObject(id.gameObject);Frame();Step(form);
   UnityEngine.InputSystem.Keyboard.current.Compose(1);Frame(KeyCode.Return);Step(form);
   Check(submits==6,"new Input System IME composition blocks Enter");
   UnityEngine.InputSystem.Keyboard.current.Compose(0);Frame(KeyCode.Return);Step(form);
   Check(submits==6,"new Input System IME commit blocks Enter");
   Frame();Step(form);Frame(KeyCode.KeypadEnter);Step(form);Check(submits==7,"new Input System submits after IME ends");
#endif
   Close(form);
#if ENABLE_INPUT_SYSTEM
   Check(UnityEngine.InputSystem.Keyboard.current.CompositionListeners==0,"new Input System IME subscriptions removed");
#endif
   return count;
  }
 }
}
'@
if($InputSystem){$stubs="#define ENABLE_INPUT_SYSTEM`n"+$stubs}
Add-Type -TypeDefinition ($stubs+"`nnamespace FormKeyChecks {`n"+$production+"`n}`n"+$tests)
"Form keyboard behavior checks passed: $([FormKeyChecks.Tests]::Run())"

function Block([string]$yaml,[string]$id){
 $m=[regex]::Match($yaml,'(?ms)^--- !u!\d+ &'+$id+'\r?\n.*?(?=^--- |\z)')
 if(!$m.Success){throw "Missing prefab object $id"};$m.Value
}
$intro=Get-Content Assets/UI/Prefab/IntroUI.prefab -Raw
$nickname=Get-Content Assets/UI/Prefab/NicknameUI.prefab -Raw
$specs=@(
 @($intro,'4806010000000000001','339252761557932549','8796346009109322142',@('5092167647095901355','2566357969899844294','8796346009109322142')),
 @($intro,'4806010000000000002','1587235897564120300','1405532762901754677',@('1863349794139021693','7741405999295681165','6345727366716840103','7644330837947231154','1405532762901754677')),
 @($nickname,'4806010000000000003','7525182515965870452','4127114834380639270',@('5846445425067743483','4127114834380639270'))
)
foreach($spec in $specs){
 $binding=Block $spec[0] $spec[1]
 if(!(Block $spec[0] $spec[2]).Contains('component: {fileID: '+$spec[1]+'}')){throw 'Navigation must be attached to its form root'}
 if(!$binding.Contains('confirmButton: {fileID: '+$spec[3]+'}')){throw 'Wrong confirmation button'}
 $order=[regex]::Match($binding,'(?s)  tabOrder:\r?\n(.*?)  confirmButton:').Groups[1].Value
 $ids=@([regex]::Matches($order,'fileID: (\d+)') | ForEach-Object {$_.Groups[1].Value})
 if(($ids -join ',') -ne ($spec[4] -join ',')){throw 'Incorrect keyboard order'}
 foreach($id in $ids){$null=Block $spec[0] $id}
 if(!(Block $spec[0] $spec[3]).Contains('m_OnClick:')){throw 'Confirm must reuse the real authored button'}
}
foreach($yaml in $intro,$nickname){
 $ids=[regex]::Matches($yaml,'(?m)^--- !u!\d+ &(\d+)') | ForEach-Object {$_.Groups[1].Value}
 if(@($ids | Group-Object | Where-Object Count -gt 1).Count){throw 'Duplicate prefab object IDs'}
}
'Login, signup and nickname prefab keyboard order / existing confirm bindings passed.'
