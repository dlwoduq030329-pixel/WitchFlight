# Execute the production shared preview and check both authored UI bindings, without Unity.
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$fixture=Get-Content (Join-Path $repo 'Tests/MagicCustomizationRegression.ps1') -Raw
$stubs=[regex]::Match($fixture,'(?s)\$stubs = @''\r?\n(.*?)\r?\n''@').Groups[1].Value
$enums=[regex]::Match((Get-Content (Join-Path $repo 'Assets/Script/PlayerData.cs') -Raw),'public enum MagicType\s*\{[^}]+\}').Value
$table=Get-Content (Join-Path $repo 'Assets/Script/MagicStatTable.cs') -Raw
$view=Get-Content (Join-Path $repo 'Assets/Script/MagicDescriptionUI.cs') -Raw
$perks=Get-Content (Join-Path $repo 'Assets/Script/PerkStatTable.cs') -Raw
$rules=Get-Content (Join-Path $repo 'Assets/Script/PerkRules.cs') -Raw
$formatter=[regex]::Match((Get-Content (Join-Path $repo 'Assets/Script/BattlePerkUI.cs') -Raw),'(?ms)^    public static string Description\(.*?;').Value
$round=Get-Content (Join-Path $repo 'Assets/Script/BattleRoundUI.cs') -Raw
$select=[regex]::Match($round,'(?ms)^    public void SelectMagic\(int magicId\).*?^    \}').Value
$refresh=[regex]::Match($round,'(?ms)^    private void RefreshMagicDescription\(\).*?^    \}').Value
$visibility=[regex]::Match($round,'(?ms)^    private void SetDescriptionVisible\(bool visible\).*?^    \}').Value
$roundMethods=@()
foreach($method in 'EquipSelectedMagic','ResetRoundChanges','SendLoadout','OnMagicChangeResult','UpdatePendingRequest'){
 $m=[regex]::Match($round,'(?ms)^    (?:public|private) void '+$method+'\([^{}]*?\)\r?\n    \{.*?^    \}')
 if(!$m.Success){throw "Missing round method: $method"};$roundMethods+=$m.Value.Replace('private void','public void')
}
$roundMethods+=[regex]::Match($round,'(?m)^    public void SelectSlot\([^\r\n]+').Value
$roundMethods+=[regex]::Match($round,'(?m)^    private static void Show\([^\r\n]+').Value
$roundRules=Get-Content (Join-Path $repo 'Assets/Script/BattleRoundRules.cs') -Raw
$source=$stubs+"`nnamespace MagicCustomizationChecks { $enums }`nnamespace MagicCustomizationChecks { $table }`nnamespace MagicCustomizationChecks { $view }`n"
$source+="namespace MagicCustomizationChecks { $perks }`nnamespace MagicCustomizationChecks { $rules }`nnamespace MagicCustomizationChecks { public static class BattlePerkUI { $formatter } }`n"
$source+="namespace MagicCustomizationChecks { $roundRules }`nnamespace UnityEngine {public static class Time {public static float unscaledTime;}}`n"
$source+=@'
namespace MagicCustomizationChecks {
 using System;
 using System.Reflection;
 using UnityEngine;
 using UnityEngine.UI;
 using TMPro;
 public class RoundLoadout {
  public MagicType magic1=MagicType.Fire,magic2=MagicType.Ice,MagicChangeBase1=MagicType.Fire,MagicChangeBase2=MagicType.Ice;
  public bool canSend=true;public int requests;
  public bool RequestRoundMagicChange(int round,MagicType first,MagicType second,int id){requests++;return canSend;}
 }
 public class RoundPreviewProbe : MonoBehaviour {
  public MagicType selectedMagic;public MagicStatTable magicTable;public MagicDescriptionUI magicDescriptionUI;public GameObject magicSwapPanel;
  public bool descriptionOpen,closeDescriptionOnAccept,requestPending,requestAccepted,CanEdit=true,HasLocalData=true;
  public int descriptionRevision,requestedDescriptionRevision,pendingRequestId,nextRequestId,swapRound=1,selectedSlot=1;
  public float requestStarted;public MagicType requestedFirst,requestedSecond;
  public RoundLoadout localData=new RoundLoadout();public string status;
  private void Status(string message){status=message;}
  public void RefreshForTest()=>RefreshMagicDescription();
'@+$select+$refresh+$visibility.Replace('private void','public void')+($roundMethods -join "`n")+@'
 }
 public static class DescriptionChecks {
  static int count;
  static void Check(bool ok,string message){if(!ok)throw new Exception(message);count++;}
  static void Set(object target,string field,object value)=>target.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
  public static int Run(){
   var table=new MagicStatTable();
   for(int i=0;i<table.magics.Length;i++){
    table.magics[i].displayDamage=.13f;table.magics[i].displayAimTime=.37f;table.magics[i].displayRange=.62f;table.magics[i].displayManaCost=.89f;
    table.magics[i].displayDamageLabel="낮음";table.magics[i].displayAimTimeLabel="짧음";table.magics[i].displayRangeLabel="중간";table.magics[i].displayManaCostLabel="높음";
    table.magics[i].displayCategory="공격 마법";table.magics[i].displayCastType="록온";table.magics[i].displayFeatures="마법 특징";
    table.magics[i].icon=new Sprite();
   }
   Array.Reverse(table.magics);
   var ui=new MagicDescriptionUI();Set(ui,"magicTable",table);
   var perkTable=new PerkStatTable();Array.Reverse(perkTable.perks);Set(ui,"perkTable",perkTable);
   var perk1=new TMP_Text();var perk2=new TMP_Text();Set(ui,"perk1DescriptionText",perk1);Set(ui,"perk2DescriptionText",perk2);
   var damage=new Slider();var aim=new Slider();var range=new Slider();var mana=new Slider();
   Set(ui,"damageSlider",damage);Set(ui,"aimTimeSlider",aim);Set(ui,"rangeSlider",range);Set(ui,"manaCostSlider",mana);
   var icon=new Image();var name=new TMP_Text();var description=new TMP_Text();var category=new TMP_Text();var castType=new TMP_Text();var features=new TMP_Text();
   Set(ui,"magicIcon",icon);Set(ui,"magicNameText",name);Set(ui,"descriptionText",description);Set(ui,"categoryText",category);Set(ui,"castTypeText",castType);Set(ui,"featuresText",features);
   var damageText=new TMP_Text();var aimText=new TMP_Text();var rangeText=new TMP_Text();var manaText=new TMP_Text();
   Set(ui,"damageLabel",damageText);Set(ui,"aimTimeLabel",aimText);Set(ui,"rangeLabel",rangeText);Set(ui,"manaCostLabel",manaText);
   foreach(var e in table.magics){
    ui.ShowMagic((int)e.magic);
    Check(name.text==e.displayName&&description.text==e.description&&icon.sprite==e.icon,"lookup by MagicType after reordered table: "+e.magic);
    Check(perk1.text==BattlePerkUI.Description(perkTable.Get(PerkRules.Spell(e.magic,1)))&&perk2.text==BattlePerkUI.Description(perkTable.Get(PerkRules.Spell(e.magic,2))),"fixed stage perks by ID after reordered table: "+e.magic);
    Check(!perk1.text.Contains("{")&&!perk2.text.Contains("{"),"perk numeric placeholders resolved: "+e.magic);
   }
   Check(damage.value==.13f&&aim.value==.37f&&range.value==.62f&&mana.value==.89f,"float values are not rounded to cells or derived from combat stats");
   Check(!damage.interactable&&!aim.interactable&&!range.interactable&&!mana.interactable,"stat bars are read-only");
   Check(damageText.text=="낮음"&&aimText.text=="짧음"&&rangeText.text=="중간"&&manaText.text=="높음","four independent rating labels");
   Check(category.text=="공격 마법"&&castType.text=="록온"&&features.text=="마법 특징","optional metadata from same table");
   int index=Array.FindIndex(table.magics,e=>e.magic==MagicType.Fire);var before=table.magics[index];
   table.magics[index].displayDamage=.73f;ui.ShowMagic(1);
   Check(damage.value==.73f&&table.magics[index].damage==before.damage&&table.magics[index].apCost==before.apCost&&table.magics[index].range==before.range&&table.magics[index].lockChargeSeconds==before.lockChargeSeconds,"UI balancing never changes combat balancing");
   table.magics[index].displayDamage=.44f;ui.Refresh();Check(damage.value==.44f,"explicit refresh reflects Play Mode table edits");
   table.magics[index].displayDamage=-5;table.magics[index].displayAimTime=8;table.magics[index].displayRange=float.NaN;table.magics[index].displayManaCost=float.PositiveInfinity;
   ui.Refresh();Check(damage.value==0&&aim.value==1&&range.value==0&&mana.value==0,"malformed values clamped safely");
   damage.minValue=2;damage.maxValue=10;table.magics[index].displayDamage=.5f;ui.Refresh();Check(damage.value==6&&damage.minValue==2&&damage.maxValue==10,"normalized data respects authored slider range without resetting it");
   table.fallback=new MagicStatEntry{displayName="Must not leak",icon=new Sprite(),displayDamage=1};
   foreach(int invalid in new[]{0,-1,999}){
    ui.ShowMagic(invalid);Check(damage.value==2&&aim.value==0&&!icon.enabled&&name.text==""&&description.text==""&&damageText.text==""&&perk1.text==""&&perk2.text=="","invalid magic clears all previous content: "+invalid);
   }
   ui.ShowMagic(1);ui.Clear();Check(name.text==""&&!icon.enabled&&rangeText.text==""&&perk1.text==""&&perk2.text=="","explicit clear resets stale contents");
   ui.gameObject.activeSelf=false;ui.ShowMagic(1);Check(!ui.gameObject.activeSelf&&ui.gameObject.Writes==0,"renderer never controls designer-owned panel visibility");
   var other=new MagicStatTable();other.magics[0].displayName="Alternate";other.magics[0].displayDamage=.25f;
   ui.ShowMagic(MagicType.Fire,other);Check(name.text=="Alternate"&&damage.value==4,"owning screen can supply its table");
   ui.ShowMagic(1);Check(name.text=="Alternate"&&damage.value==4,"prefab-local button preserves owning screen table override");
   var round=new RoundPreviewProbe{magicTable=table,magicDescriptionUI=ui};round.SelectMagic(2);
   Check(name.text==table.GetStats(MagicType.Ice).displayName&&description.text==table.GetStats(MagicType.Ice).description&&aim.value==.37f,"intermission selection invokes the same renderer");
   round.SelectMagic(999);Check(round.selectedMagic==MagicType.Ice,"invalid round selection leaves existing valid selection unchanged");
   round.magicDescriptionUI=null;round.magicSwapPanel=new GameObject{ChildComponent=ui};round.SelectMagic(3);
   Check(round.magicDescriptionUI==ui&&name.text==table.GetStats(MagicType.Vision).displayName,"replacement panel resolves its own description view when field is unassigned");
   round.SetDescriptionVisible(false);round.RefreshForTest();Check(!ui.gameObject.activeSelf,"screen initialization and content refresh leave explanation closed");
   round.SelectSlot(2);Check(!ui.gameObject.activeSelf,"selecting target slot alone never opens description");
   round.SelectMagic(3);Check(ui.gameObject.activeSelf&&round.localData.magic2==MagicType.Ice,"spell click opens preview without equipping");
   BattleManager.Instance=new BattleManager{Perks=perkTable};round.EquipSelectedMagic();
   Check(round.requestPending&&ui.gameObject.activeSelf,"pending request does not pretend equip succeeded");
   round.OnMagicChangeResult(round.pendingRequestId,1,true,"");round.UpdatePendingRequest();
   Check(ui.gameObject.activeSelf,"RPC acceptance waits for replicated loadout");
   round.localData.magic2=MagicType.Vision;round.UpdatePendingRequest();
   Check(!ui.gameObject.activeSelf&&!round.requestPending&&round.selectedMagic==MagicType.Vision,"accepted replicated equip closes description and retains candidate");
   round.RefreshForTest();round.SelectSlot(1);round.RefreshForTest();ui.ShowMagic(3);
   Check(!ui.gameObject.activeSelf,"frame refresh, slot change and local content callback cannot reopen equipped details");
   round.SelectMagic(4);round.EquipSelectedMagic();round.SelectMagic(6);
   round.OnMagicChangeResult(round.pendingRequestId,1,true,"");round.localData.magic1=MagicType.Thunder;round.UpdatePendingRequest();
   Check(ui.gameObject.activeSelf&&round.selectedMagic==MagicType.Binding,"late approval for an older selection never closes a newer preview");
   round.EquipSelectedMagic();round.OnMagicChangeResult(round.pendingRequestId,1,false,"Rejected");
   Check(ui.gameObject.activeSelf&&!round.requestPending,"server rejection preserves preview");
   round.EquipSelectedMagic();Time.unscaledTime=6;round.UpdatePendingRequest();
   Check(ui.gameObject.activeSelf&&!round.requestPending,"timeout preserves preview");
   round.localData.canSend=false;round.EquipSelectedMagic();Check(!round.requestPending&&ui.gameObject.activeSelf,"send failure preserves preview");round.localData.canSend=true;
   BattleManager.Instance.AllowedMagicChanges=1;int sent=round.localData.requests;round.EquipSelectedMagic();
   Check(round.localData.requests==sent&&ui.gameObject.activeSelf,"slot-limit failure preserves preview");BattleManager.Instance.AllowedMagicChanges=2;
   round.ResetRoundChanges();round.OnMagicChangeResult(round.pendingRequestId,1,true,"");round.localData.magic1=MagicType.Fire;round.localData.magic2=MagicType.Ice;round.UpdatePendingRequest();
   Check(ui.gameObject.activeSelf,"resetting loadout is not an equip confirmation and leaves preview open");
   round.SetDescriptionVisible(false);round.SelectMagic(2);Check(ui.gameObject.activeSelf,"new spell click reopens closed details");
   var bad=new RoundPreviewProbe{magicDescriptionUI=new MagicDescriptionUI()};bad.magicDescriptionUI.gameObject=bad.gameObject;bad.SetDescriptionVisible(false);
   Check(bad.gameObject.activeSelf,"wrong self reference cannot deactivate battle controller");
   bad.magicDescriptionUI=new MagicDescriptionUI();bad.magicSwapPanel=bad.magicDescriptionUI.gameObject;bad.SetDescriptionVisible(false);
   Check(bad.magicSwapPanel.activeSelf,"wrong panel reference cannot hide the entire magic selection screen");
   var battlePerks=new PerkStatTable();var firePerk=battlePerks.Get(PerkId.FireBurn);
   firePerk.description="{amount} damage / {seconds}s / {percent}% / {secondaryPercent}%";firePerk.amount=.125f;firePerk.seconds=3.5f;firePerk.secondaryAmount=.25f;
   BattleManager.Instance=new BattleManager{Perks=battlePerks};round.SelectMagic(1);
   Check(perk1.text=="0.13 damage / 3.5s / 12.5% / 25%","battle preview uses active match perk table and live formatter");
   ui.ShowMagic(1);Check(perk1.text.Contains("3.5s"),"prefab-local callback preserves active match perk table override");
   firePerk.seconds=6;ui.Refresh();Check(perk1.text.Contains("6s"),"perk value edits appear on refresh");
   int perkIndex=Array.FindIndex(battlePerks.perks,p=>p.id==PerkId.FireIgnition);battlePerks.perks[perkIndex]=null;ui.Refresh();
   Check(perk1.text!=""&&perk2.text=="","missing stage clears only that description without stale fallback");
   battlePerks.perks=null;ui.Refresh();Check(perk1.text==""&&perk2.text=="","empty perk definitions clear both previous descriptions");
   BattleManager.Instance=null;Set(ui,"perkTable",null);ui.ShowMagic(MagicType.Fire,table);
   Check(perk1.text!=""&&perk2.text!="","unassigned perk table resolves default definitions");
   Set(ui,"perk1DescriptionText",null);ui.ShowMagic(2);Check(perk2.text!="","either description can be assigned independently");Set(ui,"perk1DescriptionText",perk1);
   var empty=new MagicDescriptionUI();empty.ShowMagic(1);empty.Clear();empty.Refresh();Check(true,"unassigned fields and missing table are safe");
   Set(ui,"magicTable",null);Set(ui,"currentTable",null);typeof(MagicStatTable).GetField("defaultTable",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,null);
   ui.ShowMagic(1);Check(name.text==""&&!icon.enabled&&damage.value==2&&perk1.text==""&&perk2.text=="","missing table does not retain stale content");
   return count;
  }
 }
}
'@
Add-Type -IgnoreWarnings -WarningAction SilentlyContinue -TypeDefinition $source
"Shared magic description behavior checks passed: $([MagicCustomizationChecks.DescriptionChecks]::Run())"

