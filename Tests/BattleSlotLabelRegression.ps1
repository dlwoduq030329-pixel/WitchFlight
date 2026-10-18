# Execute production intermission refresh, and verify the authored labels match their icons.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$source = Get-Content Assets/Script/BattleRoundUI.cs -Raw
function Method([string]$name) {
    $m = [regex]::Match($source, '(?ms)^    private (?:static )?[\w<>]+ ' + $name + '\([^{}]*?\)\r?\n    \{.*?^    \}')
    if (!$m.Success) { throw "Missing method: $name" }
    $m.Value -replace '\bprivate\b','public'
}
$code = @'
using System;
using System.Collections.Generic;
namespace SlotLabelChecks {
 public enum MagicType {None=0,Fire=1,Ice=2,Vision=3,Thunder=4,Healing=5,Binding=6,Dark=7,Curse=8,Mine=9,Razier=10,Flare=11,Smoke=12}
 public class TMP_Text {public string text="불꽃 마법";public bool richText=true;}
 public class Sprite {}
 public class Image {public Sprite sprite;public bool enabled;}
 public class Button {public bool interactable;}
 public static class Mathf {public static int CeilToInt(float x)=>(int)Math.Ceiling(x);}
 public class Entry {public string displayName;public Sprite icon=new();}
 public class MagicStatTable {
  public static MagicStatTable Default=new();public Dictionary<MagicType,Entry> Entries=new();
  public Entry GetStats(MagicType magic)=>Entries.TryGetValue(magic,out var e)?e:new Entry();
  public Sprite GetIcon(MagicType magic)=>magic==MagicType.None?null:GetStats(magic).icon;
 }
 public class PlayerData {public MagicType magic1=MagicType.Curse,magic2=MagicType.Razier;}
 public class BattleManager {public float PhaseRemainingSeconds=40;public int RoundNumber=1,AllowedMagicChanges=2;}
 public class Probe {
  public TMP_Text swapCountdownText=new(),roundScoreText=new(),selectedMagicNameText=new(),slot1Text=new(),slot2Text=new();
  public Image slot1Icon=new(),slot2Icon=new();public PlayerData localData=new();public Button equipButton=new();
  public bool HasLocalData=true,CanEdit=true,requestPending,showSlotNumberInText;public int selectedSlot=1;
  public MagicType selectedMagic;public MagicStatTable magicTable=MagicStatTable.Default;
  public void RefreshMagicDescription(){}public string Score(BattleManager battle)=>"0 : 0";
'@
foreach($name in @('RefreshSwap','SlotText','Name','Icon','Text')) { $code += Method $name }
$code += @'
 }
 public static class Tests {
  static int count;static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;}
  public static int Run(){
   var p=new Probe();var b=new BattleManager();
   foreach(MagicType magic in Enum.GetValues(typeof(MagicType)))p.magicTable.Entries[magic]=new(){displayName="테이블 이름 "+(int)magic};
   p.RefreshSwap(b);
   Check(p.slot1Text.text=="테이블 이름 8"&&p.slot2Text.text=="테이블 이름 10","both equipped names replace prefab placeholders independently");
   Check(p.slot1Icon.sprite==p.magicTable.GetIcon(MagicType.Curse)&&p.slot2Icon.sprite==p.magicTable.GetIcon(MagicType.Razier),"names and icons use same equipped IDs/table");
   p.selectedMagic=MagicType.Fire;p.selectedSlot=2;p.RefreshSwap(b);
   Check(p.slot1Text.text=="테이블 이름 8"&&p.slot2Text.text=="테이블 이름 10","candidate/slot click alone does not rename equipped spells");
   p.localData.magic2=MagicType.Binding;p.RefreshSwap(b);
   Check(p.slot2Text.text=="테이블 이름 6"&&p.slot2Icon.sprite==p.magicTable.GetIcon(MagicType.Binding),"replicated equip updates name and icon together");
   p.localData.magic1=MagicType.Fire;p.localData.magic2=MagicType.Ice;p.RefreshSwap(b);
   Check(p.slot1Text.text=="테이블 이름 1"&&p.slot2Text.text=="테이블 이름 2","reset loadout updates both labels");
   p.localData.magic1=MagicType.None;p.RefreshSwap(b);
   Check(p.slot1Text.text=="비어 있음"&&!p.slot1Icon.enabled,"empty slot is not mislabeled as fire");
   p.magicTable.Entries[MagicType.Ice].displayName="새 얼음 이름";p.RefreshSwap(b);
   Check(p.slot2Text.text=="새 얼음 이름","table display name remains authoritative");
   p.showSlotNumberInText=true;p.RefreshSwap(b);
   Check(p.slot2Text.text=="▶ 2번 · 새 얼음 이름"&&p.slot1Text.text=="1번 · 비어 있음","legacy/generated numbered selection style still available");
   p.slot1Text=p.slot2Text=null;p.RefreshSwap(b);Check(true,"optional labels remain null-safe");
   return count;
  }
 }
}
'@
Add-Type -TypeDefinition $code
"Battle slot label behavior checks passed: $([SlotLabelChecks.Tests]::Run())"

