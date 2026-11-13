# Offline execution of the production rules, table, stat transform and input policy.
# Does not replace Fusion weaving or two-client Play Mode verification.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo
function Method([string]$source,[string]$name) {
    $m=[regex]::Match($source,'(?ms)^    private static void '+$name+'\([^{}]*?\)\r?\n    \{.*?^    \}')
    if(!$m.Success){throw "Missing production validator $name"}; $m.Value
}
$combat=Get-Content Tests/MagicCombatRegression.ps1 -Raw
$stubs=[regex]::Match($combat,'(?ms)\$stubs = @''\r?\n(.*?)\r?\n''@').Groups[1].Value
$stubs=$stubs.Substring(0,$stubs.IndexOf('namespace CombatChecks'))
$stubs=$stubs.Replace('public class Object {}','public enum HideFlags {DontSave} public class Object {public HideFlags hideFlags;}')
$stubs=$stubs.Replace('public class ScriptableObject:Object {}','public class ScriptableObject:Object { public static T CreateInstance<T>() where T:new()=>new T(); }')
$stubs=$stubs.Replace('public class Object {','public class HideInInspectorAttribute:Attribute {} public class Object {')
$stubs=$stubs.Replace('where T:class=>null;','where T:class=>(T)Activator.CreateInstance(typeof(T));')
$stubs=$stubs.Replace('public static float Max(float a,float b)=>Math.Max(a,b);','public static float Max(float a,float b)=>Math.Max(a,b); public static float Abs(float v)=>Math.Abs(v);')
$enums=[regex]::Match((Get-Content Assets/Script/PlayerData.cs -Raw),'public enum MagicType\s*\{[^}]+\}').Value
$stubs=$stubs.Replace('public static class Mathf {','public static class Mathf { public static int Clamp(int v,int a,int b)=>Math.Clamp(v,a,b); public static int Max(int a,int b)=>Math.Max(a,b); public static int RoundToInt(float v)=>(int)Math.Round(v); public static float Lerp(float a,float b,float t)=>a+(b-a)*Clamp01(t); public static float Min(params float[] v)=>System.Linq.Enumerable.Min(v);')
$stubs=$stubs.Replace('public float sqrMagnitude=>x*x+y*y+z*z;', 'public float sqrMagnitude=>x*x+y*y+z*z; public float magnitude=>(float)Math.Sqrt(sqrMagnitude); public static Vector3 operator*(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);')
$code=$stubs+"`nnamespace Fusion {public enum PlayerRef {None,A,B}}`nnamespace ThunderChecks { $enums }`n"
foreach($file in 'MagicStatTable','PerkStatTable','PerkRules','PerkEffects','PerkInputGate') {
    $code+="`nnamespace ThunderChecks {`n"+(Get-Content "Assets/Script/$file.cs" -Raw)+"`n}`n"
}
$code+=@'
namespace UnityEngine {
 public struct Vector2 {
  public float x,y; public Vector2(float x,float y){this.x=x;this.y=y;}
  public float sqrMagnitude=>x*x+y*y;
  public static implicit operator Vector2(Vector3 v)=>new(v.x,v.y);
  public static Vector2 operator-(Vector2 a,Vector2 b)=>new(a.x-b.x,a.y-b.y);
 }
 public struct Ray {public Vector3 origin,direction; public Ray(Vector3 o,Vector3 d){origin=o;direction=d;} public Vector3 GetPoint(float d)=>origin+direction*d;}
 public class Camera {
  public Vector3 WorldToViewportPoint(Vector3 p)=>new(.5f+p.x/p.z,.5f+p.y/p.z,p.z);
  public Vector3 WorldToScreenPoint(Vector3 p){var v=WorldToViewportPoint(p);return new(v.x*1000,v.y*1000,v.z);}
 }
}
namespace ThunderChecks { using UnityEngine; using System.Collections.Generic;
public class NetObject {public bool IsValid=true;}
public static class BattleHud {public static Ray Aim=new(default,Vector3.forward);public static Ray GetMagicAimRay(Camera c)=>Aim;}
public static class MagicProjectile {
 public static bool Sight=true; public static HashSet<float> BlockedDepths=new();
 public static bool HasSummonSight(Vector3 a,Vector3 b)=>Sight&&!BlockedDepths.Contains(b.z);
}
public class Player {
 public static List<Player> ActiveCombatants=new(); public NetObject Object=new();public object Runner;
 public Vector3 LockAimPoint,MagicCastPosition; public bool Targetable=true,Eligible=true;
 public bool localAreaAimStarted; public Player areaSnapTarget; public float areaAimDistance;
 public MagicStatEntry SelectedMagicStats=MagicStatTable.DefaultEntry(MagicType.Thunder);
 public bool IsTargetableBy(Player p)=>Targetable&&p!=this;
 public bool CanAimAreaMagic()=>Eligible;
 public static bool IsFiniteDirection(Vector3 p)=>!float.IsNaN(p.x)&&!float.IsNaN(p.y)&&!float.IsNaN(p.z);
 public Vector3 TraceMagic(MagicStatEntry s,Vector3 o,Vector3 d,out Player victim){victim=null;return o+d*s.range;}
'@
$area=Get-Content Assets/Script/Player.AreaMagic.cs -Raw
foreach($name in 'BeginLocalAreaAim','FindAreaSnapTarget','IsAreaSnapTarget','SampleAreaAimInput','ResetLocalAreaAim','TryGetAreaAimPoint','ValidateAreaPoint','IsFiniteAreaPoint') {
 $m=[regex]::Match($area,'(?ms)^    (?:public|private) (?:static )?[\w<>]+ '+$name+'\([^{}]*?\)\r?\n    \{.*?^    \}')
 if(!$m.Success){throw "Missing production area method $name"}
 $code+=$m.Value.Replace('private ','public ')
}
$code+=@'
}
public static class Tests {
 static int count; static void Check(bool b,string message){if(!b)throw new System.Exception(message);count++;}
 static bool Near(float a,float b)=>System.Math.Abs(a-b)<.001f;
 public static int Run(){
  var table=new PerkStatTable(); var baseStats=MagicStatTable.DefaultEntry(MagicType.Thunder);
  var first=PerkEffects.Apply(baseStats,0,1,0,200,table);var second=PerkEffects.Apply(baseStats,0,2,0,200,table);
  Check(Near(baseStats.radius,20)&&Near(first.radius,24)&&Near(second.radius,24),"base / first / second radii 20 / 24 / 24");
  Check(Near(baseStats.damage,120)&&Near(baseStats.strikeDelay,1),"base damage 120 and strike delay one second");
  Check(Near(PerkEffects.AreaHit(second,10.7f).damage,156)&&Near(PerkEffects.AreaHit(second,10.9f).damage,120),"core bonus follows new radius, boundary 10.8");
  var runner=new object(); var owner=new Player{Runner=runner};var camera=new Camera();
  var near=new Player{Runner=runner,LockAimPoint=new(2,0,20)};
  var centered=new Player{Runner=runner,LockAimPoint=new(1,0,100)};
  Player.ActiveCombatants.AddRange(new[]{owner,near,centered});
  Check(owner.SampleAreaAimInput(camera,true,out var point)&&Near(point.z,100),"screen cursor proximity outranks world distance");
  near.LockAimPoint=new(0,0,20);centered.LockAimPoint=new(3,0,105);
  Check(owner.SampleAreaAimInput(camera,true,out point)&&Near(point.x,3)&&Near(point.z,105),"held cast follows chosen enemy without retarget churn");
  Check(owner.SampleAreaAimInput(camera,false,out var release)&&Near(release.z,105)&&!owner.localAreaAimStarted,"release samples then clears state");
  centered.LockAimPoint=new(0,0,120);
  Check(Near(release.z,105)&&!owner.SampleAreaAimInput(camera,false,out _),"confirmed coordinate stays fixed and idle input does not reacquire");
  Check(owner.SampleAreaAimInput(camera,true,out point)&&Near(point.z,20),"next cast selects anew");
  owner.areaSnapTarget=null;owner.areaAimDistance=70;
  Check(owner.SampleAreaAimInput(camera,true,out point)&&Near(point.z,70),"manual wheel override is not reacquired while holding");
  owner.ResetLocalAreaAim();near.LockAimPoint=new(0,0,20);centered.LockAimPoint=new(20,0,100);
  BattleHud.Aim=new(default,new Vector3(.2f,0,1).normalized);
  Check(owner.SampleAreaAimInput(camera,true,out point)&&Near(point.x,20),"moving aim marker, not screen center, chooses the target");
  centered.Object.IsValid=false;
  Check(owner.SampleAreaAimInput(camera,true,out point)&&owner.areaSnapTarget==null,"lost target returns to manual placement");
  centered.Object.IsValid=true;BattleHud.Aim=new(default,Vector3.forward);
  owner.ResetLocalAreaAim();near.Object.IsValid=false;centered.Targetable=false;
  Check(owner.SampleAreaAimInput(camera,true,out point)&&Near(point.z,50),"invalid/dead/friendly targets fall back to manual distance");
  owner.ResetLocalAreaAim();near.Object.IsValid=true;near.Runner=new object();centered.Targetable=true;centered.LockAimPoint=new(0,0,310);
  Check(owner.FindAreaSnapTarget(camera,BattleHud.Aim,baseStats)==null,"different runner and out of range rejected");
  near.Runner=runner;near.LockAimPoint=new(0,0,-20);centered.LockAimPoint=new(100,0,100);
  Check(owner.FindAreaSnapTarget(camera,BattleHud.Aim,baseStats)==null,"behind camera and offscreen rejected");
  near.LockAimPoint=new(0,0,20);centered.LockAimPoint=new(1,0,100);MagicProjectile.BlockedDepths.Add(20);
  Check(owner.FindAreaSnapTarget(camera,BattleHud.Aim,baseStats)==centered,"wall-blocked nearest target skipped");
  MagicProjectile.Sight=false;
  Check(!owner.ValidateAreaPoint(new(0,0,100),baseStats,out _),"host rejects obstructed center");
  MagicProjectile.Sight=true;MagicProjectile.BlockedDepths.Clear();
  Check(!owner.ValidateAreaPoint(new(float.NaN,0,50),baseStats,out _)&&!owner.ValidateAreaPoint(new(0,0,301),baseStats,out _),"host rejects NaN and distant center");
  owner.BeginLocalAreaAim(camera,BattleHud.Aim);owner.Eligible=false;
  Check(!owner.SampleAreaAimInput(camera,false,out _)&&!owner.localAreaAimStarted,"ineligible release cancels local selection");
  return count;
 }
}}
'@
Add-Type -TypeDefinition $code
"Thunder selection, release and perk checks passed: $([ThunderChecks.Tests]::Run())"
$asset=Get-Content Assets/Resources/MagicStatTable.asset -Raw
$thunder=[regex]::Match($asset,'(?ms)^  - magic: 4\r?\n.*?(?=^  - magic:|\z)').Value
if($thunder-notmatch '(?m)^    radius: 20\r?$' -or $thunder-notmatch '(?m)^    damage: 120\r?$' -or $thunder-notmatch '(?m)^    strikeDelay: 1\r?$'){throw 'Serialized thunder stats differ from tested defaults'}
'Serialized Thunder radius 20 / damage 120 / delay 1 verified. Camera and visibility use controlled doubles; real Unity/Fusion play testing is separate.'
