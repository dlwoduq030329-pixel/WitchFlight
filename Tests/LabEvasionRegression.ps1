# Execute the real offline threat reader and decision state machine. Physics queries are controlled doubles.
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
function Method($source,$name) {
 $m=[regex]::Match($source,'(?ms)^    (?:public|private) (?:static )?[\w<>]+ '+$name+'\([^{}]*?\)\r?\n    \{.*?^    \}')
 if(!$m.Success){throw "Missing $name"};$m.Value
}
# Reuse mathematical doubles from the match-bot suite, not its network simulation.
$base=Get-Content Tests/BotMobilityRegression.ps1 -Raw
$unity=[regex]::Match($base,'(?s)namespace UnityEngine \{.*?\n\}\r?\n(?=public class MatchBotSettings)').Value
$settings=Get-Content Assets/Script/MatchBotSettings.cs -Raw
$section=$settings.Substring($settings.IndexOf('public bool useCombatDash'),$settings.IndexOf('[Header("3~5인')-$settings.IndexOf('public bool useCombatDash'))
$fields=([regex]::Matches($section,'public (?:bool|float|Vector2) \w+[^;]+;')|ForEach-Object Value)-join "`n"
$code="using System;using System.Collections.Generic;using UnityEngine;using Random=UnityEngine.Random;`n"+$unity
$code+='public class MatchBotSettings {public static MatchBotSettings Default=new();'+$fields+'}'
$code+=@'
public enum MagicType {Fire}
public struct MagicStatEntry {public bool requiresTarget; public float projectileSpeed,projectileTurnSpeed;}
public class MagicStatTable {
 public MagicStatEntry stats=new(){requiresTarget=true,projectileSpeed=85,projectileTurnSpeed=450};
 public float homingTurnReferenceSpeed=67.5f,homingTurnSpeedExponent=2;
 public MagicStatEntry GetStats(MagicType type)=>stats;
}
public class MagicProjectile { public static bool UsesFlightExhaustion(MagicType m,MagicStatTable t)=>true;
__TURN__
}
public class Broom {public float maxSpeed=39,boostMultiplier=1.5f;}
public class Equipment {public Broom GetBroomStats(int broom)=>new();}
public class MagicBalanceLabActor {
 public Transform transform=new();public Vector3 LockAimPoint=>transform.position;public Vector3 Velocity;
 public int DashCharges=3,Broom;public float Speed=26;public bool IsBoosting,CanManeuver=true;
}
public class GameObject {public bool activeSelf=true;}
public class MagicBalanceLabShot {
 public GameObject gameObject=new();public Transform transform=new();public bool falling;
 public MagicBalanceLabSession session;public MagicBalanceLabActor target;public MagicType magic;
 public float fraction=1;public Vector3 direction=Vector3.forward;public int LaunchSequence=1;
 __LIVE__
 __THREAT__
}
public sealed partial class MagicBalanceLabSession {
 public MagicBalanceLabActor bot=new();public MagicStatTable Magic=new();public Equipment Equipment=new();
 public List<MagicBalanceLabShot> shots=new();public bool Clear=true;public int SightQueries;
 private bool LabEvasionRouteClear(Vector3 h,float l)=>Clear;
 private bool LabEvasionSight(Vector3 from,Vector3 to){SightQueries++;return Clear;}
 public bool HasPlan=>evasionShot!=null; public bool NeedsDash=>evasionNeedsDash;
 public Vector3 Heading;public bool Dash;
 public void Tick(float dt=.1f){Heading=bot.transform.forward;if(useReactiveBotEvasion)UpdateReactiveBot(dt,ref Heading,out Dash);}
 public void Reset()=>ResetReactiveBot();
 public MagicBalanceLabShot Shot(float fraction=1) {
  var shot=new MagicBalanceLabShot{session=this,target=bot,fraction=fraction};
  shot.transform.position=new(0,0,-60);shots.Add(shot);return shot;
 }
}
'@
$shot=Get-Content Assets/Script/MagicBalanceLabShot.cs -Raw
$live=[regex]::Match($shot,'(?ms)^    public bool IsLiveHomingThreat\(.*?;').Value
$code=$code.Replace('__TURN__',(Method (Get-Content Assets/Script/MagicProjectile.cs -Raw) FlightTurnSpeed)).Replace('__LIVE__',$live).Replace('__THREAT__',(Method $shot TryGetHomingThreat))
$policy=(Get-Content Assets/Script/BotHomingEvasion.cs -Raw)-replace '(?m)^using UnityEngine;\r?\n',''
$evasion=Get-Content Assets/Script/MagicBalanceLabSession.Evasion.cs -Raw
$evasion=$evasion.Substring(0,$evasion.IndexOf('    private bool LabEvasionRouteClear'))+"}`n"
$evasion=$evasion -replace '(?m)^using UnityEngine;\r?\n','' -replace '(?m)^    private readonly RaycastHit\[\].*\r?\n',''
$code+=$policy+$evasion
$code+=@'
public static class Checks {
 static int n;static void Check(bool ok,string name){n++;if(!ok)throw new Exception(name);}
 static MagicBalanceLabSession Lab(){MatchBotSettings.Default=new(){evasionUseChance=1,evasionMistakeChance=0,dashUseChance=1,dashMistakeChance=0};return new();}
 public static string Run(){
  var lab=Lab();var shot=lab.Shot();
  Check(shot.TryGetHomingThreat(lab.bot,out var eta,out var speed,out var turn,out var initial)&&speed==85,"Fresh projectile observed");
  lab.Magic.stats.projectileSpeed=110;
  Check(shot.TryGetHomingThreat(lab.bot,out var eta2,out speed,out var turn2,out initial)&&speed==110&&turn2>turn&&eta2<eta,"Live speed changes arrival and turn rate");
  shot.fraction=.7f;
  Check(shot.TryGetHomingThreat(lab.bot,out eta,out speed,out turn,out initial)&&Math.Abs(turn/initial-.49f)<.001f,"Deceleration changes current turn strength");
  lab.Tick();Check(lab.HasPlan&&!lab.NeedsDash&&lab.Heading.z<0,"Weakened shot triggers sharp turn");
  var heading=lab.Heading;int calls=Random.Calls;lab.Tick();Check(lab.Heading==heading&&Random.Calls==calls,"Plan holds without per-frame reroll");
  shot.transform.position=new(0,0,10);lab.Tick();Check(lab.HasPlan,"Passing projectile does not interrupt committed turn");
  shot.LaunchSequence++;lab.Tick();Check(!lab.HasPlan,"Pooled relaunch invalidates previous plan");
  lab=Lab();shot=lab.Shot();lab.Tick();Check(lab.HasPlan&&lab.NeedsDash&&!lab.Dash,"Fresh shot plans dash, waits for alignment");
  lab.bot.transform.forward=lab.Heading;lab.Tick();Check(lab.Dash&&lab.bot.DashCharges==3,"Dash uses normal actor consumption, no extra charge grants");
  lab=Lab();lab.bot.DashCharges=0;lab.Shot();lab.Tick();Check(!lab.HasPlan,"No free dash when charges empty");
  lab=Lab();shot=lab.Shot(.7f);lab.Clear=false;lab.Tick();Check(!lab.HasPlan,"Occlusion excludes threats");
  lab=Lab();shot=lab.Shot(.7f);shot.target=new();lab.Tick();Check(!lab.HasPlan,"Other target ignored");
  lab=Lab();shot=lab.Shot(.7f);shot.falling=true;lab.Tick();Check(!lab.HasPlan,"Exhausted shot ignored");
  lab=Lab();shot=lab.Shot(.7f);lab.bot.CanManeuver=false;lab.Tick();Check(!lab.HasPlan,"Bound bot cannot evade");
  lab=Lab();lab.Shot(.7f);MatchBotSettings.Default.evasionMistakeChance=1;lab.Tick();
  Check(lab.HasPlan&&lab.Heading==Vector3.forward,"Mistake delays the turn");
  lab.Tick(.5f);Check(lab.Heading.z>.8f&&lab.Heading!=Vector3.forward,"Mistake uses shallow turn");
  lab=Lab();lab.Shot(.7f);MatchBotSettings.Default.evasionUseChance=0;lab.Tick();calls=Random.Calls;lab.Tick();
  Check(!lab.HasPlan&&Random.Calls==calls,"Skipped evasion respects decision cooldown");
  lab=Lab();lab.Shot(.7f);lab.Tick();int scans=lab.SightQueries;lab.Tick();Check(lab.SightQueries==scans,"Active plan does not rescan projectiles");
  lab.Reset();Check(!lab.HasPlan,"Reset clears plan");
  return n+" offline live-threat and evasion checks passed";
 }
}
'@
Add-Type -TypeDefinition $code -WarningAction SilentlyContinue
[Checks]::Run()
