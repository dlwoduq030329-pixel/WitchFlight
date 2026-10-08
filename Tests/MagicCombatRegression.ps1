# Offline checks of actual Player combat methods. No live BACKND/Fusion connection.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$player = Get-Content (Join-Path $repo 'Assets/Script/Player.cs') -Raw
$table = Get-Content (Join-Path $repo 'Assets/Script/MagicStatTable.cs') -Raw
$enum = [regex]::Match((Get-Content (Join-Path $repo 'Assets/Script/PlayerData.cs') -Raw), 'public enum MagicType\s*\{[^}]+\}').Value
function Method([string]$name) {
    $m = [regex]::Match($player, '(?ms)^    (?:private|public) (?:static )?[\w<>]+ ' + $name + '\([^{}]*?\)\r?\n    \{.*?^    \}')
    if (!$m.Success) {throw "Missing method: $name"}
    $m.Value -replace '^    private ', '    public '
}
$methods = @('TryCastSelectedMagic','TryStartParry','ReceiveMagicHit','TakeDamage','CastMagic',
    'RestoreHealth','TryConsumeAp','UpdateChannel','StopChannel','RegenerateAp','TimerIsActive',
    'SetInputLockTarget','UpdateLockCharge','TryGetValidLockTarget','IsValidLockTarget','ClearLockTargetInternal',
    'ProcessAuthoritativeCombat','ProcessPredictedProjectileInput','UpdatePendingCast','NotifyDamageDirection','NotifyConfirmedMagicHit',
    'CanAcquireMagicTarget','GetDisplayedLockTarget','RPC_SetLockTarget') | ForEach-Object { Method $_ }
