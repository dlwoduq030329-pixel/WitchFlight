# Actual presentation methods with offline Unity doubles. No editor Play/network connection.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$presentation = Get-Content Assets/Script/CombatPresentation.cs -Raw
function Method([string]$name) {
    $m = [regex]::Match($presentation, '(?ms)^    (?:public|private) (?:static )?[\w<>]+ '+$name+'\([^{}]*?\)\r?\n    \{.*?^    \}')
    if (!$m.Success) { throw "Missing method: $name" }
    $m.Value -replace '^    private ', '    public '
}
$methods = ('OwnerStats','ShowLockComplete','UpdateLockCompleteVfx','ClearLockCompleteVfx','ShowCast','ShowCastVfx','UpdateStatusEffectVfx','ClearStatusEffectVfxView','ClearStatusEffectVfx' | ForEach-Object { Method $_ }) -join "`n"
$binding = [regex]::Match($presentation,'(?ms)^    private sealed class StatusEffectVfxBinding\r?\n    \{.*?^    \}').Value.Replace('private sealed','public sealed')
$statusEnum = Get-Content Assets/Script/StatusEffectType.cs -Raw
$stubs = @'
using System;
using System.Collections.Generic;
namespace VfxChecks {
 public class Object {public bool destroyed;public static void Destroy(Object o){if(o!=null)o.destroyed=true;}public static void Destroy(Object o,float delay) {}}
 public class Material:Object {}
 public class TooltipAttribute:Attribute {public TooltipAttribute(string s){}}
 public class MinAttribute:Attribute {public MinAttribute(float n){}}
 public struct Color {}
 public struct Vector3 {public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}public static Vector3 one=>new(1,1,1);public static Vector3 up=>new(0,1,0);public static Vector3 operator*(Vector3 v,float f)=>new(v.x*f,v.y*f,v.z*f);}
 public struct Quaternion {public static Quaternion Euler(Vector3 v)=>default;}
 public class Transform {public Transform parent;public Vector3 position,localPosition,localScale=Vector3.one;public Quaternion rotation,localRotation;public void SetParent(Transform p,bool world){parent=p;}}
 public class Collider:Object {public bool enabled=true;}
 public enum ShadowCastingMode {Off}
 public class Renderer:Object {public Material sharedMaterial;public ShadowCastingMode shadowCastingMode;public bool receiveShadows=true;}
 public enum PrimitiveType {Sphere}
 public class GameObject:Object {
  public string name;public Transform transform=new();public bool activeSelf=true;public Collider collider=new();public Renderer renderer=new();
  public static GameObject CreatePrimitive(PrimitiveType p)=>new();public void SetActive(bool v){activeSelf=v;}
  public T GetComponent<T>() where T:class=>(collider as T)??(renderer as T);
 }
 public static class Time {public static float time;}
 public static class Mathf {public static float Max(float a,float b)=>Math.Max(a,b);}
 public enum MagicType {None,Fire,Ice,Vision,Thunder,Healing,Binding,Dark,Curse,Mine,Razier,Flare,Smoke}
 public enum MagicEffectKind {DirectDamage,Healing,Flare,Smoke,DelayedArea,PursuitSummon,GuidedChannel,BeamChannel}
 public class MagicStatEntry {
  public MagicType magic;public MagicEffectKind effect;public bool requiresTarget;public float projectileSpeed,vfxLifetime=1.5f;
  public GameObject lockOnCompleteVfxPrefab,castVfxPrefab,beamVfxPrefab;
  public Material beamMaterial;
  public bool IsChanneled=>effect==MagicEffectKind.GuidedChannel||effect==MagicEffectKind.BeamChannel;
 }
 public class MagicStatTable {public Dictionary<MagicType,MagicStatEntry> Entries=new();public MagicStatEntry GetStats(MagicType m)=>Entries[m];}
 public class NetworkObject {public bool IsValid=true;}
 public class Player {
  public static Player LocalPlayer=new();public NetworkObject Object=new();public bool IsAlive=true,Hidden;public MagicType Selected=MagicType.Fire;
  public MagicStatTable MagicTable=new();public Transform MagicCastAnchor=new();
  public HashSet<StatusEffectType> ActiveStatuses=new();
  public bool HasStatusEffect(StatusEffectType s)=>Object!=null&&Object.IsValid&&IsAlive&&ActiveStatuses.Contains(s);
  public bool IsHiddenBySmokeFor(Player viewer)=>Hidden;public MagicType GetSelectedMagic()=>Selected;
 }
 public class BattleManager {public static BattleManager Instance=new();public bool IsGameplayActive=true;}
 public static class UtilityMagicVisual {public static int Flares;public static void PlayFlare(Player p,Vector3 a,Vector3 b,MagicStatEntry s){Flares++;}}
 public static class CombatTransientEffect {public static int Beams;public static void PlayBeam(Vector3 a,Vector3 b,Color c){Beams++;}}
 public class CombatMagicVisual {
  public GameObject gameObject=new();public static int Beams;public static Material LastMaterial;
  public static CombatMagicVisual Beam(MagicType m,GameObject p,Material material=null){Beams++;LastMaterial=material;return new();}public void SetEndpoints(Vector3 a,Vector3 b){}
 }
 public class CombatPresentation:Object {
  public Player owner=new();public bool isActiveAndEnabled=true;public Transform transform=new();
  public StatusEffectVfxBinding[] statusEffectVfxViews;
  public GameObject lockCompleteVfx;public Material lockCompleteMaterial;public MagicType lockCompleteMagic;public float lockCompleteExpiresAt;
  public static int Instantiates,Impacts,Pulses;public static GameObject LastPrefab;public static Vector3 LastOrigin;
  public static MagicStatEntry Stats(MagicType m)=>new(){magic=m};public static Color MagicColor(MagicType m)=>default;
  public static Material CreateEffectMaterial(Color c)=>new();
  public static GameObject InstantiateVfx(GameObject prefab,Vector3 p,Quaternion q,Transform parent=null){
   if(prefab==null)return null;Instantiates++;LastPrefab=prefab;LastOrigin=p;
   var view=new GameObject();view.transform.parent=parent;view.transform.position=p;return view;
  }
  public static void ShowImpact(Vector3 p,MagicType m){Impacts++;}public static void SpawnPulse(Vector3 p,Color c,float d,float t){Pulses++;}
'@
$tests = @'
 }
 public static class Tests {
  static int n;static void Check(bool ok,string name){if(!ok)throw new Exception(name);n++;}
  static CombatPresentation New(){
   Time.time=0;BattleManager.Instance=new();var p=new CombatPresentation();
   p.owner.MagicTable.Entries[MagicType.Fire]=new(){magic=MagicType.Fire,requiresTarget=true,projectileSpeed=75,lockOnCompleteVfxPrefab=new(),castVfxPrefab=new()};
   p.owner.MagicCastAnchor.position=new(2,3,4);return p;
  }
  public static int Run(){
   var p=New();p.ShowLockComplete(MagicType.Fire);var view=p.lockCompleteVfx;
   Check(view!=null&&view.transform.parent==p.owner.MagicCastAnchor,"completion attaches to existing wand anchor");
   Check(view.transform.position.z==4&&CombatPresentation.LastPrefab==p.owner.MagicTable.GetStats(MagicType.Fire).lockOnCompleteVfxPrefab,"correct per-spell prefab and anchor world position");
   int created=CombatPresentation.Instantiates;p.UpdateLockCompleteVfx();p.UpdateLockCompleteVfx();
   Check(CombatPresentation.Instantiates==created,"render updates cannot restart completion");
   p.owner.Hidden=true;p.UpdateLockCompleteVfx();Check(!view.activeSelf,"hidden enemy wand effect is concealed by smoke");
   p.owner.Hidden=false;p.UpdateLockCompleteVfx();Check(view.activeSelf&&CombatPresentation.Instantiates==created,"leaving smoke reveals same effect without respawn");
   p.ClearLockCompleteVfx();Check(p.lockCompleteVfx==null&&view.destroyed&&!view.activeSelf,"cancel immediately hides and destroys effect");
   p.ShowLockComplete(MagicType.Fire);view=p.lockCompleteVfx;p.ShowLockComplete(MagicType.Fire);
   Check(view.destroyed&&p.lockCompleteVfx!=view,"replacement has no orphan effect");
   foreach(string reason in new[]{"expiry","death","invalid","slot","round"}){
    p=New();p.ShowLockComplete(MagicType.Fire);view=p.lockCompleteVfx;
    if(reason=="expiry")Time.time=2;if(reason=="death")p.owner.IsAlive=false;
    if(reason=="invalid")p.owner.Object.IsValid=false;if(reason=="slot")p.owner.Selected=MagicType.Ice;
    if(reason=="round")BattleManager.Instance.IsGameplayActive=false;
    p.UpdateLockCompleteVfx();Check(p.lockCompleteVfx==null&&view.destroyed,"cleanup on "+reason);
   }
   p=New();p.owner.MagicTable.GetStats(MagicType.Fire).lockOnCompleteVfxPrefab=null;p.ShowLockComplete(MagicType.Fire);
   view=p.lockCompleteVfx;var material=p.lockCompleteMaterial;
   Check(view!=null&&material!=null&&!view.collider.enabled,"unassigned completion uses harmless fallback");
   p.ClearLockCompleteVfx();Check(material.destroyed,"fallback material is released");
   p=New();p.isActiveAndEnabled=false;p.ShowLockComplete(MagicType.Fire);Check(p.lockCompleteVfx==null,"disabled presentation cannot spawn effects");
   p=New();p.owner.MagicTable.GetStats(MagicType.Fire).requiresTarget=false;p.ShowLockComplete(MagicType.Fire);Check(p.lockCompleteVfx==null,"instant spells never get completion effects");
   foreach(var effect in new[]{MagicEffectKind.DirectDamage,MagicEffectKind.Healing,MagicEffectKind.DelayedArea,MagicEffectKind.PursuitSummon,MagicEffectKind.Flare,MagicEffectKind.Smoke,MagicEffectKind.BeamChannel}){
    p=New();p.ShowLockComplete(MagicType.Fire);view=p.lockCompleteVfx;
    var s=p.owner.MagicTable.GetStats(MagicType.Fire);s.effect=effect;s.projectileSpeed=0;
    int impact=CombatPresentation.Impacts,beams=CombatTransientEffect.Beams,casts=CombatPresentation.Instantiates;
    p.ShowCast(MagicType.Fire,new(1,2,3),new(4,5,6));
    Check(view.destroyed&&p.lockCompleteVfx==null,"cast ends completion for "+effect);
    Check(CombatPresentation.Instantiates==casts+1&&CombatPresentation.LastPrefab==s.castVfxPrefab,"separate cast prefab for "+effect);
    bool hitscan=effect==MagicEffectKind.DirectDamage;
    Check(CombatPresentation.Impacts==impact+(hitscan?1:0)&&CombatTransientEffect.Beams==beams+(hitscan?1:0),"only hitscan produces immediate beam/impact for "+effect);
   }
   p=New();int impacts=CombatPresentation.Impacts;p.ShowCast(MagicType.Fire,default,default);
   Check(CombatPresentation.Impacts==impacts,"moving projectile waits for actual impact");
   Check(UtilityMagicVisual.Flares==1,"flare uses existing utility visual once");
   p=New();var instant=p.owner.MagicTable.GetStats(MagicType.Fire);instant.projectileSpeed=0;
   instant.beamMaterial=new();int customBeams=CombatMagicVisual.Beams;
   p.ShowCast(MagicType.Fire,default,default);
   Check(CombatMagicVisual.Beams==customBeams+1&&CombatMagicVisual.LastMaterial==instant.beamMaterial,"material-only hitscan uses configured beam material without prefab");
   instant.beamVfxPrefab=new();p.ShowCast(MagicType.Fire,default,default);
   Check(CombatMagicVisual.LastMaterial==instant.beamMaterial&&!instant.beamMaterial.destroyed,"custom prefab also receives override without destroying authored material");
   p=New();var anchor=new Transform();
   p.statusEffectVfxViews=new[]{
    new CombatPresentation.StatusEffectVfxBinding{status=StatusEffectType.Binding,vfxPrefab=new(),anchor=anchor,localOffset=new(0,2,0),scale=2},
    new CombatPresentation.StatusEffectVfxBinding{status=StatusEffectType.Slow,vfxPrefab=new()},
    new CombatPresentation.StatusEffectVfxBinding{status=StatusEffectType.Burn,vfxPrefab=new()},
    new CombatPresentation.StatusEffectVfxBinding{status=StatusEffectType.HitStun},null};
   p.UpdateStatusEffectVfx();Check(p.statusEffectVfxViews[0].instance==null,"status VFX absent before an actual status");
   p.owner.ActiveStatuses.UnionWith(new[]{StatusEffectType.Binding,StatusEffectType.Slow,StatusEffectType.Burn,StatusEffectType.HitStun});
   p.UpdateStatusEffectVfx();var bound=p.statusEffectVfxViews[0].instance;var slow=p.statusEffectVfxViews[1].instance;
   Check(bound.transform.parent==anchor&&bound.transform.localPosition.y==2&&bound.transform.localScale.x==2,"world VFX uses victim anchor, offset and scale");
   Check(slow.transform.parent==p.transform&&p.statusEffectVfxViews[2].instance!=null,"simultaneous statuses attach independently to victim");
   Check(p.statusEffectVfxViews[3].instance==null,"empty VFX slot does not create an unsolicited effect");
   created=CombatPresentation.Instantiates;for(int i=0;i<100;i++)p.UpdateStatusEffectVfx();
   Check(CombatPresentation.Instantiates==created,"held/refreshed status does not restart or duplicate world effect");
   p.owner.Hidden=true;p.UpdateStatusEffectVfx();Check(!bound.activeSelf&&!slow.activeSelf,"smoke hides world status effects from enemy viewer");
   p.owner.ActiveStatuses.Remove(StatusEffectType.Binding);p.UpdateStatusEffectVfx();
   Check(bound.destroyed&&p.statusEffectVfxViews[0].instance==null,"status expiration cleans up even while hidden");
   p.owner.Hidden=false;p.UpdateStatusEffectVfx();Check(slow.activeSelf&&!slow.destroyed,"remaining status reappears after smoke");
   p.owner.ActiveStatuses.Add(StatusEffectType.Binding);p.UpdateStatusEffectVfx();
   Check(p.statusEffectVfxViews[0].instance!=bound,"next application creates a fresh animation");
   p.owner.IsAlive=false;p.UpdateStatusEffectVfx();Check(slow.destroyed&&p.statusEffectVfxViews[2].instance==null,"death clears all world status effects");
   p.owner.IsAlive=true;p.UpdateStatusEffectVfx();var burn=p.statusEffectVfxViews[2].instance;
   BattleManager.Instance.IsGameplayActive=false;p.UpdateStatusEffectVfx();Check(burn.destroyed,"round end clears world status effects");
   BattleManager.Instance.IsGameplayActive=true;p.UpdateStatusEffectVfx();burn=p.statusEffectVfxViews[2].instance;p.ClearStatusEffectVfx();
   Check(burn.destroyed&&!burn.activeSelf,"disable/despawn cleanup hides immediately");
   return n;
  }
 }
}
'@
Add-Type -TypeDefinition ($stubs + $binding + $methods + $tests + "`nnamespace VfxChecks { $statusEnum }")
"Magic VFX stages / wand attachment / lifecycle checks passed: $([VfxChecks.Tests]::Run())"