function Blocks([string]$text){
 $map=@{};foreach($m in [regex]::Matches($text,'(?ms)^--- !u!\d+ &(\d+)[^\r\n]*\r?\n.*?(?=^--- !u!|\z)')){
  if($map.ContainsKey($m.Groups[1].Value)){throw 'Duplicate Unity object ID'};$map[$m.Groups[1].Value]=$m.Value
 };return $map
}
$custom=Blocks (Get-Content (Join-Path $repo 'Assets/UI/Prefab/CustumUI.prefab') -Raw)
$swap=Blocks (Get-Content (Join-Path $repo 'Assets/UI/Prefab/MagicSelectUI.prefab') -Raw)
foreach($entry in @(@{map=$custom;id='7255314972244087112'},@{map=$swap;id='6101320497413326010'})){
 $component=$entry.map[$entry.id]
 if($component -notmatch 'guid: bda78c6d48c34d5596f8a2b47e15a903'){throw 'Shared component missing'}
 foreach($field in @('magicIcon','magicNameText','descriptionText','categoryText','castTypeText','damageSlider','aimTimeSlider','rangeSlider','manaCostSlider','damageLabel','aimTimeLabel','rangeLabel','manaCostLabel')){
  $id=[regex]::Match($component,'(?m)^  '+$field+': \{fileID: (\d+)').Groups[1].Value
  if(!$entry.map.ContainsKey($id)){throw "Unresolved field: $field"}
  if($field.EndsWith('Slider') -and $entry.map[$id] -notmatch 'm_MaxValue: 1'){throw "Wrong Slider component: $field"}
 }
 $go=[regex]::Match($component,'m_GameObject: \{fileID: (\d+)').Groups[1].Value
 if($entry.map[$go] -notmatch ('component: \{fileID: '+$entry.id+'\}')){throw 'Component not attached to its GameObject'}
}
if($custom['7255314972244087111'] -notmatch 'magicDescriptionUI: \{fileID: 7255314972244087112\}'){throw 'Customization controller not connected'}
if($custom['7255314972244087111'] -notmatch 'manageSelectionPanel: 1' -or $custom['7255314972244087111'] -notmatch 'selectionPanel: \{fileID: 972526212568827029\}'){throw 'Customization detail visibility not connected'}
if($custom['972526212568827029'] -notmatch 'm_IsActive: 0' -or $swap['7905461114255029523'] -notmatch 'm_IsActive: 0'){throw 'Description panels must start hidden in both prefabs'}
# Inspect the effective Main scene instance, not just the source prefab. A removed
# component override can invalidate an otherwise-correct inherited controller reference.
$main=Blocks (Get-Content (Join-Path $repo 'Assets/Scenes/Main.unity') -Raw)
$mainInstance=$main['2689755113089230609']
$override=[regex]::Match($mainInstance,'(?m)^    - target: \{fileID: 7255314972244087111,[^\r\n]+\r?\n      propertyPath: magicDescriptionUI\r?\n      value:[^\r\n]*\r?\n      objectReference: \{fileID: (\d+)\}')
$removed=[regex]::Match($mainInstance,'(?ms)^    m_RemovedComponents:.*?(?=^    m_RemovedGameObjects:)').Value
if($override.Success){
 $viewId=$override.Groups[1].Value
 if(!$main.ContainsKey($viewId)){throw 'Main controller references a missing scene description component'}
 $mainView=$main[$viewId];$viewObjects=$main
 $ownerId=[regex]::Match($mainView,'m_GameObject: \{fileID: (\d+)\}').Groups[1].Value
 if($main[$ownerId] -notmatch 'm_CorrespondingSourceObject: \{fileID: 972526212568827029,'){throw 'Main description component must belong to Explanation, not its controller'}
 if($mainInstance -notmatch ('(?s)targetCorrespondingSourceObject: \{fileID: 972526212568827029,[^\r\n]+\r?\n      insertIndex: -?\d+\r?\n      addedObject: \{fileID: '+$viewId+'\}')){throw 'Main description component attachment does not match its GameObject'}
}else{
 $viewId=[regex]::Match($custom['7255314972244087111'],'magicDescriptionUI: \{fileID: (\d+)\}').Groups[1].Value
 if($removed.Contains('fileID: '+$viewId+',')){throw 'Main controller inherits a reference to a removed MagicDescriptionUI component'}
 $mainView=$custom[$viewId];$viewObjects=$custom
}
if($mainView -notmatch 'guid: bda78c6d48c34d5596f8a2b47e15a903'){throw 'Main preview reference is not a MagicDescriptionUI'}
foreach($field in @('magicIcon','magicNameText','descriptionText','categoryText','castTypeText','damageSlider','aimTimeSlider','rangeSlider','manaCostSlider','damageLabel','aimTimeLabel','rangeLabel','manaCostLabel')){
 $id=[regex]::Match($mainView,'(?m)^  '+$field+': \{fileID: (\d+)').Groups[1].Value
 if(!$viewObjects.ContainsKey($id)){throw "Unresolved Main scene preview field: $field"}
 $sourceId=[regex]::Match($viewObjects[$id],'m_CorrespondingSourceObject: \{fileID: (\d+), guid: c65b85f415ffdf64ab4962c17ee06dc6').Groups[1].Value
 if($sourceId -and (!$custom.ContainsKey($sourceId) -or $removed.Contains('fileID: '+$sourceId+','))){throw "Main scene preview field was removed: $field"}
}
if($mainInstance -notmatch '(?m)^    - target: \{fileID: 972526212568827029,[^\r\n]+\r?\n      propertyPath: m_IsActive\r?\n      value: 0'){throw 'Main scene Explanation must remain initially inactive'}
'Main scene override / live component / inactive Explanation bindings passed.'
$scene=Blocks (Get-Content (Join-Path $repo 'Assets/Scenes/Battle.unity') -Raw)
if($scene['1515047383'] -notmatch 'magicDescriptionUI: \{fileID: 7268131556410986592\}' -or $scene['7268131556410986592'] -notmatch 'm_CorrespondingSourceObject: \{fileID: 6101320497413326010, guid: 14ff792b49290734a87c242a17b34d51'){throw 'Round controller not connected to shared prefab component'}
if($round -notmatch '(?s)private void RefreshSwap.*?RefreshMagicDescription\(\)' -or $refresh -notmatch 'magicDescriptionUI.ShowMagic\(selectedMagic, magicTable, BattleManager.Instance != null \? BattleManager.Instance.Perks : null\)'){throw 'Round reset does not refresh shared description with match perk table'}
$swapView=$swap['6101320497413326010']
if($swapView -notmatch 'perkTable: \{fileID: 11400000, guid: 26a08712a9f8258cdae35dbe4cb1a8f4'){throw 'Intermission perk table not connected'}
foreach($binding in @(@{field='perk1DescriptionText';id='3477313122563535347'},@{field='perk2DescriptionText';id='6547261278373676641'})){
 if($swapView -notmatch ($binding.field+': \{fileID: '+$binding.id+'\}') -or $swap[$binding.id] -notmatch 'guid: f4688fdb7df04437aeb418b961361dc5'){throw "Unresolved perk text: $($binding.field)"}
}
if($view -match '\.RequestPerkSelection\(|\.EquipSelectedMagic\(|\.RequestMagicChange\('){throw 'Preview must not grant perks or equip magic'}
$buttonIds=@()
foreach($key in $swap.Keys) {
 $button=$swap[$key]
 if($button -notmatch 'm_MethodName: SelectMagic'){continue}
 $selected=[regex]::Match($button,'(?s)m_MethodName: SelectMagic.*?m_IntArgument: (\d+)').Groups[1].Value
 $preview=[regex]::Match($button,'(?s)m_Target: \{fileID: 6101320497413326010\}\s+m_TargetAssemblyTypeName: MagicDescriptionUI, Assembly-CSharp\s+m_MethodName: ShowMagic.*?m_IntArgument: (\d+)').Groups[1].Value
 if($preview -ne $selected){throw "Button selects $selected but previews $preview"}
 $buttonIds += [int]$selected
}
if(($buttonIds | Sort-Object -Unique) -join ',' -ne '1,2,3,4,5,6,7,8,9,10,11,12'){throw 'All twelve buttons need prefab-local table preview callbacks'}
if($view -match '\.onValueChanged|\.minValue\s*=|\.maxValue\s*=|\.value\s*='){throw 'Description renderer must not trigger value-change callbacks or rewrite authored ranges'}
'Main / intermission icon, text and Slider bindings passed; callback-safe rendering verified.'
