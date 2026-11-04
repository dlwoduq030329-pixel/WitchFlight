# Production LoginManager against a controllable SDK: slow startup, missing and late callbacks.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$manager = (Get-Content Assets/Script/login/LoginManager.cs -Raw) -replace '\bprivate\b','public'
$manager = $manager.Replace('get; public set;', 'get; set;')
$stub = @'
using System;
using System.Collections.Generic;
namespace UnityEngine {
 public class MonoBehaviour {public object gameObject=new();public static void Destroy(object o){}public static void DontDestroyOnLoad(object o){}}
 public class SerializeField:Attribute{} public class HeaderAttribute:Attribute{public HeaderAttribute(string s){}}public class MinAttribute:Attribute{public MinAttribute(float v){}}
 public static class Time {public static float realtimeSinceStartup;}
 public static class Mathf {public static float Max(float a,float b)=>Math.Max(a,b);}
 public static class Debug {public static void Log(object o){}public static void LogError(object o){}public static void LogException(Exception e){}}
}
namespace UnityEngine.UI {} namespace TMPro {}
namespace UnityEngine.Events {
 public class UnityEvent {Action handlers;public void AddListener(Action a){handlers+=a;}public void Invoke(){handlers?.Invoke();}}
 public class UnityEvent<T> {Action<T> handlers;public void AddListener(Action<T> a){handlers+=a;}public void Invoke(T value){handlers?.Invoke(value);}}
}
namespace BackEnd {
 public class Result {public bool success;public bool IsSuccess()=>success;public string GetMessage()=>"request failed";}
 public static class Backend {
  public static bool IsInitialized,ThrowInitialize;public static List<Action<Result>> Inits=new();public static Member BMember=new();
  public static void InitializeAsync(Action<Result> action){if(ThrowInitialize)throw new Exception("init exception");Inits.Add(action);}
  public static void Reset(){IsInitialized=false;ThrowInitialize=false;Inits.Clear();BMember=new();UnityEngine.Time.realtimeSinceStartup=0;}
 }
 public class Member {
  public bool ThrowLogin;public List<Action<Result>> Logins=new();
  public void CustomLogin(string id,string password,Action<Result> action){if(ThrowLogin)throw new Exception("login exception");Logins.Add(action);}
  public void CustomSignUp(string id,string password,Action<Result> action){}
  public void Logout(){}
 }
}
namespace LoginChecks {public class DatabaseManager {public static DatabaseManager Instance;public void ClearSession(){}}}
'@
$tests = @'
namespace LoginChecks {
public static class Tests {
 static int n,successes,failures;static void Check(bool ok,string msg){if(!ok)throw new Exception(msg);n++;}
 static LoginManager New(){BackEnd.Backend.Reset();successes=failures=0;var m=new LoginManager();m.OnLoginSuccess=new();m.OnLoginFailed=new();m.OnLoginSuccess.AddListener(()=>successes++);m.OnLoginFailed.AddListener(_=>failures++);return m;}
 static void Init(LoginManager m,bool success,int index=0){BackEnd.Backend.IsInitialized=success;BackEnd.Backend.Inits[index](new(){success=success});m.Update();}
 static void Auth(LoginManager m,bool success,int index=0){BackEnd.Backend.BMember.Logins[index](new(){success=success});m.Update();}
 public static int Run(){
  var m=New();m.Start();m.Login("test-account","test-password");
  Check(m.IsLoggingIn&&m.IsInitializing,"early click remains pending");
  Check(BackEnd.Backend.Inits.Count==1&&BackEnd.Backend.BMember.Logins.Count==0,"startup/login share one initialization");
  m.Login("another","another");Check(BackEnd.Backend.Inits.Count==1,"repeated clicks coalesced");
  Init(m,true);Check(BackEnd.Backend.BMember.Logins.Count==1&&m.LoginStatus=="로그인 중...","pending login automatically submitted");
  BackEnd.Backend.BMember.Logins[0](new(){success=true});Check(!m.IsLoggedIn&&m.IsLoggingIn,"SDK callback waits for main thread");
  m.Update();Check(m.IsLoggedIn&&!m.IsLoggingIn&&successes==1,"success opens flow exactly once");
  m.Login("test","test");Check(successes==2&&BackEnd.Backend.BMember.Logins.Count==1,"profile failure retry does not authenticate twice");
  m=New();m.Login("", "");Check(failures==1&&!m.IsLoggingIn&&BackEnd.Backend.Inits.Count==0,"invalid input fails visibly before initialization");
  m=New();m.Start();Init(m,false);m.Login("test","test");Check(BackEnd.Backend.Inits.Count==2,"startup failure can retry on login click");
  Init(m,true,1);Auth(m,true);Check(m.IsLoggedIn,"initialization retry completes login");
  m=New();m.Login("test","test");Init(m,false);Check(!m.IsLoggingIn&&failures==1&&m.LoginStatus.Length>0,"initialization failure releases busy state and displays error");
  m=New();m.Login("test","test");UnityEngine.Time.realtimeSinceStartup=21;m.Update();Check(!m.IsLoggingIn&&!m.IsInitializing&&failures==1,"initialization watchdog unlocks UI");
  Init(m,true);Check(BackEnd.Backend.BMember.Logins.Count==0,"late initialization cannot submit timed-out request");
  m.Login("test","test");Check(BackEnd.Backend.BMember.Logins.Count==1,"already initialized SDK recovered on retry");Auth(m,true);Check(m.IsLoggedIn,"retry succeeds");
  m=New();BackEnd.Backend.IsInitialized=true;m.Login("test","test");UnityEngine.Time.realtimeSinceStartup=21;m.Update();
  Check(!m.IsLoggingIn&&failures==1,"authentication timeout releases busy state");
  Auth(m,true);Check(!m.IsLoggedIn&&successes==0,"late authentication does not open lobby");
  m.Login("test","test");Auth(m,true,1);Check(m.IsLoggedIn&&successes==1,"second attempt completes independently");
  m=New();BackEnd.Backend.IsInitialized=true;m.Login("test","test");Auth(m,false);Check(!m.IsLoggingIn&&failures==1,"bad credentials release UI");m.Login("test","test");Auth(m,true,1);Check(m.IsLoggedIn,"retry after bad credentials");
  m=New();BackEnd.Backend.ThrowInitialize=true;m.Login("test","test");Check(!m.IsInitializing&&!m.IsLoggingIn&&failures==1,"synchronous initialization exception releases flags");
  m=New();BackEnd.Backend.IsInitialized=true;BackEnd.Backend.BMember.ThrowLogin=true;m.Login("test","test");Check(!m.IsLoggingIn&&failures==1,"synchronous login exception releases flag");
  m=New();m.Login("test","test");m.Logout();Init(m,true);Check(BackEnd.Backend.BMember.Logins.Count==0,"logout cancels pending initialization continuation");
  m=New();BackEnd.Backend.IsInitialized=true;m.Login("test","test");m.Logout();Auth(m,true);Check(!m.IsLoggedIn&&successes==0,"logout invalidates late UI success");
  return n;
 }
}}
'@
Add-Type -TypeDefinition ($stub + "`nnamespace LoginChecks {`n" + $manager + "`n}`n" + $tests)
"Login request checks passed: $([LoginChecks.Tests]::Run())"
$prefab = Get-Content Assets/UI/Prefab/IntroUI.prefab -Raw
if ($prefab -notmatch 'loginButton: \{fileID: 8572463070984290043\}' -or $prefab -notmatch 'statusText: \{fileID: 1868391281230346376\}') { throw 'Existing login feedback UI must be bound' }
"Existing login button and DialogText bindings verified."
