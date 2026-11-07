$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$source = Get-Content -Raw Assets/Script/login/DatabaseManager.ColorPalette.cs
$stubs = @'
namespace UnityEngine {
 public struct Color { public float r,g,b,a; public static Color white=>new Color(1,1,1); public Color(float r,float g,float b,float a=1) {this.r=r;this.g=g;this.b=b;this.a=a;} }
 public struct Color32 {
  public byte r,g,b,a;
  public static implicit operator Color32(Color c) => new Color32 {r=(byte)(System.Math.Clamp(c.r,0,1)*255),g=(byte)(System.Math.Clamp(c.g,0,1)*255),b=(byte)(System.Math.Clamp(c.b,0,1)*255),a=(byte)(System.Math.Clamp(c.a,0,1)*255)};
  public static implicit operator Color(Color32 c) => new Color(c.r/255f,c.g/255f,c.b/255f,c.a/255f);
 }
 public static class ColorUtility { public static string ToHtmlStringRGBA(Color c) {Color32 b=c;return $"{b.r:X2}{b.g:X2}{b.b:X2}{b.a:X2}";} }
}
namespace BackEnd {
 public class Where {}
 public class Param : System.Collections.Generic.Dictionary<string,object> {}
 public class Row { public int money=20000; public string inDate="row"; }
 public class Response {
  public bool ok=true;
  public Row row=new Row();
  public bool IsSuccess()=>ok;
  public System.Collections.Generic.List<Row> FlattenRows()=>new System.Collections.Generic.List<Row>{row};
 }
 public static class Backend {
  public static class GameData {
   public static System.Action<Response> read,write;
   public static int writes; public static Param last;
   public static void GetMyData(string table,Where where,System.Action<Response> callback) {read=callback;}
   public static void UpdateV2(string table,string row,string owner,Param param,System.Action<Response> callback) {writes++;last=param;write=callback;}
   public static void Reset() {read=null;write=null;last=null;writes=0;}
  }
 }
}
public static class DataConfig {
 public static int money=20000;
 public static UnityEngine.Color clothColor=new UnityEngine.Color(1,1,1);
 public static UnityEngine.Color hairColor=UnityEngine.Color.white,eyeColor=UnityEngine.Color.white,eyePaletteColor=new UnityEngine.Color(1,1,1,0);
 sealed class Batch:System.IDisposable {public void Dispose(){}}
 public static System.IDisposable BeginChangeBatch()=>new Batch();
}
public class EyeTexturePresetTable {
 public class Preset {public UnityEngine.Color savedColorKey;}
 public static EyeTexturePresetTable Default=new EyeTexturePresetTable();
 public bool TryGetPaletteShape(bool fourWhite,out Preset p) {p=new Preset{savedColorKey=fourWhite?new UnityEngine.Color(0,0,0):new UnityEngine.Color(0,0,1)};return true;}
}
public partial class DatabaseManager {
 const string TABLE_NAME="test";
 bool CanAutoSave=>IsDataConfigReady;
 public bool IsDataConfigReady=true,IsVoicePurchaseInProgress,claimingMission,IsAutoSavePending;
 public bool HasLoadedProfile=true,LastSaveSucceeded;
 public string LastError;
 int dataSessionVersion;
 string currentPlayerSettingInDate="row",loadedUserInDate="account";
 class Setting {public int money;public UnityEngine.Color clothColor,hairColor,eyeColor,eyePaletteColor;}
 Setting currentPlayerSetting=new Setting();
 System.Collections.Concurrent.ConcurrentQueue<System.Action> saveCompletions=new System.Collections.Concurrent.ConcurrentQueue<System.Action>();
 public static event System.Action DataConfigReadyChanged { add {} remove {} }
 // Invocable delegate mirrors the production event, without unrelated subscribers.
 static System.Action Ready = delegate {};
 static string ReadString(BackEnd.Row row,string key,string fallback)=>row.inDate;
 static Setting ReadPlayerSetting(BackEnd.Row row)=>new Setting {money=row.money};
 public void Pump() {while(saveCompletions.TryDequeue(out var callback))callback();}
 public void Logout() {dataSessionVersion++;IsPalettePurchaseInProgress=false;HasLoadedProfile=false;DataConfig.money=0;}
}
public static class PaletteChecks {
 static int checks;
 static void Check(bool value,string label) {if(!value)throw new System.Exception(label);checks++;}
 static DatabaseManager Fresh() {BackEnd.Backend.GameData.Reset();DataConfig.money=20000;DataConfig.clothColor=DataConfig.hairColor=DataConfig.eyeColor=UnityEngine.Color.white;DataConfig.eyePaletteColor=new UnityEngine.Color(1,1,1,0);return new DatabaseManager();}
 static UnityEngine.Color Red=>new UnityEngine.Color(1,0,0);
 static void Read(DatabaseManager db,int money=20000) {BackEnd.Backend.GameData.read(new BackEnd.Response {row=new BackEnd.Row{money=money}});db.Pump();}
 static void Write(DatabaseManager db,bool success=true) {BackEnd.Backend.GameData.write(new BackEnd.Response{ok=success});db.Pump();}
 public static int Run() {
  bool ok=false; string message="";
  var db=Fresh();db.PurchaseClothColor(Red,(s,m)=>{ok=s;message=m;});
  Check(db.IsPalettePurchaseInProgress,"lock held during read");
  Check(DataConfig.money==20000 && DataConfig.clothColor.g==1,"no speculative debit/color");
  bool duplicateAccepted=true;db.PurchaseClothColor(Red,(s,m)=>duplicateAccepted=s);
  Check(!duplicateAccepted,"double click rejected");Read(db);
  Check(BackEnd.Backend.GameData.writes==1,"one write");
  Check((int)BackEnd.Backend.GameData.last["money"]==15000 && (string)BackEnd.Backend.GameData.last["clothColor"]=="#FF0000FF","money and color same update");
  Check(DataConfig.money==20000,"wait for write acknowledgement");Write(db);
  Check(ok && DataConfig.money==15000 && DataConfig.clothColor.g==0 && !db.IsPalettePurchaseInProgress,"success commits and unlocks");
  db.PurchaseClothColor(Red,(s,m)=>ok=s);Check(!ok && BackEnd.Backend.GameData.writes==1,"same color never charges again");
  db=Fresh();db.PurchaseClothColor(Red,(s,m)=>ok=s);Read(db,4999);
  Check(!ok && !db.IsPalettePurchaseInProgress && BackEnd.Backend.GameData.writes==0 && DataConfig.money==20000,"insufficient server balance");
  db=Fresh();db.PurchaseClothColor(Red,(s,m)=>ok=s);Read(db);Write(db,false);
  Check(!ok && !db.IsDataConfigReady && !db.IsPalettePurchaseInProgress && DataConfig.money==20000,"uncertain write blocks stale autosave");
  db.PurchaseClothColor(Red,(s,m)=>ok=s);Check(BackEnd.Backend.GameData.writes==1,"no retry debit after uncertain response");
  db=Fresh();db.PurchaseClothColor(Red,(s,m)=>ok=s);Read(db);db.Logout();Write(db);
  Check(DataConfig.money==0,"late response never touches logged-out account");
  db=Fresh();db.IsAutoSavePending=true;db.PurchaseClothColor(Red,(s,m)=>ok=s);
  Check(!ok && BackEnd.Backend.GameData.read==null,"wait for pending autosave");
  db=Fresh();db.PurchaseClothColor(new UnityEngine.Color(float.NaN,0,0),(s,m)=>ok=s);
  Check(!ok && BackEnd.Backend.GameData.read==null,"reject invalid color");
  db=Fresh();db.PurchasePaletteColor(ColorPaletteTarget.Hair,Red,UnityEngine.Color.white,(s,m)=>ok=s);Read(db);Write(db);
  Check(ok && DataConfig.hairColor.g==0 && DataConfig.clothColor.g==1 && DataConfig.eyePaletteColor.a==0,"hair purchase changes only hair");
  Check(BackEnd.Backend.GameData.last.ContainsKey("hairColor") && !BackEnd.Backend.GameData.last.ContainsKey("clothColor"),"hair server column");
  db=Fresh();var fourWhite=new UnityEngine.Color(0,0,0);db.PurchasePaletteColor(ColorPaletteTarget.Eyes,Red,fourWhite,(s,m)=>ok=s);Read(db);Write(db);
  Check(ok && DataConfig.eyePaletteColor.g==0 && DataConfig.eyePaletteColor.a==1 && DataConfig.eyeColor.b==0 && DataConfig.hairColor.g==1 && DataConfig.clothColor.g==1,"eye tint and shape together, other parts unchanged");
  Check(BackEnd.Backend.GameData.last.Count==3 && BackEnd.Backend.GameData.last.ContainsKey("eyePaletteColor") && BackEnd.Backend.GameData.last.ContainsKey("eyeColor"),"money shape and tint in single update");
  var sparkle=new UnityEngine.Color(0,0,1);db.PurchasePaletteColor(ColorPaletteTarget.Eyes,Red,sparkle,(s,m)=>ok=s);Read(db,15000);Write(db);
  Check(ok && DataConfig.money==10000 && DataConfig.eyeColor.b==1,"shape-only change accepted for same tint");
  db=Fresh();db.PurchasePaletteColor(ColorPaletteTarget.Eyes,Red,new UnityEngine.Color(1,1,0),(s,m)=>ok=s);
  Check(!ok && BackEnd.Backend.GameData.read==null,"invalid eye shape rejected before charging");
  db=Fresh();db.PurchasePaletteColor((ColorPaletteTarget)99,Red,UnityEngine.Color.white,(s,m)=>ok=s);
  Check(!ok && BackEnd.Backend.GameData.read==null,"invalid target rejected");
  return checks;
 }
}
'@
# Replace the test-only event accessor with a delegate field so production can invoke it.
$stubs = $stubs.Replace('public static event System.Action DataConfigReadyChanged { add {} remove {} }', 'public static System.Action DataConfigReadyChanged = delegate {};')
Add-Type -TypeDefinition ($source + $stubs)
Write-Output "Cloth palette purchase checks passed: $([PaletteChecks]::Run())"
