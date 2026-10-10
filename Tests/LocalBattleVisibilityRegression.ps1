# Offline behavior tests of production visibility methods; no live Fusion/renderer.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
function Method([string]$source, [string]$name) {
    $m = [regex]::Match($source, '(?ms)^    (?:private|public) (?:static )?[\w<>]+ '+$name+'\([^{}]*?\)\r?\n    \{.*?^    \}')
    if (!$m.Success) { throw "Missing method $name" }
    $m.Value -replace '^    private ', '    public '
}
$flag = Get-Content Assets/Script/BattleFlag.cs -Raw
$hp = Get-Content Assets/Script/hpfollow.cs -Raw
$source = @'
using System;
namespace VisibilityChecks {
public struct Vector3 {
 public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
 public static float Distance(Vector3 a,Vector3 b)=>(float)Math.Sqrt((a.x-b.x)*(a.x-b.x)+(a.y-b.y)*(a.y-b.y)+(a.z-b.z)*(a.z-b.z));
 public float magnitude=>Distance(this,default);
 public static Vector3 operator-(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
 public static Vector3 operator/(Vector3 a,float b)=>new(a.x/b,a.y/b,a.z/b);
 public static Vector3 operator+(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
 public static Vector3 operator*(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);
}
public class Transform {
 public Vector3 position,localPosition,localScale=new(1,1,1);public int yawQuarter;public Transform Parent;
 public bool IsChildOf(Transform t)=>this==t||(Parent!=null&&Parent.IsChildOf(t));
 public Vector3 TransformDirection(Vector3 v)=>yawQuarter==1?new(v.z,v.y,-v.x):v;
}
public class Camera {public static Camera main;public bool isActiveAndEnabled=true;public Transform transform=new();}
public static class Mathf {public static float Clamp(float v,float a,float b)=>Math.Clamp(v,a,b);}
public enum QueryTriggerInteraction {Ignore}
public class Collider {}
public struct RaycastHit {public Collider collider;public Transform transform;}
public static class Physics {
 public static RaycastHit[] Hits=Array.Empty<RaycastHit>();
 public static int RaycastNonAlloc(Vector3 o,Vector3 d,RaycastHit[] result,float distance,int mask,QueryTriggerInteraction q){int n=Math.Min(Hits.Length,result.Length);Array.Copy(Hits,result,n);return n;}
}
public enum PlayerRef {None,One,Two}
public enum BattleStartPhase {Waiting,Playing,Intermission,Ended}
public enum ShadowCastingMode {Off,On,TwoSided}
public class Renderer {public bool forceRenderingOff;public ShadowCastingMode shadowCastingMode;}
public class NetworkObject {public bool IsValid=true,HasInputAuthority;public PlayerRef InputAuthority;}
public class BattleManager {public static BattleManager Instance=new();public bool IsGameplayActive=true;}
public class Player {
 public static Player LocalPlayer; public NetworkObject Object=new(); public object Runner;
 public bool IsAlive=true,IsStealthed,Hidden; public int TeamIndex;public Vector3 LockAimPoint;
 public Transform transform=new();
 public bool IsHiddenBySmokeFor(Player local)=>Hidden;
}
public class Flag {
 public NetworkObject Object=new();public object Runner=new();public BattleStartPhase Phase=BattleStartPhase.Playing;
 public PlayerRef Carrier;public Player PlayerCarrier;public Transform transform=new();
 public bool CloudActive;public Vector3 CloudCenter;public float CloudRadius=8;
 public Renderer[] flagRenderers;public bool[] originalRenderingOff;public ShadowCastingMode[] originalShadows;public bool flagSmokeHidden;
 public Transform flagVisualRoot;public Vector3 originalVisualPosition,originalVisualScale,carriedVisualOffset=new(1.1f,-.3f,0);
 public bool visualTransformCached;public float carriedVisualScale=.35f;public Camera carrierViewCamera;
 public Player GetCarrierPlayer()=>PlayerCarrier;
 public bool ContainsSmoke(Vector3 p)=>CloudActive&&Phase==BattleStartPhase.Playing&&Vector3.Distance(p,CloudCenter)<=CloudRadius;
'@
$source += (@('IsConcealedBySmoke','TryGetCarrierMarker','UpdateFlagVisibility','RestoreFlagVisibility',
    'CacheFlagVisualTransform','UpdateCarriedVisual','RestoreFlagVisualTransform') | ForEach-Object { Method $flag $_ }) -join "`n"
$source += "`n} public class HealthView {public Player owner;public float maxHealth=100;public Camera viewCamera=new();public int obstructionMask=-1;public RaycastHit[] visibilityHits=new RaycastHit[32];`n" + (Method $hp 'CanShowToLocalPlayer') + "`n" + (Method $hp 'HasLineOfSight')
$source += @'
}
public static class Tests {
 static int count;static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;}
 public static int Run(){
  var flag=new Flag();var me=new Player{Runner=flag.Runner,TeamIndex=1};me.Object.HasInputAuthority=true;me.Object.InputAuthority=PlayerRef.One;Player.LocalPlayer=me;
  var enemy=new Player{Runner=flag.Runner,TeamIndex=2,LockAimPoint=new(0,0,20)};
  enemy.Object.InputAuthority=PlayerRef.Two;
  flag.PlayerCarrier=enemy;flag.Carrier=PlayerRef.Two;flag.transform.position=enemy.LockAimPoint;
  Check(flag.TryGetCarrierMarker(me,out var pos)&&pos.z==20,"carrier marker uses the current carrier render position");
  Check(!flag.TryGetCarrierMarker(enemy,out _),"carrier never sees their own location marker");
  flag.Carrier=PlayerRef.One;flag.PlayerCarrier=null;
  Check(!flag.TryGetCarrierMarker(me,out _),"own carrier ID suppresses marker while carrier object resolves");
  flag.Carrier=PlayerRef.None;flag.transform.position=new(4,12,25);
  Check(flag.TryGetCarrierMarker(me,out pos)&&pos.y==12&&pos.z==25,"dropped flag has a marker at its render position");
  enemy.IsAlive=false;flag.transform.position=new(4,5,25);
  Check(flag.TryGetCarrierMarker(me,out pos)&&pos.y==5,"marker follows descent after former carrier dies");enemy.IsAlive=true;
  Check(flag.TryGetCarrierMarker(enemy,out _),"former carrier sees dropped flag again after respawn");
  flag.transform.position=enemy.LockAimPoint;
  flag.Carrier=PlayerRef.Two;flag.PlayerCarrier=null;Check(!flag.TryGetCarrierMarker(me,out _),"missing/despawned carrier hidden");flag.PlayerCarrier=enemy;
  enemy.IsAlive=false;Check(!flag.TryGetCarrierMarker(me,out _),"dead carrier marker hidden");enemy.IsAlive=true;
  me.IsAlive=false;Check(!flag.TryGetCarrierMarker(me,out _),"dead observer gets no marker");me.IsAlive=true;
  me.Runner=new();Check(!flag.TryGetCarrierMarker(me,out _),"different session gets no marker");me.Runner=flag.Runner;
  flag.Phase=BattleStartPhase.Intermission;Check(!flag.TryGetCarrierMarker(me,out _),"intermission clears markers");flag.Phase=BattleStartPhase.Playing;
  flag.Object.IsValid=false;Check(!flag.TryGetCarrierMarker(me,out _),"invalid flag ignored");flag.Object.IsValid=true;
  flag.CloudActive=true;flag.CloudCenter=enemy.LockAimPoint;
  Check(flag.IsConcealedBySmoke()&&!flag.TryGetCarrierMarker(me,out _),"smoke hides both flag and location marker");
  flag.transform.position=new(0,0,30);Check(flag.IsConcealedBySmoke(),"carrier inside smoke hides flag even when offset outside");
  enemy.LockAimPoint=new(0,0,40);flag.transform.position=new(0,0,20);Check(flag.IsConcealedBySmoke(),"flag itself inside smoke is hidden");
  flag.Carrier=PlayerRef.None;Check(flag.IsConcealedBySmoke()&&!flag.TryGetCarrierMarker(me,out _),"dropped flag and marker inside smoke are hidden too");
  flag.transform.position=new(0,0,50);
  Check(flag.TryGetCarrierMarker(me,out pos)&&pos.z==50,"fallen flag marker returns when outside smoke");
  flag.transform.position=new(0,0,20);flag.Carrier=PlayerRef.Two;
  flag.flagRenderers=new[]{new Renderer{shadowCastingMode=ShadowCastingMode.On},new Renderer{forceRenderingOff=true,shadowCastingMode=ShadowCastingMode.TwoSided}};
  flag.originalRenderingOff=new[]{false,true};flag.originalShadows=new[]{ShadowCastingMode.On,ShadowCastingMode.TwoSided};
  flag.UpdateFlagVisibility();Check(flag.flagRenderers[0].forceRenderingOff&&flag.flagRenderers[0].shadowCastingMode==ShadowCastingMode.Off,"flag render and shadow suppressed locally");
  flag.CloudActive=false;flag.UpdateFlagVisibility();Check(!flag.flagRenderers[0].forceRenderingOff&&flag.flagRenderers[0].shadowCastingMode==ShadowCastingMode.On,"smoke expiry restores flag");
  Check(flag.flagRenderers[1].forceRenderingOff&&flag.flagRenderers[1].shadowCastingMode==ShadowCastingMode.TwoSided,"authored hidden renderers remain hidden");
  Check(flag.TryGetCarrierMarker(me,out _),"marker returns after smoke expires");
  flag.CloudActive=true;flag.UpdateFlagVisibility();flag.RestoreFlagVisibility();Check(!flag.flagRenderers[0].forceRenderingOff&&!flag.flagSmokeHidden,"pool/despawn cleanup restores presentation");
  var hp=new HealthView{owner=enemy};Check(hp.CanShowToLocalPlayer(),"enemy bar visible only on observer client");
  hp.owner=me;Check(!hp.CanShowToLocalPlayer(),"never own HP over own body");hp.owner=enemy;
  enemy.Object.HasInputAuthority=true;Check(!hp.CanShowToLocalPlayer(),"never owner-authority bar");enemy.Object.HasInputAuthority=false;
  enemy.TeamIndex=1;Check(!hp.CanShowToLocalPlayer(),"teammates have no red halo or enemy bar");enemy.TeamIndex=2;
  enemy.Hidden=true;Check(!hp.CanShowToLocalPlayer(),"smoke hides enemy bar and halo");enemy.Hidden=false;
  Check(hp.CanShowToLocalPlayer(),"leaving smoke restores enemy visibility");
  enemy.IsAlive=false;Check(!hp.CanShowToLocalPlayer(),"dead enemy hidden");enemy.IsAlive=true;
  enemy.Runner=new();Check(!hp.CanShowToLocalPlayer(),"other runner's HP hidden");enemy.Runner=me.Runner;
  me.IsAlive=false;Check(!hp.CanShowToLocalPlayer(),"observer death hides views");me.IsAlive=true;
  hp.maxHealth=0;Check(!hp.CanShowToLocalPlayer(),"wait for initial health snapshot");hp.maxHealth=100;
  BattleManager.Instance.IsGameplayActive=false;Check(!hp.CanShowToLocalPlayer(),"between rounds hides enemy adornments");
  Check(hp.HasLineOfSight(me),"unobstructed enemy remains readable");
  Physics.Hits=new[]{new RaycastHit{collider=new(),transform=new()}};
  Check(!hp.HasLineOfSight(me),"wall hides bar and halo instead of showing an outline through cover");
  Physics.Hits=new[]{new RaycastHit{collider=new(),transform=new(){Parent=enemy.transform}},new RaycastHit{collider=new(),transform=me.transform}};
  Check(hp.HasLineOfSight(me),"self and target colliders do not hide the health bar");
  Physics.Hits=new RaycastHit[32];Check(!hp.HasLineOfSight(me),"full query buffer conservatively hides overlays");
  enemy.LockAimPoint=default;Check(hp.HasLineOfSight(me),"coincident camera avoids zero direction raycasts");
  var visual=new Transform{Parent=flag.transform,localPosition=new(.2f,.4f,.6f),localScale=new(2,3,4)};
  flag.flagVisualRoot=visual;flag.CacheFlagVisualTransform();flag.UpdateCarriedVisual(enemy);
  Check(Vector3.Distance(visual.localScale,new(.7f,1.05f,1.4f))<.0001f,"carried flag shrinks proportionally from authored non-uniform scale");
  Check(Vector3.Distance(visual.localPosition,new(1.3f,.1f,.6f))<.0001f,"remote carrier flag offset stays alongside body");
  for(int i=0;i<100;i++)flag.UpdateCarriedVisual(enemy);
  Check(Vector3.Distance(visual.localScale,new(.7f,1.05f,1.4f))<.0001f,"many render frames do not shrink cumulatively");
  Check(Vector3.Distance(flag.transform.localScale,new(1,1,1))<.0001f,"root and pickup collider scale remain untouched");
  flag.UpdateCarriedVisual(null);
  Check(Vector3.Distance(visual.localScale,new(2,3,4))<.0001f&&Vector3.Distance(visual.localPosition,new(.2f,.4f,.6f))<.0001f,"drop restores exact original pose and scale");
  flag.UpdateCarriedVisual(enemy);flag.CacheFlagVisualTransform();flag.UpdateCarriedVisual(enemy);
  Check(Vector3.Distance(visual.localScale,new(.7f,1.05f,1.4f))<.0001f,"pooled respawn does not cache a shrunken baseline");
  flag.carriedVisualScale=.5f;flag.UpdateCarriedVisual(enemy);Check(visual.localScale.x==1,"editable carried scale is honored");
  enemy.IsAlive=false;flag.UpdateCarriedVisual(enemy);Check(visual.localScale.x==2,"dead or lost carrier restores original scale");enemy.IsAlive=true;
  Camera.main=new();flag.UpdateCarriedVisual(me);
  Check(Vector3.Distance(visual.position,new(1.1f,-.3f,0))<.0001f,"own flag is offset to viewing camera's right");
  Camera.main.transform.yawQuarter=1;flag.UpdateCarriedVisual(me);
  Check(Vector3.Distance(visual.position,new(0,-.3f,-1.1f))<.0001f,"camera orbit moves own flag to the new screen-right side");
  flag.RestoreFlagVisualTransform();Check(visual.localScale.x==2&&visual.localPosition.x==.2f,"disable/despawn restores visuals");
  flag.flagVisualRoot=flag.transform;flag.visualTransformCached=false;flag.CacheFlagVisualTransform();
  Check(!flag.visualTransformCached,"root cannot be assigned as shrink target");
  flag.flagVisualRoot=new Transform();flag.CacheFlagVisualTransform();Check(!flag.visualTransformCached,"unrelated objects cannot be moved or resized");
  return count;
 }
}}
'@
Add-Type -TypeDefinition $source
"Local flag/health visibility checks passed: $([VisibilityChecks.Tests]::Run())"

$hud = Get-Content Assets/Script/BattleHud.cs -Raw
$presentation = Get-Content Assets/Script/CombatPresentation.cs -Raw
foreach ($token in 'owner.HealthChanged += OnHealthChanged', 'owner.HealthChanged -= OnHealthChanged',
    'if (!healthDirty) return;', 'UpdateEnemyHalo();', 'haloRoot.SetActive(false)', 'Physics.RaycastNonAlloc',
    'Time.unscaledTime + Mathf.Max(0.02f, visibilityRefreshInterval)', 'CreateFallback();', 'minimumScreenHeight') {
    if (!$hp.Contains($token)) {throw "Missing HP/halo contract: $token"}
}
if ($hp -match '\[Networked|\[Rpc|Runner.Spawn') {throw 'Enemy UI must stay local, without extra replication'}
if ((Method $flag 'UpdateFlagVisibility') -match '\.SetActive|\.enabled\s*=|Carrier\s*=') {throw 'Smoke must not mutate flag pickup or lifetime'}
foreach ($token in '!lockTarget.IsHiddenBySmokeFor(owner)', 'TryGetCarrierMarker(local, out Vector3 position)',
    'GetThreatScreenPoint(', 'HideFlagCarrierMarker();', 'ContainsSmoke(slot.Position)') {
    if (!$hud.Contains($token)) {throw "Missing hidden-marker contract: $token"}
}
if (!$presentation.Contains('if (!owner.IsHiddenBySmokeFor(Player.LocalPlayer))')) {throw 'Damage numbers must not reveal concealed enemies'}
$prefab = Get-Content Assets/Ch/ChPrefab.prefab -Raw
if ($prefab -notmatch 'showEnemyHalo: 1' -or $prefab -notmatch 'guid: e2d972b5e63a4d33949c1f9ad4cd108b') {throw 'Network character requires enabled hpfollow/halo'}
'Local-only, event-driven health, smoke concealment and saved prefab contracts passed.'

$flagPrefab = Get-Content Assets/FlagOBJ.prefab -Raw
$root = [regex]::Match($flagPrefab,'(?ms)^--- !u!4 &451996814917360748\r?\n.*?(?=^--- !u!|\z)').Value
$visual = [regex]::Match($flagPrefab,'(?ms)^--- !u!4 &7639268800183515211\r?\n.*?(?=^--- !u!|\z)').Value
if ($root -notmatch 'm_LocalScale: \{x: 1, y: 1, z: 1\}' -or $root -notmatch 'fileID: 7639268800183515211' -or
    $visual -notmatch 'm_Father: \{fileID: 451996814917360748\}' -or $flagPrefab -notmatch 'flagVisualRoot: \{fileID: 7639268800183515211\}') {throw 'Visual root must remain a correctly bound child of the unscaled network root'}
$sphere = [regex]::Match($flagPrefab,'(?ms)^--- !u!135 &7639268800183515202\r?\n.*?(?=^--- !u!|\z)').Value
if ($sphere -notmatch 'm_GameObject: \{fileID: 7655080942296914563\}') {throw 'Pickup sphere must remain on the unscaled root'}
$ids = [regex]::Matches($flagPrefab,'(?m)^--- !u!\d+ &(\d+)') | ForEach-Object {$_.Groups[1].Value}
if (@($ids | Group-Object | Where-Object Count -gt 1).Count) {throw 'Duplicate flag prefab object IDs'}
'Carried scale/offset, camera-relative placement, drop restoration and prefab hierarchy checks passed.'
