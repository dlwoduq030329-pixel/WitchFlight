# Run with PowerShell 7. Uses production join/start/result methods and a fake transport.
# This is not a live Photon connectivity or Unity Play Mode test.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$manager = Get-Content (Join-Path $repo 'Assets/Script/NetworkGameManager.cs') -Raw
function Method([string]$signature) {
    $m = [regex]::Match($manager, '(?ms)^    ' + [regex]::Escape($signature) + '.*?^    \}')
    if (!$m.Success) { throw "Production method not found: $signature" }
    $m.Value
}
$methods = @(
    'public void JoinRoom(string roomId)',
    'public void JoinRoom(string roomId, Action onSuccess, Action<string> onFailed)',
    'private void NotifyRoomJoinResult(',
    'private async void StartGame(',
    'private static string GetMatchFailureMessage('
) | ForEach-Object { Method $_ }
$source = @'
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace UnityEngine {
 public class Object {
  public bool Destroyed;
  static bool Dead(Object x)=>ReferenceEquals(x,null)||x.Destroyed;
  public static bool operator ==(Object a,Object b)=>(Dead(a)&&Dead(b))||ReferenceEquals(a,b);
  public static bool operator !=(Object a,Object b)=>!(a==b);
  public override bool Equals(object obj)=>ReferenceEquals(this,obj);
  public override int GetHashCode()=>base.GetHashCode();
 }
 public static class Debug {
  public static int Exceptions;
  public static void LogWarning(string m,object context=null){}
  public static void LogException(Exception e,object context=null){Exceptions++;}
 }
}
namespace Fusion.Photon.Realtime {public enum MatchmakingMode {FillRoom}}
namespace RoomJoinChecks {
 using UnityEngine;
 public enum GameMode {Host,Client,AutoHostOrClient}
 public enum ShutdownReason {Ok,OperationCanceled,GameNotFound,GameIdAlreadyExists,GameIsFull,GameClosed,ConnectionTimeout}
 public class StartResult {public bool Ok;public ShutdownReason ShutdownReason;public string ErrorMessage;}
 public class Transform {public void SetParent(Transform parent,bool world){}}
 public class GameObject {
  public Transform transform=new();public GameObject(string name){}
  public T AddComponent<T>() where T:new()=>typeof(T)==typeof(NetworkRunner)?(T)(object)NetworkRunner.Next:new T();
 }
 public class NetworkSceneManagerDefault {}
 public struct SessionProperty {
  public static implicit operator SessionProperty(string s)=>default;
  public static implicit operator SessionProperty(int s)=>default;
 }
 public struct SceneRef {public static SceneRef FromIndex(int i)=>default;}
 public struct Scene {public int buildIndex;}
 public static class SceneManager {public static Scene GetActiveScene()=>new Scene{buildIndex=0};}
 public static class Mathf {public static int Max(int a,int b)=>Math.Max(a,b);}
 public class StartGameArgs {
  public GameMode GameMode;public bool EnableClientSessionCreation,IsVisible,IsOpen;
  public string SessionName,CustomLobbyName;public int PlayerCount;
  public Dictionary<string,SessionProperty> SessionProperties;
  public Fusion.Photon.Realtime.MatchmakingMode MatchmakingMode;public SceneRef Scene;
  public NetworkSceneManagerDefault SceneManager;public CancellationToken StartGameCancellationToken;
 }
 public class NetworkRunner {
  public static NetworkRunner Next;public bool IsRunning,ProvideInput,StopAfterResult;
  public int Starts;public StartGameArgs Args;
  public Task<StartResult> Reply=Task.FromResult(new StartResult{Ok=true});
  public void AddCallbacks(object c){}
  public async Task<StartResult> StartGame(StartGameArgs args){Starts++;Args=args;var r=await Reply;IsRunning=r.Ok&&!StopAfterResult;return r;}
 }
 public class LoginManager {public static LoginManager Instance;public bool IsLoggedIn;}
 public class DatabaseManager {public static DatabaseManager Instance;public bool IsDataConfigReady;}
 public class ManagerProbe:UnityEngine.Object {
  enum MatchRequest {CreateRoom,JoinRoom,Random,LegacyCodeMatch}
  const int RandomPlayerCount=2;const string RandomLobbyName="WitchFlight-Random-1v1-v1";
  public Transform transform=new();private NetworkRunner _runner;private GameObject runnerObject;
  private CancellationTokenSource matchCancellation;
  public bool startInProgress,stopInProgress,cancelRequested,ValidSetup=true,IsRandomMatch;
  private float randomPlayersReadyAt;private int activePlayerCount,maxPlayerCount=2,battleSceneIndex=1;
  public int Shutdowns,WaitingUpdates;public string MatchStatus="";
  public bool IsMatching=>_runner!=null||startInProgress||stopInProgress;
  public bool IsConnected=>_runner!=null&&_runner.IsRunning;
  bool ValidateMatchSetup(){if(!ValidSetup)SetMatchStatus("설정 오류");return ValidSetup;}
  void SetMatchStatus(string s){MatchStatus=s;}
  void UpdateWaitingStatus(){WaitingUpdates++;MatchStatus="대기실";}
  Task ShutdownSession(NetworkRunner r){Shutdowns++;if(r!=null)r.IsRunning=false;if(ReferenceEquals(_runner,r))_runner=null;return Task.CompletedTask;}
  public void CancelPending(){cancelRequested=true;matchCancellation?.Cancel();}
  public void StartOtherMode(bool random)=>StartGame(random?MatchRequest.Random:MatchRequest.CreateRoom,random?null:"abcd");
'@ + ($methods -join "`n") + @'
 }
 public class UiProbe:UnityEngine.Object {public int Calls;public void Success(){Calls++;}public void Failure(string error){Calls++;}}
 public static class Tests {
  static int checks;
  static void Check(bool b,string label){if(!b)throw new Exception(label);checks++;}
  static ManagerProbe Fresh(){NetworkRunner.Next=new();LoginManager.Instance=null;DatabaseManager.Instance=null;return new ManagerProbe();}
  static StartResult Failed(ShutdownReason why)=>new StartResult{ShutdownReason=why};
  static void Wait(ManualResetEventSlim done){if(!done.Wait(3000))throw new Exception("Callback timeout");}
  public static int Run(){
   foreach(string empty in new string[]{null,"","  "}){
    var m=Fresh();int ok=0,bad=0;m.JoinRoom(empty,()=>ok++,s=>{bad++;Check(s.Contains("입력"),"empty-code error");});
    Check(ok==0&&bad==1&&NetworkRunner.Next.Starts==0,"empty code reports once without starting");
   }
   {
    var m=Fresh();m.ValidSetup=false;int failed=0;m.JoinRoom("1234",null,s=>failed++);
    Check(failed==1&&!m.IsMatching&&NetworkRunner.Next.Starts==0,"invalid setup reports failure");
   }
   foreach(bool loggedIn in new[]{false,true}){
    var m=Fresh();LoginManager.Instance=new LoginManager{IsLoggedIn=loggedIn};DatabaseManager.Instance=new DatabaseManager{IsDataConfigReady=false};
    int bad=0;m.JoinRoom("1234",null,s=>{bad++;Check(s.Contains("로그인"),"login-readiness error");});
    Check(bad==1&&NetworkRunner.Next.Starts==0,"login/data gate reports failure");
   }
   {
    var m=Fresh();int ok=0,bad=0;m.JoinRoom(" 1234 ",()=>{ok++;Check(!m.startInProgress&&m.IsConnected,"success delivered after finally");},s=>bad++);
    Check(ok==1&&bad==0&&m.Shutdowns==0,"success exactly once");
    Check(NetworkRunner.Next.Args.SessionName=="1234"&&NetworkRunner.Next.Args.GameMode==GameMode.Client&&!NetworkRunner.Next.Args.EnableClientSessionCreation,"code join never creates room");
    string status=m.MatchStatus;m.JoinRoom("5678",()=>ok++,s=>bad++);
    Check(ok==1&&bad==1&&m.Shutdowns==0&&m.MatchStatus==status,"duplicate does not replace active request/status");
   }
   foreach(var reason in new[]{ShutdownReason.GameNotFound,ShutdownReason.GameIsFull,ShutdownReason.GameClosed,ShutdownReason.ConnectionTimeout,ShutdownReason.OperationCanceled}){
    var m=Fresh();NetworkRunner.Next.Reply=Task.FromResult(Failed(reason));int ok=0,bad=0;string error=null;
    m.JoinRoom("9999",()=>ok++,s=>{bad++;error=s;Check(!m.IsMatching&&m.Shutdowns==1,"failure after cleanup: "+reason);});
    Check(ok==0&&bad==1&&!string.IsNullOrWhiteSpace(error),"server failure exactly once: "+reason);
    if(reason==ShutdownReason.GameNotFound)Check(error.Contains("방이 없습니다"),"wrong-code message");
    if(reason==ShutdownReason.GameIsFull)Check(error.Contains("가득"),"full-room message");
    if(reason==ShutdownReason.GameClosed)Check(error.Contains("닫힌"),"closed-room message");
   }
   foreach(Exception exception in new Exception[]{new OperationCanceledException(),new InvalidOperationException("transport")}){
    var m=Fresh();NetworkRunner.Next.Reply=Task.FromException<StartResult>(exception);int ok=0,bad=0;
    m.JoinRoom("1234",()=>ok++,s=>bad++);Check(ok==0&&bad==1&&!m.IsMatching&&m.Shutdowns==1,"thrown transport/cancel reports once");
   }
   {
    var m=Fresh();NetworkRunner.Next.StopAfterResult=true;int ok=0,bad=0;m.JoinRoom("1234",()=>ok++,s=>bad++);
    Check(ok==0&&bad==1&&m.Shutdowns==1,"ok result with stopped runner is not success");
   }
   {
    var m=Fresh();var pending=new TaskCompletionSource<StartResult>();NetworkRunner.Next.Reply=pending.Task;
    int ok=0,firstBad=0,secondBad=0;using var done=new ManualResetEventSlim();
    m.JoinRoom("1234",()=>{ok++;done.Set();},s=>{firstBad++;done.Set();});
    Check(ok==0&&firstBad==0&&m.startInProgress,"no premature success while awaiting server");
    m.JoinRoom("5678",null,s=>secondBad++);Check(secondBad==1&&firstBad==0,"duplicate callback stays request-local");
    pending.SetResult(new StartResult{Ok=true});Wait(done);Check(ok==1&&firstBad==0,"original pending success preserved");
   }
   {
    var m=Fresh();var pending=new TaskCompletionSource<StartResult>();NetworkRunner.Next.Reply=pending.Task;
    int ok=0,bad=0;using var done=new ManualResetEventSlim();m.JoinRoom("1234",()=>{ok++;done.Set();},s=>{bad++;done.Set();});
    m.CancelPending();pending.SetResult(new StartResult{Ok=true});Wait(done);
    Check(ok==0&&bad==1&&!m.IsMatching,"cancel wins over racing successful reply");
   }
   {
    var m=Fresh();NetworkRunner.Next.Reply=Task.FromResult(Failed(ShutdownReason.GameNotFound));int bad=0,ok=0;
    m.JoinRoom("9999",null,s=>{bad++;NetworkRunner.Next=new();m.JoinRoom("1234",()=>ok++,null);});
    Check(bad==1&&ok==1&&m.IsConnected&&!m.startInProgress,"retry inside failure callback is safe");
   }
   {
    var m=Fresh();int bad=0;int logs=Debug.Exceptions;m.JoinRoom("1234",()=>throw new Exception("UI"),s=>bad++);
    Check(bad==0&&m.IsConnected&&m.Shutdowns==0&&Debug.Exceptions==logs+1,"UI exception does not turn success into failed connection");
   }
   {
    var m=Fresh();var deadUi=new UiProbe{Destroyed=true};var aliveUi=new UiProbe();Action callbacks=deadUi.Success;callbacks+=aliveUi.Success;
    m.JoinRoom("1234",callbacks,null);Check(deadUi.Calls==0&&aliveUi.Calls==1,"destroyed UI skipped without losing other handlers");
   }
   {
    var m=Fresh();m.JoinRoom("1234");Check(m.IsConnected,"existing string-only overload still works");
    m=Fresh();m.StartOtherMode(false);Check(m.IsConnected&&NetworkRunner.Next.Args.GameMode==GameMode.Host,"create-room flow preserved");
    m=Fresh();m.StartOtherMode(true);Check(m.IsConnected&&NetworkRunner.Next.Args.GameMode==GameMode.AutoHostOrClient&&NetworkRunner.Next.Args.EnableClientSessionCreation,"random flow preserved");
   }
   return checks;
  }
 }
}
'@
Add-Type -IgnoreWarnings -WarningAction SilentlyContinue -TypeDefinition $source
"Room join callback checks passed: $([RoomJoinChecks.Tests]::Run())"
