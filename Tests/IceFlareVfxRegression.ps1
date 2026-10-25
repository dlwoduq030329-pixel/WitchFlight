# Production hit/RPC and trajectory math with Unity/Fusion doubles; no live particle rendering.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$projectile = Get-Content Assets/Script/MagicProjectile.cs -Raw
$ice = Get-Content Assets/Script/IceImpactFollowUpVisual.cs -Raw
function Method([string]$source,[string]$name) {
    $m=[regex]::Match($source,'(?ms)^    (?:public|private) (?:static )?[\w<>]+ '+$name+'\([^{}]*?\)\r?\n    \{.*?^    \}')
    if(!$m.Success){throw "Missing method: $name"}
    $m.Value -replace '^    private ','    public '
}
$source=@'
using System;
using System.Collections.Generic;
namespace IceFlareChecks {
 public struct Vector3 {
  public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}
  public float sqrMagnitude=>x*x+y*y+z*z;
  public static Vector3 operator+(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
  public static Vector3 operator-(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
  public static Vector3 operator*(float f,Vector3 v)=>new(f*v.x,f*v.y,f*v.z);
  public static float Distance(Vector3 a,Vector3 b)=>(float)Math.Sqrt((a-b).sqrMagnitude);
 }
 public static class Mathf {
  public const float PI=(float)Math.PI;
  public static float Clamp(float x,float a,float b)=>Math.Clamp(x,a,b);
  public static float Clamp01(float x)=>Clamp(x,0,1);
  public static float Max(float a,float b)=>Math.Max(a,b);
  public static float Sin(float x)=>(float)Math.Sin(x);public static float Cos(float x)=>(float)Math.Cos(x);
 }
 public struct NetworkId {public int Id;public NetworkId(int id){Id=id;}}
 public class NetworkObject {
  public NetworkId Id;public object Component;public bool IsValid=true,HasStateAuthority=true;
  public T GetComponent<T>() where T:class=>Component as T;
 }
 public class NetworkRunner {
  public float Time;
  public Dictionary<int,NetworkObject> Objects=new();
  public bool TryFindObject(NetworkId id,out NetworkObject obj)=>Objects.TryGetValue(id.Id,out obj);
 }
 public struct TickTimer {public float Seconds,End;public bool Expired(NetworkRunner r)=>r.Time>=End;public static TickTimer CreateFromSeconds(NetworkRunner r,float s)=>new(){Seconds=s,End=r.Time+s};}
 public enum MagicType {Fire=1,Ice=2}
 public enum MagicEffectKind {DirectDamage,AreaDamage}
 public enum MagicConstructKind {Projectile,SummonBolt}
 public struct MagicStatEntry {public float radius,damage;public MagicEffectKind effect;}
 public class MagicStatTable {public bool iceImpactFollowUpEnabled=true;public float iceImpactLaunchInterval=.07f,iceImpactShardSpeed=110;}
 public static class PerkEffects {public static MagicStatEntry AreaHit(MagicStatEntry s,float d)=>s;}
 public class Transform {public Vector3 position;}
 public class Player {
  public static List<Player> ActiveCombatants=new();public NetworkObject Object;
  public NetworkRunner Runner;public MagicStatTable MagicTable=new();
  public bool IsAlive=true,Parrying,Invulnerable;public int TeamIndex=2,HitSequence,Received;
  public bool IsParrying=>Parrying;
  public float Health=100;public Vector3 LockAimPoint;
  public void ReceiveMagicHit(MagicStatEntry s,int a,NetworkId id,Vector3 incomingDirection=default) {
   if(Parrying||Invulnerable)return;
   Received++;HitSequence++;Health-=s.damage;if(Health<=0)IsAlive=false;
  }
 }
 public static class IceImpactFollowUpVisual {
  public const int ShardCount=8;
  public static int Calls;public static Player Caster,Victim;public static Vector3 Point;
  public static bool Play(Player caster,Player victim,Vector3 hit,MagicStatTable table,float interval=-1,float flight=-1){Calls++;Caster=caster;Victim=victim;Point=hit;return true;}
 }
 public class MagicProjectile {
  public Vector3 direction=new(0,0,1);
  public NetworkObject Object=new();public bool iceDamagePending;public Player iceDamageVictim;public TickTimer iceDamageTimer;
  public bool Finished,IsMine,iceFollowUpPresented,iceFollowUpStarted;public Transform transform=new();public TickTimer Lifetime;
  public int Shooter=1,ShooterTeam=1,ImpactCalls,SummonHits;public NetworkId ShooterId=new(1);
  public NetworkRunner Runner=new();public MagicType Magic=MagicType.Ice;public MagicConstructKind ConstructKind;
  public MagicStatEntry stats=new(){damage=25};
  public void ReceiveSummonHit(MagicStatEntry s,int team,NetworkId id){SummonHits++;}
  static void DamageSummonsInArea(NetworkRunner r,int team,Vector3 p,float radius,MagicStatEntry s,NetworkId id){}
  bool HasBlastSight(Player p)=>true;
  void RPC_Impact(Vector3 p,MagicType m){ImpactCalls++;}
'@
$source += (Method $projectile 'Impact') + (Method $projectile 'RPC_IceFollowUp') + (Method $projectile 'TryBeginIceFollowUp') + (Method $projectile 'TickIceFollowUpDamage')
$source += "`n}`npublic static class IceMath { public const int ShardCount=8;`n"
$source += (Method $ice 'ClockwiseOffset') + (Method $ice 'FlightPosition')
$source += [regex]::Match($ice,'(?ms)^    public static bool ShouldLaunch\(.*?;').Value
$source += @'
 }
 public static class Tests {
  static int count;
  static void Check(bool ok,string label){if(!ok)throw new Exception(label);count++;}
  static bool Near(Vector3 a,Vector3 b)=>Vector3.Distance(a,b)<.0001f;
  static Player Actor(MagicProjectile p,int id,int team) {
   var actor=new Player{Runner=p.Runner,TeamIndex=team,Object=new NetworkObject{Id=new(id)}};
   actor.Object.Component=actor;p.Runner.Objects[id]=actor.Object;return actor;
  }
  public static int Run(){
   for(int i=0;i<8;i++) {
    var direction=IceMath.ClockwiseOffset(i);var next=IceMath.ClockwiseOffset((i+1)%8);
    Check(Math.Abs(direction.sqrMagnitude-1)<.0001,"unit ring direction "+i);
    Check(direction.x*next.y-direction.y*next.x<0,"clockwise order "+i);
    Check(IceMath.ShouldLaunch(i,i*.07f,.07f),"launch reaches scheduled time "+i);
    Check(!IceMath.ShouldLaunch(i,i*.07f-.0001f,.07f),"not before scheduled time "+i);
   }
   Check(Near(IceMath.ClockwiseOffset(0),new(0,1,0))&&Near(IceMath.ClockwiseOffset(2),new(1,0,0)),"starts at top then passes right");
   Check(!IceMath.ShouldLaunch(8,100,.07f)&&!IceMath.ShouldLaunch(-1,100,.07f),"exactly eight shards");
   var start=new Vector3(5,3,2);var control=new Vector3(10,8,20);var end=new Vector3(20,10,100);
   Check(Near(IceMath.FlightPosition(start,control,end,0),start),"shard starts at body ring, not enemy");
   Check(Near(IceMath.FlightPosition(start,control,end,1),end),"shard ends at victim");
   Check(Near(IceMath.FlightPosition(start,control,new(40,10,200),1),new(40,10,200)),"target movement updates endpoint");
   Check(Near(IceMath.FlightPosition(start,control,end,-1),start)&&Near(IceMath.FlightPosition(start,control,end,2),end),"trajectory progress bounded");
   var p=new MagicProjectile();var owner=Actor(p,1,1);var victim=Actor(p,2,2);int calls=IceImpactFollowUpVisual.Calls;
   p.Impact(end,victim);
   Check(victim.Received==0&&victim.Health==100,"core contact defers all damage until final shard");
   Check(IceImpactFollowUpVisual.Calls==calls+1&&IceImpactFollowUpVisual.Caster==owner&&IceImpactFollowUpVisual.Victim==victim,"confirmed hit creates one caster-to-victim batch");
   Check(p.ImpactCalls==1&&p.Finished&&p.Lifetime.End>p.iceDamageTimer.End,"hidden core survives until scheduled damage");
   p.Runner.Time=p.iceDamageTimer.End-.001f;p.TickIceFollowUpDamage();Check(victim.Received==0,"no damage before final scheduled arrival");
   p.Runner.Time=p.iceDamageTimer.End;p.TickIceFollowUpDamage();p.TickIceFollowUpDamage();
   Check(victim.Received==1&&victim.Health==75&&!p.iceDamagePending,"final arrival deals exactly one full hit");
   p.Impact(end,victim);p.RPC_IceFollowUp(victim.Object.Id,end);
   Check(IceImpactFollowUpVisual.Calls==calls+1&&victim.Received==1,"finished core and duplicate presentation cannot replay batch");
   foreach(string kind in new[]{"wall","parry","friendly","dead","fire","summonBolt","summon"}) {
    p=new MagicProjectile();Actor(p,1,1);victim=Actor(p,2,2);calls=IceImpactFollowUpVisual.Calls;
    victim.Parrying=kind=="parry";victim.Invulnerable=kind=="immune";victim.IsAlive=kind!="dead";
    if(kind=="friendly")victim.TeamIndex=1;
    if(kind=="fire")p.Magic=MagicType.Fire;
    if(kind=="summonBolt")p.ConstructKind=MagicConstructKind.SummonBolt;
    if(kind=="summon")p.Impact(end,null,new MagicProjectile());else p.Impact(end,kind=="wall"?null:victim);
    Check(IceImpactFollowUpVisual.Calls==calls,"no ice follow-up for "+kind);
    Check(p.ImpactCalls==1,"normal impact preserved for "+kind);
    if(kind=="parry") {
     Check(!p.iceDamagePending&&p.iceDamageVictim==null&&victim.Received==0,"parried core leaves no shards, pending damage or victim damage");
     Check(!p.iceFollowUpPresented&&!p.iceFollowUpStarted&&Math.Abs(p.Lifetime.Seconds-.2f)<.0001f,"parried core uses immediate impact and short cleanup instead of waiting for shards");
     p.Runner.Time=10;p.TickIceFollowUpDamage();p.Impact(end,victim);
     Check(victim.Received==0&&p.ImpactCalls==1&&IceImpactFollowUpVisual.Calls==calls,"parried core cannot later deal delayed damage or replay impact");
    }
   }
   p=new MagicProjectile();Actor(p,1,1);victim=Actor(p,2,2);victim.Health=1;calls=IceImpactFollowUpVisual.Calls;
   p.Impact(end,victim);Check(victim.IsAlive&&IceImpactFollowUpVisual.Calls==calls+1,"lethal damage also waits for the batch");
   p.Runner.Time=p.iceDamageTimer.End;p.TickIceFollowUpDamage();Check(!victim.IsAlive,"lethal hit applies at final arrival");
   foreach(string reason in new[]{"parry","dead","despawn","friendly","otherRunner","proxy"}) {
    p=new();Actor(p,1,1);victim=Actor(p,2,2);p.Impact(end,victim);
    if(reason=="parry")victim.Parrying=true;if(reason=="dead")victim.IsAlive=false;
    if(reason=="despawn")victim.Object.IsValid=false;if(reason=="friendly")victim.TeamIndex=1;
    if(reason=="otherRunner")victim.Runner=new();if(reason=="proxy")p.Object.HasStateAuthority=false;
    p.Runner.Time=p.iceDamageTimer.End;p.TickIceFollowUpDamage();Check(victim.Received==0,"delayed damage revalidates "+reason);
   }
   p=new();owner=Actor(p,1,1);victim=Actor(p,2,2);owner.MagicTable.iceImpactFollowUpEnabled=false;p.Impact(end,victim);
   Check(victim.Received==1&&!p.iceDamagePending,"explicitly disabled follow-up retains immediate hit");
   p=new MagicProjectile();victim=Actor(p,2,2);calls=IceImpactFollowUpVisual.Calls;p.Impact(end,victim);
   Check(IceImpactFollowUpVisual.Calls==calls&&victim.Received==1,"missing original caster does not retarget or change damage");
   p=new MagicProjectile();Actor(p,1,1);calls=IceImpactFollowUpVisual.Calls;p.RPC_IceFollowUp(new(99),end);
   Check(IceImpactFollowUpVisual.Calls==calls+1&&IceImpactFollowUpVisual.Victim==null&&Near(IceImpactFollowUpVisual.Point,end),"missing victim uses confirmed hit position");
   return count;
  }
 }
}
'@
Add-Type -TypeDefinition $source
Write-Output ("Ice confirmed-hit/clockwise trajectory checks passed: " + [IceFlareChecks.Tests]::Run())
function Require([bool]$value,[string]$label){if(!$value){throw $label}}
Require ($projectile -match '\[Rpc\(RpcSources.StateAuthority, RpcTargets.All\)\]\s+private void RPC_IceFollowUp') 'Only authoritative hits may broadcast the sequence'
Require ($ice.Contains('public const int ShardCount = 8;') -and $ice.Contains('effect.bodyPoint = caster.LockAimPoint;') -and $ice.Contains('bodyPoint = caster.LockAimPoint;')) 'Eight shards must originate from the moving caster body'
Require ($ice -notmatch 'Runner\.Spawn|TakeDamage\(|ReceiveMagicHit\(|Physics\.|NotifyConfirmedMagicHit\(') 'Follow-up must remain cosmetic'
foreach($token in 'collider.enabled = false','body.detectCollisions = false','collision.enabled = false','trigger.enabled = false','Destroy(ownedMaterial)','Destroy(ownedMesh)') {Require ($ice.Contains($token)) "Ice VFX safety missing: $token"}
$prediction = Get-Content Assets/Script/ProjectilePredictionView.cs -Raw
Require ($prediction.Contains('stats.magic != MagicType.Ice || !table.iceImpactFollowUpEnabled')) 'Ice launch satellites must be deferred'
$utility = Get-Content Assets/Script/UtilityMagicVisual.cs -Raw
foreach($token in 'main.maxParticles = 160','glowMain.maxParticles = 48','emission.rateOverTime = 64f','glowEmission.rateOverTime = 22f','new Color(1f,.2f,.025f,.7f)','Flare orange afterglow','Destroy(ownedFlareMaterial)') {Require ($utility.Contains($token)) "Flare visual/lifetime contract missing: $token"}
Require ($utility.Contains('if (custom == null)') -and $utility.Contains('custom.transform.localScale *= scale')) 'Preserve custom flare VFX replacement'
$smoke=[regex]::Match($utility,'(?ms)^    public static GameObject CreateSmoke.*?^    \}').Value
Require ($smoke.Contains('main.maxParticles = 64') -and $smoke.Contains('smokeColor.a = 0.65f') -and !$smoke.Contains('FlareParticle')) 'Smoke visuals must remain unchanged'
$shader=Get-Content Assets/Resources/FlareParticle.shader -Raw
Require ($shader.Contains('Blend SrcAlpha One') -and $shader.Contains('_Intensity') -and $shader.Contains('ZWrite Off')) 'Flare has a dedicated additive glow shader'
$asset=Get-Content Assets/Resources/MagicStatTable.asset -Raw
foreach($setting in 'iceImpactFollowUpEnabled: 1','iceImpactLaunchInterval: 0.07','iceImpactShardSpeed: 110','flareVfxScale: 1.8','flareVfxBrightness: 2'){Require ($asset -match ('(?m)^\s*'+[regex]::Escape($setting)+'\r?$')) "Saved visual setting missing: $setting"}
$ids=[regex]::Matches($asset,'(?m)^  - magic: (\d+)\r?$') | ForEach-Object {[int]$_.Groups[1].Value}
Require ((($ids | Sort-Object) -join ',') -eq '1,2,3,4,5,6,7,8,9,10,11,12') 'Explicit magic IDs preserved regardless of Inspector order'
Write-Output 'Flare visibility, custom VFX, bounded cleanup, Ice collision-free presentation and saved settings passed.'
