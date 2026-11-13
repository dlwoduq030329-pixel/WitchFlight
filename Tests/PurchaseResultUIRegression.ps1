# Runs the production confirmation handlers with fake transaction results; no live currency spent.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
function Method($path) {
    $text = Get-Content $path -Raw
    $match = [regex]::Match($text, '(?ms)^    public void ConfirmPurchase\(\).*?^    \}')
    if (!$match.Success) { throw "Missing confirmation handler: $path" }
    $match.Value
}
$source = @'
using System;
public class Panel { public bool activeInHierarchy=true; public void SetActive(bool value){activeInHierarchy=value;} }
public class Label { public string text; }
public class Event { public int Calls; public void Invoke(){Calls++;} }
public class Tween { public int Calls; public void DORestart(){Calls++;} }
public static class ItemInventory { public static bool Owned; public static bool HasItem(string id)=>Owned; public static bool TryEquipItem(string id)=>Owned; }
public class DatabaseManager {
 public static DatabaseManager Instance=new(); public bool Success; public int Calls;
 public bool TryPurchaseVoice(string id,out string message){Calls++;message=Success?"OK":"재화가 부족합니다.";return Success;}
 public bool TryPurchaseEmote(string id,out string message)=>TryPurchaseVoice(id,out message);
}
public class Voice {
 public bool purchasing,pendingEquip; public string pendingPurchaseId="voice",selectedItemId="voice";
 public Panel purchaseCheck=new(); public Label confirmationDialog=new(); public Event onPurchaseSucceeded=new(),onPurchaseFailed=new();
 public void SetStatus(string s){} public void Refresh(){} public void CancelPurchase(){pendingPurchaseId=null;purchaseCheck.SetActive(false);}
'@
$source += Method 'Assets/Script/VoiceShopUI.cs'
$source += @'
}
public class Emote {
 public static Emote active; public bool purchasing; public string pendingId="emote";
 public Panel purchaseCheck=new(); public Label dialog=new(); public Tween confirmationTween=new(); public Event onPurchaseSucceeded=new();
 public Emote(){active=this;} public void CancelPurchase(){pendingId=null;active=null;purchaseCheck.SetActive(false);}
'@
$source += Method 'Assets/Script/EmoteShopUI.cs'
$source += @'
}
public static class Checks {
 static int count; static void Check(bool ok,string message){if(!ok)throw new Exception(message);count++;}
 public static int Run(){
  DatabaseManager.Instance=new(); ItemInventory.Owned=false;
  var v=new Voice();v.ConfirmPurchase();
  Check(v.onPurchaseSucceeded.Calls==0 && v.onPurchaseFailed.Calls==1,"Voice failure must never show success");
  Check(v.purchaseCheck.activeInHierarchy && v.confirmationDialog.text=="재화가 부족합니다.","Voice failure is visible in confirmation");
  DatabaseManager.Instance.Success=true;v.ConfirmPurchase();v.ConfirmPurchase();
  Check(v.onPurchaseSucceeded.Calls==1 && !v.purchaseCheck.activeInHierarchy,"Voice success shown once after purchase");
  ItemInventory.Owned=true;v=new Voice();v.ConfirmPurchase();
  Check(v.onPurchaseSucceeded.Calls==0,"Equip does not pretend to purchase");ItemInventory.Owned=false;
  DatabaseManager.Instance.Success=false;var e=new Emote();e.ConfirmPurchase();
  Check(e.onPurchaseSucceeded.Calls==0,"Emote failure must never show success");
  Check(e.purchaseCheck.activeInHierarchy && e.dialog.text=="재화가 부족합니다.","Emote failure is visible in confirmation");
  DatabaseManager.Instance.Success=true;e.ConfirmPurchase();e.ConfirmPurchase();
  Check(e.onPurchaseSucceeded.Calls==1 && !e.purchaseCheck.activeInHierarchy,"Emote success shown once after purchase");
  DatabaseManager.Instance=null;e=new Emote();e.ConfirmPurchase();
  Check(e.onPurchaseSucceeded.Calls==0,"Missing login must never show success");
  return count;
 }
}
'@
Add-Type -TypeDefinition $source
"Purchase result UI checks passed: $([Checks]::Run())"

$scene = Get-Content Assets/Scenes/Main.unity -Raw
$confirm = [regex]::Matches($scene, '(?m)^    - target: \{fileID: 6415862287094986039, guid: 70b54e6e19823c24bbe5ca6d2f7792af, type: 3\}\r?\n      propertyPath: m_OnClick[^\r\n]*\r?\n      value: ([^\r\n]*)')
if ($confirm.Count -ne 1 -or $confirm[0].Groups[1].Value -ne '0') { throw 'Purchase confirmation still has unconditional popup events' }
foreach ($id in @('911121230000000001','911121230000000002')) {
    if ($scene -notmatch "--- !u!114 &$id\r?\n" -or $scene -notmatch "m_Target: \{fileID: $id\}\r?\n        m_TargetAssemblyTypeName: MainTrainingButton, Assembly-CSharp") {
        throw "Training button bridge missing: $id"
    }
}
'Scene bindings passed: confirmation has no unconditional success, both training buttons use scene-local bridges.'