function Block([string]$yaml,[string]$id) {
    $m=[regex]::Match($yaml,'(?ms)^--- !u!\d+ &'+$id+'(?: stripped)?\r?\n.*?(?=^--- |\z)')
    if(!$m.Success){throw "Missing YAML object $id"};$m.Value
}
$scene=Get-Content Assets/Scenes/Battle.unity -Raw
$prefab=Get-Content Assets/UI/Prefab/MagicSelectUI.prefab -Raw
$round=Block $scene '1515047383'
if(!$round.Contains('showSlotNumberInText: 0')){throw 'Authored compact labels must show only magic names'}
foreach($slot in 1,2){
    $textId=[regex]::Match($round,"slot${slot}Text: \{fileID: (\d+)\}").Groups[1].Value
    $iconId=[regex]::Match($round,"slot${slot}Icon: \{fileID: (\d+)\}").Groups[1].Value
    $parents=@()
    foreach($id in $textId,$iconId){
        $ref=Block $scene $id
        if(!$ref.Contains('m_PrefabInstance: {fileID: 7268131556410986585}')){throw 'Slot label/icon reference points outside the swap panel'}
        $prefabId=[regex]::Match($ref,'m_CorrespondingSourceObject: \{fileID: (\d+), guid: 14ff792b49290734a87c242a17b34d51').Groups[1].Value
        $component=Block $prefab $prefabId
        if($id -eq $textId -and !$component.Contains('guid: f4688fdb7df04437aeb418b961361dc5')){throw 'Slot name must reference a TMP component'}
        $goId=[regex]::Match($component,'m_GameObject: \{fileID: (\d+)\}').Groups[1].Value
        $go=Block $prefab $goId
        $transformId=[regex]::Match($go,'component: \{fileID: (\d+)\}').Groups[1].Value
        $parents += [regex]::Match((Block $prefab $transformId),'m_Father: \{fileID: (\d+)\}').Groups[1].Value
    }
    if($parents[0] -ne $parents[1]){throw "Slot $slot label and icon belong to different buttons"}
    $buttonId=[regex]::Match((Block $prefab $parents[0]),'m_GameObject: \{fileID: (\d+)\}').Groups[1].Value
    if(!(Block $prefab $buttonId).Contains("m_Name: CurrentMagic0${slot}Button")){throw "Slot $slot points to wrong button"}
}
$ids=[regex]::Matches($scene,'(?m)^--- !u!\d+ &(\d+)') | ForEach-Object {$_.Groups[1].Value}
if(@($ids | Group-Object | Where-Object Count -gt 1).Count){throw 'Duplicate scene object IDs'}
'Both saved TMP bindings match the equipped slot icons and have unique scene IDs.'
