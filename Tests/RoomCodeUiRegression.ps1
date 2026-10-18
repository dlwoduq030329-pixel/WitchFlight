# Offline behavior checks using the complete production popup scripts, not Unity Play Mode.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$source = @'
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
namespace UnityEngine {
 public class SerializeField:Attribute {}
 public class TooltipAttribute:Attribute {public TooltipAttribute(string s){}}
 public class GameObject {public bool activeSelf=true;public void SetActive(bool v){activeSelf=v;}}
 public class Coroutine {public IEnumerator Iterator;public bool Stopped;}
 public class MonoBehaviour {
  public bool isActiveAndEnabled=true;
  readonly List<Coroutine> routines=new();
  public Coroutine StartCoroutine(IEnumerator e){var c=new Coroutine{Iterator=e};e.MoveNext();routines.Add(c);return c;}
  public void StopCoroutine(Coroutine c){c.Stopped=true;}
  public void NextFrame(){var copy=routines.ToArray();routines.Clear();foreach(var c in copy)if(!c.Stopped)c.Iterator.MoveNext();}
 }
 public static class Debug {public static void LogWarning(string s,object c){}}
}
namespace UnityEngine.Events {
 public delegate void UnityAction<T>(T v);
 public class TextEvent {
  public List<UnityAction<string>> Listeners=new();
  public void AddListener(UnityAction<string> f){Listeners.Add(f);}
  public void RemoveListener(UnityAction<string> f){Listeners.Remove(f);}
  public void Invoke(string s){foreach(var f in Listeners.ToArray())f(s);}
 }
}
namespace UnityEngine.UI {public class Button {public bool interactable=true;}}
namespace TMPro {
 public class TMP_Text {public string text="";}
 public class TextMeshProUGUI:TMP_Text {}
 public class TMP_InputField {
  public string text="";public int characterLimit,Selections,Activations;
  public bool richText=true,onFocusSelectAll,isActiveAndEnabled=true,interactable=true;
  public TextEvent onValueChanged=new();
  public bool IsInteractable()=>interactable;
  public void Select(){Selections++;} public void ActivateInputField(){Activations++;}
  public void SetTextWithoutNotify(string s){text=s;}
  public void Type(string s){text=s;onValueChanged.Invoke(s);}
 }
}
public class NetworkGameManager {
 public static NetworkGameManager Instance;
 public int Creates,Joins;public string Code;public Action Success;public Action<string> Failure;
 public void CreateRoom(string code,Action ok,Action<string> fail){Creates++;Code=code;Success=ok;Failure=fail;}
 public void JoinRoom(string code,Action ok,Action<string> fail){Joins++;Code=code;Success=ok;Failure=fail;}
}
'@
foreach ($file in @('RoomCodeRules','EnterRoom','CreateRoomUI')) {
    $production = Get-Content (Join-Path $repo "Assets/Script/$file.cs") -Raw
    $source += [regex]::Replace($production, '(?m)^using .*;\r?\n', '')
}
$source += @'
public static class RoomCodeUiChecks {
 static int checks;
 static void Check(bool v,string name){if(!v)throw new Exception(name);checks++;}
 static void Set(object o,string field,object v)=>o.GetType().GetField(field,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(o,v);
 static void Call(object o,string name)=>o.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(o,null);
 public static int Run(){
  foreach(string code in new[]{"0000","1234","abcd","가나다라"})
   Check(RoomCodeRules.TryValidate(code,out var result,out _)&&result==code,"valid four-character code "+code);
  foreach(string code in new[]{"","1","123","12345","1 23","12\t3"})
   Check(!RoomCodeRules.TryValidate(code,out _,out _),"invalid code "+code);

  var manager=new NetworkGameManager();NetworkGameManager.Instance=manager;
  var create=new CreateRoomUI();var input=new TMP_InputField();var status=new TMP_Text();var button=new Button();
  var waiting=new GameObject{activeSelf=false};var panel=new GameObject();
  Set(create,"roomCodeInput",input);Set(create,"statusText",status);Set(create,"createButton",button);
  Set(create,"waitingRoomUI",waiting);Set(create,"inputPanel",panel);Call(create,"OnEnable");
  foreach(string invalid in new[]{"","1","123","12345"}){
   input.text=invalid;create.TryCreateRoom();
   Check(manager.Creates==0&&panel.activeSelf&&!waiting.activeSelf&&status.text.Contains("4"),"invalid create stays on popup "+invalid);
  }
  input.text="0123";create.TryCreateRoom();create.TryCreateRoom();
  Check(manager.Creates==1&&manager.Code=="0123"&&panel.activeSelf&&!waiting.activeSelf&&!button.interactable,"pending creation does not prematurely open waiting UI; duplicate ignored");
  manager.Failure("방 생성 실패");Check(panel.activeSelf&&!waiting.activeSelf&&button.interactable&&status.text=="방 생성 실패","create failure stays open and allows retry");
  create.TryCreateRoom();manager.Success();
  Check(!panel.activeSelf&&waiting.activeSelf&&input.text==""&&button.interactable,"only successful creation switches panels");

  var enter=new EnterRoom();var fields=new[]{new TMP_InputField(),new TMP_InputField(),new TMP_InputField(),new TMP_InputField()};
  var warning=new TextMeshProUGUI();waiting=new GameObject{activeSelf=false};panel=new GameObject();
  Set(enter,"texts",fields);Set(enter,"waringText",warning);Set(enter,"robbyUI",waiting);Set(enter,"idInputText",panel);
  int foreign=0;fields[0].onValueChanged.AddListener(s=>foreign++);
  Call(enter,"OnEnable");enter.NextFrame();Check(fields[0].Activations==1,"open popup focuses first field");
  for(int i=0;i<4;i++)Check(fields[i].characterLimit==1&&!fields[i].richText,"one character per field "+i);
  enter.TryEnterRoom();Check(manager.Joins==0&&warning.text.Contains("4"),"empty join rejected");enter.NextFrame();
  for(int i=0;i<4;i++){
   int before=i<3?fields[i+1].Activations:fields[3].Activations;
   fields[i].Type((i+1).ToString());enter.NextFrame();
   Check(i<3?fields[i+1].Activations==before+1:fields[3].Activations==before,"advance except final field "+i);
  }
  Check(manager.Joins==0,"last digit does not auto-submit");
  enter.TryEnterRoom();enter.TryEnterRoom();Check(manager.Joins==1&&manager.Code=="1234","join combines four cells once");
  manager.Failure("없는 방");Check(panel.activeSelf&&!waiting.activeSelf&&warning.text=="없는 방","join failure preserves popup");
  fields[1].Type("");enter.TryEnterRoom();enter.NextFrame();Check(manager.Joins==1,"missing cell rejected without network");
  fields[1].Type(" ");enter.TryEnterRoom();Check(manager.Joins==1,"whitespace cannot join");
  fields[1].Type("2");enter.NextFrame();enter.TryEnterRoom();manager.Success();
  Check(waiting.activeSelf&&!panel.activeSelf,"join success switches UI");
  foreach(var f in fields)Check(f.text=="","join clears inputs silently");
  Call(enter,"OnDisable");Check(fields[0].onValueChanged.Listeners.Count==1&&fields[1].onValueChanged.Listeners.Count==0,"only owned listeners removed");
  Call(enter,"OnEnable");Call(enter,"OnEnable");enter.NextFrame();
  Check(fields[0].onValueChanged.Listeners.Count==2&&fields[1].onValueChanged.Listeners.Count==1,"reopening never stacks listeners");
  int secondBefore=fields[1].Activations;fields[0].Type("9");Call(enter,"OnDisable");enter.NextFrame();
  Check(fields[1].Activations==secondBefore&&foreign==2,"closing cancels pending focus; authored listener preserved");
  return checks;
 }
}
'@
Add-Type -TypeDefinition $source -IgnoreWarnings -WarningAction SilentlyContinue
"Room code UI behavior checks passed: $([RoomCodeUiChecks]::Run())"

$multi = Get-Content (Join-Path $repo 'Assets/UI/Prefab/Multi.prefab') -Raw
$buttonBlock = [regex]::Match($multi, '(?ms)^--- !u!114 &5391984762738150523\r?\n.*?(?=^--- !u!|\z)').Value
if (($buttonBlock | Select-String 'm_Target:' -AllMatches).Matches.Count -ne 1 -or
    $buttonBlock -notmatch 'm_MethodName: TryCreateRoom' -or $buttonBlock -match 'm_MethodName: SetActive') {
    throw 'Create confirm must call the validated popup controller only.'
}
foreach ($file in @('Assets/UI/Prefab/IntroAndRobby.prefab','Assets/Scenes/Main.unity','Assets/Scenes/Eunseo.unity')) {
    $yaml = Get-Content (Join-Path $repo $file) -Raw
    if ($yaml -match 'target: \{fileID: 5391984762738150523, guid: b0fa21d2f98732245bed505eaa601452') {
        throw "Old unconditional create button override remains: $file"
    }
}
foreach ($reference in @('roomCodeInput: {fileID: 5195840136103155584}', 'statusText: {fileID: 4014209640514025098}',
    'createButton: {fileID: 5391984762738150523}', 'waitingRoomUI: {fileID: 5142426033498602125}',
    'inputPanel: {fileID: 4109178344409849289}')) {
    if (!$multi.Contains($reference)) { throw "Missing create popup reference: $reference" }
}
$unusedEnter = [regex]::Match($multi, '(?ms)^--- !u!114 &7255987890633575930\r?\n.*?(?=^--- !u!|\z)').Value
if ($unusedEnter -notmatch 'm_Enabled: 0') { throw 'Unused duplicate input controller should be disabled.' }
foreach ($file in @('Assets/UI/Prefab/IntroAndRobby.prefab','Assets/Scenes/Eunseo.unity')) {
    $yaml = Get-Content (Join-Path $repo $file) -Raw
    $hasReplacement = $yaml -match 'm_Script: \{fileID: 11500000, guid: a8347015f0377d34a9b523d15deff38e, type: 3\}'
    $baseDisabled = $yaml -match 'target: \{fileID: 5543811334814847006, guid: b0fa21d2f98732245bed505eaa601452, type: 3\}\r?\n      propertyPath: m_Enabled\r?\n      value: 0'
    # The merged Eunseo scene now uses Multi directly, without an added EnterRoom.
    # Disable the inherited controller only when this variant actually replaces it.
    if ($hasReplacement -and !$baseDisabled) {
        throw "Inherited input controller must not compete with this variant's controller: $file"
    }
    if (!$hasReplacement -and $baseDisabled) {
        throw "Input controller is disabled without a replacement: $file"
    }
}

function RoomBlock([string]$fileId) {
    $result = [regex]::Match($multi, '(?ms)^--- !u!\d+ &' + $fileId + '\r?\n.*?(?=^--- !u!|\z)')
    if (!$result.Success) { throw "Missing room UI object: $fileId" }
    $result.Value
}
$joinButton = RoomBlock '1382500624263959315'
if ($joinButton -notmatch 'm_Target: \{fileID: 5543811334814847006\}' -or
    $joinButton -notmatch 'm_MethodName: TryEnterRoom') { throw 'Join button must call the active EnterRoom controller.' }
$joinController = RoomBlock '5543811334814847006'
if ($joinController -notmatch 'm_Enabled: 1') { throw 'Auto-advance controller must be enabled.' }
$inputs = [regex]::Match($joinController, '(?s)  texts:\r?\n(.*?)  robbyUI:').Groups[1].Value
$ids = @([regex]::Matches($inputs, 'fileID: (\d+)') | ForEach-Object { $_.Groups[1].Value })
if ($ids.Count -ne 4 -or @($ids | Select-Object -Unique).Count -ne 4) { throw 'Join requires four distinct input fields.' }
for ($i=0; $i -lt $ids.Count; $i++) {
    $inputBlock = RoomBlock $ids[$i]
    $goId = [regex]::Match($inputBlock, 'm_GameObject: \{fileID: (\d+)').Groups[1].Value
    $goBlock = RoomBlock $goId
    $expectedName = 'InputField0' + ($i+1)
    if ($goBlock -notmatch ('m_Name: ' + $expectedName + '\r?\n') -or
        $goBlock -notmatch 'm_IsActive: 1' -or $inputBlock -notmatch 'm_Enabled: 1' -or
        $inputBlock -notmatch 'm_Interactable: 1' -or $inputBlock -notmatch 'm_ReadOnly: 0' -or
        $inputBlock -notmatch 'm_CharacterLimit: 1') { throw "Incorrect auto-advance field order or state: $expectedName" }
}
'Room code prefab wiring checks passed: 19'
