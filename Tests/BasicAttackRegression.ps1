# Execute production targeting, recharge and firing with deterministic Unity/Fusion doubles.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$source = @'
using System;
using System.Collections.Generic;
using UnityEngine;
using Fusion;
namespace UnityEngine {
 public struct Vector3 {
  public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
  public float sqrMagnitude=>x*x+y*y+z*z;public Vector3 normalized=>this*(1/(float)Math.Sqrt(sqrMagnitude));
  public static Vector3 forward=>new(0,0,1);
  public static Vector3 operator-(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
  public static Vector3 operator+(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
  public static Vector3 operator*(Vector3 a,float n)=>new(a.x*n,a.y*n,a.z*n);
  public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
  public static float Angle(Vector3 a,Vector3 b)=>Mathf.Acos(Mathf.Clamp(Dot(a.normalized,b.normalized),-1,1))*Mathf.Rad2Deg;
 }
 public struct Quaternion {public static Quaternion LookRotation(Vector3 a)=>new();}
 public static class Mathf {
  public const float Deg2Rad=(float)Math.PI/180,Rad2Deg=180/(float)Math.PI;
  public static float Clamp(float v,float a,float b)=>Math.Clamp(v,a,b);public static float Clamp01(float v)=>Clamp(v,0,1);
  public static int Max(int a,int b)=>Math.Max(a,b);public static float Max(float a,float b)=>Math.Max(a,b);
  public static int Min(int a,int b)=>Math.Min(a,b);public static float Min(float a,float b)=>Math.Min(a,b);
  public static float Cos(float v)=>(float)Math.Cos(v);public static float Acos(float v)=>(float)Math.Acos(v);
 }
 public class Transform {public Vector3 forward=Vector3.forward;}
}
namespace Fusion {
 public class NetworkedAttribute:Attribute{}
 public struct NetworkId {public int value;public static bool operator==(NetworkId a,NetworkId b)=>a.value==b.value;public static bool operator!=(NetworkId a,NetworkId b)=>a.value!=b.value;public override bool Equals(object o)=>o is NetworkId n&&this==n;public override int GetHashCode()=>value;}
 public struct TickTimer {public bool active;public static TickTimer None=>default;public static TickTimer CreateFromSeconds(TestRunner r,float s)=>new(){active=true};}
}
public enum MagicType {BasicAttack}
public struct MagicStatEntry {public float damage,range;}
public class BasicAttackSettings {
 public static BasicAttackSettings Default=new();public int capacity=3;public float rechargeSeconds=4,shotInterval=.2f,aimAssistAngle=12,similarAimAngle=2;
 public static MagicStatEntry Stats=>new(){damage=15,range=130};
}
public class NetObject {public bool IsValid=true,HasStateAuthority=true;public NetworkId Id;}
public class Prefab {public bool IsValid=true;}
public class TestRunner {
 public float DeltaTime=.02f;public bool FailSpawn;public MagicProjectile lastShot;
 public Shot Spawn(Prefab p,Vector3 v,Quaternion q,int participant){if(FailSpawn)return null;lastShot=new();return new(){projectile=lastShot};}
}
public class Shot {public MagicProjectile projectile;public T GetComponent<T>() where T:class=>projectile as T;}
public class MagicProjectile {
 public static HashSet<float> blocked=new();public static bool HasSummonSight(Vector3 a,Vector3 b)=>!blocked.Contains(b.z);
 public Player target;public float damage;
 public void Initialize(Player owner,Player t,MagicStatEntry s,Vector3 dir,int tick){target=t;damage=s.damage;}
}
public partial class Player {
 public static List<Player> ActiveCombatants=new();public NetObject Object=new();public TestRunner Runner;
 public bool IsAlive=true,IsHitStunned,IsReturningToMap,Hostile=true;public int BladeCharges,ParticipantRef,CastSequence,castInputTick;
 public float NowHp=100;public MagicType LastCastMagic;public Vector3 LockAimPoint,MagicCastPosition,castAimDirection=Vector3.forward;
 public Transform transform=new();public Prefab magicProjectilePrefab=new();
 public bool IsTargetableBy(Player p)=>IsAlive&&Hostile&&this!=p;
 public bool TimerIsActive(TickTimer t)=>t.active;
 public bool IsFiniteDirection(Vector3 v)=>!float.IsNaN(v.x)&&!float.IsNaN(v.y)&&!float.IsNaN(v.z);
 public void RPC_PresentCast(MagicType t,Vector3 a,Vector3 b,int i){}
 public void Reset()=>ResetBasicAttack();public void Recharge(float dt){Runner.DeltaTime=dt;TickBasicAttackRecharge();}
 public Player Pick()=>FindBasicAttackTarget(MagicCastPosition,Vector3.forward,130);
 public void Fire(){BasicAttackShotCooldown=TickTimer.None;FireBasicAttack();}
}
'@
$source += (Get-Content Assets/Script/Player.BasicAttack.cs -Raw) -replace '(?m)^using [^;]+;\r?\n',''
$source += @'
public static class Checks {
 static int count;static void Check(bool b,string why){if(!b)throw new Exception(why);count++;}
 static Player Enemy(Player p,int id,float angle,float depth,float hp=100){var e=new Player{Runner=p.Runner,NowHp=hp,LockAimPoint=new((float)Math.Tan(angle*Mathf.Deg2Rad)*depth,0,depth)};e.Object.Id=new(){value=id};Player.ActiveCombatants.Add(e);return e;}
 public static int Run(){
  Player.ActiveCombatants.Clear();var p=new Player{Runner=new()};p.Object.Id=new(){value=99};p.Reset();
  Check(p.BasicAttackCharges==3,"starts with three charges");
  p.Fire();Check(p.BasicAttackCharges==3&&p.CastSequence==0&&p.Runner.lastShot==null,"no target cannot spawn, consume charges or present a cast");
  var rechargeTarget=Enemy(p,10,0,50);
  p.Fire();Check(p.BasicAttackCharges==2&&p.Runner.lastShot.damage==15,"basic damage 15 and consumes one charge");
  p.Recharge(3);Check(p.BasicAttackCharges==2&&Math.Abs(p.BasicAttackRechargeProgress-.75f)<.001f,"partial recharge");
  p.Fire();Check(p.BasicAttackCharges==1&&Math.Abs(p.BasicAttackRechargeProgress-.75f)<.001f,"second shot preserves current recharge");
  p.Recharge(1);Check(p.BasicAttackCharges==2,"one charge at four seconds");p.Recharge(4);Check(p.BasicAttackCharges==3&&p.BasicAttackRechargeProgress==0,"second charge sequentially after another four seconds");
  p.Recharge(100);p.Fire();Check(p.BasicAttackRechargeProgress==0,"full capacity never banks recovery");
  p.Fire();p.Fire();int sequence=p.CastSequence;p.Fire();Check(p.BasicAttackCharges==0&&p.CastSequence==sequence,"empty charge cannot fire");
  p.Recharge(12);Check(p.BasicAttackCharges==3,"large tick recovers exactly up to capacity");
  p.BladeCharges=3;p.Fire();Check(p.BasicAttackCharges==3,"armed blade cannot consume basic charge");p.BladeCharges=0;
  p.Runner.FailSpawn=true;p.Fire();Check(p.BasicAttackCharges==3,"failed spawn never consumes a charge");p.Runner.FailSpawn=false;
  Player.ActiveCombatants.Clear();p.Reset();
  var close=Enemy(p,1,1,50,100);var healthy=Enemy(p,2,2,60,90);var low=Enemy(p,3,2.5f,55,20);var far=Enemy(p,4,10,40,1);
  Check(p.Pick()==low,"similar aim distance prefers lowest current health but distant low hp cannot steal");
  Player.ActiveCombatants.Reverse();Check(p.Pick()==low,"selection independent of candidate iteration order");
  p.Fire();low.NowHp=500;Check(p.Pick()==low,"previous fired target wins among valid aim candidates");
  low.LockAimPoint=new(100,0,50);Check(p.Pick()==healthy,"previous target outside aim cone is excluded");
  low.LockAimPoint=new(0,0,200);Check(p.Pick()==healthy,"previous target outside range is excluded");
  low.LockAimPoint=new(0,0,55);MagicProjectile.blocked.Add(55);Check(p.Pick()==healthy,"occluded previous target excluded");MagicProjectile.blocked.Clear();
  low.IsAlive=false;Check(p.Pick()==healthy,"dead previous target excluded");low.IsAlive=true;low.Hostile=false;Check(p.Pick()==healthy,"allied previous target excluded");
  low.Hostile=true;low.Runner=new();Check(p.Pick()==healthy,"other runner previous target excluded");low.Runner=p.Runner;low.Object.IsValid=false;Check(p.Pick()==healthy,"despawned previous target excluded");
  p.Reset();healthy.NowHp=100;Check(p.Pick()==close,"equal health breaks tie using aim distance; reset clears previous target");
  Player.ActiveCombatants.Clear();Check(p.Pick()==null,"empty cone has no target");
  var invalid=Enemy(p,20,20,50);p.Fire();Check(p.BasicAttackCharges==3,"outside aim cone cannot consume a charge");
  invalid.LockAimPoint=new(0,0,200);p.Fire();Check(p.BasicAttackCharges==3,"outside range cannot consume a charge");
  invalid.LockAimPoint=new(0,0,50);MagicProjectile.blocked.Add(50);p.Fire();Check(p.BasicAttackCharges==3,"wall-blocked target cannot consume a charge");MagicProjectile.blocked.Clear();
  invalid.IsAlive=false;p.Fire();Check(p.BasicAttackCharges==3,"dead target cannot consume a charge");
  invalid.IsAlive=true;p.Fire();p.Recharge(2);Player.ActiveCombatants.Clear();sequence=p.CastSequence;var previousShot=p.Runner.lastShot;
  p.Fire();Check(p.BasicAttackCharges==2&&p.BasicAttackRechargeProgress==.5f&&p.CastSequence==sequence&&p.Runner.lastShot==previousShot,"target loss preserves partial recharge and does not spawn or present a cast");
  return count;
 }
}
'@
Add-Type -TypeDefinition $source -CompilerOptions '/nowarn:0414'
"Basic attack recharge / targeting / firing checks passed: $([Checks]::Run())"
