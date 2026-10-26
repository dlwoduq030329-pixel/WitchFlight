# Exercise the production preparation state machine without starting Unity or a network session.
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$flag=Get-Content Assets/Script/BattleFlag.cs -Raw
function Method([string]$name) {
 $m=[regex]::Match($flag,'(?ms)^    private void '+$name+'\([^{}]*\)\r?\n    \{.*?^    \}')
 if(!$m.Success){throw "Missing $name"};$m.Value
}
$source=@'
using System;
public enum BattleStartPhase {WaitingForPlayers,Intro,Countdown,Playing,Ended,Intermission,MapTour,TrainingTutorial}
public class Runner {public float Time;}
public struct TickTimer {
 public float End;public bool Running;
 public static TickTimer None=>default;
 public static TickTimer CreateFromSeconds(Runner r,float t)=>new(){End=r.Time+t,Running=true};
 public bool Expired(Runner r)=>Running&&r.Time>=End;
}
public static class CatTutorialDirector {public static bool TrainingGuideRequired;}
public class BattleManager {
 public static BattleManager Instance;public bool IsTraining,AutoStartWhenReady=true;public BattleFlag Flag;
 public void startGame()=>Flag.Request();
}
public class BattleFlag {
 public Runner Runner=new();public BattleStartPhase Phase;public TickTimer PhaseTimer;
 public bool Ready=true,startRequested,mapTourStarted;public int RoundNumber=1;
 public float TourDuration,introSeconds=5,matchSeconds=160,WorldPosition;
 public void Request()=>startRequested=true;
 private bool AreAllPlayersReady()=>Ready;
 private void ResetPreparation(){Phase=BattleStartPhase.WaitingForPlayers;PhaseTimer=TickTimer.None;}
 private void BeginMatch(float p,float d)=>Phase=BattleStartPhase.Playing;
 public void Tick()=>TickPreparation();
'@
$source+=(Method 'TickPreparation')+(Method 'BeginTrainingGuideOrCountdown')
$source+=@'
}
public static class GuideTests {
 static int count;
 static void Check(bool value,string name){if(!value)throw new Exception(name);count++;}
 static BattleFlag Create(bool training,bool required,float tour=0){
  var flag=new BattleFlag{TourDuration=tour};
  BattleManager.Instance=new(){IsTraining=training,Flag=flag};
  CatTutorialDirector.TrainingGuideRequired=required;return flag;
 }
 public static int Run(){
  var f=Create(true,true);f.Tick();Check(f.Phase==BattleStartPhase.TrainingTutorial,"first training opens guide before countdown");
  f.Runner.Time=300;f.Tick();Check(f.Phase==BattleStartPhase.TrainingTutorial,"reading has no deadline");
  CatTutorialDirector.TrainingGuideRequired=false;f.Tick();Check(f.Phase==BattleStartPhase.Countdown&&f.PhaseTimer.End==303,"completion starts a fresh full countdown");
  f.Runner.Time=302.9f;f.Tick();Check(f.Phase==BattleStartPhase.Countdown,"never starts combat early");
  f.Runner.Time=303;f.Tick();Check(f.Phase==BattleStartPhase.Playing,"combat begins after 321");
  f=Create(true,false);f.Tick();Check(f.Phase==BattleStartPhase.Countdown,"returning account skips completed guide");
  f=Create(true,true,12);f.Tick();Check(f.Phase==BattleStartPhase.MapTour,"optional map tour retained");
  f.Runner.Time=12;f.Tick();Check(f.Phase==BattleStartPhase.TrainingTutorial,"tour ends in guide, never an overlapping countdown");
  f=Create(false,true);f.Tick();Check(f.Phase==BattleStartPhase.Intro,"online matches retain VS and ignore training completion");
  f.Runner.Time=5;f.Tick();Check(f.Phase==BattleStartPhase.Countdown,"online intro still starts countdown");
  f=Create(true,true);f.Ready=false;f.Tick();Check(f.Phase==BattleStartPhase.WaitingForPlayers,"readiness barrier retained");
  return count;
 }
}
'@
Add-Type -TypeDefinition $source
"Training guide phase checks passed: $([GuideTests]::Run())"
$player=Get-Content Assets/Script/Player.cs -Raw
$decelerate=[regex]::Match($player,'(?s)if \(buttons.WasPressed\(previousButtons, PlayerInputButton.Decelerate\)\).*?\n        \}').Value
if(!$decelerate.Contains('SpeedStage = Mathf.Max') -or $decelerate.Contains('TryStartCobra')){throw 'S must only decrement speed stage'}
if(!$player.Contains('PlayerInputButton.QuickBrake) && TryStartCobra()')){throw 'Quick brake must have a separate input action'}
'S deceleration and quick-brake actions are separate.'
