# Execute production logout flow with controlled save/network/backend failures.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$flow = Get-Content Assets/Script/login/LoginManager.AccountLogout.cs -Raw
$doubles = @'
using System;
using System.Threading;
using System.Threading.Tasks;
namespace UnityEngine {
 public static class Time {public static float realtimeSinceStartup,timeScale;}
 public enum CursorLockMode {None} public static class Cursor {public static CursorLockMode lockState;public static bool visible;}
 public static class Application {public static bool loadable=true;public static bool CanStreamedLevelBeLoaded(string path)=>loadable;}
 public static class Debug {public static void LogError(object x){}public static void LogException(Exception e){}}
}
namespace UnityEngine.SceneManagement {
 public enum LoadSceneMode {Single} public struct Scene {public string path;}
 public class Operation {public bool isDone=true;}
 public static class SceneManager {public static int loads;public static string loaded;public static Scene GetActiveScene()=>new(){path="Assets/Scenes/Main.unity"};public static Operation LoadSceneAsync(string p,LoadSceneMode m){loaded=p;loads++;return new();}}
}
namespace BackEnd {
 public class Result {public bool success=true;public bool IsSuccess()=>success;public string GetMessage()=>"offline";}
 public static class Backend {public static bool IsLogin=true;public static Member BMember=new();}
 public class Member {public int calls;public bool success=true;public Result Logout(){calls++;if(success)Backend.IsLogin=false;return new(){success=success};}}
}
public class DatabaseManager {public static DatabaseManager Instance;public bool IsAutoSavePending;public int clears;public void ClearSession(){clears++;}}
public class NetworkGameManager {public static NetworkGameManager Instance;public int disconnects,disposals;public TaskCompletionSource<bool> pending;public Task DisconnectForLogoutAsync(){disconnects++;return pending?.Task??Task.CompletedTask;}public void DisposeAfterLogout(){disposals++;}}
public class PlayerData {public static PlayerData Local;}
public static class TitleProgression {public static void Observe(PlayerData p,bool flush){}}
public class Event {public int calls;public void Invoke(){calls++;}}
public partial class LoginManager {
 public bool IsLoggingOut,IsLoggedIn=true,IsLoggingIn;public int loginAttempt;public string LoginStatus;public Event OnLoggedOut=new();
 private void SetLoginStatus(string s){LoginStatus=s;}
}
public static class LogoutChecks {
 static int checks;static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;}
 static void Wait(Func<bool> condition){var until=DateTime.UtcNow.AddSeconds(3);while(!condition()){if(DateTime.UtcNow>until)throw new Exception("test timeout");System.Threading.Thread.Sleep(5);}}
 static LoginManager New(){DatabaseManager.Instance=new();NetworkGameManager.Instance=new();BackEnd.Backend.IsLogin=true;BackEnd.Backend.BMember=new();UnityEngine.Application.loadable=true;UnityEngine.Time.realtimeSinceStartup=0;UnityEngine.SceneManagement.SceneManager.loads=0;return new();}
 public static int Run(){
  var m=New();m.LogoutAndReloadScene();Wait(()=>!m.IsLoggingOut);
  Check(!m.IsLoggedIn&&!BackEnd.Backend.IsLogin&&DatabaseManager.Instance.clears==1,"logout clears local and SDK session");
  Check(NetworkGameManager.Instance.disconnects==1&&NetworkGameManager.Instance.disposals==1,"disconnect and discard old UI manager");
  Check(UnityEngine.SceneManagement.SceneManager.loads==1&&UnityEngine.SceneManagement.SceneManager.loaded=="Assets/Scenes/Main.unity","reload exact current scene once");
  Check(m.OnLoggedOut.calls==1&&UnityEngine.Cursor.visible&&UnityEngine.Time.timeScale==1,"notify login UI and restore input");
  m=New();NetworkGameManager.Instance.pending=new();m.LogoutAndReloadScene();m.LogoutAndReloadScene();
  Check(m.IsLoggingOut&&NetworkGameManager.Instance.disconnects==1&&BackEnd.Backend.BMember.calls==0,"duplicate click ignored while shutdown pending");
  DatabaseManager.Instance.IsAutoSavePending=true;NetworkGameManager.Instance.pending.SetResult(true);Wait(()=>m.LoginStatus=="계정 정보 저장 중...");
  Check(BackEnd.Backend.BMember.calls==0&&DatabaseManager.Instance.clears==0,"pending save blocks logout");
  DatabaseManager.Instance.IsAutoSavePending=false;Wait(()=>!m.IsLoggingOut);Check(BackEnd.Backend.BMember.calls==1,"logout proceeds after save acknowledgement");
  m=New();DatabaseManager.Instance.IsAutoSavePending=true;m.LogoutAndReloadScene();Wait(()=>m.LoginStatus=="계정 정보 저장 중...");UnityEngine.Time.realtimeSinceStartup=21;Wait(()=>!m.IsLoggingOut);
  Check(m.IsLoggedIn&&DatabaseManager.Instance.clears==0&&UnityEngine.SceneManagement.SceneManager.loads==0,"save timeout preserves account and progress");
  m=New();BackEnd.Backend.BMember.success=false;m.LogoutAndReloadScene();Wait(()=>!m.IsLoggingOut);
  Check(m.IsLoggedIn&&DatabaseManager.Instance.clears==0&&UnityEngine.SceneManagement.SceneManager.loads==0,"backend failure does not discard session");
  m=New();UnityEngine.Application.loadable=false;m.LogoutAndReloadScene();Check(NetworkGameManager.Instance.disconnects==0&&m.IsLoggedIn,"invalid scene fails before destructive cleanup");
  return checks;
 }
}
'@
Add-Type -TypeDefinition ($flow + "`n" + ($doubles -replace '(?m)^using .*;\r?\n',''))
"Account logout checks passed: $([LogoutChecks]::Run())"

