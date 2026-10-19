# Offline checks; no Unity Play Mode or live Photon session is started.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$network = Get-Content Assets/Script/NetworkGameManager.cs -Raw
$loader = Get-Content Assets/Script/BattleNetworkSceneManager.cs -Raw
function Method([string]$name) {
    $match = [regex]::Match($network, '(?ms)^    private void ' + $name + '\(\).*?^    \}')
    if (!$match.Success) { throw "Missing production method: $name" }
    $match.Value -replace '^    private ', '    public '
}
$source = @'
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using UnityEngine.SceneManagement;
namespace UnityEngine.SceneManagement {
 public struct Scene {
  public int buildIndex;public Root[] roots;
  public Root[] GetRootGameObjects()=>roots;
 }
}
namespace Fusion {
 public struct SceneRef {}
 public struct NetworkLoadSceneParameters {}
 public class NetworkSceneManagerDefault {
  public static bool BaseCalled,BotsActiveOnRegistration;
  protected T GetComponentInParent<T>() where T:class=>NetworkGameManager.Instance as T;
  protected virtual IEnumerator OnSceneLoaded(SceneRef r,Scene s,NetworkLoadSceneParameters p){
   BaseCalled=true;BotsActiveOnRegistration=s.roots.SelectMany(x=>x.Bots).All(x=>x.gameObject.activeSelf);yield break;
  }
  public void Load(Scene scene){var work=OnSceneLoaded(default,scene,default);while(work.MoveNext()){} }
 }
}
public class Root {
 public MagicTestBot[] Bots;
 public T[] GetComponentsInChildren<T>(bool includeInactive)=>Bots.Cast<T>().ToArray();
}
public class BotObject {public bool activeSelf;public int Activations;public void SetActive(bool b){activeSelf=b;Activations++;}}
public class MagicTestBot {public BotObject gameObject=new();}
public class NetworkGameManager {
 public static NetworkGameManager Instance;
 public bool IsTraining,IsRandomMatch,CanBegin=true,PreviewReady;
 public bool isGameStarting;public float randomPlayersReadyAt=-1;public int Loads,PreviewChecks;
 public class Session {public bool IsOpen=true,IsVisible=true;}
 public class Runner {public Session SessionInfo=new();}
 public Runner _runner=new();public string Status;
 public bool IsTrainingScene(Scene scene)=>IsTraining&&scene.buildIndex==1;
 bool CanBeginBattle()=>CanBegin;
 bool AreRandomStartRequirementsMet(){PreviewChecks++;return PreviewReady;}
 void SetMatchStatus(string value){Status=value;}
 void LoadBattleScene(){Loads++;}
'@ + (Method 'CheckPlayerCount') + (Method 'BeginBattleTransition') + "`n}`n" +
    ($loader -replace '(?m)^using .*;\r?\n', '') + @'

public static class Checks {
 static int n;static void Check(bool condition,string message){if(!condition)throw new Exception(message);n++;}
 static Scene SceneWithBots(int buildIndex){return new Scene{buildIndex=buildIndex,roots=new[]{new Root{Bots=Enumerable.Range(0,6).Select(_=>new MagicTestBot()).ToArray()}}};}
 public static int Run(){
  var manager=new NetworkGameManager{IsTraining=true};NetworkGameManager.Instance=manager;
  manager.CheckPlayerCount();
  Check(manager.Loads==1&&manager.isGameStarting&&manager.PreviewChecks==0,"training starts without remote profiles or matchmaking delay");
  Check(manager._runner.SessionInfo.IsOpen&&manager._runner.SessionInfo.IsVisible,"single mode never writes Photon SessionInfo");
  manager=new(){IsTraining=true,CanBegin=false};manager.CheckPlayerCount();Check(manager.Loads==0,"training still waits for startup/scene/loadout readiness");
  manager=new(){IsRandomMatch=true};manager.CheckPlayerCount();Check(manager.Loads==0,"random still waits for profile presentation");
  manager.PreviewReady=true;manager.CheckPlayerCount();Check(manager.Loads==1&&!manager._runner.SessionInfo.IsOpen,"random still closes public room before loading");
  manager=new();manager.CheckPlayerCount();Check(manager.Loads==0,"code rooms never auto-start");
  manager.IsTraining=true;NetworkGameManager.Instance=manager;
  var loader=new BattleNetworkSceneManager();var scene=SceneWithBots(1);var originals=scene.roots[0].Bots;
  loader.Load(scene);
  Check(NetworkSceneManagerDefault.BaseCalled&&NetworkSceneManagerDefault.BotsActiveOnRegistration,"bots activated before Fusion scene registration/Spawned");
  Check(ReferenceEquals(originals,scene.roots[0].Bots)&&originals.All(x=>x.gameObject.Activations==1),"all six authored bots reused, no clones");
  scene=SceneWithBots(0);loader.Load(scene);Check(scene.roots[0].Bots.All(x=>x.gameObject.Activations==0),"main scene untouched");
  manager.IsTraining=false;scene=SceneWithBots(1);loader.Load(scene);Check(scene.roots[0].Bots.All(x=>x.gameObject.Activations==0),"normal battles keep authored inactive bots");
  return n;
 }
}
'@
Add-Type -TypeDefinition $source
"Training transition and scene-bot activation checks passed: $([Checks]::Run())"
foreach ($contract in @(
    @($network, 'public void StartTraining()'),
    @($network, 'public async void ExitTraining()'),
    @($network, 'IsTraining = false;'),
    @((Get-Content Assets/Script/BattleHud.Training.cs -Raw), 'public void ExitTraining()'),
    @((Get-Content Assets/Script/BattleIntroPresentation.cs -Raw), 'if (battle.IsTraining)'),
    @((Get-Content Assets/Script/BattleHud.cs -Raw), 'battle.IsTraining ? "훈련장"')
)) { if (!$contract[0].Contains($contract[1])) { throw "Missing training integration: $($contract[1])" } }
'Training public button callbacks, cleanup, intro bypass and HUD contracts passed.'
