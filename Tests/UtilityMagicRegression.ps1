# Runs actual utility gameplay/visibility methods with small Fusion/Unity test doubles.
# Does not prove rendered particles or two-machine network behavior; verify those in Unity.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$flag = Get-Content Assets/Script/BattleFlag.cs -Raw
$projectile = Get-Content Assets/Script/MagicProjectile.cs -Raw
$player = Get-Content Assets/Script/Player.cs -Raw
$presentation = Get-Content Assets/Script/CombatPresentation.cs -Raw
$state = Get-Content Assets/Script/SmokeCloudState.cs -Raw
function Method([string]$source, [string]$name) {
    $m = [regex]::Match($source, '(?ms)^    (?:private|public) (?:static )?[\w<>]+ '+$name+'\([^{}]*?\)\r?\n    \{.*?^    \}')
    if (!$m.Success) { throw "Missing method $name" }
    return $m.Value -replace '^    private ', '    public '
}
function Expression([string]$source, [string]$name) {
    $m = [regex]::Match($source, '(?ms)^    public bool '+$name+'\([^)]*\) =>.*?;')
    if (!$m.Success) { throw "Missing expression $name" }
    return $m.Value
}
$stubs = @'
using System;
using System.Collections.Generic;
namespace UnityEngine {
 public struct Vector3 {
  public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}
  public float sqrMagnitude=>x*x+y*y+z*z;
  public static Vector3 operator-(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
 }
 public class GameObject {public bool destroyed;}
 public enum ShadowCastingMode {Off,On,TwoSided}
 public class Renderer {public bool forceRenderingOff;public ShadowCastingMode shadowCastingMode;}
 public static class Time {public static float unscaledTime;}
 public static class Mathf {public static float Min(float a,float b)=>Math.Min(a,b);}
}
namespace Fusion {
 public interface INetworkStruct {}
 public class NetworkRunner {public float Time;public Dictionary<int,NetworkObject> Objects=new();public bool TryFindObject(NetworkId id,out NetworkObject o)=>Objects.TryGetValue(id.Id,out o);}
 public struct NetworkId {public int Id;public NetworkId(int id){Id=id;}}
 public class NetworkObject {
  public bool IsValid=true,HasStateAuthority=true;public NetworkId Id;public object Component;
  public T GetComponent<T>() where T:class=>Component as T;
 }
 public struct TickTimer {
  public float End;public bool IsRunning;public bool Expired(NetworkRunner r)=>IsRunning&&r.Time>=End;
  public static TickTimer CreateFromSeconds(NetworkRunner r,float s)=>new(){End=r.Time+s,IsRunning=true};
 }
 public class NetworkArray<T> {private T[] values;public NetworkArray(int n){values=new T[n];}public int Length=>values.Length;public T this[int i]=>values[i];public void Set(int i,T v){values[i]=v;}}
}
namespace UtilityChecks {
using UnityEngine;using Fusion;
public enum MagicType {Smoke}
public enum BattleStartPhase {Waiting,Playing,Ended}
public struct MagicStatEntry {public float radius,effectDuration;public bool requiresTarget;public GameObject utilityVfxPrefab;}
public static class CombatPresentationStats {public static MagicStatEntry Stats(MagicType m)=>default;}
public static class UtilityMagicVisual {public static int Creates;public static GameObject CreateSmoke(Vector3 c,float r,GameObject p){Creates++;return new();}}
public class Player {
 public static Player LocalPlayer;
 public NetworkObject Object=new();public NetworkRunner Runner;public int TeamIndex=1;public Vector3 LockAimPoint;
 public bool IsAlive=true,IsStealthed;
'@
$playerEnd = "`n}`npublic class BattleFlag {`n" + @'
 public static BattleFlag Instance;
 public const int MaxSmokeClouds=16;
 public NetworkObject Object=new();public NetworkRunner Runner=new();public BattleStartPhase Phase=BattleStartPhase.Playing;
 public NetworkArray<SmokeCloudState> SmokeClouds=new(MaxSmokeClouds);public int SmokeSequence;
 public GameObject[] smokeViews=new GameObject[MaxSmokeClouds];public int[] smokeViewSequences=new int[MaxSmokeClouds];
 public static void Destroy(GameObject g){g.destroyed=true;}
'@
$flagMethods = @('FindFreeSmokeSlot','CreateSmoke','ContainsSmoke','RenderSmoke','ClearSmokeViews') | ForEach-Object { Method $flag $_ }
$flagMethods = ($flagMethods -join "`n").Replace('CombatPresentation.Stats', 'CombatPresentationStats.Stats')
$projectileStart = @'
}
public class MagicProjectile {
 public static HashSet<MagicProjectile> active=new();
 public NetworkRunner Runner;public NetworkObject Object=new();public bool initialized=true,Finished,IsMine;
 public MagicStatEntry stats=new(){requiresTarget=true};public int ShooterTeam=2;public NetworkId TargetId;
 public Vector3 direction=new(0,0,1);
'@
$presentationStart = @'
}
public class CombatPresentation {
 public class Visual {public Renderer renderer;public bool authoredForceRenderingOff;public ShadowCastingMode authoredShadows;}
 public List<Visual> visuals=new();public Player owner;
 public float deathStarted=-1,deathFadeSeconds=2;
 public bool visibilityInitialized,lastSmokeHidden,lastFadeHidden;
'@
$tests = @'
}
public static class Tests {
 static int checks;static void Check(bool b,string s){if(!b)throw new Exception(s);checks++;}
 static Player Pilot(NetworkRunner r,int id,int team=1){var p=new Player{Runner=r,TeamIndex=team};p.Object.Id=new(id);p.Object.Component=p;r.Objects[id]=p.Object;return p;}
 static MagicProjectile Shot(Player target){var s=new MagicProjectile{Runner=target.Runner,TargetId=target.Object.Id};MagicProjectile.active.Add(s);return s;}
 public static int Run(){
  var flag=new BattleFlag();BattleFlag.Instance=flag;
  var defender=Pilot(flag.Runner,1);var other=Pilot(flag.Runner,2);
  var targeted=Shot(defender);var another=Shot(other);var mine=Shot(defender);mine.IsMine=true;
  var friendly=Shot(defender);friendly.ShooterTeam=1;
  var direct=Shot(defender);direct.stats.requiresTarget=false;
  var finished=Shot(defender);finished.Finished=true;
  var remote=Shot(defender);remote.Runner=new();
  var proxy=Shot(defender);proxy.Object.HasStateAuthority=false;
  Check(MagicProjectile.BreakHomingFor(defender)==1,"only hostile live homing shot for this player and runner breaks");
  Check(targeted.FindTarget()==null&&targeted.direction.z==1&&!targeted.Finished,"broken shot flies on last direction, no despawn or damage cancellation");
  Check(another.TargetId.Equals(other.Object.Id)&&mine.TargetId.Equals(defender.Object.Id),"other targets and mines remain intact");
  Check(MagicProjectile.BreakHomingFor(defender)==0,"breaking same shots twice is idempotent");
  var fresh=Shot(defender);Check(fresh.FindTarget()==defender,"new lock-on shot after flare can still home");
  defender.Object.HasStateAuthority=false;
  Check(MagicProjectile.BreakHomingFor(defender)==0&&fresh.TargetId.Equals(defender.Object.Id),"clients cannot clear authoritative targets");
  defender.Object.HasStateAuthority=true;

  Check(!SmokeCloudState.ValidSettings(float.NaN,5)&&!SmokeCloudState.ValidSettings(8,float.PositiveInfinity)&&!SmokeCloudState.ValidSettings(0,5),"invalid smoke settings rejected");
  var stats=new MagicStatEntry{radius=8,effectDuration=5};
  defender.LockAimPoint=new(10,5,0);
  Check(flag.CreateSmoke(defender,stats)&&flag.ContainsSmoke(new(10,5,0)),"smoke is created at body point");
  Check(flag.ContainsSmoke(new(18,5,0))&&!flag.ContainsSmoke(new(18.1f,5,0)),"sphere boundary includes edge but excludes outside");
  Check(!flag.ContainsSmoke(new(10,13.1f,0)),"altitude is part of sphere membership");
  defender.LockAimPoint=new(100,5,0);Check(flag.ContainsSmoke(new(10,5,0))&&!flag.ContainsSmoke(defender.LockAimPoint),"cloud stays where cast instead of following caster");
  var enemy=Pilot(flag.Runner,3,2);enemy.LockAimPoint=new(10,5,0);
  Check(enemy.IsHiddenBySmokeFor(defender)&&enemy.IsAlive&&!enemy.IsStealthed,"enemy rendering concealed without stealth/untargetability");
  Check(!enemy.IsHiddenBySmokeFor(enemy)&&!enemy.IsHiddenBySmokeFor(Pilot(flag.Runner,4,2)),"self and teammate retain character view");
  Check(!enemy.IsHiddenBySmokeFor(Pilot(new NetworkRunner(),1)),"isolated runners do not share smoke concealment");
  enemy.Object.IsValid=false;Check(!enemy.IsHiddenBySmokeFor(defender),"despawned objects do not read replicated visibility state");enemy.Object.IsValid=true;
  flag.RenderSmoke();int before=UtilityMagicVisual.Creates;
  for(int i=0;i<120;i++)flag.RenderSmoke();
  Check(UtilityMagicVisual.Creates==before,"persistent smoke does not allocate a new VFX each frame");
  defender.IsAlive=false;defender.Object.IsValid=false;
  Check(flag.ContainsSmoke(new(10,5,0)),"caster death/despawn does not remove area");
  defender.IsAlive=true;defender.Object.IsValid=true;
  defender.LockAimPoint=new(12,5,0);Check(flag.CreateSmoke(defender,stats),"overlapping cloud can be added");
  Check(flag.ContainsSmoke(new(19,5,0)),"overlapping clouds evaluated independently");
  for(int i=2;i<BattleFlag.MaxSmokeClouds;i++)Check(flag.CreateSmoke(defender,stats),"fill bounded area pool");
  Check(!flag.CanCreateSmoke(defender.Runner)&&!flag.CreateSmoke(defender,stats),"full pool refuses without deleting live clouds");
  flag.Runner.Time=5;
  Check(!flag.ContainsSmoke(new(12,5,0))&&flag.CanCreateSmoke(defender.Runner),"exact expiry restores visibility and frees slot");
  flag.RenderSmoke();Check(flag.smokeViews[0]==null,"expired visual released");
  Check(flag.CreateSmoke(defender,stats),"expired slots reused");flag.RenderSmoke();
  Check(UtilityMagicVisual.Creates==before+1,"reused slot receives new visual");
  var late=new BattleFlag{Runner=flag.Runner};late.SmokeClouds.Set(0,flag.SmokeClouds[0]);late.RenderSmoke();
  Check(late.smokeViews[0]!=null,"late observer reconstructs persistent cloud from snapshot, not missed cast RPC");
  flag.Phase=BattleStartPhase.Ended;flag.RenderSmoke();
  Check(!flag.ContainsSmoke(new(12,5,0))&&flag.smokeViews[0]==null,"match end removes concealment and visuals");
  flag.Phase=BattleStartPhase.Playing;flag.Object.HasStateAuthority=false;
  Check(!flag.CreateSmoke(defender,stats),"non-authority flag cannot create area");flag.Object.HasStateAuthority=true;

  Player.LocalPlayer=defender;enemy.LockAimPoint=new(12,5,0);
  var visual=new CombatPresentation.Visual{renderer=new Renderer(),authoredShadows=ShadowCastingMode.TwoSided};
  var cp=new CombatPresentation{owner=enemy};cp.visuals.Add(visual);
  cp.UpdateSmokeVisibility();Check(visual.renderer.forceRenderingOff&&visual.renderer.shadowCastingMode==ShadowCastingMode.Off,"enemy mesh and shadow hidden");
  enemy.LockAimPoint=new(100,5,0);cp.UpdateSmokeVisibility();
  Check(!visual.renderer.forceRenderingOff&&visual.renderer.shadowCastingMode==ShadowCastingMode.TwoSided,"exit restores authored shadow mode");
  cp.deathStarted=0;Time.unscaledTime=3;enemy.LockAimPoint=new(12,5,0);cp.UpdateSmokeVisibility();
  enemy.LockAimPoint=new(100,5,0);cp.UpdateSmokeVisibility();
  Check(visual.renderer.forceRenderingOff,"smoke exit must not reveal death-faded mesh");
  cp.OnDisable();Check(!visual.renderer.forceRenderingOff&&!cp.visibilityInitialized,"disable cleans local visibility overrides");
  return checks;
 }
}}
'@
$source = $stubs + (Expression $player 'IsHiddenBySmokeFor') + $playerEnd +
    (Expression $flag 'CanCreateSmoke') + $flagMethods + $projectileStart +
    (Method $projectile 'BreakHomingFor') + (Method $projectile 'FindTarget') + $presentationStart +
    (Method $presentation 'UpdateSmokeVisibility') + (Method $presentation 'OnDisable') + $tests +
    "`nnamespace UtilityChecks {`n$state`n}"
