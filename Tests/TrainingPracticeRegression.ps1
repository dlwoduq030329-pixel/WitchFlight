$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$table = Get-Content Assets/Script/CatCompanionTable.cs -Raw
$enum = [regex]::Match($table, 'public enum CatTrainingAction \{[^}]+\}').Value
$progress = Get-Content Assets/Script/TrainingPracticeProgress.cs -Raw
$director = Get-Content Assets/Script/CatTutorialDirector.Training.cs -Raw
$gates = @('TrainingSessionActive','PracticeInputActive','CanTrainingBotAttack') | ForEach-Object {
    $match = [regex]::Match($director, '(?s)public static bool ' + $_ + '.*?;')
    if (!$match.Success) { throw "Missing production gate $_" }; $match.Value
}
$source = "using System;`n" + $enum + "`n" + $progress + @'
public enum BattleStartPhase { Playing, TrainingTutorial, Countdown }
public class BattleManager { public static BattleManager Instance; public bool IsTraining; public BattleStartPhase Phase; }
public class Player { public object gameObject = new(); }
public class MagicTestBot { public object gameObject; }
public class CatTutorialDirector {
 public static CatTutorialDirector trainingDirector;
 public bool practiceArmed;
 public CatTrainingAction CurrentPractice;
 public float practiceCompletedAt = -1;
 public Player practiceTarget;
'@ + ($gates -join "`n") + @'
}
public static class PracticeChecks {
 static int count;
 static void Check(bool value, string name) { if (!value) throw new Exception(name); count++; }
 public static int Run() {
  var p = new TrainingPracticeProgress();
  bool Observe(CatTrainingAction a, int speed=0, int slot=1, bool locked=false, int casts=0, int parries=0,
      int choices=0, bool boost=false, bool brake=false, bool emote=false, float dt=.1f)
      => p.Observe(a,speed,slot,locked,casts,parries,choices,boost,brake,emote,dt);
  p.Reset(0,0,0,0);
  Check(!Observe(CatTrainingAction.SpeedUpAndDown,-1), "S alone cannot finish W/S practice");
  Check(!Observe(CatTrainingAction.SpeedUpAndDown,1), "acceleration alone is incomplete");
  Check(Observe(CatTrainingAction.SpeedUpAndDown,0), "actual increase then decrease completes");
  p.Reset(0,4,2,1);
  Check(!Observe(CatTrainingAction.AimAndFire,casts:5), "unlocked casts do not complete aiming");
  Check(!Observe(CatTrainingAction.AimAndFire,locked:true,casts:4), "lock alone cannot complete release practice");
  Check(Observe(CatTrainingAction.AimAndFire,casts:5), "new cast after lock completes");
  Check(!Observe(CatTrainingAction.Parry,parries:2), "past success and parry attempt cannot pass");
  Check(Observe(CatTrainingAction.Parry,parries:3), "new real parry passes");
  Check(!Observe(CatTrainingAction.ChoosePerk,choices:1), "opening cards does not pass");
  Check(Observe(CatTrainingAction.ChoosePerk,choices:2), "accepted choice passes");
  p.Reset(0,0,0,0);
  Check(!Observe(CatTrainingAction.SwitchSlots), "initial slot is not a switch");
  Check(!Observe(CatTrainingAction.SwitchSlots,slot:2), "slot 2 alone incomplete");
  Check(Observe(CatTrainingAction.SwitchSlots,slot:1), "2 then 1 completes");
  Check(!Observe(CatTrainingAction.Boost,dt:2), "elapsed time without boost cannot pass");
  Check(!Observe(CatTrainingAction.Boost,boost:true,dt:.5f), "short boost incomplete");
  Check(Observe(CatTrainingAction.Boost,boost:true,dt:.25f), "sustained boost passes");
  Check(!Observe(CatTrainingAction.QuickBrake), "rejected Q is not success");
  Check(Observe(CatTrainingAction.QuickBrake,brake:true), "active braking passes");
  Check(!Observe(CatTrainingAction.Emote), "opening wheel is not success");
  Check(Observe(CatTrainingAction.Emote,emote:true), "accepted emote passes");
  p.Reset(0,0,0,0);
  Check(!p.IncreasedSpeed && !p.SelectedSecond && !p.Charged, "new exercise clears previous progress");
  Check(!Observe(CatTrainingAction.Boost,boost:true,dt:.01f), "boost progress resets");

  var g = new CatTutorialDirector {practiceArmed=true,CurrentPractice=CatTrainingAction.Parry,practiceTarget=new Player()};
  CatTutorialDirector.trainingDirector=g;
  var chosen = new MagicTestBot {gameObject=g.practiceTarget.gameObject};
  var other = new MagicTestBot {gameObject=new object()};
  BattleManager.Instance = new BattleManager {IsTraining=false,Phase=BattleStartPhase.TrainingTutorial};
  Check(!CatTutorialDirector.PracticeInputActive && !CatTutorialDirector.CanTrainingBotAttack(chosen), "online mode never enters practice");
  BattleManager.Instance.IsTraining=true;
  Check(CatTutorialDirector.CanTrainingBotAttack(chosen), "chosen bot allowed during parry");
  Check(!CatTutorialDirector.CanTrainingBotAttack(other), "other bots stay silent");
  g.CurrentPractice=CatTrainingAction.AimAndFire;
  Check(!CatTutorialDirector.CanTrainingBotAttack(chosen), "player may practice firing without enemy attacks");
  g.CurrentPractice=CatTrainingAction.Parry;g.practiceArmed=false;
  Check(!CatTutorialDirector.CanTrainingBotAttack(chosen), "reading parry instruction cannot trigger attacks");
  g.practiceArmed=true;g.practiceCompletedAt=5;
  Check(!CatTutorialDirector.CanTrainingBotAttack(chosen), "success stops further bot firing immediately");
  BattleManager.Instance.Phase=BattleStartPhase.Countdown;
  Check(!CatTutorialDirector.TrainingSessionActive, "countdown leaves tutorial restrictions");
  BattleManager.Instance.Phase=BattleStartPhase.Playing;
  Check(!CatTutorialDirector.TrainingSessionActive, "normal training uses authored bot attacks");
  return count;
 }
}
'@
Add-Type -TypeDefinition $source
"Training practice checks passed: $([PracticeChecks]::Run())"
