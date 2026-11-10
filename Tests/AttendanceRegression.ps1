$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$source = "#pragma warning disable 0649`nusing System;`nusing System.Globalization;`nusing UnityEngine;`nusing BackEnd;`n"
foreach ($file in @('Assets/Script/AttendanceSchedule.cs','Assets/Script/login/DatabaseManager.Attendance.cs','Assets/Script/AttendanceLoginPrompt.cs')) {
    $source += (Get-Content -Raw $file) -replace '(?m)^using [^;]+;\r?\n',''
}
$stubs = @'
namespace UnityEngine {
 public class MonoBehaviour {}
 public class SerializeField:Attribute {}
 public class Transform {public void SetAsLastSibling(){}}
 public class GameObject {public Transform transform=new Transform();public bool active;public int opens;public void SetActive(bool value){active=value;if(value)opens++;}}
 public static class Time {public static double realtimeSinceStartupAsDouble;}
 public static class JsonUtility {
  static System.Text.Json.JsonSerializerOptions settings=new System.Text.Json.JsonSerializerOptions{IncludeFields=true};
  public static T FromJson<T>(string text)=>System.Text.Json.JsonSerializer.Deserialize<T>(text,settings);
  public static string ToJson(object value)=>System.Text.Json.JsonSerializer.Serialize(value,value.GetType(),settings);
 }
}
namespace BackEnd {
 public class Where {}
 public class Param:System.Collections.Generic.Dictionary<string,object>{}
 public class Row {public int money=20000;public string attendanceState="",inDate="row";}
 public class Response {
  public bool ok=true;public Row row=new Row();public string utc="2026-11-08T12:00:00Z";
  public bool IsSuccess()=>ok;
  public System.Collections.Generic.List<Row> FlattenRows()=>new System.Collections.Generic.List<Row>{row};
  public System.Collections.Generic.Dictionary<string,string> GetReturnValuetoJSON()=>new System.Collections.Generic.Dictionary<string,string>{{"utcTime",utc}};
 }
 public static class Backend {
  public static class GameData {
   public static Action<Response> read,write;public static Param last;public static int reads,writes;
   public static void GetMyData(string table,Where where,Action<Response> callback){reads++;read=callback;}
   public static void UpdateV2(string table,string row,string owner,Param p,Action<Response> callback){writes++;last=p;write=callback;}
   public static void Reset(){reads=writes=0;last=null;read=write=null;}
  }
  public static class Utils {public static Action<Response> clock;public static void GetServerTime(Action<Response> callback){clock=callback;}}
 }
}
public sealed class AttendanceRewardTable {
 public sealed class DayReward {public int money;}
 public static AttendanceRewardTable Default=new AttendanceRewardTable();
 public bool TryGet(int day,out DayReward reward){reward=new DayReward{money=new[]{100,150,200,250,300,400,1000}[day-1]};return true;}
}
public static class DataConfig {
 public static int money;
 public static event Action Changed;
 public static int catTutorialCompleted;
 public static void CompleteTutorial(){catTutorialCompleted=1;Changed?.Invoke();}
 public static void NotifyChanged()=>Changed?.Invoke();
 class Batch:IDisposable {public void Dispose(){}}
 public static IDisposable BeginChangeBatch(bool notify)=>new Batch();
}
public partial class DatabaseManager {
 public static DatabaseManager Instance;
 const string TABLE_NAME="test";
 public bool HasLoadedProfile=true,IsDataConfigReady=true,IsAutoSavePending,IsPalettePurchaseInProgress,IsVoicePurchaseInProgress,IsItemPurchaseInProgress,claimingMission,LastSaveSucceeded;
 bool CanAutoSave=>HasLoadedProfile && IsDataConfigReady;
 int dataSessionVersion;
 string loadedUserInDate="owner",currentPlayerSettingInDate="row";
 public string LastError;
 class Setting {public int money;}
 Setting currentPlayerSetting=new Setting();
 System.Collections.Concurrent.ConcurrentQueue<Action> saveCompletions=new System.Collections.Concurrent.ConcurrentQueue<Action>();
 public static Action DataConfigReadyChanged=delegate{};
 public DatabaseManager(){Instance=this;DataConfigReadyChanged=TryStartAttendance;}
 static string ReadString(Row r,string key,string fallback)=>key=="inDate"?r.inDate:r.attendanceState;
 static Setting ReadPlayerSetting(Row r)=>new Setting{money=r.money};
 public void Pump(){while(saveCompletions.TryDequeue(out var c))c();}
 public void Saved(){IsAutoSavePending=false;TryStartAttendance();}
 public void Logout(){dataSessionVersion++;HasLoadedProfile=false;ResetAttendance();DataConfig.money=0;}
}
public static class AttendanceChecks {
 static int checks;
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;}
 static DateTime Utc(string value)=>DateTimeOffset.Parse(value).UtcDateTime;
 static DatabaseManager Fresh(){Backend.GameData.Reset();Backend.Utils.clock=null;Time.realtimeSinceStartupAsDouble=100;DataConfig.money=20000;return new DatabaseManager();}
 static void Read(DatabaseManager db,string state="",int money=20000){Backend.GameData.read(new Response{row=new Row{attendanceState=state,money=money}});db.Pump();}
 static void Clock(DatabaseManager db,string time="2026-11-08T12:00:00Z",bool success=true){Backend.Utils.clock(new Response{ok=success,utc=time});db.Pump();}
 static void Write(DatabaseManager db,bool success=true){Backend.GameData.write(new Response{ok=success});db.Pump();}
 static void UiEvent(AttendanceLoginPrompt ui,string method)=>typeof(AttendanceLoginPrompt).GetMethod(method,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(ui,null);
 static AttendanceLoginPrompt Prompt(GameObject panel){
  var ui=new AttendanceLoginPrompt();typeof(AttendanceLoginPrompt).GetField("attendancePanel",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(ui,panel);
  UiEvent(ui,"OnEnable");return ui;
 }
 public static int Run(){
  Check(AttendanceSchedule.Day(Utc("2026-11-08T20:59:59Z"))==new DateTime(2026,11,8),"before KST06");
  Check(AttendanceSchedule.Day(Utc("2026-11-08T21:00:00Z"))==new DateTime(2026,11,9),"at KST06");
  Check(AttendanceSchedule.NextResetUtc(Utc("2026-11-08T12:00:00Z"))==Utc("2026-11-08T21:00:00Z"),"reset UTC");
  var state=new AttendanceState();var start=Utc("2026-11-08T12:00:00Z");
  for(int i=0;i<8;i++){
   Check(AttendanceSchedule.TryPlan(state,start.AddDays(i),out var next) && next.streakDay==i%7+1,"7 day streak then wrap "+i);
   state=next;Check(!AttendanceSchedule.TryPlan(state,start.AddDays(i),out _),"same day receipt "+i);
  }
  Check(AttendanceSchedule.TryPlan(state,start.AddDays(10),out var restart) && restart.streakDay==1,"missed days restart");
  Check(!AttendanceSchedule.TryPlan(state,start,out _),"future receipt blocks repeat");
  bool invalid=false;try{AttendanceSchedule.Parse("{\"version\":1,\"lastRewardDay\":\"bad\",\"streakDay\":3}");}catch{invalid=true;}
  Check(invalid,"malformed receipt rejected");
  Check(AttendanceSchedule.Parse("").streakDay==0 && AttendanceSchedule.Parse("{}").streakDay==0,"legacy empty state");
  var db=Fresh();db.RequestAttendanceCheck();db.RequestAttendanceCheck();
  Check(Backend.GameData.reads==1 && db.IsAttendanceInProgress,"one in-flight check");
  Read(db);Clock(db);
  Check(Backend.GameData.writes==0 && DataConfig.money==20000 && db.AttendanceStatusKnown && !db.AttendanceClaimedToday,"login only checks, never awards");
  Check(db.ConsumeAttendancePrompt() && !db.ConsumeAttendancePrompt(),"unclaimed login prompt exactly once");
  db.RequestAttendanceCheck();Check(Backend.GameData.reads==1,"opening panel uses cached status");
  db.ClaimAttendanceReward();db.ClaimAttendanceReward();
  Check(Backend.GameData.reads==2,"double click starts one claim");
  Read(db);Clock(db);Check(Backend.GameData.writes==1 && DataConfig.money==20000,"wait for commit");
  Check((int)Backend.GameData.last["money"]==20100 && Backend.GameData.last.Count==2,"money receipt same write");
  var receipt=(string)Backend.GameData.last["attendanceState"];
  Check(AttendanceSchedule.Parse(receipt).streakDay==1,"receipt day1");
  Write(db);Check(DataConfig.money==20100 && db.AttendanceClaimedToday && db.AttendanceCompletedDays==1 && !db.IsAttendanceInProgress,"commit reward and check mark");
  Check(db.ConsumeAttendanceNotice(out int day,out int money) && day==1 && money==100 && !db.ConsumeAttendanceNotice(out _,out _),"notice once");
  db.RequestAttendanceCheck();db.ClaimAttendanceReward();Check(Backend.GameData.reads==2,"claimed day ignores repeated clicks and panel openings");
  db=Fresh();DataConfig.money=20100;db.RequestAttendanceCheck();Read(db,receipt,20100);Clock(db);
  Check(Backend.GameData.writes==0 && db.AttendanceClaimedToday && db.AttendanceCompletedDays==1 && DataConfig.money==20100 && !db.ConsumeAttendancePrompt(),"relogin restores check without popup or award");
  db=Fresh();db.RequestAttendanceCheck();Read(db,receipt,20100);Clock(db,"2026-11-09T12:00:00Z");
  Check(db.AttendanceDay==2 && db.AttendanceCompletedDays==1 && !db.AttendanceClaimedToday && Backend.GameData.writes==0,"next day preserves previous check");
  db.ClaimAttendanceReward();Read(db,receipt,20100);Clock(db,"2026-11-09T12:00:00Z");Write(db);
  Check(db.AttendanceDay==2 && db.AttendanceCompletedDays==2 && DataConfig.money==20250,"next day server balance plus day2 reward");
  db=Fresh();db.RequestAttendanceCheck();Read(db,receipt);Clock(db,"2026-11-11T12:00:00Z");
  Check(db.AttendanceDay==1 && db.AttendanceCompletedDays==0,"missed day clears old cycle checks");
  db=Fresh();db.RequestAttendanceCheck();Read(db);Clock(db);db.ClaimAttendanceReward();Read(db);Clock(db);Write(db,false);
  Check(!db.IsDataConfigReady && DataConfig.money==20000 && !db.IsAttendanceInProgress,"uncertain write blocks stale overwrite");
  db=Fresh();db.RequestAttendanceCheck();Read(db);Clock(db,success:false);
  Check(db.IsDataConfigReady && Backend.GameData.writes==0 && !db.IsAttendanceInProgress && !db.ConsumeAttendancePrompt(),"clock failure never awards or prompts");
  db=Fresh();db.IsAutoSavePending=true;db.RequestAttendanceCheck();Check(Backend.GameData.reads==0,"wait for autosave");db.Saved();
  Check(Backend.GameData.reads==1,"resume on autosave completion event");
  Read(db);Clock(db);db.IsAutoSavePending=true;db.ClaimAttendanceReward();
  Check(Backend.GameData.reads==1,"claim waits for pending save");db.Saved();Read(db);Clock(db);db.Logout();Write(db);
  Check(DataConfig.money==0 && !db.AttendanceClaimedToday && db.AttendanceCompletedDays==0,"late response cannot affect next account");
  db=Fresh();db.RequestAttendanceCheck();Read(db);Clock(db);db.ClaimAttendanceReward();Read(db,receipt,20100);Clock(db);
  Check(Backend.GameData.writes==0 && db.AttendanceClaimedToday,"claim re-reads existing server receipt to prevent repeat");
  db=Fresh();DataConfig.catTutorialCompleted=0;
  var panel=new GameObject();var ui=Prompt(panel);
  db.RequestAttendanceCheck();Read(db);Clock(db);
  Check(!panel.active && panel.opens==0,"first login attendance waits for tutorial without consuming prompt");
  DataConfig.NotifyChanged();Check(!panel.active,"other profile changes do not open over tutorial");
  DataConfig.CompleteTutorial();Check(panel.active && panel.opens==1,"tutorial completion opens pending attendance immediately");
  panel.SetActive(false);DataConfig.NotifyChanged();Check(!panel.active && panel.opens==1,"closing attendance does not reopen it on profile edits");
  UiEvent(ui,"OnDisable");
  db=Fresh();DataConfig.catTutorialCompleted=0;panel=new GameObject();ui=Prompt(panel);
  DataConfig.CompleteTutorial();Check(!panel.active,"tutorial completion before server reply waits for attendance");
  db.RequestAttendanceCheck();Read(db);Clock(db);Check(panel.active && panel.opens==1,"late attendance reply opens after tutorial");
  UiEvent(ui,"OnDisable");
  db=Fresh();DataConfig.catTutorialCompleted=1;panel=new GameObject();ui=Prompt(panel);
  db.RequestAttendanceCheck();Read(db);Clock(db);Check(panel.active,"returning account keeps normal login prompt");
  UiEvent(ui,"OnDisable");
  db=Fresh();DataConfig.catTutorialCompleted=0;panel=new GameObject();ui=Prompt(panel);
  db.RequestAttendanceCheck();Read(db);Clock(db);UiEvent(ui,"OnDisable");DataConfig.CompleteTutorial();
  Check(!panel.active,"disabled prompt does not react to completion");
  UiEvent(ui,"OnEnable");Check(panel.active,"re-enabled prompt consumes deferred attendance once");UiEvent(ui,"OnDisable");
  db=Fresh();DataConfig.catTutorialCompleted=0;panel=new GameObject();ui=Prompt(panel);
  db.RequestAttendanceCheck();Read(db,receipt);Clock(db);DataConfig.CompleteTutorial();
  Check(!panel.active,"already claimed attendance stays closed after tutorial");UiEvent(ui,"OnDisable");
  return checks;
 }
}
'@
Add-Type -TypeDefinition ($source + $stubs)
Write-Output "Attendance checks passed: $([AttendanceChecks]::Run())"
