# Offline behavioral tests against extracted production methods, not a live Photon test.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$ui = Get-Content Assets/Script/RandomMatchUI.cs -Raw
$network = Get-Content Assets/Script/NetworkGameManager.cs -Raw
function Method([string]$source, [string]$signature) {
    $match = [regex]::Match($source, '(?ms)^    ' + [regex]::Escape($signature) + '.*?^    \}')
    if (!$match.Success) { throw "Missing production method: $signature" }
    $match.Value -replace '\bprivate\b', 'public'
}
$previewMethods = @('private void UpdatePreview(bool ready)', 'private void ResetPreview()') |
    ForEach-Object { Method $ui $_ }
$sessionMethods = @('public async void CancelMatch()', 'private async Task ShutdownSession(NetworkRunner sessionRunner)',
    'private bool AreRandomStartRequirementsMet()', 'private bool AreRandomProfilesPresented()') |
    ForEach-Object { Method $network $_ }
$stubs = @'
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace RandomMatchChecks {
 using PlayerRef = System.Int32;
 public static class Time {public static float unscaledTime;}
 public static class Debug {public static void LogWarning(string value){}}
 public class GameObject {public bool Destroyed;}
 public enum FindObjectsInactive {Include}
 public enum FindObjectsSortMode {None}
 public class NetworkRunner {
  public bool IsRunning=true;public List<int> ActivePlayers=new();
  public TaskCompletionSource<bool> Completion=new();public int ShutdownCalls;
  public async Task Shutdown(bool destroyGameObject){ShutdownCalls++;await Completion.Task;IsRunning=false;}
 }
 public class NetworkObject {public bool IsValid=true;}
 public class PlayerData {
  public NetworkObject Object=new();public NetworkRunner Runner;public int playerprofile,teamIndex;
  public GameObject gameObject=new();public int Confirmations,Clears;public PlayerData ConfirmedOpponent;
  public void ConfirmLobbyProfilePreview(PlayerData opponent){Confirmations++;ConfirmedOpponent=opponent;}
  public void ClearLobbyProfilePreview(){Clears++;ConfirmedOpponent=null;}
  public bool HasPresentedLobbyOpponent(PlayerData opponent)=>ConfirmedOpponent==opponent;
 }
 public class Image {public bool Visible=true;public int Profile;}
 public class UIProbe {
  public SessionProbe manager=new();public PlayerData localData,opponentData,previewLocal,previewOpponent;
  public NetworkRunner previewRunner;public int previewOwnProfile,previewOpponentProfile;
  public float previewStartedAt=-1;public bool previewConfirmed;
  public Image localProfileImage=new(),opponentProfileImage=new();
  public int ResolveProfile(int index)=>index;
  public static bool IsPortraitVisible(Image image,int profile)=>image.Visible&&image.Profile==profile;
'@
$session = @'
 }
 public class SessionProbe {
  public bool CanCancelMatch=true,cancelRequested,startInProgress,stopInProgress;
  public CancellationTokenSource matchCancellation=new();public NetworkRunner _runner;
  public NetworkRunner Runner=>_runner;public string Status;public int CleanupCalls;
  public bool requireRandomMatchProfilePreview=true;public float randomPlayersReadyAt=-1;
  public Dictionary<int,PlayerData> playerDatas=new();public List<PlayerData> World=new();
  public T[] FindObjectsByType<T>(FindObjectsInactive i,FindObjectsSortMode s)=>World.OfType<T>().ToArray();
  public void Destroy(GameObject obj){obj.Destroyed=true;}
  public void SetMatchStatus(string status){Status=status;}
  public void CleanupSession(NetworkRunner runner){CleanupCalls++;if(_runner==runner)_runner=null;}
'@
$tests = @'
 }
 public static class Tests {
  static int count;
  static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;}
  static UIProbe View(){
   var runner=new NetworkRunner();
   var view=new UIProbe();view.manager._runner=runner;
   view.localData=new PlayerData{Runner=runner,playerprofile=2,teamIndex=1};
   view.opponentData=new PlayerData{Runner=runner,playerprofile=5,teamIndex=2};
   view.localProfileImage.Profile=2;view.opponentProfileImage.Profile=5;return view;
  }
  public static int Run(){
   var view=View();Time.unscaledTime=0;view.UpdatePreview(false);
   Check(view.localData.Confirmations==0,"no opponent: never acknowledge");
   view.UpdatePreview(true);Time.unscaledTime=0.99f;view.UpdatePreview(true);
   Check(view.localData.Confirmations==0,"full one-second presentation required");
   Time.unscaledTime=1;view.UpdatePreview(true);
   Check(view.localData.Confirmations==1&&view.localData.ConfirmedOpponent==view.opponentData,"ack exact opponent after one second");
   Time.unscaledTime=4;view.UpdatePreview(true);Check(view.localData.Confirmations==1,"no repeated RPC each frame");
   view.opponentProfileImage.Visible=false;view.UpdatePreview(true);
   Check(!view.previewConfirmed&&view.localData.Clears==1,"hidden panel retracts acknowledgement");
   Time.unscaledTime=5;view.opponentProfileImage.Visible=true;view.UpdatePreview(true);
   Time.unscaledTime=5.9f;view.UpdatePreview(true);Check(view.localData.Confirmations==1,"hidden time does not count");
   Time.unscaledTime=6;view.UpdatePreview(true);Check(view.localData.Confirmations==2,"redisplay starts full fresh delay");
   view.opponentData.playerprofile=3;view.opponentProfileImage.Profile=3;view.UpdatePreview(true);
   Check(!view.previewConfirmed&&view.localData.Clears==2,"changed opponent profile invalidates old confirmation");
   Time.unscaledTime=7;view.UpdatePreview(true);Check(view.previewConfirmed,"new profile can confirm");
   view.localData.playerprofile=1;view.localProfileImage.Profile=1;view.UpdatePreview(true);
   Check(!view.previewConfirmed,"changed own profile also restarts presentation");
   Time.unscaledTime=8;view.UpdatePreview(true);view.UpdatePreview(false);
   Check(!view.previewConfirmed&&view.previewLocal==null,"disconnect/cancel clears state");
   view=View();Time.unscaledTime=10;view.opponentProfileImage.Profile=99;view.UpdatePreview(true);
   Check(view.previewStartedAt<0,"wrong/stale sprite cannot start timer");
   view.opponentProfileImage.Profile=5;view.UpdatePreview(true);
   var oldOpponent=view.opponentData;view.opponentData=new PlayerData{Runner=view.manager.Runner,playerprofile=5,teamIndex=2};
   Time.unscaledTime=10.8f;view.UpdatePreview(true);Time.unscaledTime=11;view.UpdatePreview(true);
   Check(view.localData.Confirmations==0,"replacement opponent with same icon resets delay");
   Time.unscaledTime=12;view.UpdatePreview(true);Check(view.localData.ConfirmedOpponent==view.opponentData,"replacement acknowledged instead of old player");
   var host=view.manager;host.Runner.ActivePlayers.AddRange(new[]{1,2});
   host.playerDatas[1]=view.localData;host.playerDatas[2]=view.opponentData;
   Check(!host.AreRandomStartRequirementsMet(),"host cannot start on its own presentation only");
   view.opponentData.ConfirmLobbyProfilePreview(view.localData);
   Check(host.AreRandomStartRequirementsMet(),"both peer acknowledgements allow battle");
   host.playerDatas.Remove(2);Check(!host.AreRandomStartRequirementsMet(),"disconnected peer blocks transition");
   var starting=new SessionProbe{_runner=new NetworkRunner(),startInProgress=true};starting.CancelMatch();
   Check(starting.cancelRequested&&starting.matchCancellation.IsCancellationRequested,"startup cancel requests token cancellation");
   Check(starting.Runner.ShutdownCalls==0,"startup owns shutdown, no concurrent double shutdown");
   var blocked=new SessionProbe{CanCancelMatch=false};blocked.CancelMatch();
   Check(!blocked.cancelRequested,"cannot cancel a committed battle transition");
   var current=new NetworkRunner();var other=new NetworkRunner();
   var session=new SessionProbe{_runner=current};
   var ours=new PlayerData{Runner=current};var unrelated=new PlayerData{Runner=other};session.World.AddRange(new[]{ours,unrelated});
   session.CancelMatch();Check(session.cancelRequested&&session.stopInProgress&&current.ShutdownCalls==1,"connected cancel starts shutdown");
   Check(!ours.gameObject.Destroyed,"do not destroy network data before shutdown finishes");
   current.Completion.SetResult(true);
   Check(SpinWait.SpinUntil(()=>!session.stopInProgress,2000),"cancel completes");
   Check(ours.gameObject.Destroyed&&!unrelated.gameObject.Destroyed,"cleanup only destroys captured matching-session data");
   Check(session.Runner==null&&session.CleanupCalls==1,"runner cleared for retry");
   Check(session.Status=="매칭을 취소했습니다.","completion status");
   var failure=new SessionProbe{_runner=new NetworkRunner()};var stale=new PlayerData{Runner=failure.Runner};failure.World.Add(stale);
   Task shutdown=failure.ShutdownSession(failure.Runner);failure.Runner.Completion.SetException(new Exception("transport failure"));shutdown.GetAwaiter().GetResult();
   Check(stale.gameObject.Destroyed&&failure.CleanupCalls==1,"failed shutdown still removes captured stale session objects");
   return count;
  }
 }
}
'@
Add-Type -TypeDefinition ($stubs + ($previewMethods -join "`n") + $session + ($sessionMethods -join "`n") + $tests)
"Random match behavior checks passed: $([RandomMatchChecks.Tests]::Run())"

