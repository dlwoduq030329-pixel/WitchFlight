# Runs production input/construct methods against controlled clocks, targets and collision doubles.
# Full Unity physics/rendering and two-peer Fusion replication still require Play Mode.
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$area=Get-Content Assets/Script/Player.AreaMagic.cs -Raw
$construct=Get-Content Assets/Script/MagicProjectile.Constructs.cs -Raw
$summonCombat=Get-Content Assets/Script/MagicProjectile.SummonCombat.cs -Raw
function Method([string]$source,[string]$name) {
 $m=[regex]::Match($source,'(?ms)^    (?:public|private) (?:static )?[\w<>]+ '+$name+'\([^{}]*?\)\r?\n    \{.*?^    \}')
 if(!$m.Success){throw "Missing production method $name"}
 $m.Value -replace '^    private ','    public '
}
$code=@'
using System;
using System.Collections.Generic;
namespace AreaSummonChecks {
public struct Vector3 {
 public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
 public float sqrMagnitude=>x*x+y*y+z*z; public float magnitude=>(float)Math.Sqrt(sqrMagnitude);
 public Vector3 normalized=>magnitude>.00001f?this/magnitude:default;
 public void Normalize(){this=normalized;}
 public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
 public static Vector3 operator+(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
 public static Vector3 operator-(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
 public static Vector3 operator*(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);
 public static Vector3 operator/(Vector3 a,float b)=>a*(1f/b);
 public static float Distance(Vector3 a,Vector3 b)=>(a-b).magnitude;
}
public struct Quaternion {public static Quaternion LookRotation(Vector3 d)=>default;}
public class Transform {public Vector3 position,forward=new(0,0,1);public Quaternion rotation;}
public static class Mathf {
 public static float Max(float a,float b)=>Math.Max(a,b); public static float Min(float a,float b)=>Math.Min(a,b);
 public static int Clamp(int x,int a,int b)=>Math.Clamp(x,a,b);public static float Clamp01(float x)=>Math.Clamp(x,0,1);
 public static float Sqrt(float x)=>(float)Math.Sqrt(x);
}
public enum MagicType {Fire=1,Ice=2,Vision=3,Thunder=4,Healing=5,Binding=6,Dark=7,Curse=8,Mine=9,Razier=10,Flare=11,Smoke=12}
public enum MagicEffectKind {DirectDamage,DelayedArea,PursuitSummon,Binding}
public enum MagicConstructKind {Projectile,ThunderArea,Summon,SummonBolt}
public struct MagicStatEntry {
 public MagicType magic;public MagicEffectKind effect;
 public float strikeDelay=1.5f,radius=12,activationDelay=.5f,effectDuration=15,projectileSpeed=45,range=100;
 public float summonMoveSpeed=28,summonAttackRange=55,summonFollowDistance=18,summonAttackInterval=2,lockChargeSeconds=.65f;
 public float castSeconds,damage; public bool requiresTarget,requiresFullLock;
 public float summonHitRadius=.65f,summonChannelHealth=50;
 public MagicStatEntry(){}
}
public struct NetworkId {public int Raw;public NetworkId(int id){Raw=id;}}
public struct Tick {public int Raw;}
public struct NetworkPrefabRef {public bool IsValid;}
public struct TickTimer {
 public bool IsRunning; public float End;
 public static TickTimer None=>default;
 public bool Expired(NetworkRunner r)=>IsRunning&&r.Time>=End;
 public static TickTimer CreateFromSeconds(NetworkRunner r,float s)=>new(){IsRunning=true,End=r.Time+s};
}
public class NetworkObject {
 public bool IsValid=true,HasStateAuthority=true;public int InputAuthority; public NetworkId Id;public object Component;
 public T GetComponent<T>() where T:class=>Component as T;
 public bool TryGetComponent<T>(out T c) where T:class {c=Component as T;return c!=null;}
}
public class NetworkRunner {
 public float Time,DeltaTime=.1f;public Tick Tick=>new(){Raw=(int)(Time*100)};
 public Dictionary<int,NetworkObject> Objects=new();public List<MagicProjectile> Spawned=new(); public int Despawns;
 public bool TryFindObject(NetworkId id,out NetworkObject o)=>Objects.TryGetValue(id.Raw,out o)&&o.IsValid;
 public NetworkObject Spawn(NetworkPrefabRef p,Vector3 position,Quaternion rotation,int authority){
  var s=new MagicProjectile{Runner=this};s.Object.Id=new(1000+Spawned.Count);s.Object.Component=s;
  s.transform.position=position;s.Object.InputAuthority=authority;Objects[s.Object.Id.Raw]=s.Object;
  Spawned.Add(s);MagicProjectile.active.Add(s);return s.Object;
 }
 public void Despawn(NetworkObject o){o.IsValid=false;Despawns++;if(o.Component is MagicProjectile s)MagicProjectile.active.Remove(s);}
}
public struct RaycastHit {public float distance;}
public static class PerkEffects {public static MagicStatEntry AreaHit(MagicStatEntry s,float d)=>s;}
public class BattleManager {public static BattleManager Instance=new();public bool IsGameplayActive=true;}
public struct NetworkInputData {public bool hasAreaAimPoint;public Vector3 areaAimPoint;}
public class Player {
 public static List<Player> ActiveCombatants=new(); public NetworkRunner Runner;public NetworkObject Object=new();
 public Transform transform=new();public Vector3 LockAimPoint=>transform.position;public int TeamIndex=1;
 public bool IsAlive=true,IsStealthed,Visible=true,CanAim=true,areaAimArmed,hasConfirmedAreaPoint,ValidPoint=true;
 public int ConfirmedHits;public void ConfirmSummonHit(){ConfirmedHits++;}
 public float Mana=100;public int Casts,Hits;public Vector3 confirmedAreaPoint;
 public NetworkPrefabRef magicProjectilePrefab=new(){IsValid=true};public MagicStatEntry SelectedMagicStats=new();
 public bool CanAimAreaMagic()=>CanAim&&Mana>=80;
 public bool ValidateAreaPoint(Vector3 point,MagicStatEntry stats,out Vector3 valid){valid=point;return ValidPoint;}
 public void TryCastSelectedMagic(){if(CanCreateSpecialMagic(SelectedMagicStats)){Casts++;Mana-=80;}}
 public void ReceiveMagicHit(MagicStatEntry s,int shooter,NetworkId id,Vector3 incomingDirection=default){Hits++;}
'@
$code+=(Method $area 'ProcessAreaAim')+(Method $area 'CanCreateSpecialMagic')
$code+='} public class AreaPointProbe { public Vector3 MagicCastPosition; public bool Wall; public Vector3 TraceMagic(MagicStatEntry s,Vector3 origin,Vector3 direction,out Player victim){victim=null;return origin+direction*(Wall?1:s.range);}'
$code+=(Method $area 'ValidateAreaPoint')+(Method $area 'IsFiniteAreaPoint')
$code+=@'
}
public class MagicProjectile {
 public static HashSet<MagicProjectile> active=new();public NetworkObject Object=new();public NetworkRunner Runner;
 public Transform transform=new();public MagicConstructKind ConstructKind;public MagicType Magic;
 public NetworkId ShooterId,TargetId;public int Shooter,ShooterTeam,constructSpawnTick;
 public MagicStatEntry stats;public bool Settled,Finished,Overlapping,Blocked;public float CollisionDistance=.2f;
 public float ConstructRadius,ConstructDelay,SummonLockProgress;public int Impacts;
 public bool SummonMoving;public int SummonAttackSequence;
 public TickTimer ConstructTimer,Lifetime,summonShotTimer,summonScanTimer;public NetworkPrefabRef summonProjectilePrefab;
 public Vector3 direction;
 public float SummonDamageTaken;
 public bool IsLiveSummon=>Object!=null&&Object.IsValid&&ConstructKind==MagicConstructKind.Summon&&!Finished;
 public MagicStatEntry PresentationStats=>stats;
 public float SummonHitRadius=>stats.summonHitRadius;
 public static bool SummonSight=true;public static bool HasSummonSight(Vector3 a,Vector3 b)=>SummonSight;
 public void Initialize(Player p,Player target,MagicStatEntry s,Vector3 aim){
  stats=s;Magic=s.magic;ShooterId=p.Object.Id;Shooter=p.Object.InputAuthority;ShooterTeam=p.TeamIndex;
  TargetId=target!=null?target.Object.Id:default;direction=aim;
 }
 public Player FindTarget()=>Runner.TryFindObject(TargetId,out var o)?o.GetComponent<Player>():null;
 public bool HasBlastSight(Player p)=>p.Visible;
 public bool HasBlastSight(Vector3 origin,Player p)=>p.Visible;
 public bool TryGetInitialOverlap(out object hit){hit=null;return Overlapping;}
 public bool TryGetCollision(float step,out RaycastHit hit){hit=new(){distance=CollisionDistance};return Blocked&&step>=CollisionDistance;}
 public void RPC_Impact(Vector3 point,MagicType magic){Impacts++;}
'@
foreach($method in 'SummonBoltStats','InitializeConstruct','TrimSummons','TickConstruct','FindSummonTarget'){$code+=Method $construct $method}
foreach($method in 'ReceiveSummonHit','TryTraceSummon','TrySphereContact','DamageSummonsInArea','FindSummonTargetById'){$code+=Method $summonCombat $method}
$code+=[regex]::Match($construct,'(?ms)^    private bool IsSummonTarget\(.*?;').Value.Replace('private bool','public bool')
$code+=@'
}
public static class Tests {
 static int n;static void Check(bool ok,string label){if(!ok)throw new Exception(label);n++;}
 static bool Near(float a,float b)=>Math.Abs(a-b)<.002f;
 static Player Pilot(NetworkRunner r,int id,int team=1,float z=0){
  var p=new Player{Runner=r,TeamIndex=team};p.Object.Id=new(id);p.Object.InputAuthority=id;p.Object.Component=p;
  p.transform.position=new(0,0,z);r.Objects[id]=p.Object;Player.ActiveCombatants.Add(p);return p;
 }
 static MagicProjectile Construct(Player owner,MagicEffectKind kind){
  var s=owner.Runner.Spawn(new(){IsValid=true},new(0,0,0),default,owner.Object.InputAuthority).GetComponent<MagicProjectile>();
  s.InitializeConstruct(owner,new(){effect=kind,magic=kind==MagicEffectKind.DelayedArea?MagicType.Thunder:MagicType.Mine,damage=18},new(){IsValid=true});return s;
 }
 static void Advance(MagicProjectile s,float seconds){int steps=(int)Math.Ceiling(seconds/s.Runner.DeltaTime);for(int i=0;i<steps;i++){s.Runner.Time+=s.Runner.DeltaTime;s.TickConstruct();}}
 public static int Run(){
  var aim=new AreaPointProbe {MagicCastPosition=new(300,50,200)};
  Check(aim.ValidateAreaPoint(new(300,50,240),new(){range=100},out var picked)&&Near(picked.z,240),"world point far from world origin is valid");
  Check(!aim.ValidateAreaPoint(new(float.NaN,0,0),new(),out _)&&!aim.ValidateAreaPoint(new(float.PositiveInfinity,0,0),new(),out _),"nonfinite coordinates rejected");
  Check(!aim.ValidateAreaPoint(new(300,50,400),new(){range=100},out _),"out of range coordinates rejected");
  aim.Wall=true;Check(!aim.ValidateAreaPoint(new(300,50,240),new(){range=100},out _),"point behind caster-side wall rejected");
  var r=new NetworkRunner();var owner=Pilot(r,1);owner.SelectedMagicStats=new(){effect=MagicEffectKind.DelayedArea};
  var input=new NetworkInputData{hasAreaAimPoint=true,areaAimPoint=new(0,0,40)};
  owner.ProcessAreaAim(input,true,true,false);Check(owner.areaAimArmed&&owner.Casts==0&&owner.Mana==100,"press only aims, no resources spent");
  owner.ProcessAreaAim(input,true,false,false);Check(owner.Casts==0,"hold never casts");
  owner.ProcessAreaAim(input,false,false,true);Check(owner.Casts==1&&owner.Mana==20&&!owner.areaAimArmed,"release commits exactly once");
  owner.ProcessAreaAim(input,false,false,true);Check(owner.Casts==1,"duplicate release ignored");
  owner.ProcessAreaAim(input,true,true,false);Check(!owner.areaAimArmed,"insufficient MP blocks aiming");
  owner.Mana=100;owner.ProcessAreaAim(input,true,true,false);owner.CanAim=false;owner.ProcessAreaAim(input,true,false,false);
  owner.CanAim=true;owner.ProcessAreaAim(input,false,false,true);Check(owner.Casts==1,"stun/cooldown cancellation does not cast on release");
  owner.ProcessAreaAim(input,true,true,false);owner.ValidPoint=false;owner.ProcessAreaAim(input,false,false,true);
  Check(owner.Casts==1&&owner.Mana==100,"invalid/obstructed point does not spend MP");owner.ValidPoint=true;
  owner.ProcessAreaAim(input,true,true,false);owner.areaAimArmed=false;owner.ProcessAreaAim(input,false,false,true);
  Check(owner.Casts==1,"slot/menu/perk cancellation does not release-cast");
  Check(!owner.CanCreateSpecialMagic(new(){effect=MagicEffectKind.PursuitSummon,effectDuration=0}),"invalid summon lifetime rejected");
  owner.magicProjectilePrefab=default;Check(!owner.CanCreateSpecialMagic(new(){effect=MagicEffectKind.PursuitSummon}),"missing prefab rejected before spending");
  var enemy=Pilot(r,2,2,8);var friend=Pilot(r,3,1,4);var outside=Pilot(r,4,2,20);var hidden=Pilot(r,5,2,3);hidden.Visible=false;
  var dead=Pilot(r,6,2,3);dead.IsAlive=false;var other=Pilot(new NetworkRunner(),7,2,2);
  var area=Construct(owner,MagicEffectKind.DelayedArea);Advance(area,1.4f);Check(enemy.Hits==0,"no early lightning hit");
  owner.IsAlive=false;Advance(area,.2f);Check(enemy.Hits==1&&area.Impacts==1,"confirmed area survives caster death and hits once");
  Check(friend.Hits==0&&outside.Hits==0&&hidden.Hits==0&&dead.Hits==0&&other.Hits==0,"area filters team/range/wall/dead/runner");
  Advance(area,.1f);Check(enemy.Hits==1,"area never damages twice");Advance(area,.3f);Check(!area.Object.IsValid,"finished area despawns");
  owner.IsAlive=true;Player.ActiveCombatants.Clear();Player.ActiveCombatants.Add(owner);Player.ActiveCombatants.Add(enemy);enemy.transform.position=new(0,0,40);
  var summon=Construct(owner,MagicEffectKind.PursuitSummon);Advance(summon,.4f);Check(summon.transform.position.z==0,"summon activation delay");
  int initial=r.Spawned.Count;Advance(summon,1f);Check(summon.transform.position.z>0&&summon.transform.position.z<=22.01f,"summon pursues but respects standoff");
  Check(r.Spawned.Count==initial+1,"summon fires after lock");var bolt=r.Spawned[r.Spawned.Count-1];
  Check(summon.SummonAttackSequence==1,"Successful spawn emits exactly one replicated animation event");
  Check(bolt.ConstructKind==MagicConstructKind.SummonBolt&&bolt.stats.requiresTarget&&bolt.stats.effect==MagicEffectKind.DirectDamage,"bolt is parryable/flare-breakable homing shot, not mine explosion");
  Check(bolt.ShooterId.Equals(owner.Object.Id)&&bolt.ShooterTeam==1,"kill/XP attribution retained");
  int count=r.Spawned.Count;Advance(summon,1f);Check(r.Spawned.Count==count,"attack cadence not every tick");
  Check(summon.SummonAttackSequence==1,"Attack cooldown cannot replay animation every tick");
  Advance(summon,1f);Check(r.Spawned.Count==count+1,"repeat attack after interval");
  Check(summon.SummonAttackSequence==2,"Next actual shot advances animation sequence");
  enemy.Visible=false;count=r.Spawned.Count;Advance(summon,2.5f);Check(r.Spawned.Count==count&&summon.SummonLockProgress==0,"wall breaks lock and prevents fire");
  enemy.Visible=true;enemy.transform.position=new(0,0,200);Advance(summon,1f);Check(r.Spawned.Count==count,"out of detection range prevents fire");
  enemy.transform.position=new(0,0,40);summon.transform.position=new(0,0,0);summon.Blocked=true;Advance(summon,.1f);
  Check(summon.transform.position.z<=.171f,"wall sweep caps travel");
  summon.Overlapping=true;float position=summon.transform.position.z;Advance(summon,.2f);Check(Near(summon.transform.position.z,position),"embedded summon does not tunnel");
  owner.IsAlive=false;Advance(summon,.1f);Check(!summon.Object.IsValid,"owner death ends summon");owner.IsAlive=true;
  var expired=Construct(owner,MagicEffectKind.PursuitSummon);r.Time=expired.Lifetime.End;expired.TickConstruct();Check(!expired.Object.IsValid,"lifetime bounded");
  MagicProjectile.active.Clear();var a=Construct(owner,MagicEffectKind.PursuitSummon);r.Time+=1;var b=Construct(owner,MagicEffectKind.PursuitSummon);
  var alien=Construct(Pilot(new NetworkRunner(),8),MagicEffectKind.PursuitSummon);r.Time+=1;var c=Construct(owner,MagicEffectKind.PursuitSummon);
  MagicProjectile.TrimSummons(owner,2,c.Object);Check(!a.Object.IsValid&&b.Object.IsValid&&c.Object.IsValid&&alien.Object.IsValid,"multiple summons retained; only oldest same-owner same-runner is replaced");
  var origin=new MagicStatEntry{effect=MagicEffectKind.PursuitSummon,radius=9,requiresTarget=false};var shot=MagicProjectile.SummonBoltStats(origin);
  Check(origin.effect==MagicEffectKind.PursuitSummon&&!origin.requiresTarget&&shot.radius==0,"bolt conversion never mutates summon snapshot");
  MagicProjectile.active.Clear();Player.ActiveCombatants.Clear();r=new();owner=Pilot(r,1);enemy=Pilot(r,2,2,40);
  var pursuer=Construct(owner,MagicEffectKind.PursuitSummon);Advance(pursuer,.6f);float chased=pursuer.transform.position.z;
  owner.transform.position=new(0,0,-200);enemy.transform.position=new(0,0,200);enemy.Visible=false;
  count=r.Spawned.Count;Advance(pursuer,.4f);
  Check(pursuer.transform.position.z>chased&&pursuer.TargetId.Equals(enemy.Object.Id),"summon follows enemy, not caster, after leaving acquisition range or losing sight");
  Check(r.Spawned.Count==count&&pursuer.SummonLockProgress==0,"occluded pursuit does not shoot through wall");
  Check(pursuer.SummonMoving,"Moving animation follows actual enemy pursuit, including occlusion");
  Check(pursuer.SummonAttackSequence==0,"Occlusion cannot trigger an attack animation");
  pursuer.Blocked=true;pursuer.CollisionDistance=0;Advance(pursuer,.2f);
  Check(!pursuer.SummonMoving,"Wall-stopped summon does not play movement");pursuer.Blocked=false;
  enemy.IsAlive=false;Advance(pursuer,.2f);Check(pursuer.SummonLockProgress==0,"dead target cancels lock");enemy.IsAlive=true;enemy.Visible=true;
  foreach(var magic in new[]{MagicType.Fire,MagicType.Ice,MagicType.Vision,MagicType.Thunder,MagicType.Binding,MagicType.Dark,MagicType.Mine}){
   var body=Construct(owner,MagicEffectKind.PursuitSummon);
   var attack=new MagicStatEntry{magic=magic,damage=magic==MagicType.Binding?0:.01f,effect=magic==MagicType.Binding?MagicEffectKind.Binding:MagicEffectKind.DirectDamage};
   Check(body.ReceiveSummonHit(attack,2,enemy.Object.Id)&&body.Finished&&body.Impacts==1,"one hostile hit removes summon even for zero-damage binding: "+magic);
   Check(!body.ReceiveSummonHit(attack,2,enemy.Object.Id)&&body.Impacts==1,"finished body rejects duplicate hit: "+magic);
   Advance(body,.3f);Check(!body.Object.IsValid,"destroyed body despawns after impact replication: "+magic);
  }
  var durable=Construct(owner,MagicEffectKind.PursuitSummon);
  Check(durable.ReceiveSummonHit(new(){magic=MagicType.Curse,damage=20},2,enemy.Object.Id)&&!durable.Finished,"curse contributes damage without one-hit kill");
  durable.ReceiveSummonHit(new(){magic=MagicType.Razier,damage=29.9f},2,enemy.Object.Id);
  Check(!durable.Finished&&Near(durable.SummonDamageTaken,49.9f),"beam and curse damage share a cumulative fifty-point pool");
  durable.ReceiveSummonHit(new(){magic=MagicType.Razier,damage=.1f},2,enemy.Object.Id);Check(durable.Finished,"exactly fifty sustained damage destroys body");
  durable=Construct(owner,MagicEffectKind.PursuitSummon);durable.stats.summonChannelHealth=75;
  durable.ReceiveSummonHit(new(){magic=MagicType.Razier,damage=50},2,enemy.Object.Id);Check(!durable.Finished,"editable channel threshold honored");
  durable.ReceiveSummonHit(new(){magic=MagicType.Fire,damage=1},2,enemy.Object.Id);Check(durable.Finished,"ordinary hit destroys partially damaged summon immediately");
  durable=Construct(owner,MagicEffectKind.PursuitSummon);
  Check(!durable.ReceiveSummonHit(new(){magic=MagicType.Fire,damage=50},1,owner.Object.Id),"owner and allied attacks cannot destroy own summon");
  durable.Object.HasStateAuthority=false;Check(!durable.ReceiveSummonHit(new(){magic=MagicType.Fire,damage=50},2,enemy.Object.Id),"proxy cannot damage or destroy summon");
  durable.Object.HasStateAuthority=true;BattleManager.Instance.IsGameplayActive=false;
  Check(!durable.ReceiveSummonHit(new(){magic=MagicType.Fire,damage=50},2,enemy.Object.Id),"intermission cannot damage summon");BattleManager.Instance.IsGameplayActive=true;
  Check(!durable.ReceiveSummonHit(new(){magic=MagicType.Curse,damage=float.NaN},2,enemy.Object.Id)&&!durable.ReceiveSummonHit(new(){magic=MagicType.Curse,damage=-1},2,enemy.Object.Id),"invalid channel damage cannot corrupt pool");
  Check(!durable.ReceiveSummonHit(new(){magic=MagicType.Healing,damage=0},2,enemy.Object.Id),"non-attacking utility is not a hit");
  MagicProjectile.active.Clear();durable=Construct(owner,MagicEffectKind.PursuitSummon);durable.transform.position=new(0,0,10);
  var nearer=Construct(owner,MagicEffectKind.PursuitSummon);nearer.transform.position=new(0,0,5);
  Check(MagicProjectile.TryTraceSummon(r,2,default,new(0,0,1),20,0,out var traced,out var distance)&&traced==nearer&&Near(distance,4.35f),"nearest hostile body selected by sphere contact");
  Check(!MagicProjectile.TryTraceSummon(r,2,default,new(0,0,1),3,0,out _,out _),"physical wall hit bounds summon sweep");
  Check(!MagicProjectile.TryTraceSummon(r,1,default,new(0,0,1),20,0,out _,out _),"friendly body does not block shots");
  Check(!MagicProjectile.TryTraceSummon(new(),2,default,new(0,0,1),20,0,out _,out _),"different runner bodies cannot be hit");
  Check(!MagicProjectile.TrySphereContact(default,new(0,0,1),new(2,0,5),.65f,20,out _),"miss outside body radius");
  Check(MagicProjectile.TryTraceSummon(r,2,nearer.transform.position,new(0,0,1),0,0,out traced,out distance)&&Near(distance,0),"origin inside body counts as contact");
  MagicProjectile.SummonSight=false;MagicProjectile.DamageSummonsInArea(r,2,new(0,0,5),12,new(){magic=MagicType.Thunder,damage=100},enemy.Object.Id);
  Check(!durable.Finished&&!nearer.Finished,"area attack cannot destroy bodies behind wall");
  MagicProjectile.SummonSight=true;MagicProjectile.DamageSummonsInArea(r,2,new(0,0,5),2,new(){magic=MagicType.Thunder,damage=100},enemy.Object.Id);
  Check(nearer.Finished&&!durable.Finished,"area damage hits only hostile bodies in radius");
  Check(MagicProjectile.TryTraceSummon(r,2,default,new(0,0,1),20,0,out traced,out _)&&traced==durable,"destroyed body immediately stops intercepting attacks");
  return n;
 }
}}
'@
Add-Type -TypeDefinition $code
"Area/summon behavior checks passed: $([AreaSummonChecks.Tests]::Run())"
$p=Get-Content Assets/Script/MagicProjectile.cs -Raw
if($p-notmatch 'if \(!Object.HasStateAuthority \|\| !initialized\)' -or $p-notmatch 'if \(IsConstruct\) \{ TickConstruct\(\); return; \}') {throw 'Construct simulation authority gate missing'}
if($p-notmatch 'shot.IsConstruct \|\| !shot.stats.requiresTarget' -or $p-notmatch 'shot.Finished \|\| shot.IsConstruct'){throw 'Summon bodies must not count as incoming projectiles / lose AI to flare'}
if($construct-match 'Player target = FindTarget\(\);\s*\r?\n\s*summonVisual'){throw 'Render must never write TargetId'}
$asset=Get-Content Assets/Resources/MagicStatTable.asset -Raw
foreach($entry in @(@{Id=4;Effect=11},@{Id=9;Effect=12})) {
 $block=[regex]::Match($asset,'(?ms)^  - magic: '+$entry.Id+'\r?\n.*?(?=^  - magic:|\z)').Value
 if($block-notmatch ('(?m)^    effect: '+$entry.Effect+'\r?$')){throw "Saved spell mode not migrated: $($entry.Id)"}
 if($block-notmatch '(?m)^    icon: \{fileID: 21300000, guid:'){throw 'Existing icon reference lost'}
}
'Authority, render purity, projectile counter compatibility, saved modes and sprite preservation passed.'
$summonBlock=[regex]::Match($asset,'(?ms)^  - magic: 9\r?\n.*?(?=^  - magic:|\z)').Value
if($summonBlock-notmatch 'summonChannelHealth: 50' -or $summonBlock-notmatch 'summonHitRadius: 0.65'){throw 'Summon hit settings not serialized'}
if($p-notmatch 'TryTraceSummon\(Runner, ShooterTeam' -or $p-notmatch 'summonHit.ReceiveSummonHit' -or $construct-notmatch 'DamageSummonsInArea'){throw 'Projectile/area summon damage routes missing'}