Add-Type -TypeDefinition $source
"Utility magic behavior checks passed: $([UtilityChecks.Tests]::Run())"

# Saved assets must preserve existing spell IDs and expose both new UI choices.
$tableAsset = Get-Content Assets/Resources/MagicStatTable.asset -Raw
$customAsset = Get-Content Assets/UI/Prefab/CustumUI.prefab -Raw
$characterAsset = Get-Content Assets/Ch/ChPrefab.prefab -Raw
$mainAsset = Get-Content Assets/Scenes/Main.unity -Raw
$spellIds = [regex]::Matches($tableAsset, '(?m)^  - magic: (\d+)\r?$') | ForEach-Object { [int]$_.Groups[1].Value }
if (($spellIds -join ',') -ne '1,2,3,4,5,6,7,8,9,10,11,12') { throw 'Magic table IDs must remain stable and include both utilities.' }
$choices = [regex]::Match($customAsset, '(?ms)^  magicChoices:\r?\n(.*?)^  magicTable:').Groups[1].Value
$choiceIds = [regex]::Matches($choices, '(?m)^  - magic: (\d+)\r?$') | ForEach-Object { [int]$_.Groups[1].Value }
if (($choiceIds -join ',') -ne ($spellIds -join ',')) { throw 'Customization choices do not cover table spells.' }
$staffs = [regex]::Match($characterAsset, '(?ms)^  magicStaffPrefabs:\r?\n(.*?)^  hatPrefabs:').Groups[1].Value
$staffIds = [regex]::Matches($staffs, 'fileID: (\d+)') | ForEach-Object { $_.Groups[1].Value }
if ($staffIds.Count -ne 13 -or $staffIds[11] -eq '0' -or $staffIds[12] -eq '0') { throw 'New utility staff visual slots are missing.' }
if ($mainAsset -match 'propertyPath: magicChoices.Array.size') { throw 'Main overrides spell choice count; verify both new entries are included.' }
if (!(Test-Path Assets/Resources/UtilityMagicParticle.shader)) { throw 'Generated utility VFX shader is missing.' }
$hpSource = Get-Content Assets/Script/hpfollow.cs -Raw
if ((Method $hpSource 'CanShowToLocalPlayer') -notmatch '!owner.IsHiddenBySmokeFor\(local\)') { throw 'Enemy HP must respect smoke concealment.' }
'Utility magic saved asset and health-bar contracts passed: 6'

$utilityVisual = Get-Content Assets/Script/UtilityMagicVisual.cs -Raw
$smokeVisual = Method $utilityVisual 'CreateSmoke'
foreach ($token in 'smokeColor.a = 0.65f', 'main.maxParticles = 64', 'emission.rateOverTime = 24f',
    'ParticleSystem.Burst(0f, 32)', 'GradientAlphaKey(1f, 0.75f)') {
    if (!$smokeVisual.Contains($token)) { throw "Smoke opacity/density contract missing: $token" }
}
if ($smokeVisual.IndexOf('if (custom != null)') -gt $smokeVisual.IndexOf('smokeColor.a = 0.65f')) {
    throw 'Generated smoke settings must not replace authored VFX'
}
'Denser generated smoke with bounded particles and custom VFX preservation passed: 6'
