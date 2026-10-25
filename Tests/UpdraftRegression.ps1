# Authoritative launch/contact logic with lightweight doubles; no live Unity physics or multiplayer.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$updraft = Get-Content Assets/Script/BattleUpdraft.cs -Raw
$player = Get-Content Assets/Script/Player.Updraft.cs -Raw
$flag = Get-Content Assets/Script/BattleFlag.cs -Raw
function Method($source,$name) {
    $match=[regex]::Match($source,'(?ms)^    (?:private|internal|public) (?:static )?[\w<>]+ '+$name+'\([^{}]*?\)\r?\n    \{.*?^    \}')
    if(!$match.Success){throw "Missing $name"}
    $match.Value -replace '^    (?:private|internal) ','    public '
}
$source=@'
using System;
using System.Collections.Generic;
namespace UpdraftChecks {
public static class Mathf {public static float Max(float a,float b)=>Math.Max(a,b);public static float Clamp01(float x)=>Math.Clamp(x,0,1);}
public struct Vector3 {
 public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
 public float sqrMagnitude=>x*x+y*y+z*z;public float magnitude=>(float)Math.Sqrt(sqrMagnitude);
 public static Vector3 operator+(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
 public static Vector3 operator-(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
 public static Vector3 operator*(Vector3 a,float f)=>new(a.x*f,a.y*f,a.z*f);
 public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
}
public struct Tick {public int Raw;}
public class Runner {public float Time,DeltaTime=1f/60;public Tick Tick;}
public class NetworkObject {public bool IsValid=true,HasStateAuthority=true;}
public struct TickTimer {
 public float End;public bool running;
 public bool ExpiredOrNotRunning(Runner r)=>!running||r.Time>=End;
 public static TickTimer CreateFromSeconds(Runner r,float s)=>new(){running=true,End=r.Time+s};
}
public class Transform {public Vector3 forward=new(0,0,1);public Vector3 TransformPoint(Vector3 p)=>p;}
public enum BattleItemType {Booster}
public enum BattleSound {Updraft}
public class BattleManager {public static BattleManager Instance=new();public bool IsGameplayActive=true;public int RoundNumber=1;}
public class Player {
 public int AudioEvents;private void RPC_PlayBattleSound(BattleSound sound){AudioEvents++;}
 public NetworkObject Object=new();public Runner Runner=new();public Transform transform=new();
 public bool IsAlive=true,IsBound,IsHitStunned,IsReturningToMap,IsTestBot;
 public Vector3 KnockbackVelocity,center;public float Speed;public Vector3 ParryHintVelocity=>transform.forward*Speed+KnockbackVelocity;
 public bool CanReceiveBattleItem(BattleItemType t)=>Object!=null&&Object.IsValid&&Object.HasStateAuthority&&IsAlive&&!IsReturningToMap&&!IsTestBot&&BattleManager.Instance.IsGameplayActive;
 public static bool IsPositiveFiniteItemValue(float v)=>v>0&&!float.IsNaN(v)&&!float.IsInfinity(v);
'@
$source+=(Method $player 'TryApplyUpdraft')+(Method $player 'ResolveUpdraftVelocity')+"`n}`npublic static class BattleFlag {`n"
$source+='public static void GetCapsule(Player p,out Vector3 center,out Vector3 axis,out float radius){center=p.center;axis=default;radius=.2f;}'
$source+= (Method $flag 'SegmentDistanceSquared')+"`n}`npublic class Pad {`n"
$source+=[regex]::Match($updraft,'(?ms)^    private struct Contact\r?\n    \{.*?^    \}').Value
$source+=@'
 private readonly Dictionary<Player,Contact> contacts=new();private readonly List<Player> stale=new();private int round=-1;
 public float launchSpeed=35,cooldownSeconds=1,activationRadius=1;public Vector3 activationOffset;public Transform transform=new();public bool sight=true;
 private bool HasSight(Vector3 origin,Vector3 point)=>sight;
'@
$source+=(Method $updraft 'Sample')+"`n}`n"
$source+=@'
public static class Checks {
 static int count;static void Check(bool value,string message){if(!value)throw new Exception(message);count++;}
 static void Step(Pad pad,Player p,int tick,float x){p.Runner.Tick=new(){Raw=tick};p.Runner.Time=tick/60f;p.center=new(x,0,0);pad.Sample(p);}
 public static int Run(){
  var p=new Player();Check(p.TryApplyUpdraft(35)&&p.KnockbackVelocity.y==35,"normal launch world-up");
  p.KnockbackVelocity=new(3,0,7);p.transform.forward=new(0,-1,0);p.Speed=60;
  Check(p.TryApplyUpdraft(35)&&p.KnockbackVelocity.y-60==35,"downward/inverted flight still launches upward");
  Check(p.KnockbackVelocity.x==3&&p.KnockbackVelocity.z==7&&p.Speed==60,"horizontal impulse and flight speed preserved");
  Check(p.TryApplyUpdraft(35)&&p.KnockbackVelocity.y==95,"repeat doesn't stack unbounded impulses");
  p.KnockbackVelocity=new(0,120,0);p.TryApplyUpdraft(35);Check(p.KnockbackVelocity.y==120,"stronger upward impulse is not reduced");
  foreach(float bad in new[]{0f,-1f,float.NaN,float.PositiveInfinity})Check(!p.TryApplyUpdraft(bad),"invalid launch rejected");
  p.Object.HasStateAuthority=false;Check(!p.TryApplyUpdraft(35),"client cannot grant launch");p.Object.HasStateAuthority=true;
  p.IsBound=true;Check(!p.TryApplyUpdraft(35),"binding remains a movement lock");p.IsBound=false;
  p.IsHitStunned=true;Check(!p.TryApplyUpdraft(35),"hit stun cannot collect launch");p.IsHitStunned=false;
  p.IsAlive=false;Check(!p.TryApplyUpdraft(35),"dead player rejected");p.IsAlive=true;
  p.IsReturningToMap=true;Check(!p.TryApplyUpdraft(35),"boundary return protected");
  p=new Player();var pad=new Pad();Step(pad,p,1,0);Check(p.KnockbackVelocity.y==35,"enter pad launches");
  p.KnockbackVelocity=default;for(int i=2;i<90;i++)Step(pad,p,i,0);
  Check(p.KnockbackVelocity.y==0,"remaining inside never auto-retriggers even after cooldown");
  Step(pad,p,90,4);Step(pad,p,91,0);Check(p.KnockbackVelocity.y==35,"exit and re-enter launches again");
  p.KnockbackVelocity=default;Step(pad,p,92,4);Step(pad,p,93,0);Check(p.KnockbackVelocity.y==0,"quick re-entry respects cooldown");
  var teammate=new Player();Step(pad,teammate,93,0);Check(teammate.KnockbackVelocity.y==35,"cooldown does not block other players");
  p=new Player{Speed=300};pad=new Pad();Step(pad,p,1,-5);Step(pad,p,2,5);
  Check(p.KnockbackVelocity.y==35,"fast crossing between samples still activates pad");
  p=new Player();pad=new Pad();Step(pad,p,1,-30);Step(pad,p,2,30);Check(p.KnockbackVelocity.y==0,"teleport path cannot activate pad");
  p=new Player();pad=new Pad{sight=false};Step(pad,p,1,0);Check(p.KnockbackVelocity.y==0,"solid obstruction blocks contact");
  pad.sight=true;BattleManager.Instance.RoundNumber++;Step(pad,p,2,0);Check(p.KnockbackVelocity.y==35,"new round clears old contacts");
  return count;
 }
}
}
'@
Add-Type -TypeDefinition $source
"Updraft launch/contact checks passed: $([UpdraftChecks.Checks]::Run())"
$prefab=Get-Content Assets/Item/JumpBooster.prefab -Raw
$guid=[regex]::Match((Get-Content Assets/Script/BattleUpdraft.cs.meta -Raw),'guid: (\w+)').Groups[1].Value
if(!$prefab.Contains("guid: $guid,")){throw 'JumpBooster not connected'}
$collider=[regex]::Match($prefab,'(?ms)^--- !u!64 &8890673623580941894\r?\n.*?(?=^--- !u!|\z)').Value
if(!$collider.Contains('m_Enabled: 0')){throw 'Wind pad still has solid mesh collision'}
'JumpBooster prefab binding and non-solid collision verified.'
