# Actual offline movement methods with lightweight Unity doubles; not a physics/visual test.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$source = Get-Content Assets/Script/MagicBalanceLabActor.cs -Raw
function Method([string]$name) {
    $match = [regex]::Match($source, '(?ms)^    public [\w<>]+ ' + $name + '\([^{}]*?\)\r?\n    \{.*?^    \}')
    if (!$match.Success) { throw "Missing production method: $name" }
    $match.Value
}
$code = @'
using System;
namespace LabChecks {
public static class Mathf {
 public static float Max(float a,float b)=>Math.Max(a,b);
 public static float Min(float a,float b)=>Math.Min(a,b);
 public static float Clamp(float x,float a,float b)=>Math.Clamp(x,a,b);
 public static float Clamp01(float x)=>Math.Clamp(x,0,1);
 public static float Exp(float x)=>(float)Math.Exp(x);
 public static float Lerp(float a,float b,float t)=>a+(b-a)*Clamp01(t);
 public static float MoveTowards(float a,float b,float d)=>a+Math.Sign(b-a)*Math.Min(Math.Abs(b-a),d);
}
public struct Vector3 {
 public float x,y,z;
 public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
 public static Vector3 forward=>new Vector3(0,0,1);
 public float sqrMagnitude=>x*x+y*y+z*z;
 public static Vector3 operator-(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
 public static Vector3 operator+(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
 public static Vector3 operator*(Vector3 a,float k)=>new Vector3(a.x*k,a.y*k,a.z*k);
 public static Vector3 operator/(Vector3 a,float k)=>a*(1/k);
}
public struct Quaternion {
 public float angle;
 public static float Angle(Quaternion a,Quaternion b)=>Math.Abs(a.angle-b.angle);
 public static Quaternion RotateTowards(Quaternion a,Quaternion b,float dt)=>new Quaternion{angle=Mathf.MoveTowards(a.angle,b.angle,dt)};
 public static Vector3 operator*(Quaternion q,Vector3 v) {
  double r=q.angle*Math.PI/180;
  return new Vector3((float)(v.x*Math.Cos(r)+v.z*Math.Sin(r)),v.y,(float)(v.z*Math.Cos(r)-v.x*Math.Sin(r)));
 }
}
public class Transform { public Vector3 position;public Quaternion rotation;public Vector3 forward=>rotation*Vector3.forward; }
public class Controller { public Transform transform;public void Move(Vector3 delta){transform.position+=delta;} }
public class Broom { public float maxSpeed=60,boostMultiplier=1.5f,turnSpeed=100,turnAcceleration=4,turnReturnSpeed=4,brakeSpeed=60,speedStageTransitionSpeed=30; }
public class Hat {public float maxAp=100,apRecoveryPerSecond=5;}
public enum MagicEffectKind {SlowDamage, Binding, DirectDamage}
public struct MagicStatEntry {public float damage,effectDuration,movementMultiplier;public MagicEffectKind effect;}
public class Actor {
 public Transform transform=new Transform();
 public float NowHp=100,NowAp=0,Speed;
 public Vector3 Velocity;
 public int DashCharges=3;
 public int MaximumDashCharges=3;
 public float RechargeSeconds=4;
 private Controller controller;
 private Broom broom=new Broom();private Hat hat=new Hat();
 private float turnRate,boost,recharge,glide,glideStart,slowed,bound,slowFactor=1;
 public Actor(){controller=new Controller{transform=transform};}
 __SIMULATE__
 __HIT__
 public void Tick(float dt,bool dash=false,bool brake=false,bool stationary=false,bool infinite=false) { Simulate(default,stationary,brake,dash,infinite,dt); }
}
public class Harness {
 static int checks;
 static void Check(bool pass,string name){checks++;if(!pass)throw new Exception(name);}
 public static string Run() {
  foreach(int hz in new[]{30,60,120}) {
   float dt=1f/hz;var a=new Actor();
   for(int i=0;i<3*hz;i++)a.Tick(dt);
   Check(Math.Abs(a.Speed-40)<.01f,"Cruise reaches stage 2");
   Check(Math.Abs(a.NowAp-15)<.02f,"Hat mana recovery");
   for(int i=0;i<2*hz;i++)a.Tick(dt,brake:true);
   Check(a.Speed==0,"Holding brake stops flight");
   a.Tick(dt);Check(a.Speed>0,"Releasing brake accelerates");
   a.Tick(dt,dash:true);Check(a.DashCharges==2,"Dash consumes exactly one charge");
   a.Tick(dt,dash:true);Check(a.DashCharges==2,"Cannot dash twice during active boost");
   for(int i=0;i<hz;i++)a.Tick(dt);
   Check(a.Speed>60,"Boost accelerates above cruise");
   for(int i=0;i<2*hz;i++)a.Tick(dt);
   Check(a.DashCharges==2,"No early four-second recharge");
   a.Tick(dt,dash:true);Check(a.DashCharges==1,"Second dash after boost ends");
   for(int i=0;i<hz;i++)a.Tick(dt);
   Check(a.DashCharges==2,"Additional spending preserves recharge progress");
   for(int i=0;i<5*hz;i++)a.Tick(dt);
   Check(a.DashCharges==3,"Sequential recharge reaches cap");
   var pos=a.transform.position;a.Tick(dt,stationary:true);
   Check((a.transform.position-pos).sqrMagnitude==0 && a.Speed==0,"Defense bot remains stationary");
   a.Hit(new MagicStatEntry{damage=200,effect=MagicEffectKind.DirectDamage});
   Check(a.NowHp==1,"Test actors remain alive after lethal hit");
   a.Tick(dt,infinite:true);Check(a.NowAp==100,"Unlimited mana refills");
  }
  foreach(int capacity in new[]{2,3,4,5}) {
   var a=new Actor{MaximumDashCharges=capacity,DashCharges=capacity};
   a.Tick(.01f,dash:true);Check(a.DashCharges==capacity-1,"Equipment charge spent in offline lab");
   for(int i=0;i<410;i++)a.Tick(.01f);
   Check(a.DashCharges==capacity,"Offline recharge stops at equipment maximum");
   a=new Actor{MaximumDashCharges=capacity,DashCharges=0};
   for(int i=0;i<(capacity+1)*410;i++)a.Tick(.01f);
   Check(a.DashCharges==capacity,"Empty offline pool refills without exceeding equipment cap");
  }
  return checks+" offline lab checks passed";
 }
}
}
'@
$code = $code.Replace('__SIMULATE__', (Method Simulate)).Replace('__HIT__', (Method Hit))
Add-Type -TypeDefinition $code -WarningAction SilentlyContinue
[LabChecks.Harness]::Run()
