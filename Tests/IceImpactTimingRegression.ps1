# Actual follow-up update and projectile RPC routing. Cosmetic timing only, no Unity session.
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$ice=Get-Content Assets/Script/IceImpactFollowUpVisual.cs -Raw
$projectile=Get-Content Assets/Script/MagicProjectile.cs -Raw
function Method([string]$source,[string]$name) {
 $m=[regex]::Match($source,'(?ms)^    (?:public|private) (?:static )?[\w<>]+ '+$name+'\([^{}]*?\)\r?\n    \{.*?^    \}')
 if(!$m.Success){throw "Missing method $name"};$m.Value -replace '^    private ','    public '
}
$source=@'
using System;
using System.Collections.Generic;
namespace IceTiming {
 public class Object {public bool destroyed;public static void Destroy(Object o){o.destroyed=true;}}
 public class GameObject:Object {public bool activeSelf=true;public void SetActive(bool b){activeSelf=b;}}
 public struct Vector3 {
  public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
  public float sqrMagnitude=>x*x+y*y+z*z;
  public static Vector3 operator-(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
  public static Vector3 operator*(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);
 }
 public struct Quaternion {public static Quaternion LookRotation(Vector3 v)=>default;}
 public class Transform {public Vector3 position,localScale;public Quaternion rotation;public GameObject gameObject=new();}
 public static class Time {public static float time;}
 public static class Mathf {public static float Clamp(float v,float min,float max)=>Math.Clamp(v,min,max);public static float Clamp01(float a)=>Math.Clamp(a,0,1);public static float Lerp(float a,float b,float t)=>a+(b-a)*t;public static float Max(float a,float b)=>Math.Max(a,b);}
 public class BattleManager {public static BattleManager Instance=new();public bool IsGameplayActive=true;}
 public enum MagicType {Fire,Ice}
 public struct NetworkId {public int id;public NetworkId(int i){id=i;}}
 public class NetworkObject {public bool IsValid=true;public Player player;public T GetComponent<T>() where T:class=>player as T;}
 public class NetworkRunner {public Dictionary<int,NetworkObject> objects=new();public bool TryFindObject(NetworkId id,out NetworkObject obj)=>objects.TryGetValue(id.id,out obj);}
 public class MagicStatTable {}
 public class Player {public NetworkObject Object=new();public bool IsAlive=true;public Transform transform=new();public Vector3 LockAimPoint;public MagicStatTable MagicTable=new();}
 public static class CombatPresentation {public static int Hits;public static Vector3 LastPoint;public static void ShowImpact(Vector3 p,MagicType m){Hits++;LastPoint=p;}}
 public class Shard {public Transform view;public Vector3 origin,control,scale;public float launchedAt,duration;public bool arrived;}
 public class FollowUp:Object {
  public const int ShardCount=8;public bool impactPlayed,missingVisual;
  public GameObject gameObject=new();public MagicStatTable table=new();public Player caster=new(),victim=new();
  public Vector3 bodyPoint,targetPoint;public Quaternion bodyRotation;
  public float startedAt,interval=.07f,expiresAt=5;public int launched,arrived;public Shard[] shards=new Shard[8];
  void Launch(int i){shards[i]=new(){view=missingVisual?null:new Transform(),launchedAt=startedAt+i*interval,duration=1};}
  static Vector3 FlightPosition(Vector3 o,Vector3 c,Vector3 target,float t)=>target*t;
'@
$source+=(Method $ice 'Update')+(Method $ice 'FinishImpact')
$source+=[regex]::Match($ice,'(?ms)^    public static bool ShouldLaunch\(.*?;').Value
$source+=@'
 }
 public static class IceImpactFollowUpVisual {public static bool Enabled=true;public static int Calls;public static bool Play(Player c,Player v,Vector3 p,MagicStatTable t,float interval=-1,float flight=-1){Calls++;return Enabled;}}
 public class PredictionView {public int Confirmed;public void ConfirmImpact(){Confirmed++;}}
 public enum MagicConstructKind {Projectile,ThunderArea}
 public struct MagicStatEntry {public float radius;}
 public static class MagicAreaVisual {public static int Bursts;public static float Radius;public static void ThunderBurst(Vector3 p,MagicStatEntry stats){Bursts++;Radius=stats.radius;}}
 public class Shot {
  public bool iceFollowUpPresented,iceFollowUpStarted,impactPresented,IsConstruct;public MagicType Magic=MagicType.Ice;
  public NetworkRunner Runner=new();public NetworkId ShooterId=new(1);public PredictionView view=new();public MagicConstructKind ConstructKind;
  public MagicStatEntry PresentationStats;public float ConstructRadius;public void ClearConstructVisual(bool b){}
'@
$source+=(Method $projectile 'RPC_IceFollowUp')+(Method $projectile 'RPC_Impact')
$source+=@'
 }
 public static class Tests {
  static int n;static void Check(bool b,string s){if(!b)throw new Exception(s);n++;}
  public static int Run(){
   BattleManager.Instance=new();CombatPresentation.Hits=0;
   var f=new FollowUp();Time.time=.5f;f.Update();Check(f.launched==8&&f.arrived==0&&CombatPresentation.Hits==0,"all eight launch without impact");
   Time.time=1;f.Update();Check(f.arrived==1&&CombatPresentation.Hits==0,"first arrival cannot play impact");
   Time.time=1.45f;f.Update();Check(f.arrived==7&&CombatPresentation.Hits==0,"seven arrivals still wait for final shard");
   f.victim.LockAimPoint=new(4,5,6);Time.time=1.5f;f.Update();
   Check(f.arrived==8&&CombatPresentation.Hits==1&&CombatPresentation.LastPoint.z==6,"eighth arrival plays one impact at current victim position");
   f.Update();f.FinishImpact();Check(CombatPresentation.Hits==1&&f.gameObject.destroyed,"final impact cannot repeat");
   f=new(){missingVisual=true};Time.time=.5f;f.Update();Check(f.arrived==0,"self-destroyed shard prefab still respects flight duration");
   Time.time=1.5f;f.Update();Check(CombatPresentation.Hits==2,"missing visuals finish once after all scheduled arrivals");
   f=new();Time.time=.5f;f.Update();BattleManager.Instance.IsGameplayActive=false;Time.time=1.5f;f.Update();
   Check(f.gameObject.destroyed&&CombatPresentation.Hits==2,"round end cancels pending impact");
   BattleManager.Instance.IsGameplayActive=true;f=new();Time.time=.5f;f.Update();f.targetPoint=new(7,8,9);f.victim=null;Time.time=6;f.Update();
   Check(CombatPresentation.Hits==3&&CombatPresentation.LastPoint.z==9,"frame stall expires safely at last known position");
   var s=new Shot();s.Runner.objects[1]=new(){player=new()};s.Runner.objects[2]=new(){player=new()};int hits=CombatPresentation.Hits;
   s.RPC_IceFollowUp(new(2),default);s.RPC_Impact(default,MagicType.Ice);
   Check(s.iceFollowUpStarted&&CombatPresentation.Hits==hits&&s.view.Confirmed==1,"confirmed main hit disappears but defers impact to batch");
   int calls=IceImpactFollowUpVisual.Calls;s.RPC_IceFollowUp(new(2),default);Check(IceImpactFollowUpVisual.Calls==calls,"duplicate follow-up event cannot create another batch");
   s=new();s.RPC_Impact(default,MagicType.Ice);Check(CombatPresentation.Hits==++hits,"wall or parry without batch impacts immediately");
   s=new();s.RPC_IceFollowUp(new(2),default);s.RPC_Impact(default,MagicType.Ice);Check(CombatPresentation.Hits==++hits,"missing caster retains immediate impact");
   s=new();s.Runner.objects[1]=new(){player=new()};IceImpactFollowUpVisual.Enabled=false;
   s.RPC_IceFollowUp(new(2),default);s.RPC_Impact(default,MagicType.Ice);Check(CombatPresentation.Hits==++hits,"disabled follow-up retains immediate impact");
   s=new();s.RPC_Impact(default,MagicType.Fire);Check(CombatPresentation.Hits==++hits,"other spells retain impact timing");
   s=new(){ConstructKind=MagicConstructKind.ThunderArea,ConstructRadius=18,PresentationStats=new(){radius=12}};
   s.RPC_Impact(default,MagicType.Fire);
   Check(MagicAreaVisual.Bursts==1&&MagicAreaVisual.Radius==18&&CombatPresentation.Hits==hits,"thunder uses authoritative upgraded radius and suppresses duplicate impact");
   Check(Math.Abs(BurstMath.BurstScale(0,.12f,.08f)-.08f)<.0001f,"thunder burst begins at eight percent");
   Check(BurstMath.BurstScale(.06f,.12f,.08f)>.8f,"thunder expands quickly for a punchy impact");
   Check(BurstMath.BurstScale(.12f,.12f,.08f)==1&&BurstMath.BurstScale(10,.12f,.08f)==1,"thunder stops exactly at configured damage radius");
   Check(BurstMath.BurstScale(0,.01f,1)==1,"initial flash fills entire area immediately");
   return n;
  }
 }
}
'@
$area=Get-Content Assets/Script/MagicAreaVisual.cs -Raw
$source=$source.Replace('public static class Tests {',('public static class BurstMath {'+(Method $area 'BurstScale')+'} public static class Tests {'))
Add-Type -TypeDefinition $source
"Ice final-impact sequencing / fallback checks passed: $([IceTiming.Tests]::Run())"
$construct=Get-Content Assets/Script/MagicProjectile.Constructs.cs -Raw
$render=Method $construct 'RenderConstruct'
if ($render -notmatch 'if \(areaVisual == null\)\s*\{[^}]*MagicAreaVisual.ThunderFlash') { throw 'Flash must play once on confirmed-area creation' }
if ($area.Contains('public static void Lightning') -or !(Method $area 'ThunderBurst').Contains('stats.impactVfxPrefab')) { throw 'Replace lightning with burst and preserve existing impact prefab fallback' }
if (!(Method $area 'CreateBurst').Contains('root.SetActive(false);')) { throw 'Initialize small scale before enabling particles' }
