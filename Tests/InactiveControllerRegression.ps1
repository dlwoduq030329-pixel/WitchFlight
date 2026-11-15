# Exercises actual GoForward/ApplyKnockback/Die methods with synchronous collision callbacks.
# Controlled controller double throws if Move is called while inactive, as Unity warns.
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$source=Get-Content Assets/Script/Player.cs -Raw
function Method([string]$name) {
 $m=[regex]::Match($source,'(?ms)^    private void '+$name+'\([^{}]*?\)\r?\n    \{.*?^    \}')
 if(!$m.Success){throw "Missing production method $name"}
 $m.Value.Replace('private void','public void')
}
$code=@'
using System;
namespace InactiveControllerChecks {
public struct Vector3 {
 public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
 public static Vector3 zero=>default;public float sqrMagnitude=>x*x+y*y+z*z;
 public static Vector3 operator+(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
 public static Vector3 operator-(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
 public static Vector3 operator*(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);
 public static Vector3 operator/(Vector3 a,float b)=>a*(1f/b);
 public static Vector3 MoveTowards(Vector3 a,Vector3 b,float step){var d=b-a;float m=(float)Math.Sqrt(d.sqrMagnitude);return m<=step?b:a+d*(step/m);}
}
public static class Mathf {public static float Max(float a,float b)=>Math.Max(a,b);}
public class GameObject {public bool activeInHierarchy=true;}
public class Transform {public Vector3 position,forward=new(0,0,1);}
public class CharacterController {
 public bool enabled=true;public GameObject gameObject;public Transform transform;public Action OnMove;public int Moves;
 public bool isActiveAndEnabled=>enabled&&gameObject.activeInHierarchy;
 public void Move(Vector3 delta){if(!isActiveAndEnabled)throw new Exception("Move called on inactive controller");Moves++;transform.position+=delta;OnMove?.Invoke();}
}
public class Runner {public float DeltaTime=.1f;}
public struct TickTimer {public static TickTimer None=>default;public static TickTimer CreateFromSeconds(Runner runner,float seconds)=>default;}
public enum MagicType {None}
public class BattleManager {public static BattleManager Instance=new();public float RespawnDelaySeconds=3;public void PlayerKilled(int p,int a){}}
public class Player {
 public GameObject gameObject=new();public Transform transform=new();public CharacterController characterController;public Runner Runner=new();
 public bool IsAlive=true,CobraActive,IsReturningToMap,IsBoosting;public float Speed=20,knockbackDamping=10,timeFieldHealing,timeFieldHealElapsed;
 public int SpeedStage=3,DeathSequence,ParticipantRef,LastAttacker,Rams;public object testBot;
 public TickTimer FlareProtection,RespawnTimer;public MagicType pendingMagic;
 public Vector3 KnockbackVelocity=new(10,0,0),LastFlightVelocity;public Action OnRam;
 public Player(){characterController=new(){gameObject=gameObject,transform=transform};}
 public Vector3 ConstrainMapMovement(Vector3 v)=>v;
 public void TryRam(Vector3 a,Vector3 b,Vector3 v){Rams++;OnRam?.Invoke();}
 public void StopChannel(){}public void ResetItemBoost(){}public void ResetBladeState(){}public void CancelBladeRain(){}
 public void ResetMeowFestival(){}public void StopMagicStorm(){}public void ClearLockTargetInternal(){}
 public void NotifyConfirmedMagicHit(int attacker,int id,bool lethal){}
'@
foreach($name in 'GoForward','ApplyKnockback','Die'){$code+=Method $name}
$code+=@'
}
public static class Tests {
 static int n;static void Check(bool ok,string message){if(!ok)throw new Exception(message);n++;}
 static bool Near(float a,float b)=>Math.Abs(a-b)<.0001f;
 public static int Run(){
  var normal=new Player();normal.GoForward();normal.ApplyKnockback();
  Check(normal.characterController.Moves==2&&Near(normal.transform.position.z,2)&&Near(normal.transform.position.x,1),"normal forward and knockback distances unchanged");
  Check(Near(normal.KnockbackVelocity.x,9)&&normal.Rams==1&&Near(normal.LastFlightVelocity.z,20),"normal damping and ram evaluation unchanged");
  var wall=new Player();wall.characterController.OnMove=wall.Die;wall.GoForward();wall.ApplyKnockback();
  Check(!wall.IsAlive&&!wall.characterController.enabled&&wall.characterController.Moves==1,"fatal collision during Move never moves inactive controller afterward");
  Check(wall.KnockbackVelocity.sqrMagnitude==0&&wall.Rams==0&&wall.DeathSequence==1,"death clears impulse and skips ram after fatal wall contact");
  var ram=new Player();ram.OnRam=ram.Die;ram.GoForward();ram.ApplyKnockback();
  Check(!ram.IsAlive&&ram.characterController.Moves==1&&ram.Rams==1,"counter-ram death before ApplyKnockback is safe");
  var disabled=new Player();disabled.characterController.enabled=false;disabled.GoForward();disabled.ApplyKnockback();
  Check(disabled.characterController.Moves==0&&disabled.transform.position.sqrMagnitude==0&&!disabled.characterController.enabled,"disabled controller never falls back to transform movement or gets re-enabled");
  var inactive=new Player();inactive.gameObject.activeInHierarchy=false;inactive.GoForward();inactive.ApplyKnockback();
  Check(inactive.characterController.Moves==0&&inactive.transform.position.sqrMagnitude==0,"inactive object with enabled component does not move");
  var dead=new Player{IsAlive=false};dead.GoForward();dead.ApplyKnockback();
  Check(dead.characterController.Moves==0&&dead.transform.position.sqrMagnitude==0,"dead actor cannot move even if component is enabled");
  var absent=new Player{characterController=null};absent.GoForward();absent.ApplyKnockback();
  Check(Near(absent.transform.position.z,2)&&Near(absent.transform.position.x,1),"existing no-controller fallback retained for alive active actor");
  absent.Die();var deathPosition=absent.transform.position;absent.ApplyKnockback();absent.GoForward();
  Check((absent.transform.position-deathPosition).sqrMagnitude==0,"no-controller fallback cannot move dead actor");
  var noImpulse=new Player{KnockbackVelocity=default};noImpulse.ApplyKnockback();
  Check(noImpulse.characterController.Moves==0,"zero impulse does not issue Move");
  disabled.characterController.enabled=true;disabled.GoForward();disabled.ApplyKnockback();
  Check(disabled.characterController.Moves==2,"intentional re-enable resumes normal movement");
  return n;
 }
}}
'@
Add-Type -TypeDefinition $code
"Inactive controller movement checks passed: $([InactiveControllerChecks.Tests]::Run())"
