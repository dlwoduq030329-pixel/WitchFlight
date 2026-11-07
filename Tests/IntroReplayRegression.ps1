# Exercises the production tween lifecycle with deterministic component/tween doubles.
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$presentation=Get-Content Assets/Script/BattleIntroPresentation.cs -Raw
function Method($name) {
 $m=[regex]::Match($presentation,'(?ms)^    private void '+$name+'\([^\r\n]*\)\r?\n    \{.*?^    \}')
 if(!$m.Success){throw "Missing $name"};$m.Value.Replace('private void','public void')
}
$source=@'
using System;
using System.Collections.Generic;
public class Tween {
 public static List<string> Log=new(); public bool active=true,playing,autoKill=true;public int Restarts;public string Name;
 public bool IsActive()=>active;
 public Tween SetAutoKill(bool value){autoKill=value;return this;}
 public void Pause(){playing=false;}
 public void ForceInit(){}
 public void Rewind(){playing=false;Log.Add("rewind:"+Name);}
 public void Restart(){playing=true;Restarts++;Log.Add("play:"+Name);}
 public void Complete(){playing=false;if(autoKill)active=false;}
}
public class DOTweenAnimation {
 public string Name;public bool autoPlay=true,autoKill=true,isActiveAndEnabled=true,isActive=true,isValid=true;
 public Tween tween;public int Creations;
 public void CreateTween(bool regenerate,bool play){if(tween!=null&&tween.IsActive())return;Creations++;tween=new(){Name=Name,autoKill=autoKill,playing=play};}
}
public class Panel {
 public DOTweenAnimation[] animations;
 public T[] GetComponentsInChildren<T>(bool includeInactive)=>animations as T[];
}
public class GameObject {
 public bool activeSelf=true;
 public void SetActive(bool value){activeSelf=value;}
}
public class BattleManager {
 public static BattleManager Instance;
 public int ExpectedPlayerCount;
}
public class Probe {
 public Panel introPanel;private readonly List<IntroTween> introTweens=new();
 public GameObject localProfileRoot=new(),opponentProfileRoot=new(),teammateProfileRoot=new(),secondEnemyProfileRoot=new();
 public bool IsTeamBattle;
 private class IntroTween {public DOTweenAnimation animation;public bool autoPlay,autoKill;}
'@
foreach($name in @('PrepareIntroTweens','RestartIntroTweens','PauseIntroTweens','SetProfileVisibility')){$source+=Method $name}
$source+=@'
}
public static class IntroChecks {
 static int n;static void Check(bool b,string s){if(!b)throw new Exception(s);n++;}
 public static int Run(){
  var left=new DOTweenAnimation{Name="left"};var right=new DOTweenAnimation{Name="right"};
  var chained=new DOTweenAnimation{Name="chained",autoPlay=false,autoKill=false};
  var p=new Probe{introPanel=new(){animations=new[]{left,right,chained}}};
  p.PrepareIntroTweens();Check(!left.autoPlay&&!right.autoPlay,"waiting cannot autoplay and consume entry animation");
  Check(!left.autoKill&&!right.autoKill,"first completion retains replayable tweens");
  // Simulate DOTween's Awake/Start after the controller activates the panel.
  foreach(var a in p.introPanel.animations)a.CreateTween(false,a.autoPlay);
  for(int round=1;round<=3;round++){
   Tween.Log.Clear();p.RestartIntroTweens();
   Check(left.tween.Restarts==round&&right.tween.Restarts==round,"both portraits replay each round "+round);
   Check(chained.tween.Restarts==0&&!chained.tween.playing,"event-driven effect is not forcibly autoplayed "+round);
   Check(Tween.Log[0]=="rewind:left"&&Tween.Log[1]=="rewind:right"&&Tween.Log[2]=="rewind:chained"&&Tween.Log[3]=="play:left","rewind all before starting callbacks "+round);
   foreach(var a in p.introPanel.animations)a.tween.Complete();
   p.PauseIntroTweens();Check(left.tween.active&&!left.tween.playing,"retain paused state outside VS "+round);
  }
  Check(left.Creations==1&&right.Creations==1,"replay never rebuilds From tweens using displaced endpoints");
  right.isActiveAndEnabled=false;p.RestartIntroTweens();Check(right.tween.Restarts==3,"inactive optional portrait stays inactive");
  p=new Probe();p.PrepareIntroTweens();p.RestartIntroTweens();p.PauseIntroTweens();Check(true,"unassigned panel is safe");
  BattleManager.Instance=new(){ExpectedPlayerCount=4};p.IsTeamBattle=true;
  p.SetProfileVisibility(false);
  Check(!p.localProfileRoot.activeSelf&&!p.opponentProfileRoot.activeSelf&&!p.teammateProfileRoot.activeSelf&&!p.secondEnemyProfileRoot.activeSelf,"all four authored cards stay hidden while waiting");
  p.SetProfileVisibility(true);
  Check(p.localProfileRoot.activeSelf&&p.opponentProfileRoot.activeSelf&&p.teammateProfileRoot.activeSelf&&p.secondEnemyProfileRoot.activeSelf,"all four ready cards open together");
  p.SetProfileVisibility(false);
  Check(!p.localProfileRoot.activeSelf&&!p.secondEnemyProfileRoot.activeSelf,"disconnect or next-round waiting hides cards again");
  BattleManager.Instance.ExpectedPlayerCount=2;p.IsTeamBattle=false;p.SetProfileVisibility(true);
  Check(p.localProfileRoot.activeSelf&&p.opponentProfileRoot.activeSelf&&!p.teammateProfileRoot.activeSelf&&!p.secondEnemyProfileRoot.activeSelf,"duel keeps unused cards hidden");
  BattleManager.Instance.ExpectedPlayerCount=1;p.SetProfileVisibility(true);
  Check(p.localProfileRoot.activeSelf&&!p.opponentProfileRoot.activeSelf,"solo does not reveal an unprepared enemy");
  return n;
 }
}
'@
Add-Type -TypeDefinition $source
"VS waiting, three-round replay and chained-effect checks passed: $([IntroChecks]::Run())"
# Verify the actual authored cause and the controller entry point remain covered.
$prefab=Get-Content Assets/UI/Prefab/BattleUI.prefab -Raw
if(!($prefab -match 'id: Vs\r?\n(?:[^\r\n]*\r?\n){0,6}  autoKill: 1')){throw 'Expected authored one-shot VS tween fixture changed'}
if(!($presentation -match 'if \(phase == BattleStartPhase.Intro\)\s*\{\s*RestartIntroTweens\(\);')){throw 'VS phase does not restart authored animation'}
'Authored one-shot tween and Intro entry wiring verified.'
if(!($presentation -match 'SetProfileVisibility\(!training && intro\)')){throw 'Profiles can be exposed before Intro'}
$initializer=Get-Content Assets/Script/BattleIntroInitializer.cs -Raw
if(!($initializer -match 'DefaultExecutionOrder\(-10000\)' -and $initializer -match 'PrepareBeforeDisplay\(\)')){throw 'Initially active VSUI is not protected before DOTween Awake'}
$scene=Get-Content Assets/Scenes/Battle.unity -Raw
if(!($scene -match 'guid: d4d65119bd944da5b1a0b0b5a9b22c70')){throw 'Battle scene initializer missing'}
if(!($scene -match 'fileID: 5387489171717556408, guid: f85a690f8032072489dd7b498ab4110d, type: 3\}\r?\n      propertyPath: m_IsActive\r?\n      value: 1')){throw 'Battle VSUI must start active'}
'Readiness visibility and scene startup wiring verified.'
