# Production purchase/ownership/loadout/card methods, with an in-memory backend.
# No live currency is spent; this does not replace testing the actual BackEnd service.
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
function Extract($file,$name) {
 $s=Get-Content $file -Raw
 $m=[regex]::Match($s,'(?ms)^    (?:public|private) (?:static )?[^\r\n]+ '+$name+'\([^\r\n]*\)\r?\n    \{.*?^    \}')
 if(!$m.Success){throw "Missing $name"};$m.Value
}
$source=@'
using System;
using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;
using BackEnd;
namespace UnityEngine {
 public class Sprite {}
 public static class JsonUtility {public static string ToJson(object o)=>JsonSerializer.Serialize(o,o.GetType(),Options);public static JsonSerializerOptions Options=new(){IncludeFields=true};}
 public static class Debug {public static void LogWarning(string s){}}
}
public enum ItemCategory {Hair,Animation,Emote,Voice}
public enum EmoteDirection {Up,Right,Down,Left}
public class ItemDefinition {public string id,displayName;public ItemCategory category;public int emoteId,price;public Sprite icon;}
public class EmoteEntry {public Sprite icon=new();}
public class EmoteTable {public static EmoteTable Default=new();public bool TryGet(int id,out EmoteEntry e){e=new();return id>=1&&id<=8;}}
public class ItemCatalog {
 public static ItemCatalog Default=new();public List<ItemDefinition> Items=new();
 public bool TryGet(string id,out ItemDefinition item){item=Items.Find(x=>x.id==id);return item!=null&&item.price>=0;}
}
public class Inventory {public int version=1;public List<string> ownedItemIds=new();public List<string> equippedItems=new(){"voice:3.2"};}
public static class ItemInventory {
 public static Inventory Parse(string s)=>JsonSerializer.Deserialize<Inventory>(s,JsonUtility.Options);
 public static bool HasItem(string id)=>Parse(DataConfig.inventory).ownedItemIds.Contains(id);
'@
$source+=Extract 'Assets/Script/ItemInventory.cs' 'OwnsEmote'
$source+=@'
}
public static class DataConfig {
 public static string inventory=JsonUtility.ToJson(new Inventory());public static int money;
 public static int emoteUpId=1,emoteRightId=2,emoteDownId=3,emoteLeftId=4;
 public static event Action InventoryChanged,SnapshotApplied;
 public static void Notify(){InventoryChanged?.Invoke();SnapshotApplied?.Invoke();}
'@
$source+=Extract 'Assets/Script/DataConfig.cs' 'GetEmote'
$source+=Extract 'Assets/Script/DataConfig.cs' 'SetEmote'
$source+=@'
}
namespace BackEnd {
 public class Where {}
 public class Param:Dictionary<string,object>{}
 public class Row {public string id="row";public int money=4000;public string itemInventoryJson=JsonUtility.ToJson(new Inventory());}
 public class Rows:List<Row>{public bool IsArray=>true;}
 public class Result {public bool ok=true;public bool IsSuccess()=>ok;public string GetMessage()=>"simulated error";public Rows FlattenRows()=>new(){Backend.GameData.Server};}
 public static class Backend {public static Service GameData=new();}
 public class Service {
  public Row Server=new();public int Writes;public bool ReadFail,WriteFail,LostResponse;
  public Result GetMyData(string t,Where w)=>new(){ok=!ReadFail};
  public Result UpdateV2(string t,string row,string user,Param p){Writes++;if(!WriteFail){Server.money=(int)p["money"];Server.itemInventoryJson=(string)p["itemInventoryJson"];}return new(){ok=!WriteFail&&!LostResponse};}
 }
}
public partial class DatabaseManager {
 public bool IsVoicePurchaseInProgress,claimingMission,IsPalettePurchaseInProgress,IsAttendanceInProgress,IsAutoSavePending;
 public bool IsDataConfigReady=true,LastSaveSucceeded;public string LastError;
 private bool CanAutoSave=>IsDataConfigReady;
 const string TABLE_NAME="account";string currentPlayerSettingInDate="row",loadedUserInDate="user";
 static string ReadString(Row row,string k,string fallback)=>row.id;
 Row ReadPlayerSetting(Row r)=>new(){id=r.id,money=r.money,itemInventoryJson=r.itemInventoryJson};
 void ApplyEconomySnapshot(int money,string json){DataConfig.money=money;DataConfig.inventory=json;DataConfig.Notify();}
 void TryStartAttendance(){}
}
'@
$source+=(Get-Content Assets/Script/login/DatabaseManager.EmoteShop.cs -Raw) -replace '(?m)^using [^;]+;\r?\n',''
$source+=@'
public static class EmoteShopUI {
'@
$source+=Extract 'Assets/Script/EmoteShopUI.cs' 'TryGetItem'
$source+=@'
 public static Sprite Icon(ItemDefinition item)=>item.icon??new Sprite();
}
public class Text {public string text;}
public class Image {public Sprite sprite;public bool enabled;}
public class GameObject {public bool active;public void SetActive(bool b){active=b;}}
public class Button {public bool interactable;}
public class Card {
 public string itemId="2.1";public Image itemIcon=new();public Text itemName=new(),priceText=new();public GameObject ownedCheck=new();public Button button=new();
'@
$source+=Extract 'Assets/Script/EmoteShopItemButton.cs' 'Refresh'
$source+=(Extract 'Assets/Script/EmoteShopItemButton.cs' 'OnEnable').Replace('private void','public void')
$source+=(Extract 'Assets/Script/EmoteShopItemButton.cs' 'OnDisable').Replace('private void','public void')
$source+=@'
}
public static class EmoteShopChecks {
 static int n;static void Check(bool b,string s){if(!b)throw new Exception(s);n++;}
 static DatabaseManager Reset(){Backend.GameData=new();DataConfig.inventory=JsonUtility.ToJson(new Inventory());DataConfig.money=4000;return new();}
 public static int Run(){
  ItemCatalog.Default.Items=new(){new(){id="2.1",category=ItemCategory.Emote,emoteId=6,price=2000,displayName="PeroPero"},new(){id="2.2",category=ItemCategory.Emote,emoteId=7,price=2000},new(){id="voice",category=ItemCategory.Voice},new(){id="free",category=ItemCategory.Emote,emoteId=1,price=2000}};
  var db=Reset();for(int i=1;i<=5;i++)Check(ItemInventory.OwnsEmote(i),"basic emote missing "+i);
  Check(!ItemInventory.OwnsEmote(6)&&!DataConfig.SetEmote(0,6),"unowned paid emote must not equip");
  DataConfig.emoteUpId=6;Check(DataConfig.GetEmote(0)==1,"legacy unpaid slot must fall back");
  var card=new Card();card.OnEnable();Check(card.button.interactable&&!card.ownedCheck.active&&card.priceText.text!="구매완료"&&card.itemIcon.enabled,"unowned product card");
  Check(db.TryPurchaseEmote("2.1",out _),"purchase failed");
  Check(DataConfig.money==2000&&Backend.GameData.Server.money==2000&&Backend.GameData.Writes==1,"money and ownership require one write");
  Check(ItemInventory.OwnsEmote(6)&&DataConfig.SetEmote(0,6)&&DataConfig.GetEmote(0)==6,"paid emote must equip immediately");
  Check(card.priceText.text=="구매완료"&&card.ownedCheck.active&&!card.button.interactable,"inventory event must refresh card immediately");
  Check(ItemInventory.Parse(Backend.GameData.Server.itemInventoryJson).equippedItems.Contains("voice:3.2"),"purchase changed existing equipment");
  var nextCard=new Card(){itemId="2.2"};nextCard.OnEnable();
  Check(nextCard.button.interactable&&!ItemInventory.OwnsEmote(7),"first purchase must not disable another product");
  Check(db.TryPurchaseEmote("2.2",out _)&&DataConfig.money==0&&Backend.GameData.Writes==2,"second different purchase must succeed without reopening the store");
  Check(ItemInventory.OwnsEmote(6)&&ItemInventory.OwnsEmote(7)&&nextCard.priceText.text=="구매완료","second purchase must preserve first ownership and refresh its own card");
  Check(DataConfig.SetEmote(1,7)&&DataConfig.GetEmote(1)==7,"second paid emote must equip immediately");
  nextCard.OnDisable();card.OnDisable();
  db=Reset();db.TryPurchaseEmote("2.1",out _);
  Check(!db.TryPurchaseEmote("2.1",out _)&&Backend.GameData.Writes==1,"duplicate purchase charged twice");card.OnDisable();
  DataConfig.inventory=JsonUtility.ToJson(new Inventory());db.TryPurchaseEmote("2.1",out _);Check(ItemInventory.OwnsEmote(6)&&DataConfig.money==2000&&Backend.GameData.Writes==1,"server-owned item reload must not charge again");
  db=Reset();Backend.GameData.Server.money=100;Check(!db.TryPurchaseEmote("2.1",out _)&&Backend.GameData.Writes==0&&!ItemInventory.OwnsEmote(6),"insufficient funds must not grant");
  db=Reset();Check(!db.TryPurchaseEmote("free",out _)&&!db.TryPurchaseEmote("voice",out _)&&!db.TryPurchaseEmote("missing",out _)&&Backend.GameData.Writes==0,"invalid/default categories must not charge");
  db=Reset();db.IsAutoSavePending=true;Check(!db.TryPurchaseEmote("2.1",out _),"pending save guard");
  db=Reset();db.IsVoicePurchaseInProgress=true;Check(!db.TryPurchaseEmote("2.1",out _),"shared purchase guard");
  db=Reset();Backend.GameData.ReadFail=true;Check(!db.TryPurchaseEmote("2.1",out _)&&!db.IsEmotePurchaseInProgress&&Backend.GameData.Writes==0,"failed read must release guard without spending");
  db=Reset();Backend.GameData.WriteFail=true;Check(!db.TryPurchaseEmote("2.1",out _)&&!db.IsDataConfigReady&&!ItemInventory.OwnsEmote(6)&&DataConfig.money==4000,"failed write must not grant locally");
  db=Reset();Backend.GameData.LostResponse=true;Check(!db.TryPurchaseEmote("2.1",out _)&&!db.IsDataConfigReady,"uncertain commit must block stale saves");
  Backend.GameData.LostResponse=false;db.IsDataConfigReady=true;Check(!db.TryPurchaseEmote("2.1",out _)&&Backend.GameData.Writes==1&&ItemInventory.OwnsEmote(6)&&DataConfig.money==2000,"retry after lost response must recover without double charge");
  return n;
 }
}
'@
Add-Type -TypeDefinition $source
"Emote purchase, ownership, loadout and UI event checks passed: $([EmoteShopChecks]::Run())"