# Main wiring and fallback/call-site invariants complement method-level checks.
$main = Get-Content Assets/Scenes/Main.unity -Raw
if ($main -notmatch 'requireRandomMatchProfilePreview: 1') {throw 'Main must wait for both visible previews'}
$component = [regex]::Match($main, '(?ms)^--- !u!114 &244481547\r?\n.*?(?=^--- |\z)').Value
foreach ($value in @('m_GameObject: {fileID: 244481543}', 'guid: 4da3259defcd4337a9ccba84b57fedf4',
    'guid: 37691fc4619c43d2b0525e0e9b8de71f', 'guid: ad82f58d7f40a25448fe68d46ecb6f2c')) {
    if (!$component.Contains($value)) {throw "Main UI binding missing: $value"}
}
foreach ($field in @('matchingPanel','statusText','localProfileImage','opponentProfileImage','localNameText','opponentNameText','cancelButton','loadingObject')) {
    if ($ui -notmatch ('if \(' + $field + ' == null\)')) {throw "Missing fallback for $field"}
}
if ($ui.Contains('RemoveAllListeners') -or $ui.Contains('Application.isFocused')) {throw 'Preserve custom button callbacks and support unfocused second client'}
if ($ui -notmatch 'ready && !cancelling' -or $ui -notmatch 'data.Runner != manager.Runner' -or $ui -notmatch 'manager.Runner.ActivePlayers') {throw 'UI must reject cancelled/stale sessions'}
$shutdown = Method $network 'private async Task ShutdownSession(NetworkRunner sessionRunner)'
if ($shutdown -match 'DataConfig\.|Logout|ResetToDefaults') {throw 'Cancelling must not clear account profile/login'}
'Main bindings, optional fallback fields, callback preservation and account isolation checks passed.'