$stubs = @'
using System;
namespace UnityEngine {
 public class Object {}
 public class GameObject:Object {}
 public class Sprite:Object {}
 public class ScriptableObject:Object {}
 public class SerializeField:Attribute {}
 public class HeaderAttribute:Attribute {public HeaderAttribute(string s){}}
 public class TooltipAttribute:Attribute {public TooltipAttribute(string s){}}
 public class MinAttribute:Attribute {public MinAttribute(float f){}}
 public class RangeAttribute:Attribute {public RangeAttribute(float a,float b){}}
 public class TextAreaAttribute:Attribute {}
 public class CreateAssetMenuAttribute:Attribute {public string fileName,menuName;}
 public static class Mathf {
  public static float Max(float a,float b)=>Math.Max(a,b);
  public static float Min(float a,float b)=>Math.Min(a,b);
  public static float Clamp(float a,float b,float c)=>Math.Clamp(a,b,c);
  public static float Clamp01(float a)=>Clamp(a,0,1);
 }
 public struct Vector3 {
  public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
  public static Vector3 zero=>default; public static Vector3 forward=>new Vector3(0,0,1);
  public float sqrMagnitude=>x*x+y*y+z*z;
  public static float SqrMagnitude(Vector3 a)=>a.sqrMagnitude;
  public static Vector3 operator+(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
  public static Vector3 operator-(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
  public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
  public static float Distance(Vector3 a,Vector3 b)=>(float)Math.Sqrt((a-b).sqrMagnitude);
  public static float Angle(Vector3 a,Vector3 b)=>(float)(Math.Acos(Math.Clamp(Dot(a,b)/Math.Sqrt(a.sqrMagnitude*b.sqrMagnitude),-1,1))*180/Math.PI);
 }
 public class Transform {public Vector3 position,forward=Vector3.forward;}
}
namespace CombatChecks {
using UnityEngine;
using System.Collections.Generic;
public enum PlayerRef {None,One,Two}
public enum PlayerInputButton {Lock,Parry}
public struct NetworkId {public int Id; public NetworkId(int id){Id=id;} public bool Equals(NetworkId other)=>Id==other.Id;}
public struct NetworkButtons {
 public int Bits;
 public bool IsSet(PlayerInputButton b)=>(Bits&(1<<(int)b))!=0;
 public bool WasPressed(NetworkButtons prev,PlayerInputButton b)=>IsSet(b)&&!prev.IsSet(b);
}
public struct NetworkInputData {public NetworkId lockTarget;public Vector3 aimDirection;}
public class NetworkObject {
 public bool HasStateAuthority=true,IsValid=true; public PlayerRef InputAuthority;
 public NetworkId Id;public Player Owner;
 public T GetComponent<T>() where T:class=>Owner as T;
}
public class RunnerState {
 public struct TickValue {public int Raw;} public TickValue Tick;
 public float DeltaTime=.02f,Time;
 public readonly Dictionary<int,NetworkObject> Objects=new();
 public bool TryFindObject(NetworkId id,out NetworkObject obj)=>Objects.TryGetValue(id.Id,out obj);
}
public struct TickTimer {
 public float End; public bool IsRunning; public static TickTimer None=>default;
 public bool Expired(RunnerState r)=>IsRunning&&r.Time+0.000001f>=End;
 public static TickTimer CreateFromSeconds(RunnerState r,float t)=>new TickTimer{End=r.Time+t,IsRunning=true};
}
public class TimerArray {private TickTimer[] values=new TickTimer[11];public int Length=>values.Length;
 public TickTimer this[int i]=>values[i];public void Set(int i,TickTimer t){values[i]=t;}}
public class BattleManager {public static BattleManager Instance=new();public bool IsGameplayActive=true;}
public class MagicTestBot {public bool Immortal;}
public class PrefabRef {public bool IsValid=true;}
public class Marker {public IDisposable Auto()=>new Scope();private class Scope:IDisposable {public void Dispose(){}}}
}
'@
$probe = @'
namespace CombatChecks { using UnityEngine;
public class Player {
 public RunnerState Runner=new();public NetworkObject Object=new();public Transform transform=new();
 public MagicStatTable magicStatTable=new();
 public MagicType Selected=MagicType.Vision,pendingMagic,LastCastMagic,ChannelMagic;
 public NetworkId LockTargetId,pendingTargetId,ChannelTargetId;
 public TickTimer castTimer,ParryTimer,ParryCooldown,BindingTimer;
 public TickTimer nextHitMarkerFeedback;
 public int castInputTick,pendingInputTick;
 public TimerArray MagicCooldowns=new();
 public bool IsAlive=true,IsHitStunned,IsReturningToMap,IsFullyLocked,channelNeedsRelease,Visible=true;
 public bool IsChanneling=>ChannelMagic!=MagicType.None;
 public bool IsParrying=>TimerIsActive(ParryTimer);
 public int CurrentMagicSlot=1,CastSequence,HitSequence,ParrySequence,TeamIndex=1,Launches,Syncs;
 public float NowHp=200,MaxHp=200,NowAp=100,MaxAp=100,ApRecoveryPerSecond=10;
 public float LockProgress,Speed,channelDamageTime,LastReceivedDamage;
 public PlayerRef LastAttacker;
 public Vector3 KnockbackVelocity,ChannelEnd;
 public Vector3 LockAimPoint=>transform.position;
 public Vector3 MagicCastPosition=>transform.position+CastOffset;
 public Vector3 CastOffset,LastTraceOrigin,LastCastOrigin;
 public NetworkButtons previousButtons;
 public MagicTestBot testBot;
 public PrefabRef magicProjectilePrefab=new();
 public Player TraceTarget,Attacker;
 public float maxLockDistance=250,defaultHitStunSeconds=.25f,parryWindowSeconds=.3f,parryCooldownSeconds=2;
 public float AppliedSlow=1;public MagicStatEntry LastLaunched;
 public Marker combatMarker=new();
 public MagicStatEntry SelectedMagicStats=>GetMagicStats(GetSelectedMagic());
 public MagicType GetSelectedMagic()=>CurrentMagicSlot==3?MagicType.None:Selected;
 public MagicStatEntry GetMagicStats(MagicType m)=>magicStatTable.GetStats(m);
 public bool IsEnemyOf(Player other)=>other!=null&&other!=this&&IsAlive&&other.IsAlive&&TeamIndex!=other.TeamIndex;
 public bool IsTargetableBy(Player other)=>IsEnemyOf(other);
 public bool HasLineOfSight(Player other)=>other.Visible;
 public bool IsFiniteDirection(Vector3 v)=>!float.IsNaN(v.x+v.y+v.z);
 public Vector3 TraceMagic(MagicStatEntry s,Vector3 o,Vector3 d,out Player victim){LastTraceOrigin=o;victim=TraceTarget;return o+d;}
 public void LaunchMagic(MagicStatEntry s,Player target){Launches++;LastLaunched=s;}
 public int Predictions;
 public void TryPredictProjectile(MagicStatEntry s){Predictions++;}
 public void RPC_PresentCast(MagicType m,Vector3 a,Vector3 b,float v){LastCastOrigin=a;}
 public int DamageDirectionEvents;public Vector3 DamageDirectionPosition;
 public int HitMarkerEvents;
 public void RPC_ConfirmedMagicHit(){HitMarkerEvents++;}
 public void RPC_ReceivedDamageDirection(Vector3 p){DamageDirectionEvents++;DamageDirectionPosition=p;}
 public Player FindPlayer(PlayerRef r)=>Attacker;
 public void ApplySlow(float m,float d){AppliedSlow=m;}
 public void ApplyHitStun(float t){}
 public void ApplyKnockbackFrom(PlayerRef p,float k){}
 public void SyncHealthToPlayerData(){Syncs++;}
 public void SyncApToPlayerData(){}
 public void Die(){IsAlive=false;}
 public void Input(bool held,bool parry=false,NetworkId target=default) {
  var buttons=new NetworkButtons{Bits=(held?1:0)|(parry?2:0)};
  ProcessAuthoritativeCombat(new NetworkInputData{lockTarget=target,aimDirection=Vector3.forward},buttons);
  previousButtons=buttons;
 }
'@ + ($methods -join [Environment]::NewLine) + @'
}
public static class Tests {
 static int n;
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);n++;}
 static bool Near(float a,float b)=>System.Math.Abs(a-b)<.005f;
 static Player Caster(MagicType m){var p=new Player{Selected=m};p.Object.Owner=p;p.Object.InputAuthority=PlayerRef.One;return p;}
 static Player Enemy(Player p){var e=new Player{TeamIndex=2,Runner=p.Runner,Attacker=p};e.Object.Owner=e;e.Object.Id=new NetworkId(2);
  e.transform.position=new UnityEngine.Vector3(0,0,10);p.Runner.Objects[2]=e.Object;return e;}
 static void Tick(Player p){p.Runner.Time+=p.Runner.DeltaTime;}
 public static int Run(){
  var table=new MagicStatTable();Check(table.magics.Length==10,"ten replacement spells");
  var hitShooter=Caster(MagicType.Fire);hitShooter.Object.Id=new NetworkId(20);hitShooter.Runner.Objects[20]=hitShooter.Object;
  var hitVictim=Enemy(hitShooter);hitVictim.Object.InputAuthority=PlayerRef.Two;
  hitVictim.ReceiveMagicHit(table.GetStats(MagicType.Fire),PlayerRef.One,hitShooter.Object.Id);
  Check(hitShooter.HitMarkerEvents==1&&hitVictim.HitMarkerEvents==0,"confirmed damage notifies only its attacker");
  hitVictim.ReceiveMagicHit(table.GetStats(MagicType.Fire),PlayerRef.One,hitShooter.Object.Id);
  Check(hitShooter.HitMarkerEvents==1,"same tick multiple hits are coalesced");
  hitShooter.Runner.Time=.1f;hitVictim.ReceiveMagicHit(table.GetStats(MagicType.Fire),PlayerRef.One,hitShooter.Object.Id);
  Check(hitShooter.HitMarkerEvents==2,"subsequent hit restarts marker");
  hitShooter.Runner.Time=.2f;hitVictim.TryStartParry();hitVictim.ReceiveMagicHit(table.GetStats(MagicType.Fire),PlayerRef.One,hitShooter.Object.Id);
  Check(hitShooter.HitMarkerEvents==2,"parried hit has no hit marker");
  hitVictim.ParryTimer=default;hitVictim.ReceiveMagicHit(table.GetStats(MagicType.Binding),PlayerRef.One,hitShooter.Object.Id);
  Check(hitShooter.HitMarkerEvents==3,"binding confirms applied zero-damage status");
  hitShooter.Runner.Time=.3f;hitVictim.NowHp=1;hitVictim.ReceiveMagicHit(table.GetStats(MagicType.Fire),PlayerRef.One,hitShooter.Object.Id);
  Check(!hitVictim.IsAlive&&hitShooter.HitMarkerEvents==4,"lethal hit still confirms");
  hitShooter.Runner.Time=.4f;hitVictim.ReceiveMagicHit(table.GetStats(MagicType.Fire),PlayerRef.One,hitShooter.Object.Id);
  Check(hitShooter.HitMarkerEvents==4,"already dead victim cannot confirm another hit");
  hitVictim.IsAlive=true;hitVictim.NowHp=1;hitVictim.testBot=new MagicTestBot{Immortal=true};
  hitVictim.ReceiveMagicHit(table.GetStats(MagicType.Fire),PlayerRef.One,hitShooter.Object.Id);
  Check(hitVictim.NowHp==1&&hitShooter.HitMarkerEvents==5,"immortal training dummy still confirms accepted hits");
  hitShooter.Runner.Time=.5f;hitVictim.TeamIndex=hitShooter.TeamIndex;hitVictim.ReceiveMagicHit(table.GetStats(MagicType.Mine),PlayerRef.One,hitShooter.Object.Id);
  Check(hitShooter.HitMarkerEvents==5,"friendly mine damage is not an enemy hit marker");
  hitShooter.ReceiveMagicHit(table.GetStats(MagicType.Mine),PlayerRef.One,hitShooter.Object.Id);Check(hitShooter.HitMarkerEvents==5,"self damage has no marker");
  hitVictim.TeamIndex=2;hitVictim.ReceiveMagicHit(table.GetStats(MagicType.Fire),PlayerRef.One,new NetworkId(999));Check(hitShooter.HitMarkerEvents==5,"missing original shooter does not use a respawned replacement");
  hitVictim.TakeDamage(1,PlayerRef.None);Check(hitShooter.HitMarkerEvents==5,"environment damage does not confirm a spell");
  hitVictim.Object.HasStateAuthority=false;hitVictim.ReceiveMagicHit(table.GetStats(MagicType.Fire),PlayerRef.One,hitShooter.Object.Id);Check(hitShooter.HitMarkerEvents==5,"proxy prediction cannot confirm hits");
  hitVictim.Object.HasStateAuthority=true;hitShooter.Object.InputAuthority=PlayerRef.None;hitVictim.ReceiveMagicHit(table.GetStats(MagicType.Fire),PlayerRef.None,hitShooter.Object.Id);Check(hitShooter.HitMarkerEvents==5,"AI shooter has no player UI notification");
  var shooter=Caster(MagicType.Fire);shooter.Object.Id=new NetworkId(1);shooter.Runner.Objects[1]=shooter.Object;
  var receiver=Enemy(shooter);receiver.Object.InputAuthority=PlayerRef.Two;
  shooter.transform.position=new Vector3(12,4,30);
  receiver.ReceiveMagicHit(table.GetStats(MagicType.Fire),PlayerRef.One,shooter.Object.Id);
  Check(receiver.DamageDirectionEvents==1&&receiver.DamageDirectionPosition.x==12,"hit captures enemy position once");
  shooter.transform.position=new Vector3(99,4,99);
  Check(receiver.DamageDirectionPosition.x==12,"stored hit position does not follow moving attacker");
  receiver.ReceiveMagicHit(table.GetStats(MagicType.Fire),PlayerRef.One,shooter.Object.Id);
  Check(receiver.DamageDirectionEvents==2&&receiver.DamageDirectionPosition.x==99,"next hit captures its own new position");
  receiver.TryStartParry();receiver.ReceiveMagicHit(table.GetStats(MagicType.Fire),PlayerRef.One,shooter.Object.Id);
  Check(receiver.DamageDirectionEvents==2,"parried spell does not indicate received damage");
  receiver.ParryTimer=default;receiver.ReceiveMagicHit(table.GetStats(MagicType.Binding),PlayerRef.One,shooter.Object.Id);
  Check(receiver.DamageDirectionEvents==3,"binding hit indicates direction despite zero HP damage");
  receiver.TakeDamage(1,PlayerRef.None);Check(receiver.DamageDirectionEvents==3,"environment damage has no enemy direction");
  receiver.TakeDamage(1,PlayerRef.Two,attackerId:receiver.Object.Id);Check(receiver.DamageDirectionEvents==3,"self mine has no enemy direction");
  shooter.TeamIndex=receiver.TeamIndex;receiver.TakeDamage(1,PlayerRef.One,attackerId:shooter.Object.Id);
  Check(receiver.DamageDirectionEvents==3,"friendly damage not presented as enemy damage");
  shooter.TeamIndex=1;shooter.Object.InputAuthority=PlayerRef.None;
  receiver.ReceiveMagicHit(table.GetStats(MagicType.Ice),PlayerRef.None,shooter.Object.Id);
  Check(receiver.DamageDirectionEvents==4,"bot identified by network ID, not PlayerRef.None");
  receiver.TakeDamage(1,PlayerRef.One,attackerId:new NetworkId(999));
  Check(receiver.DamageDirectionEvents==4,"missing original shooter cannot substitute respawn position");
  Check((int)MagicType.Healing==5&&(int)MagicType.Binding==6&&(int)MagicType.Curse==8&&(int)MagicType.Razier==10,"stable saved IDs");
  Check(table.GetStats(MagicType.Fire).lockChargeSeconds>table.GetStats(MagicType.Ice).lockChargeSeconds,"Fire lock slower than Ice");
  Check(table.GetStats(MagicType.Vision).projectileSpeed==0&&table.GetStats(MagicType.Dark).projectileSpeed==0,"hitscan defaults");
  foreach(var magic in new[]{MagicType.Vision,MagicType.Dark,MagicType.Thunder,MagicType.Razier})
   Check(Near(table.GetStats(magic).hitscanRadius,.3f),"adjustable hitscan radius default "+magic);
  Check(Near(table.GetStats(MagicType.Fire).projectileRadius,.12f)&&Near(table.GetStats(MagicType.Mine).projectileRadius,.3f),"flying projectile sizes unchanged");
  Check(Near(table.GetStats(MagicType.Curse).ChannelManaPerSecond(150),37.5f),"Curse drains any hat capacity in four seconds");
  foreach(float hp in new[]{0f,49f,50f}){
   var p=Caster(MagicType.Dark);p.NowHp=hp;p.TryCastSelectedMagic();Check(p.Launches==0&&p.NowHp==hp&&p.CastSequence==0,"Dark low HP blocked "+hp);
  }
  var d=Caster(MagicType.Dark);d.NowHp=51;d.TryCastSelectedMagic();Check(d.NowHp==1&&d.Launches==1,"Dark pays 50 above threshold");
  var victim=Enemy(d);victim.ReceiveMagicHit(d.LastLaunched,PlayerRef.One);Check(d.NowHp==61&&victim.NowHp==135,"Dark actual hit heals 60");
  d=Caster(MagicType.Dark);d.NowHp=80;d.magicStatTable.magics[6].healthCost=20;d.magicStatTable.magics[6].healOnHit=30;
  d.TryCastSelectedMagic();victim=Enemy(d);victim.ReceiveMagicHit(d.LastLaunched,PlayerRef.One);Check(d.NowHp==90,"edited cost and healing honored");
  d=Caster(MagicType.Dark);d.NowHp=100;d.TryCastSelectedMagic();victim=Enemy(d);victim.TryStartParry();
  victim.ReceiveMagicHit(d.LastLaunched,PlayerRef.One);Check(victim.NowHp==200&&d.NowHp==50&&victim.ParrySequence==1,"parry nullifies Dark and prevents lifesteal, no reflection");
  victim.Runner.Time=.31f;victim.ReceiveMagicHit(d.LastLaunched,PlayerRef.One);Check(victim.NowHp==135&&d.NowHp==110,"damage resumes after parry window");
  d=Caster(MagicType.Dark);d.NowHp=100;d.TryCastSelectedMagic();victim=Enemy(d);victim.NowHp=1;
  victim.ReceiveMagicHit(d.LastLaunched,PlayerRef.One);Check(!victim.IsAlive&&d.NowHp==110,"killing hit still heals");
  d=Caster(MagicType.Healing);d.NowHp=190;d.Input(true);Check(d.NowHp==200&&d.NowAp==90,"Healing caps at max on press");
  d=Caster(MagicType.Healing);d.Input(true);Check(d.NowAp==100&&d.CastSequence==0,"full HP healing does not consume resources");
  d=Caster(MagicType.Binding);victim=Enemy(d);victim.ReceiveMagicHit(table.GetStats(MagicType.Binding),PlayerRef.One);
  Check(victim.NowHp==200&&Near(victim.BindingTimer.End,3)&&victim.Speed==0,"Binding roots three seconds without damage");
  victim.BindingTimer=default;victim.TryStartParry();victim.ReceiveMagicHit(table.GetStats(MagicType.Binding),PlayerRef.None);
  Check(!victim.BindingTimer.IsRunning&&victim.ParrySequence==1,"bot Binding with no PlayerRef still parried");
  d=Caster(MagicType.Fire);victim=Enemy(d);d.Input(true,false,victim.Object.Id);
  Check(d.LockProgress>0&&d.Launches==0,"lock charges on hold, not fires");d.Input(false,false,victim.Object.Id);Check(d.Launches==0,"early release no shot");
  for(int i=0;i<80;i++){d.Input(true,false,victim.Object.Id);Tick(d);}Check(d.IsFullyLocked,"full charge");
  d.Input(false,false,victim.Object.Id);Check(d.Launches==1&&d.LockProgress==0,"release fires and clears lock");
  Check(!d.CanAcquireMagicTarget(),"casting immediately disables target eligibility during cooldown");
  d.Input(true,false,victim.Object.Id);Check(d.LockProgress==0&&d.LockTargetId.Id==0&&!d.IsFullyLocked,"holding during cooldown cannot accumulate charge");
  d.RPC_SetLockTarget(victim.Object.Id,Vector3.forward);Check(d.LockTargetId.Id==0,"legacy RPC cannot bypass cooldown");
  d.LockTargetId=victim.Object.Id;d.LockProgress=1;d.IsFullyLocked=true;
  Check(d.GetDisplayedLockTarget()==null,"HUD hides stale lock during cooldown");
  d.UpdateLockCharge();Check(d.LockProgress==0&&!d.IsFullyLocked&&d.LockTargetId.Id==0,"cooldown clears stale completed lock");
  d.Input(false,false,victim.Object.Id);Check(d.Launches==1,"release during cooldown does not cast");
  float cooldownEnd=d.MagicCooldowns[(int)MagicType.Fire].End;
  d.Runner.Time=cooldownEnd-.05f;d.Input(true,false,victim.Object.Id);Check(d.LockProgress==0,"cannot precharge just before cooldown expires");
  d.Runner.Time=cooldownEnd;d.Input(true,false,victim.Object.Id);
  Check(d.CanAcquireMagicTarget()&&d.LockProgress>0&&d.LockProgress<.1f,"held input begins fresh charge at expiry");
  Check(d.GetDisplayedLockTarget()==victim,"HUD returns acquired target after cooldown");
  d.Input(false,false,victim.Object.Id);Check(d.Launches==1,"release immediately after expiry still requires full charge");
  for(int i=0;i<80;i++){d.Input(true,false,victim.Object.Id);Tick(d);}d.Input(false,false,victim.Object.Id);
  Check(d.Launches==2,"normal full lock and release work after cooldown");
  d.Selected=MagicType.Ice;Check(d.CanAcquireMagicTarget(),"another ready spell remains selectable while Fire cools down");
  d.Selected=MagicType.Fire;Check(!d.CanAcquireMagicTarget(),"switching back does not reset per-magic cooldown");
  d.CurrentMagicSlot=3;Check(!d.CanAcquireMagicTarget(),"parry slot cannot acquire spell targets");
  d=Caster(MagicType.Fire);victim=Enemy(d);d.Object.HasStateAuthority=false;
  d.MagicCooldowns.Set((int)MagicType.Fire,TickTimer.CreateFromSeconds(d.Runner,1));
  var heldButtons=new NetworkButtons{Bits=1};var lockInput=new NetworkInputData{lockTarget=victim.Object.Id};
  d.ProcessPredictedProjectileInput(lockInput,heldButtons);
  Check(d.LockTargetId.Id==0&&d.LockProgress==0,"owner prediction cannot start lock during replicated cooldown");
  d.previousButtons=heldButtons;d.ProcessPredictedProjectileInput(lockInput,default);
  Check(d.Predictions==0,"owner prediction cannot release a cooled-down lock spell");
  d.Runner.Time=1;d.ProcessPredictedProjectileInput(lockInput,heldButtons);
  Check(d.LockProgress>0&&d.LockTargetId.Equals(victim.Object.Id),"prediction reacquires after expiry");
  d.Object.IsValid=false;Check(!d.CanAcquireMagicTarget(),"despawned player cannot acquire targets");
  d=Caster(MagicType.Fire);victim=Enemy(d);for(int i=0;i<80;i++){d.Input(true,false,victim.Object.Id);Tick(d);}
  victim.Visible=false;d.Input(false,false,victim.Object.Id);Check(d.Launches==0,"wall invalidates completed lock");
  victim.Visible=true;victim.transform.position=new UnityEngine.Vector3(0,0,-10);Check(!d.IsValidLockTarget(victim),"rear hemisphere cannot lock");
  victim.transform.position=new UnityEngine.Vector3(10,0,.1f);Check(d.IsValidLockTarget(victim),"wide forward hemisphere accepted without aim alignment");
  d=Caster(MagicType.Vision);d.Input(true);d.Input(true);Check(d.Launches==1,"hitscan press only, no held repeats");
  d.Input(false);Tick(d);d.Input(true);Check(d.Launches==1,"cooldown blocks repeated shot");
  d.Runner.Time=1;d.Input(false);d.Input(true);Check(d.Launches==2,"cast after cooldown");
  d=Caster(MagicType.Thunder);d.Input(true);Check(d.Launches==0&&d.pendingMagic==MagicType.Thunder,"Thunder cast delay");
  d.Runner.Time=.31f;d.UpdatePendingCast();Check(d.Launches==1&&d.LastLaunched.effect==MagicEffectKind.AreaDamage,"Thunder delayed AOE");
  d=Caster(MagicType.Curse);victim=Enemy(d);victim.MaxHp=victim.NowHp=1000;
  for(int i=0;i<200;i++){d.Input(true,false,victim.Object.Id);d.RegenerateAp();Tick(d);}
  Check(Near(d.NowAp,0)&&!d.IsChanneling&&d.channelNeedsRelease,"Curse spends full mana in four seconds without regen");
  Check(Near(victim.NowHp,952),"Curse DPS integrated over four seconds");
  d.NowAp=20;d.Input(true,false,victim.Object.Id);Check(!d.IsChanneling,"exhaustion requires release");
  d.Input(false);d.Runner.Time+=1;d.Input(true,false,victim.Object.Id);Check(d.IsChanneling,"channel restarts after release");
  victim.Visible=false;d.Input(true,false,victim.Object.Id);Check(!d.IsChanneling,"Curse ends behind wall");
  d=Caster(MagicType.Razier);victim=Enemy(d);d.TraceTarget=victim;
  d.CastOffset=new Vector3(3,2,1);
  for(int i=0;i<50;i++){d.Input(true);d.RegenerateAp();Tick(d);}
  Check(Near(d.NowAp,80)&&Near(victim.NowHp,182),"Razier DPS and MP per second");
  Check(d.LastTraceOrigin.x==3&&d.LastTraceOrigin.y==2,"straight channel traces from the editable cast root");
  d.CastOffset=new Vector3(4,5,6);d.Input(true);Check(d.LastTraceOrigin.x==4&&d.LastTraceOrigin.y==5,"channel follows the current cast root each tick");
  d.Input(false);Check(!d.IsChanneling&&d.TimerIsActive(d.MagicCooldowns[(int)MagicType.Razier]),"release ends channel and starts cooldown");
  d=Caster(MagicType.Razier);victim=Enemy(d);d.TraceTarget=victim;victim.TryStartParry();
  for(int i=0;i<6;i++){d.Input(true);Tick(d);}
  Check(victim.NowHp==200&&victim.ParrySequence==1&&!d.IsChanneling,"parry extinguishes channel");
  d=Caster(MagicType.Fire);victim=Enemy(d);victim.testBot=new MagicTestBot{Immortal=true};
  victim.ReceiveMagicHit(new MagicStatEntry{damage=10000},PlayerRef.One);Check(victim.NowHp==1&&victim.IsAlive,"immortal dummy health floor");
  d=Caster(MagicType.Vision);d.Object.HasStateAuthority=false;d.TryCastSelectedMagic();Check(d.Launches==0&&d.NowAp==100,"proxy cannot spend or cast");
  victim=Enemy(d);victim.Object.HasStateAuthority=false;victim.ReceiveMagicHit(table.GetStats(MagicType.Fire),PlayerRef.One);
  Check(victim.NowHp==200&&victim.HitSequence==0,"proxy cannot apply damage");
  d=Caster(MagicType.Healing);d.CastOffset=new Vector3(3,2,1);d.NowHp=100;d.Input(true);
  Check(d.LastCastOrigin.x==d.LockAimPoint.x&&d.LastCastOrigin.y==d.LockAimPoint.y,"self-healing remains body-centered, independent of muzzle");
  return n;
 }
}}
'@
$nl = [Environment]::NewLine
Add-Type -TypeDefinition ($stubs + $nl + 'namespace CombatChecks {' + $nl + $table + $nl + $enum + $nl + '}' + $nl + $probe)
"Magic combat behavior checks passed: $([CombatChecks.Tests]::Run())"
