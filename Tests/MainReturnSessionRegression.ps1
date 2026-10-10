# Exercise the real scene coordinator with offline session/UI test doubles.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$flow = (Get-Content Assets/Script/login/LoginFlowController.cs -Raw) -replace '\bprivate\b', 'public'
$flow = $flow.Replace('get; public set;', 'get; set;')
$stubs = @'
using System;
namespace UnityEngine {
 public class MonoBehaviour {public static T FindFirstObjectByType<T>() where T:class=>null;}
 public class SerializeField:Attribute {} public class HeaderAttribute:Attribute {public HeaderAttribute(string s){}}
 public class GameObject {
  public bool activeSelf=true;public object Child;
  public void SetActive(bool v){activeSelf=v;}
  public T GetComponentInChildren<T>(bool b) where T:class=>Child as T;
 }
 public static class Debug {public static void LogException(Exception e){} public static void LogError(string s){}}
}
namespace UnityEngine.Events {
 public class UnityEvent {public event Action Event;public int Count=>Event?.GetInvocationList().Length??0;
  public void AddListener(Action a){Event+=a;} public void RemoveListener(Action a){Event-=a;}public void Invoke(){Event?.Invoke();}}
 public class UnityEvent<T> {public event Action<T> Event;public void AddListener(Action<T> a){Event+=a;}public void RemoveListener(Action<T> a){Event-=a;}public void Invoke(T t){Event?.Invoke(t);}}
}
namespace TMPro {public class TMP_Text {public string text;}}
namespace ReturnChecks {
 public class LoginManager {
  public static LoginManager Instance;public bool IsLoggedIn;
  public UnityEngine.Events.UnityEvent OnLoginSuccess=new(),OnLoginStarted=new(),OnLoggedOut=new();
  public UnityEngine.Events.UnityEvent<string> OnLoginFailed=new();
 }
 public class DatabaseManager {
  public static DatabaseManager Instance;public bool HasLoadedProfile,IsDataConfigReady,LoadResult=true;
  public int LoadCount,SuspendCount;public string LastError="load failed";
  public bool TryLoadPlayerSetting(bool apply){LoadCount++;HasLoadedProfile=LoadResult;return LoadResult;}
  public void SuspendLobbyInitialization(){IsDataConfigReady=false;SuspendCount++;}
 }
 public class LonginLink {public UnityEngine.GameObject gameObject=new();}
 public class LobbyPlayerInitializer {
  public int ApplyCount,RefreshCount;public bool ApplyResult=true;
  public bool InitializeFromLoadedData(){ApplyCount++;DatabaseManager.Instance.IsDataConfigReady=ApplyResult;return ApplyResult;}
  public void RefreshFromDataConfig(){RefreshCount++;}
 }
}
'@
$tests = @'
namespace ReturnChecks {
public static class Tests {
 static int count;static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;}
 static LoginFlowController View(){
  var f=new LoginFlowController{loginManager=new(),databaseManager=new(),introRoot=new(),loginPanel=new(),lobbyRoot=new(),lobbyInitializer=new(),errorText=new(),errorPanel=new()};
  f.introRoot.Child=new LonginLink();f.Awake();return f;
 }
 static void Session(bool authenticated,bool loaded,bool ready){LoginManager.Instance=new(){IsLoggedIn=authenticated};DatabaseManager.Instance=new(){HasLoadedProfile=loaded,IsDataConfigReady=ready};}
 public static int Run(){
  Session(false,false,false);var f=View();f.OnEnable();f.Start();
  Check(!f.IsLobbyReady&&f.introRoot.activeSelf&&!f.lobbyRoot.activeSelf,"fresh launch shows login");
  Check(DatabaseManager.Instance.LoadCount==0,"anonymous session does not fetch profile");
  LoginManager.Instance.IsLoggedIn=true;LoginManager.Instance.OnLoginSuccess.Invoke();
  Check(f.IsLobbyReady&&!f.introRoot.activeSelf&&f.lobbyRoot.activeSelf,"successful login opens initialized lobby");
  Check(DatabaseManager.Instance.LoadCount==1&&f.lobbyInitializer.ApplyCount==1,"first login loads and applies once");
  f.OnDisable();Check(LoginManager.Instance.OnLoginSuccess.Count==0,"old Main listeners removed before Battle");
  var returning=View();var duplicate=returning.loginManager;returning.OnEnable();returning.Start();
  Check(returning.loginManager==LoginManager.Instance&&returning.databaseManager==DatabaseManager.Instance,"discard duplicate scene manager references");
  Check(returning.IsLobbyReady&&!returning.introRoot.activeSelf,"return goes directly to authenticated lobby");
  Check(DatabaseManager.Instance.LoadCount==1&&returning.lobbyInitializer.ApplyCount==0&&returning.lobbyInitializer.RefreshCount==1,"return preserves current DataConfig and pending saves without reloading");
  Check(duplicate.OnLoginSuccess.Count==0&&LoginManager.Instance.OnLoginSuccess.Count==1,"bind only live persistent manager");
  returning.OnEnable();Check(LoginManager.Instance.OnLoginSuccess.Count==1,"duplicate enable does not duplicate listeners");
  returning.ContinueAfterLogin();Check(returning.lobbyInitializer.RefreshCount==1,"lobby initialization idempotent");
  LoginManager.Instance.IsLoggedIn=false;LoginManager.Instance.OnLoggedOut.Invoke();
  Check(!returning.IsLobbyReady&&returning.introRoot.activeSelf&&!returning.lobbyRoot.activeSelf,"explicit logout still shows login");returning.OnDisable();
  Session(true,true,false);f=View();f.OnEnable();f.Start();
  Check(f.IsLobbyReady&&DatabaseManager.Instance.LoadCount==0&&f.lobbyInitializer.ApplyCount==1,"loaded but not yet applied profile is initialized");f.OnDisable();
  Session(true,false,false);DatabaseManager.Instance.LoadResult=false;f=View();f.OnEnable();f.Start();
  Check(!f.IsLobbyReady&&f.errorPanel.activeSelf&&!f.lobbyRoot.activeSelf,"failed profile read never exposes stale lobby");
  DatabaseManager.Instance.LoadResult=true;f.ContinueAfterLogin();Check(f.IsLobbyReady,"retry can finish without reauthentication");f.OnDisable();
  Session(true,true,true);f=View();f.OnEnable();
  var old=LoginManager.Instance;Session(true,true,true);f.Start();
  Check(f.IsLobbyReady&&f.loginManager==LoginManager.Instance&&old.OnLoginSuccess.Count==0,"Start re-resolves managers after Awake ordering");
  f.OnDisable();Check(LoginManager.Instance.OnLoginSuccess.Count==0,"cleanup removes return listeners");
  return count;
 }
}}
'@
Add-Type -TypeDefinition ($stubs + "`nnamespace ReturnChecks {`n" + $flow + "`n}`n" + $tests)
"Main return/session behavior checks passed: $([ReturnChecks.Tests]::Run())"
$network = Get-Content Assets/Script/NetworkGameManager.cs -Raw
$returnMethod = [regex]::Match($network,'(?ms)^    public async Task<bool> ReturnToMainMenuAsync\(\).*?^    \}').Value
if (!$returnMethod.Contains('await ShutdownSession(_runner);') -or $returnMethod -match 'Logout|ClearSession|ResetToDefaults') {throw 'Return must stop Fusion without logging out or clearing player settings'}
'Return-to-Main preserves authentication and shuts down only the game session.'