$player = Get-Content Assets/Script/Player.cs -Raw
if ($player -notmatch 'public Transform MagicCastAnchor => magicCastRoot != null \? magicCastRoot : transform;') { throw 'Wand effect must reuse existing root' }
if ($player -notmatch '\[Rpc\(RpcSources.StateAuthority, RpcTargets.All\)\]\s+private void RPC_PresentLockComplete') { throw 'Completion must reach observers from authority only' }
$prefab = Get-Content Assets/Ch/ChPrefab.prefab -Raw
$rootId = [regex]::Match($prefab,'magicCastRoot: \{fileID: (\d+)\}').Groups[1].Value
$rootBlock = [regex]::Match($prefab,'(?ms)^--- !u!4 &'+$rootId+'\r?\n.*?(?=^---|\z)').Value
$rootObjectId = [regex]::Match($rootBlock,'m_GameObject: \{fileID: (\d+)\}').Groups[1].Value
$rootObject = [regex]::Match($prefab,'(?ms)^--- !u!1 &'+$rootObjectId+'\r?\n.*?(?=^---|\z)').Value
if (!$rootObject.Contains('m_Name: MAGICROOT')) { throw 'Existing MAGICROOT binding was lost' }
$onDisable = Method 'OnDisable'
$onDestroy = Method 'OnDestroy'
if (!$onDisable.Contains('ClearLockCompleteVfx();') -or !$onDestroy.Contains('ClearLockCompleteVfx();')) { throw 'Attached VFX teardown missing' }
$instantiate = Method 'InstantiateVfx'
foreach($s in 'GetComponentInChildren<Fusion.NetworkObject>(true)','collider.enabled = false','body.detectCollisions = false','collision.enabled = false','trigger.enabled = false') {
 if (!$instantiate.Contains($s)) { throw "VFX physics safety missing: $s" }
}
$ice = Get-Content Assets/Script/IceImpactFollowUpVisual.cs -Raw
if (!$ice.Contains('table.GetSecondaryProjectileVfx(table.GetStats(MagicType.Ice), true)')) { throw 'Ice shards bypass per-spell table' }
'Existing MAGICROOT, observer RPC, visual-only physics and Ice VFX routing passed.'
