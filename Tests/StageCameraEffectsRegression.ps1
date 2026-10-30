# Production camera state/math against stubs. Visual comfort still needs Unity Play Mode.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$follow = Get-Content Assets/Script/Camera/CameraFollow.cs -Raw
$effects = Get-Content Assets/Script/Camera/SpeedCameraEffects.cs -Raw
$preview = Get-Content Assets/Script/Camera/CameraFollow.LockOnPreview.cs -Raw
function Method([string]$source,[string]$name) {
    $m = [regex]::Match($source,'(?ms)^    private (?:static )?[\w<>]+ '+$name+'\([^{}]*?\)\r?\n    \{.*?^    \}')
    if (!$m.Success) { throw "Missing production method: $name" }
    $m.Value -replace '^    private ', '    public '
}
$stubs = @'
using System;
namespace CameraChecks {
 public static class Time {public static float unscaledTime,unscaledDeltaTime=.1f;public static int frameCount;}
 public static class Mathf {
  public static float Max(float a,float b)=>Math.Max(a,b);public static float Abs(float a)=>Math.Abs(a);public static float Exp(float a)=>(float)Math.Exp(a);
  public static float Min(float a,float b)=>Math.Min(a,b);
  public const float Deg2Rad=(float)Math.PI/180f;public static float Tan(float a)=>(float)Math.Tan(a);
  public static float Clamp(float a,float b,float c)=>Math.Clamp(a,b,c);public static float Clamp01(float a)=>Clamp(a,0,1);
  public static float Lerp(float a,float b,float t)=>a+(b-a)*Clamp01(t);
  public static float InverseLerp(float a,float b,float v)=>a==b?0:Clamp01((v-a)/(b-a));
  public static float SmoothStep(float a,float b,float t){t=Clamp01(t);return a+(b-a)*t*t*(3-2*t);}
 }
 public struct Vector3 {
  public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
  public static Vector3 one=>new(1,1,1);
  public static Vector3 operator+(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
  public static Vector3 operator*(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);
  public static Vector3 Scale(Vector3 a,Vector3 b)=>new(a.x*b.x,a.y*b.y,a.z*b.z);
 }
 public struct Quaternion {public static Quaternion Euler(Vector3 v)=>default;public static Quaternion operator*(Quaternion a,Quaternion b)=>default;public static Vector3 operator*(Quaternion a,Vector3 b)=>b;}
 public struct Matrix4x4 {public Matrix4x4 inverse=>this;public static Matrix4x4 Scale(Vector3 v)=>default;public static Matrix4x4 TRS(Vector3 p,Quaternion q,Vector3 s)=>default;public static Matrix4x4 operator*(Matrix4x4 a,Matrix4x4 b)=>default;}
 public struct ScriptableRenderContext {}
 public class Transform {public Vector3 position;public Quaternion rotation;}
 public class Camera {
  public float fieldOfView=60;public int MatrixWrites,Resets;
  public Matrix4x4 worldToCameraMatrix {set{MatrixWrites++;}}
  public void ResetWorldToCameraMatrix(){Resets++;}
 }
 public class Obj {public bool IsValid=true,HasInputAuthority=true;}
 public class Player {public Obj Object=new();public bool IsAlive=true,IsReturningToMap,IsBoosting;public int CurrentSpeedStage;}
 public static class CombatPresentation {public static bool MenuOpen;}
 public class BattleManager {public static BattleManager Instance=new();public bool IsGameplayActive=true;}
 public class FollowProbe {
  public float LockOnPreviewFovReduction;
  public bool isActiveAndEnabled=true,enableTopSpeedKick=true,enableSpeedShake=true,IsBoundaryPresentationActive;
  public Player targetPlayer=new();public Camera viewCamera=new();public Transform transform=new();
  public int lastKickSpeedStage=int.MinValue,speedShakeUpdatedFrame=-1;
  public float topSpeedKickStartedAt=float.NegativeInfinity,topSpeedKickDistance=.45f,boostKickDistance=.8f,topSpeedKickOutSeconds=.12f,topSpeedKickReturnSeconds=.55f;
  public float topSpeedKickStartDistance,topSpeedKickPeakDistance;public bool lastKickBoosting;
  public float speedShakeIntensity,speedShakeStage3Intensity=1,speedShakeTransitionSpeed=5,speedShakeFrequency=6;
  public Vector3 speedShakePositionAmplitude=new(.006f,.004f,0),speedShakeRotationAmplitude=new(.035f,.025f,0);
  public bool speedShakeViewApplied;
  static float ShakeNoise(float p,float s)=>.5f;
  public void Render(){BeginSpeedShakeRendering(default,viewCamera);}
'@
$followMethods = @('ShouldApplySpeedShake','ResetTopSpeedKick','UpdateTopSpeedKick','EvaluateTopSpeedKick',
    'BeginSpeedShakeRendering','EndSpeedShakeRendering','RestoreSpeedShakeView','ResetSpeedShake','GetExponentialBlend') |
    ForEach-Object { Method $follow $_ }
$followMethods += Method $preview 'BlendLockOnPreview'
$followMethods += Method $preview 'LockOnScreenOffset'
$followMethods += Method $preview 'LockOnPreviewProgress'
$fovProbe = @'
 }
 public class FovProbe {
  public FollowProbe cameraFollow;
  public Camera viewCamera=new();public float originalFieldOfView=60,fieldOfViewIncrease,stage1FieldOfViewIncrease=3,stage2FieldOfViewIncrease=6,extraFieldOfView=10,fieldOfViewTransitionSpeed=5;
'@ + (Method $effects 'UpdateFieldOfView') + (Method $effects 'StageFieldOfViewIncrease') +
    (Method $effects 'FastTurnWeight') + (Method $effects 'RecoverTurnBlur') + '}'
$checks = @'
 public static class Tests {
  static int n;static void Check(bool ok,string msg){if(!ok)throw new Exception(msg);n++;}static bool Near(float a,float b)=>Math.Abs(a-b)<.001f;
  public static int Run(){
   Check(FollowProbe.EvaluateTopSpeedKick(float.PositiveInfinity,.45f,.12f,.55f)==0&&FollowProbe.EvaluateTopSpeedKick(-1,.45f,.12f,.55f)==0,"unstarted/invalid kick stays neutral");
   Check(Near(FollowProbe.EvaluateTopSpeedKick(0,.45f,.12f,.55f),0),"kick starts continuously at zero");
   Check(Near(FollowProbe.EvaluateTopSpeedKick(.06f,.45f,.12f,.55f),.225f),"kick retreats smoothly");
   Check(Near(FollowProbe.EvaluateTopSpeedKick(.12f,.45f,.12f,.55f),.45f),"peak retreat matches Inspector distance");
   Check(Near(FollowProbe.EvaluateTopSpeedKick(.395f,.45f,.12f,.55f),.225f),"camera returns smoothly");
   Check(FollowProbe.EvaluateTopSpeedKick(.67f,.45f,.12f,.55f)==0&&FollowProbe.EvaluateTopSpeedKick(10,.45f,.12f,.55f)==0,"kick returns exactly to base distance");
   for(int i=0;i<100;i++){float v=FollowProbe.EvaluateTopSpeedKick(i*.01f,.45f,.12f,.55f);Check(v>=0&&v<=.45001f,"kick cannot overshoot "+i);}
   var p=new FollowProbe();Time.unscaledTime=0;p.targetPlayer.CurrentSpeedStage=2;Check(p.UpdateTopSpeedKick(true)==0,"initial target does not kick");
   p.targetPlayer.CurrentSpeedStage=3;p.UpdateTopSpeedKick(true);Time.unscaledTime=.12f;Check(Near(p.UpdateTopSpeedKick(true),.45f),"2-to-3 transition starts one pulse");
   float start=p.topSpeedKickStartedAt;p.UpdateTopSpeedKick(true);Check(p.topSpeedKickStartedAt==start,"multiple calls in a frame do not retrigger");
   p.targetPlayer.CurrentSpeedStage=2;Check(Near(p.UpdateTopSpeedKick(true),.45f),"downshift does not snap an active pulse");
   Time.unscaledTime=.2f;float beforeRetrigger=p.UpdateTopSpeedKick(true);p.targetPlayer.CurrentSpeedStage=3;
   Check(Near(p.UpdateTopSpeedKick(true),beforeRetrigger)&&Near(p.topSpeedKickStartedAt,.2f),"new acceleration smoothly retriggers from the current distance");
   Check(Near(p.topSpeedKickPeakDistance,.45f),"rapid up/down never adds displacement to the configured peak");
   Time.unscaledTime=2;Check(p.UpdateTopSpeedKick(true)==0,"holding top stage never loops");
   p.targetPlayer.CurrentSpeedStage=2;p.UpdateTopSpeedKick(true);p.targetPlayer.CurrentSpeedStage=3;p.UpdateTopSpeedKick(true);Check(Near(p.topSpeedKickStartedAt,2),"new entry after completion can kick again");
   Check(p.UpdateTopSpeedKick(false)==0&&p.lastKickSpeedStage==int.MinValue,"menu/death/boundary/snap reset pulse tracking");
   Check(p.UpdateTopSpeedKick(true)==0,"returning while already in stage 3 does not create a fake kick");
   p.targetPlayer.Object.HasInputAuthority=false;Check(p.UpdateTopSpeedKick(true)==0,"remote player cannot drive local camera kick");
   p.targetPlayer.Object.HasInputAuthority=true;p.enableTopSpeedKick=false;Check(p.UpdateTopSpeedKick(true)==0,"kick toggle disables effect");
   p=new FollowProbe();Time.unscaledTime=0;p.targetPlayer.CurrentSpeedStage=0;p.UpdateTopSpeedKick(true);
   for(int stage=1;stage<=3;stage++){
    Time.unscaledTime=stage;p.targetPlayer.CurrentSpeedStage=stage;Check(Near(p.UpdateTopSpeedKick(true),0),"stage "+stage+" starts continuously");
    Time.unscaledTime=stage+.12f;Check(Near(p.UpdateTopSpeedKick(true),.45f),"every forward stage increase reaches ordinary kick distance");
    Time.unscaledTime=stage+.68f;Check(Near(p.UpdateTopSpeedKick(true),0),"each stage pulse returns independently of held speed");
   }
   Time.unscaledTime=4;p.targetPlayer.IsBoosting=true;Check(p.UpdateTopSpeedKick(true)==0,"stage 3 Shift press starts a larger pulse without a snap");
   Time.unscaledTime=4.12f;Check(Near(p.UpdateTopSpeedKick(true),.8f),"top-stage boost has a larger configurable peak");
   Time.unscaledTime=5;Check(p.UpdateTopSpeedKick(true)==0,"holding Shift does not loop kick");
   p.targetPlayer.IsBoosting=false;p.UpdateTopSpeedKick(true);p.targetPlayer.IsBoosting=true;p.UpdateTopSpeedKick(true);
   Time.unscaledTime=5.12f;Check(Near(p.UpdateTopSpeedKick(true),.8f),"new actual boost press retriggers stronger kick");
   p.targetPlayer.IsBoosting=false;Check(Near(p.UpdateTopSpeedKick(true),.8f),"release does not snap distance to zero");
   Time.unscaledTime=6;Check(p.UpdateTopSpeedKick(true)==0,"released boost smoothly returns to baseline");
   p=new FollowProbe();Time.unscaledTime=0;p.targetPlayer.CurrentSpeedStage=2;p.UpdateTopSpeedKick(true);
   p.targetPlayer.CurrentSpeedStage=3;p.UpdateTopSpeedKick(true);Time.unscaledTime=.06f;float half=p.UpdateTopSpeedKick(true);
   p.targetPlayer.IsBoosting=true;Check(Near(p.UpdateTopSpeedKick(true),half),"boost during stage pulse starts from current offset");
   Time.unscaledTime=.18f;Check(Near(p.UpdateTopSpeedKick(true),.8f),"boost upgrades an unfinished ordinary pulse immediately, without waiting");
   Check(p.topSpeedKickPeakDistance==.8f,"overlapping stage and boost never sum both peaks");
   p=new FollowProbe();Time.unscaledTime=0;p.targetPlayer.CurrentSpeedStage=2;p.targetPlayer.IsBoosting=true;p.UpdateTopSpeedKick(true);
   p.targetPlayer.CurrentSpeedStage=3;p.UpdateTopSpeedKick(true);Time.unscaledTime=.12f;
   Check(Near(p.UpdateTopSpeedKick(true),.8f),"entering stage 3 with boost held uses only the boost peak");
   p=new FollowProbe();p.targetPlayer.CurrentSpeedStage=3;p.targetPlayer.IsBoosting=true;
   Check(p.UpdateTopSpeedKick(true)==0,"binding an already boosting player never invents a kick");
   p=new FollowProbe();Time.unscaledTime=0;p.targetPlayer.CurrentSpeedStage=1;p.UpdateTopSpeedKick(true);
   p.targetPlayer.IsBoosting=true;p.UpdateTopSpeedKick(true);Time.unscaledTime=.12f;Check(p.UpdateTopSpeedKick(true)==0,"Shift alone below stage 3 has no extra distance pulse");
   p.targetPlayer.CurrentSpeedStage=2;p.UpdateTopSpeedKick(true);Time.unscaledTime=.24f;
   Check(Near(p.UpdateTopSpeedKick(true),.45f),"lower stage rise with Shift still uses ordinary distance");
   p=new FollowProbe();Time.unscaledTime=0;p.targetPlayer.CurrentSpeedStage=-3;p.UpdateTopSpeedKick(true);
   p.targetPlayer.CurrentSpeedStage=-2;p.UpdateTopSpeedKick(true);Time.unscaledTime=1;Check(p.UpdateTopSpeedKick(true)==0,"slowing reverse speed is not forward acceleration");
   p.targetPlayer.CurrentSpeedStage=0;p.UpdateTopSpeedKick(true);Time.unscaledTime=2;Check(p.UpdateTopSpeedKick(true)==0,"reverse-to-stop transition has no kick");
   p=new FollowProbe();Time.unscaledTime=0;p.targetPlayer.CurrentSpeedStage=0;p.UpdateTopSpeedKick(true);
   for(int i=1;i<=100;i++){Time.unscaledTime=i*.02f;p.targetPlayer.CurrentSpeedStage=(i%2==0)?0:1;float d=p.UpdateTopSpeedKick(true);Check(d>=0&&d<=.45001f,"rapid repeat stays bounded "+i);}
   for(int stage=-3;stage<=3;stage++)foreach(bool boost in new[]{false,true})
    Check(FollowProbe.ShouldApplySpeedShake(stage,boost)==(stage==3&&boost),"shake gate stage="+stage+" boost="+boost);
   p=new FollowProbe();p.targetPlayer.CurrentSpeedStage=3;p.Render();Check(p.viewCamera.MatrixWrites==0,"top stage alone never shakes");
   p.targetPlayer.IsBoosting=true;Time.frameCount=1;p.Render();Check(p.speedShakeViewApplied&&p.viewCamera.MatrixWrites==1&&p.speedShakeIntensity>0,"top stage with active boost applies render-only shake");
   float intensity=p.speedShakeIntensity;p.Render();Check(Near(p.speedShakeIntensity,intensity),"multiple camera renders do not accelerate fade");
   Check(Near(p.transform.position.x,0)&&Near(p.transform.position.y,0),"shake never changes camera Transform or steering pose");
   p.EndSpeedShakeRendering(default,p.viewCamera);Check(!p.speedShakeViewApplied&&p.viewCamera.Resets>0,"render matrix restored at end of camera render");
   p.targetPlayer.IsBoosting=false;p.Render();Check(p.speedShakeIntensity==0&&!p.speedShakeViewApplied,"Shift release or mana exhaustion stops shake immediately");
   p.targetPlayer.IsBoosting=true;p.targetPlayer.CurrentSpeedStage=2;p.Render();Check(!p.speedShakeViewApplied,"stage 2 boost is shake-free");
   p.targetPlayer.CurrentSpeedStage=3;CombatPresentation.MenuOpen=true;p.Render();Check(!p.speedShakeViewApplied,"menu disables boost shake");CombatPresentation.MenuOpen=false;
   p.targetPlayer.IsAlive=false;p.Render();Check(!p.speedShakeViewApplied,"death disables boost shake");p.targetPlayer.IsAlive=true;
   p.IsBoundaryPresentationActive=true;p.Render();Check(!p.speedShakeViewApplied,"boundary presentation disables boost shake");
   var f=new FovProbe();foreach(int stage in new[]{-3,0})Check(FovProbe.StageFieldOfViewIncrease(stage,3,6,10)==0,"stationary/reverse keeps base FOV");
   Check(FovProbe.StageFieldOfViewIncrease(1,3,6,10)==3&&FovProbe.StageFieldOfViewIncrease(2,3,6,10)==6&&FovProbe.StageFieldOfViewIncrease(3,3,6,10)==10,"all forward stages have distinct FOV targets");
   Check(FovProbe.StageFieldOfViewIncrease(3,15,5,2)==15,"higher stages cannot accidentally narrow FOV below earlier stage");
   f.UpdateFieldOfView(0,true);Check(Near(f.viewCamera.fieldOfView,60),"neutral FOV is exact base");
   f.UpdateFieldOfView(1);Check(f.viewCamera.fieldOfView>60&&f.viewCamera.fieldOfView<63,"stage FOV expands smoothly");
   f.UpdateFieldOfView(1,true);Check(Near(f.viewCamera.fieldOfView,63),"stage 1 adds three degrees");
   f.UpdateFieldOfView(2,true);Check(Near(f.viewCamera.fieldOfView,66),"stage 2 adds six degrees");
   f.UpdateFieldOfView(3,true);Check(Near(f.viewCamera.fieldOfView,70),"stage 3 adds ten degrees");
   f.UpdateFieldOfView(3,true,.5f,4f);Check(Near(f.viewCamera.fieldOfView,69),"actual speed scales base expansion, boost adds separately");
   f.UpdateFieldOfView(3,true,0f);Check(Near(f.viewCamera.fieldOfView,60),"selected stage at rest does not expand FOV");
   f.UpdateFieldOfView(3,true);
   f.UpdateFieldOfView(1);Check(f.viewCamera.fieldOfView<70&&f.viewCamera.fieldOfView>63,"downshift FOV narrows smoothly");
   f.UpdateFieldOfView(0,true);Check(Near(f.viewCamera.fieldOfView,60)&&f.fieldOfViewIncrease==0,"menu/death reset clears FOV tail");
   var slowFrames=new FovProbe();var fastFrames=new FovProbe();Time.unscaledDeltaTime=1f/30;for(int i=0;i<30;i++)slowFrames.UpdateFieldOfView(3);
   Time.unscaledDeltaTime=1f/120;for(int i=0;i<120;i++)fastFrames.UpdateFieldOfView(3);
   Check(Near(slowFrames.viewCamera.fieldOfView,fastFrames.viewCamera.fieldOfView),"FOV smoothing independent of frame rate");
   var preview = new FovProbe { cameraFollow = new FollowProbe { LockOnPreviewFovReduction = 7 } };
   preview.UpdateFieldOfView(3,true);
   preview.UpdateFieldOfView(3);
   Check(Near(preview.viewCamera.fieldOfView,63),"lock-on zoom subtracts from speed-expanded FOV");
   preview.UpdateFieldOfView(3);
   Check(Near(preview.viewCamera.fieldOfView,63),"zoom cannot accumulate across frames");
   preview.cameraFollow.LockOnPreviewFovReduction=0;
   preview.UpdateFieldOfView(3);
   Check(Near(preview.viewCamera.fieldOfView,70),"disabled preview restores previous speed FOV exactly");
   preview.cameraFollow.LockOnPreviewFovReduction=7;
   preview.UpdateFieldOfView(0,true);
   Check(Near(preview.viewCamera.fieldOfView,60),"menu/death reset ignores lock-on zoom");
   // Same left-third framing at normal/boost distances, FOVs and screen aspect ratios.
   foreach(float depth in new[]{3.8f,5f,10f})foreach(float fov in new[]{53f,75f,100f})foreach(float aspect in new[]{1.3333f,1.7778f,2.3333f}){
    float side=FollowProbe.LockOnScreenOffset(depth,fov,aspect,.2f);
    float viewportX=.5f-side/(2f*depth*Mathf.Tan(fov*Mathf.Deg2Rad*.5f)*aspect);
    Check(Near(viewportX,.3f),"shoulder framing stays at 30 percent across speed/FOV/aspect changes");
   }
   Check(FollowProbe.LockOnPreviewProgress(0)==0,"new target starts at original framing");
   Check(Near(FollowProbe.LockOnPreviewProgress(.5f),.5f),"half lock drives half camera transition");
   Check(FollowProbe.LockOnPreviewProgress(1)==1,"completed lock reaches full framing");
   Check(FollowProbe.LockOnPreviewProgress(-1)==0&&FollowProbe.LockOnPreviewProgress(2)==1,"progress safely clamps");
   foreach(float fps in new[]{30f,60f,120f}) {
    float pose=0;
    for(int i=1;i<=(int)fps;i++) {
     float charge=i/fps;float goal=FollowProbe.LockOnPreviewProgress(charge);
     float next=FollowProbe.BlendLockOnPreview(pose,goal,1/fps,.2f);
     Check(next>=pose&&next<=goal,"camera cannot jump ahead of the charge ring");pose=next;
    }
   }
   float low=0,high=0;for(int i=0;i<6;i++)low=FollowProbe.BlendLockOnPreview(low,1,1f/30,.2f);
   for(int i=0;i<24;i++)high=FollowProbe.BlendLockOnPreview(high,1,1f/120,.2f);
   Check(Near(low,high)&&low>.94f&&low<1,"preview reaches 95 percent in 0.2 seconds independent of FPS");
   float boosted=FollowProbe.BlendLockOnPreview(7,3,1f/60,.2f);
   Check(boosted>3&&boosted<7,"boost no longer jumps immediately from seven to three degrees zoom");
   float released=FollowProbe.BlendLockOnPreview(1,0,1f/60,.2f);
   Check(released>0&&released<1,"release smoothly returns from shoulder pose");
   for(int i=0;i<150;i++)released=FollowProbe.BlendLockOnPreview(released,0,1f/60,.2f);
   Check(released==0,"release settles to the exact original pose");
   Check(FovProbe.FastTurnWeight(0,80,240)==0&&FovProbe.FastTurnWeight(80,80,240)==0,"straight flight and gentle turning retain full speed blur");
   Check(Near(FovProbe.FastTurnWeight(160,80,240),.5f),"moderate rotation gradually reduces streaks");
   Check(FovProbe.FastTurnWeight(240,80,240)==1&&FovProbe.FastTurnWeight(720,80,240)==1,"fast camera rotation caps blur reduction");
   Check(FovProbe.FastTurnWeight(300,240,80)==1,"inverted Inspector thresholds remain safe");
   float onset=FovProbe.RecoverTurnBlur(0,1,1f/120,.25f);
   Check(onset>0&&onset<.5f,"fast turn onset does not snap from full blur to minimum in one frame");
   float onset30=0,onset120=0;
   for(int i=0;i<3;i++)onset30=FovProbe.RecoverTurnBlur(onset30,1,1f/30,.25f);
   for(int i=0;i<12;i++)onset120=FovProbe.RecoverTurnBlur(onset120,1,1f/120,.25f);
   Check(Near(onset30,onset120)&&onset30>.99f,"rapid suppression remains frame-rate independent");
   float recover30=1,recover120=1;
   for(int i=0;i<9;i++)recover30=FovProbe.RecoverTurnBlur(recover30,0,1f/30,.25f);
   for(int i=0;i<36;i++)recover120=FovProbe.RecoverTurnBlur(recover120,0,1f/120,.25f);
   Check(Near(recover30,recover120)&&recover30>0&&recover30<.05f,"blur smoothly recovers at matching rates across FPS");
   return n;
  }
 }
}
'@
Add-Type -TypeDefinition ($stubs + ($followMethods -join "`n") + $fovProbe + $checks)
"Stage camera behavior checks passed: $([CameraChecks.Tests]::Run())"

$steering = [regex]::Match($follow,'(?ms)^    public bool TryGetSteeringInput\(.*?^    \}').Value
if ($steering -match 'Kick|Shake|currentLocalOffset') { throw 'Camera effects must not alter mouse steering' }
if ($follow -notmatch 'currentLocalOffset \+ Vector3.back \* \(kickDistance \+ flightDistance\)' -or $follow -match 'currentLocalOffset.z\s*[-+]=') { throw 'Dolly must not accumulate into base offset' }
if ($effects -notmatch 'int displayStage = boundaryView \? 0 : player.CobraActive \? 3 : player.CurrentSpeedStage' -or $effects -notmatch 'flightSensation.FieldOfViewScale\(displayStage\)') { throw 'FOV must combine forward stage and actual travel, including cobra, with boundary reset' }
if ((Method $effects 'ApplyWeight') -match 'fieldOfView') { throw 'Blur/wind intensity must not overwrite stage FOV' }
$scene = Get-Content Assets/Scenes/Battle.unity -Raw
# Inspector values are user-editable: verify wiring, not old hard-coded FOV defaults.
foreach ($setting in @('topSpeedKickDistance','boostKickDistance','topSpeedKickOutSeconds','topSpeedKickReturnSeconds',
    'stage1FieldOfViewIncrease','stage2FieldOfViewIncrease','extraFieldOfView','speedShakePositionAmplitude','speedShakeRotationAmplitude')) {
    if ($scene -notmatch ('(?m)^  ' + $setting + ':')) { throw "Scene setting missing: $setting" }
}
"Battle scene settings, stable steering, non-accumulating dolly and independent FOV verified."
if ($preview -match 'LockProgress|InverseLerp|GetDisplayedLockTarget') { throw 'Preview must not depend on replicated progress or suppress close-target framing' }
$selector = Get-Content Assets/Script/enemyLockOn.cs -Raw
if ($selector -notmatch 'TargetingViewport\(targetPoint\)' -or $selector -notmatch 'HasLineOfSight\(TargetingOrigin, player\)') { throw 'Presentation must not feed back into targeting frustum/visibility' }
