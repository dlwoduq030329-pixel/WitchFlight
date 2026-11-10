# Production tactical planner with deterministic world doubles, not a live Fusion simulation.
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$source=@'
using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;
namespace UnityEngine {
 public struct Vector3 {
  public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
  public float sqrMagnitude=>x*x+y*y+z*z;public float magnitude=>(float)Math.Sqrt(sqrMagnitude);
  public Vector3 normalized=>magnitude>.0001f?this*(1f/magnitude):default;
  public static Vector3 operator+(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
  public static Vector3 operator-(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
  public static Vector3 operator*(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);
  public static float Distance(Vector3 a,Vector3 b)=>(a-b).magnitude;
  public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
 }
 public class Transform {public Vector3 position,forward=new(0,0,1),right=new(1,0,0);}
 public static class Mathf {
  public static int Max(int a,int b)=>Math.Max(a,b);public static float Max(float a,float b)=>Math.Max(a,b);
  public static float Min(float a,float b)=>Math.Min(a,b);public static bool Approximately(float a,float b)=>Math.Abs(a-b)<.0001f;
  public const float Deg2Rad=(float)Math.PI/180f,Rad2Deg=180f/(float)Math.PI;
  public static float Sin(float x)=>(float)Math.Sin(x);public static float Cos(float x)=>(float)Math.Cos(x);public static float Atan2(float y,float x)=>(float)Math.Atan2(y,x);
  public static float Clamp(float v,float min,float max)=>Math.Max(min,Math.Min(max,v));public static float Clamp01(float v)=>Clamp(v,0,1);
  public static float DeltaAngle(float a,float b){float d=(b-a)%360f;if(d>180)d-=360;if(d< -180)d+=360;return d;}
 }
}
namespace Fusion {
 public struct PlayerRef:IEquatable<PlayerRef> {
  public int RawEncoded;public static PlayerRef None=>default;public static implicit operator PlayerRef(int v)=>new(){RawEncoded=v};
  public bool Equals(PlayerRef p)=>RawEncoded==p.RawEncoded;public override bool Equals(object o)=>o is PlayerRef p&&Equals(p);
  public override int GetHashCode()=>RawEncoded;public static bool operator==(PlayerRef a,PlayerRef b)=>a.Equals(b);public static bool operator!=(PlayerRef a,PlayerRef b)=>!a.Equals(b);
 }
}
public class NetObject {public bool IsValid=true;public int Id;}
public class BattleFlag {public static BattleFlag Instance;public PlayerRef Carrier;public Transform transform=new();public float PickupRadius=.85f;}
public enum PlayerInputButton {Lock}
public struct Buttons {public bool Held;public bool IsSet(PlayerInputButton b)=>Held;}
public struct NetworkInputData {public int lockTarget;public Vector3 magicAimDirection;}
public struct MagicStatEntry {public MagicType magic;public float range;public bool requiresTarget=>magic==MagicType.Fire;public bool TargetsAllies=>magic==MagicType.Healing||magic==MagicType.MeowFestival;}
public class PlayerData {
 public static List<PlayerData> PerkPlayers=new();public NetObject Object=new();public object Runner;
 public int teamIndex;public bool IsMatchBot=true;public MatchBotRole BotRole;
}
public class MatchBotSettings {public float escortDistance=28,escortThreatRange=100,fighterLeashDistance=180,fighterOpportunityDistance=70,fighterCarrierHealthMinimum=.7f,isolationRadius=65,encirclementRadius=45,encirclementHeight=12,encirclementLeadSeconds=.35f,encirclementArrivalDistance=8;}
public partial class Player {
 public static List<Player> ActiveCombatants=new();public NetObject Object=new();public object Runner;
 public bool IsAlive=true,IsMatchBot=true,HasMeowFestival,Visible=true,IsMagicStormActive;
 public PlayerRef ParticipantRef;public int TeamIndex,LockTargetId;public float NowHp=100,MaxHp=100,maxLockDistance=200,LockRangeMultiplier=1;
 public Transform transform=new();public Vector3 LockAimPoint=>transform.position;public Vector3 MagicCastPosition=>transform.position;
 public Vector3 castAimDirection;public Buttons previousButtons;public MagicType pendingMagic,Magic1=MagicType.Vision,Magic2=MagicType.Healing;
 public Player botTarget;
 public bool botRecovering;public float Speed,SpellRange=100;
 public bool HasLineOfSight(Player other)=>other.Visible;
 public MagicStatEntry GetMagicStats(MagicType magic)=>new(){magic=magic,range=SpellRange};
 public void Role(MatchBotRole role,int order){botRole=role;botTeamOrder=order;}
 public void Decide(MatchBotSettings settings)=>RefreshBotTactics(settings);
 public bool Escort=>botEscortDuty;
 public bool PickupGoal(out Vector3 destination,out float radius)=>TryGetBotFlagPickupGoal(BattleFlag.Instance,out destination,out radius);
 public Vector3 Formation(Player carrier,MatchBotSettings settings)=>BotEscortPosition(carrier,settings);
 public Player Recipient(MagicType magic)=>ChooseBotSupportTarget(new(){magic=magic,range=100});
 public NetworkInputData SupportInput(Player ally){botCastAlly=ally;NetworkInputData input=default;AimBotSupport(ref input);return input;}
 public Vector3 AttackSlot(MatchBotSettings settings){RefreshBotEncirclement(settings);return botAttackSlotOffset;}
}
'@
$source+=[regex]::Match((Get-Content Assets/Script/PlayerData.cs -Raw),'public enum MagicType \{[^}]+\}').Value
$source+=((Get-Content Assets/Script/MatchBotSettings.cs -Raw) -split '\[CreateAssetMenu')[0] -replace '(?m)^using .*;\r?\n',''
$source+=(Get-Content Assets/Script/Player.BotTactics.cs -Raw)-replace '(?m)^using .*;\r?\n',''
$source+=(Get-Content Assets/Script/BotEncirclement.cs -Raw)-replace '(?m)^using .*;\r?\n',''
$source+=@'
public static class TacticalChecks {
 static int n;static object runner=new();static MatchBotSettings settings=new();
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);n++;}
 static Player Add(int id,int team,float x,MatchBotRole role=MatchBotRole.Objective,int order=0,bool bot=true){
  var p=new Player{Runner=runner,ParticipantRef=id,TeamIndex=team,IsMatchBot=bot};p.Object.Id=id;p.Role(role,order);p.transform.position=new(x,0,0);
  Player.ActiveCombatants.Add(p);PlayerData.PerkPlayers.Add(new(){Runner=runner,teamIndex=team,BotRole=role,IsMatchBot=bot});return p;
 }
 public static int Run(){
  foreach(int size in new[]{3,4,5}){
   int supports=0,fighters=0;
   for(int i=0;i<size;i++){var role=MatchBotRoles.ForTeamSlot(size,i);if(role==MatchBotRole.Support)supports++;if(role==MatchBotRole.Fighter)fighters++;}
   Check(supports==(size==5?2:1),"support quota");Check(fighters==(size==3?1:2),"fighter quota including 4v4 correction");
  }
  Check(MatchBotRoles.ForTeamSlot(2,0)==MatchBotRole.Objective,"small modes keep original roles");
  MagicType a=MagicType.Flare,b=MagicType.Vision;MatchBotRoles.SupportLoadout(0,false,ref a,ref b);
  Check(a==MagicType.Vision&&b==MagicType.Healing,"one attack and one support");
  MatchBotRoles.SupportLoadout(1,false,ref a,ref b);Check(a==MagicType.Vision&&b==MagicType.MeowFestival,"second support varies its spell");
  MatchBotRoles.SupportLoadout(0,true,ref a,ref b);Check(a==MagicType.Healing&&b==MagicType.MeowFestival,"double support uses two distinct spells");
  var carrier=Add(1,1,0,bot:false);var support=Add(2,1,10,MatchBotRole.Support);var fighter=Add(3,1,5,MatchBotRole.Fighter,1);
  var enemyCarrier=Add(4,2,150);var isolated=Add(5,2,25);
  BattleFlag.Instance=new(){Carrier=enemyCarrier.ParticipantRef};
  support.Decide(settings);Check(support.botTarget==enemyCarrier,"support hard-focuses enemy flag, ignores nearer distraction");
  fighter.Decide(settings);Check(fighter.botTarget==enemyCarrier&&fighter.Escort,"fighter prioritizes enemy carrier over easy isolated kill");
  isolated.transform.position=new(150,0,90);var interceptor=Add(6,2,20);interceptor.LockTargetId=support.Object.Id;
  fighter.Decide(settings);Check(fighter.botTarget==enemyCarrier,"enemy carrier still wins over escort distraction");
  interceptor.LockTargetId=0;interceptor.Visible=false;interceptor.IsAlive=false;
  BattleFlag.Instance.Carrier=carrier.ParticipantRef;carrier.NowHp=60;enemyCarrier.transform.position=new(400,0,0);isolated.transform.position=new(500,0,0);
  support.Decide(settings);Check(support.botTarget==null&&support.Escort,"escort stays with allied flag instead of distant chase");
  Check(support.Recipient(MagicType.Healing)==carrier,"heal carrier even without an enemy in combat range");
  Check(support.Recipient(MagicType.MeowFestival)==carrier,"buff carrier first");
  var healInput=support.SupportInput(carrier);Check(healInput.lockTarget==carrier.Object.Id&&healInput.magicAimDirection.x<0,"support input actually targets the ally");
  Check(support.SupportInput(support).lockTarget==0,"self heal uses self fallback");
  Vector3 formation=support.Formation(carrier,settings);Check(formation.z<carrier.transform.position.z&&Vector3.Distance(formation,carrier.LockAimPoint)<100,"escort position stays behind and within support range");
  carrier.HasMeowFestival=true;Check(support.Recipient(MagicType.MeowFestival)!=carrier,"do not overwrite active festival");
  carrier.Visible=false;Check(support.Recipient(MagicType.Healing)==null,"blocked carrier cannot consume a wasted heal");
  carrier.Visible=true;carrier.transform.position=new(300,0,0);Check(support.Recipient(MagicType.Healing)==null,"out-of-range carrier is not selected");carrier.transform.position=new(0,0,0);
  carrier.NowHp=100;isolated.transform.position=new(60,0,0);
  fighter.Decide(settings);Check(!fighter.Escort&&fighter.botTarget==isolated,"safe guarded allied flag allows a nearby isolated opportunity");
  carrier.NowHp=60;fighter.Decide(settings);Check(fighter.Escort,"injured carrier immediately recalls fighter");carrier.NowHp=100;
  isolated.LockTargetId=carrier.Object.Id;fighter.Decide(settings);Check(fighter.Escort&&fighter.botTarget==isolated,"carrier threat recalls fighter to defensive interception");isolated.LockTargetId=0;
  isolated.Visible=false;fighter.Decide(settings);Check(fighter.Escort,"no assassination through blocked sight");isolated.Visible=true;
  support.transform.position=new(150,0,0);fighter.Decide(settings);Check(fighter.Escort,"support alive but far away does not count as carrier cover");support.transform.position=new(10,0,0);
  fighter.botRecovering=true;fighter.Decide(settings);Check(fighter.Escort,"recovering fighter cannot start a hunt");fighter.botRecovering=false;
  support.IsAlive=false;fighter.Decide(settings);Check(fighter.Escort,"fighter covers the dead support");
  support.IsAlive=true;fighter.Decide(settings);Check(!fighter.Escort,"fighter resumes interception after support respawns");
  var support2=Add(7,1,15,MatchBotRole.Support,2);var fighter2=Add(8,1,18,MatchBotRole.Fighter,3);
  support2.IsAlive=false;fighter.Decide(settings);fighter2.Decide(settings);
  Check(fighter.Escort&&!fighter2.Escort,"one missing support gets one replacement while remaining fighter intercepts");
  support.IsAlive=false;fighter.Decide(settings);fighter2.Decide(settings);Check(fighter.Escort&&fighter2.Escort,"both fighters cover loss of both supports");
  support.IsAlive=support2.IsAlive=true;fighter.Decide(settings);fighter2.Decide(settings);Check(!fighter.Escort&&!fighter2.Escort,"roles restore after both supports respawn");
  isolated.transform.position=new(500,0,0);fighter.Decide(settings);Check(fighter.botTarget==null&&fighter.Escort,"no opportunity means escort instead of cross-map chasing");
  BattleFlag.Instance.Carrier=PlayerRef.None;BattleFlag.Instance.transform.position=new(100,0,0);
  enemyCarrier.transform.position=new(90,0,0);isolated.transform.position=new(20,0,0);
  support.Decide(settings);fighter.Decide(settings);
  Check(support.botTarget==enemyCarrier&&fighter.botTarget==enemyCarrier,"all roles focus the spearhead closest to the loose flag, not their nearest enemy");
  Check(fighter.PickupGoal(out var pickup,out var pickupRadius)&&pickup.x==100&&pickupRadius==.85f,"fighter still flies to loose flag while an attack target exists");
  Check(support.PickupGoal(out _,out _),"support also prioritizes pickup");
  fighter.botRecovering=true;Check(!fighter.PickupGoal(out _,out _),"low-health recovery exception preserved");fighter.botRecovering=false;
  BattleFlag.Instance.Carrier=carrier.ParticipantRef;Check(!fighter.PickupGoal(out _,out _),"held flag is not a loose pickup");
  isolated.transform.position=new(500,0,0);enemyCarrier.transform.position=new(400,0,0);
  enemyCarrier.IsAlive=false;BattleFlag.Instance.Carrier=enemyCarrier.ParticipantRef;support.Decide(settings);Check(support.botTarget!=enemyCarrier,"dead flag carrier is ignored");
  for(int count=2;count<=5;count++){
   var offsets=new List<Vector3>();
   for(int i=0;i<count;i++){
    var offset=BotEncirclement.SlotOffset(i,count,45,12,4);
    Check(offset.magnitude<50,"full formation within attack distance");
    foreach(var existing in offsets)Check(Vector3.Distance(existing,offset)>40,"distinct separated attack sectors");
    offsets.Add(offset);
   }
   Vector3 center=default;foreach(var offset in offsets)center+=new Vector3(offset.x,0,offset.z);
   Check(center.magnitude<.01f,"formation surrounds target rather than clustering behind it");
  }
  var opposite=BotEncirclement.ApproachOffset(new Vector3(0,0,-45),new Vector3(0,12,45),45);
  Check(Math.Abs(opposite.x)>30,"opposite-side bot takes a flank route");
  for(int yaw=0;yaw<360;yaw+=15){
   float radians=yaw*Mathf.Deg2Rad;var from=new Vector3(Mathf.Sin(radians)*45,0,Mathf.Cos(radians)*45);
   var next=BotEncirclement.ApproachOffset(from,new Vector3(0,12,45),45);
   for(int step=0;step<=10;step++){
    var point=from+(next-from)*(step/10f);point.y=0;
    Check(point.magnitude>=45*.65f-.01f,"approach path never crosses target center");
   }
  }
  var close=BotEncirclement.ApproachOffset(new Vector3(0,0,-2),new Vector3(0,12,45),45);
  Check(close.z<0,"too-close bot retreats outward on its current side");
  enemyCarrier.IsAlive=true;enemyCarrier.transform.position=new(80,0,0);BattleFlag.Instance.Carrier=enemyCarrier.ParticipantRef;
  support.Decide(settings);support2.Decide(settings);
  var firstSector=support.AttackSlot(settings);var secondSector=support2.AttackSlot(settings);
  Check(Vector3.Distance(firstSector,secondSector)>40,"production planner allocates separate same-target bot slots");
  enemyCarrier.transform.forward=new(1,0,0);
  Check(Vector3.Distance(firstSector,support.AttackSlot(settings))<.01f,"target turns do not rotate formation sectors");
  support.SpellRange=20;enemyCarrier.Speed=1000;
  Check(support.AttackSlot(settings).magnitude<20,"radius, height and capped prediction fit shorter spell range");
  support.Magic1=MagicType.Fire;support.SpellRange=200;support.maxLockDistance=25;support.LockRangeMultiplier=.7f;
  Check(support.AttackSlot(settings).magnitude<17.5f,"formation respects reduced lock-on range");
  enemyCarrier.Speed=0;support.SpellRange=100;support2.IsAlive=false;support.Decide(settings);
  Check(support.AttackSlot(settings).magnitude>0,"formation reallocates when a teammate dies");
  return n;
 }
}
'@
Add-Type -TypeDefinition $source
"Bot role quotas, carrier focus, interception, escort fallback and ally casting checks passed: $([TacticalChecks]::Run())"
