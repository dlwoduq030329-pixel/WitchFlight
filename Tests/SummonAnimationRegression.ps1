# Production animation selection against a controlled Playables clock; actual rig playback requires Unity.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$source = Get-Content Assets/Script/SummonAnimationPlayer.cs -Raw
$stubs = @'
namespace UnityEngine {
 public class AnimationClip { public string name; public float length; public AnimationClip(string name,float length){this.name=name;this.length=length;} }
 public class Animator { public bool applyRootMotion=true,isActiveAndEnabled=true; }
 public static class Mathf {
  public static float Max(float a,float b)=>System.Math.Max(a,b);
  public static float Min(float a,float b)=>System.Math.Min(a,b);
  public static float Repeat(float a,float b)=>a-(float)System.Math.Floor(a/b)*b;
 }
}
namespace UnityEngine.Playables {
 public enum DirectorUpdateMode {Manual}
 public sealed class GraphState { public bool Valid=true; public int Evaluations,Switches; public UnityEngine.Animations.ClipState Current; }
 public struct PlayableGraph {
  public static int Created; public static GraphState Last; public GraphState State;
  public static PlayableGraph Create(string name){Created++;Last=new();return new(){State=Last};}
  public bool IsValid()=>State!=null&&State.Valid;
  public void SetTimeUpdateMode(DirectorUpdateMode mode){} public void Play(){}
  public void Evaluate(float dt){State.Evaluations++;}
  public void Destroy(){State.Valid=false;}
 }
}
namespace UnityEngine.Animations {
 public class ClipState {public UnityEngine.AnimationClip Clip;public double Time,Speed;}
 public struct AnimationClipPlayable {
  public ClipState State;
  public static AnimationClipPlayable Create(UnityEngine.Playables.PlayableGraph graph,UnityEngine.AnimationClip clip)=>new(){State=new(){Clip=clip}};
  public void SetTime(double time){State.Time=time;} public void SetSpeed(double speed){State.Speed=speed;}
 }
 public struct AnimationPlayableOutput {
  public UnityEngine.Playables.GraphState State;
  public static AnimationPlayableOutput Create(UnityEngine.Playables.PlayableGraph graph,string name,UnityEngine.Animator animator)=>new(){State=graph.State};
  public void SetSourcePlayable(AnimationClipPlayable clip){State.Current=clip.State;State.Switches++;}
 }
}
public struct MagicStatEntry {
 public UnityEngine.AnimationClip summonMoveAnimation,summonAttackAnimation,summonDeathAnimation;
}
'@
$tests = @'
public static class SummonAnimationChecks {
 static int checks;
 static void Check(bool value,string message){if(!value)throw new System.Exception(message);checks++;}
 static bool Near(double a,double b)=>System.Math.Abs(a-b)<.001;
 public static int Run(){
  var animator=new UnityEngine.Animator();
  int created=UnityEngine.Playables.PlayableGraph.Created;
  var empty=new SummonAnimationPlayer(animator,default,0);empty.Render(true,1,.2f);
  Check(empty.PlayDeath()==0&&UnityEngine.Playables.PlayableGraph.Created==created,"Empty slots preserve fallback without a graph");
  empty.Dispose();Check(animator.applyRootMotion,"Empty slots do not mutate authored animator");
  var stats=new MagicStatEntry{summonMoveAnimation=new("Move",1),summonAttackAnimation=new("Attack",.8f),summonDeathAnimation=new("Death",.6f)};
  var noAnimator=new SummonAnimationPlayer(null,stats,0);noAnimator.Render(true,1,1);Check(noAnimator.PlayDeath()==0,"Missing animator is safe");noAnimator.Dispose();
  var player=new SummonAnimationPlayer(animator,stats,7);
  var graph=UnityEngine.Playables.PlayableGraph.Last;
  Check(!animator.applyRootMotion,"Root motion cannot fight network movement");
  player.Render(true,7,.25f);
  Check(graph.Current.Clip.name=="Move"&&Near(graph.Current.Time,.25),"Initial sequence is not a historical attack");
  int switches=graph.Switches;player.Render(true,7,.25f);
  Check(graph.Switches==switches&&Near(graph.Current.Time,.5),"Stable movement does not restart each frame");
  player.Render(true,7,.75f);Check(Near(graph.Current.Time,.25),"Movement loops");
  player.Render(false,7,.25f);Check(Near(graph.Current.Time,0),"Stopped familiar holds neutral move pose");
  player.Render(true,8,.25f);Check(graph.Current.Clip.name=="Attack"&&Near(graph.Current.Time,0),"Successful shot starts attack once");
  player.Render(true,8,.25f);Check(graph.Current.Clip.name=="Attack"&&Near(graph.Current.Time,.25),"Movement cannot interrupt attack");
  player.Render(false,8,.4f);Check(Near(graph.Current.Time,.5),"Attack progresses while not moving");
  player.Render(true,8,.1f);Check(graph.Current.Clip.name=="Move","Attack finishes and resumes move without another event");
  player.Render(false,9,.1f);player.Render(false,10,.1f);
  Check(graph.Current.Clip.name=="Attack"&&Near(graph.Current.Time,0),"New actual shot can restart attack");
  Check(Near(player.PlayDeath(),.6)&&graph.Current.Clip.name=="Death"&&Near(graph.Current.Time,0),"Death interrupts attack and reports its visual lifetime");
  player.Render(true,11,.3f);Check(graph.Current.Clip.name=="Death"&&Near(graph.Current.Time,.3),"Death ignores subsequent movement and attack events");
  player.Render(true,12,2);Check(graph.Current.Time<.6&&graph.Current.Time>.59,"Death clamps instead of looping");
  Check(player.PlayDeath()==0,"Duplicate destruction cannot restart death");
  player.Dispose();Check(!graph.Valid&&animator.applyRootMotion,"Graph is disposed and animator settings restored");player.Dispose();
  stats.summonAttackAnimation=null;stats.summonDeathAnimation=null;
  player=new SummonAnimationPlayer(animator,stats,0);graph=UnityEngine.Playables.PlayableGraph.Last;
  player.Render(true,1,.2f);Check(graph.Current.Clip.name=="Move","Unassigned attack safely keeps movement");
  Check(player.PlayDeath()==0,"Unassigned death requests immediate visual removal");player.Dispose();
  animator.isActiveAndEnabled=false;
  stats.summonDeathAnimation=new("Death",1);player=new SummonAnimationPlayer(animator,stats,0);
  Check(player.PlayDeath()==0,"Disabled animator must not leave a motionless death visual");player.Dispose();
  return checks;
 }
}
'@
Add-Type -TypeDefinition ($source + $stubs + $tests)
"Summon animation behavior checks passed: $([SummonAnimationChecks]::Run())"

$visual=Get-Content Assets/Script/MagicSummonVisual.cs -Raw
$construct=Get-Content Assets/Script/MagicProjectile.Constructs.cs -Raw
$projectile=Get-Content Assets/Script/MagicProjectile.cs -Raw
if($visual-notmatch 'if \(retiring\) return;' -or $visual-notmatch 'transform.SetParent\(null, true\)' -or $visual-notmatch 'Destroy\(gameObject, seconds\)' -or $visual-notmatch 'animationPlayer\?\.Dispose\(\)') {throw 'Death visual detachment/idempotence/cleanup missing'}
if($construct-notmatch '\[Networked\] public bool SummonMoving' -or $construct-notmatch '\[Networked\] public int SummonAttackSequence' -or $construct-notmatch 'summonVisual.SetMotion\(SummonMoving, SummonAttackSequence\)') {throw 'Remote motion presentation is not connected'}
if($projectile-notmatch 'if \(IsConstruct\) ClearConstructVisual\(true\)' -or $construct-notmatch 'if \(playSummonDeath\) summonVisual.Retire\(\)') {throw 'Destruction animation not connected'}
'Replicated presentation and detached non-colliding death visual contracts passed.'
