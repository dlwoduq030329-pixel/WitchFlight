$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$tour = Get-Content Assets/Script/Camera/BattleMapTour.cs -Raw
$flag = Get-Content Assets/Script/BattleFlag.cs -Raw
function Method($text,$name) {
 $m=[regex]::Match($text,'(?ms)^    (?:internal|private) (?:static )?\w+ '+$name+'\([^{}]*\)\r?\n    \{.*?^    \}')
 if(!$m.Success){throw "Missing $name"};$m.Value.Replace('private ','public ').Replace('internal ','public ')
}
$enum = [regex]::Match($flag,'public enum BattleStartPhase[^\r\n]+').Value
$duration = [regex]::Match($flag,'    public float TourDuration =>[^;]+;').Value
$stub=@'
using System;
namespace TourChecks {
 public static class Mathf {
  public static float Max(float a,float b)=>Math.Max(a,b);
  public static int Min(int a,int b)=>Math.Min(a,b);
  public static float Clamp01(float x)=>Math.Clamp(x,0,1);
  public static int FloorToInt(float x)=>(int)Math.Floor(x);
 }
 public class Clock {public float time;}
 public struct TickTimer {
  public static TickTimer None => default;
  public float end;public bool Expired(Clock c)=>c.time>=end;
  public static TickTimer CreateFromSeconds(Clock c,float t)=>new(){end=c.time+t};
 }
 public class BattleManager {public static BattleManager Instance=new();public bool AutoStartWhenReady,IsTraining;public void startGame(){}}
 public static class CatTutorialDirector {public static bool TrainingGuideRequired=false;}
 public class Probe {
  public int RoundNumber=1; public bool mapTourStarted; public float TourMoveSeconds=3,TourFadeSeconds=.6f,introSeconds=5,matchSeconds=160;
  public BattleStartPhase Phase=BattleStartPhase.Intro;public TickTimer PhaseTimer=new(){end=5};
  public Clock Runner=new();public bool ready=true,startRequested=true;public int resets,starts;public int WorldPosition;
  bool AreAllPlayersReady()=>ready;
  void ResetPreparation(){Phase=BattleStartPhase.WaitingForPlayers;resets++;}
  void BeginMatch(int p,float t){Phase=BattleStartPhase.Playing;starts++;}
'@
$stub += $duration + (Method $tour 'SampleTimeline') + (Method $flag 'TickPreparation') + (Method $flag 'BeginTrainingGuideOrCountdown')
$stub += '}' + $enum
$stub += @'
 public static class Tests {
  static int n;static void Check(bool b,string s){if(!b)throw new Exception(s);n++;}
  static bool Near(float a,float b)=>Math.Abs(a-b)<.0001;
  public static int Run(){
   var p=new Probe();Check(Near(p.TourDuration,13.2f),"three shots plus gameplay reveal duration");
   p.Runner.time=4;p.TickPreparation();Check(p.Phase==BattleStartPhase.Intro,"VS remains until timer expires");
   p.Runner.time=5;p.TickPreparation();Check(p.Phase==BattleStartPhase.MapTour&&Near(p.PhaseTimer.end,18.2f),"VS transitions to synchronized tour");
   p.Runner.time=18.3f;p.TickPreparation();Check(p.Phase==BattleStartPhase.Countdown&&Near(p.PhaseTimer.end,21.3f),"tour ends before full three second countdown");
   p.Runner.time=21.4f;p.TickPreparation();Check(p.Phase==BattleStartPhase.Playing&&p.starts==1,"gameplay begins after countdown");
   p=new Probe{TourMoveSeconds=0};p.Runner.time=5;p.TickPreparation();Check(p.Phase==BattleStartPhase.Countdown,"missing/disabled tour skips safely");
   p=new Probe{Phase=BattleStartPhase.MapTour,ready=false};p.TickPreparation();Check(p.Phase==BattleStartPhase.WaitingForPlayers&&p.resets==1,"disconnect during tour returns to waiting");
   p=new Probe{Phase=BattleStartPhase.WaitingForPlayers,TourMoveSeconds=0};BattleManager.Instance.IsTraining=true;
   p.TickPreparation();Check(p.Phase==BattleStartPhase.Countdown&&Near(p.PhaseTimer.end,3),"training toggle off skips tour and keeps full countdown");
   p=new Probe{Phase=BattleStartPhase.WaitingForPlayers};p.TickPreparation();
   Check(p.Phase==BattleStartPhase.MapTour&&Near(p.PhaseTimer.end,13.2f),"training toggle on begins tour directly without VS");
   p.Runner.time=13.3f;p.TickPreparation();Check(p.Phase==BattleStartPhase.Countdown&&Near(p.PhaseTimer.end,16.3f),"training tour restores full countdown");
   p.Runner.time=16.4f;p.TickPreparation();Check(p.Phase==BattleStartPhase.Playing,"training starts after optional tour");
   BattleManager.Instance.IsTraining=false;
   for(int round=2;round<=3;round++) {
    p=new Probe{RoundNumber=round,Phase=BattleStartPhase.WaitingForPlayers};
    p.TickPreparation();Check(p.Phase==BattleStartPhase.Intro&&Near(p.PhaseTimer.end,5),"later round still has full VS "+round);
    p.Runner.time=5;p.TickPreparation();Check(p.Phase==BattleStartPhase.Countdown&&Near(p.PhaseTimer.end,8),"later round skips tour and keeps 321 "+round);
    p.Runner.time=8;p.TickPreparation();Check(p.Phase==BattleStartPhase.Playing&&p.starts==1,"later round begins after VS and 321 "+round);
   }
   p=new Probe();p.Runner.time=5;p.TickPreparation();p.ready=false;p.TickPreparation();p.ready=true;
   p.TickPreparation();Check(p.Phase==BattleStartPhase.Intro,"readiness retry repeats VS");
   p.Runner.time=10;p.TickPreparation();Check(p.Phase==BattleStartPhase.Countdown,"already-started tour cannot replay after readiness retry");
   for(int i=0;i<3;i++){
    float start=i*4.2f;
    Probe.SampleTimeline(start+.001f,3,.6f,out int shot,out float t,out float a);Check(shot==i&&t==0&&a>.99f,"cut while black "+i);
    Probe.SampleTimeline(start+.3f,3,.6f,out shot,out t,out a);Check(shot==i&&t==0&&Near(a,.5f),"fade in stationary start "+i);
    Probe.SampleTimeline(start+2.1f,3,.6f,out shot,out t,out a);Check(shot==i&&Near(t,.5f)&&a==0,"travel between transforms "+i);
    Probe.SampleTimeline(start+3.9f,3,.6f,out shot,out t,out a);Check(shot==i&&t==1&&Near(a,.5f),"fade out at end "+i);
   }
   Probe.SampleTimeline(12.601f,3,.6f,out int final,out float travel,out float black);Check(final==3&&black>.99f,"restore gameplay under black");
   Probe.SampleTimeline(12.9f,3,.6f,out final,out travel,out black);Check(final==3&&Near(black,.5f),"reveal gameplay before countdown");
   Probe.SampleTimeline(13.2f,3,.6f,out final,out travel,out black);Check(final==3&&black<.0001f,"fully visible for 3-2-1");
   return n;
  }
 }
}
'@
Add-Type -TypeDefinition $stub
"Map tour phase and fade checks passed: $([TourChecks.Tests]::Run())"
$scene=Get-Content Assets/Scenes/Battle.unity -Raw
$ids=[regex]::Matches($scene,'(?m)^--- !u!\d+ &(\d+)') | ForEach-Object {$_.Groups[1].Value}
if(($ids|Group-Object|Where-Object Count -gt 1).Count){throw 'Duplicate scene object IDs'}
foreach($i in 1..6){
 $name='Point{0:D2}' -f $i
 if(!$scene.Contains("m_Name: $name")){throw "Missing $name"}
 $id=981026101+$i*10
 if([regex]::Matches($scene,"fileID: $id\}").Count -ne 3){throw "Broken parent/component/point references: $name"}
}
if(!$scene.Contains('viewCamera: {fileID: 1331985548}')){throw 'Tour camera not assigned'}
'Six scene transforms, references and unique scene IDs verified.'
