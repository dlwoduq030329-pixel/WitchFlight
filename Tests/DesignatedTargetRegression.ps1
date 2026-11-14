# Execute production selection/input/outline methods with deterministic camera and sight doubles.
# This does not replace Unity rendering or a two-client Fusion play test.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$targeting = Get-Content Assets/Script/enemyLockOn.cs -Raw
$presentation = Get-Content Assets/Script/CombatPresentation.cs -Raw
function Method([string]$source, [string]$name, [string]$parameters='[^{}]*?') {
    $m = [regex]::Match($source, '(?ms)^    (?:public|private) (?:static )?[\w<>]+ ' + $name + '\(' + $parameters + '\)\r?\n    \{.*?^    \}')
    if (!$m.Success) { throw "Missing production method $name" }
    $m.Value -replace '^    private ', '    public '
}
$code = @'
using System;
using System.Collections.Generic;
using NetworkId = System.Int32;
namespace DesignatedTargetChecks {
public struct Vector2 {
 public float x,y;public Vector2(float x,float y){this.x=x;this.y=y;}
 public float sqrMagnitude=>x*x+y*y;
 public static implicit operator Vector2(Vector3 v)=>new(v.x,v.y);
 public static Vector2 operator-(Vector2 a,Vector2 b)=>new(a.x-b.x,a.y-b.y);
}
public struct Vector3 {
 public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
 public static Vector3 operator+(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
 public static Vector3 operator-(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
 public static Vector3 operator*(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);
 public static float Angle(Vector3 a,Vector3 b)=>0;
}
public struct Ray {public Vector3 origin,direction;public Vector3 GetPoint(float d)=>origin+direction*d;}
public class Transform {public Vector3 position,forward=new(0,0,1);}
public class Camera {
 public Transform transform=new();
 public Vector3 WorldToViewportPoint(Vector3 p)=>new(.5f+p.x/p.z,.5f+p.y/p.z,p.z);
 public Vector3 WorldToScreenPoint(Vector3 p){var v=WorldToViewportPoint(p);return new(v.x*1000,v.y*1000,v.z);}
}
public struct Color {public float r,g,b,a;public Color(float r,float g,float b,float a){this.r=r;this.g=g;this.b=b;this.a=a;}}
public class Outline {public bool enabled;public Color OutlineColor;}
public enum MagicType {Fire,Healing,BladeRain}
public struct MagicStatEntry {public bool TargetsAllies;}
public class NetworkObject {public bool IsValid=true,HasInputAuthority;public int Id;public Player Player;public MagicProjectile Summon;}
public class Player {
 public static Player LocalPlayer;public static List<Player> ActiveCombatants=new();
 public NetworkObject Object=new();public object Runner;public int TeamIndex;
 public bool IsAlive=true,Stealth,Hidden,Visible=true,Blocked,InRange=true;
 public Vector3 LockAimPoint;public MagicStatEntry SelectedMagicStats;public MagicType Magic;
 public HashSet<int> RainTargets=new();
 public bool HasRainTarget(int id)=>RainTargets.Contains(id);
 public bool IsTargetableBy(Player p)=>IsAlive&&!Stealth&&p!=this&&TeamIndex!=p.TeamIndex;
 public bool CanAcquireMagicTarget()=>true;public MagicType GetSelectedMagic()=>Magic;
 public bool IsHiddenBySmokeFor(Player p)=>Hidden;
}
public class MagicProjectile {
 public static List<MagicProjectile> ActiveProjectiles=new();public NetworkObject Object=new();public Transform transform=new();
 public bool IsSummonEnemyOf(Player p)=>true;
}
public class BattleManager {public static BattleManager Instance=new();public bool IsGameplayActive=true;}
public static class BattleHud {public static Ray Aim=new(){direction=new(0,0,1)};public static Ray GetMagicAimRay(Camera c)=>Aim;}
public static class BattlePerkUI {public static bool BlocksCombatInput;public static void ShowNoEnemyInRange(){}}
public static class BattleEmoteUI {public static bool BlocksCombatInput;}
public enum CursorLockMode {None,Locked}
public static class Cursor {public static CursorLockMode lockState=CursorLockMode.Locked;}
public static class Input {public static bool Click;public static bool GetMouseButtonDown(int b)=>Click&&b==2;}
public static class Time {public static float unscaledTime;}
public class enemyLockOn {
 public Player owner,designatedTarget;public static enemyLockOn localDesignator;public bool isActiveAndEnabled=true;
 public Camera targetCamera=new();public NetworkObject currentTarget;public bool wasHeld;
 public float nextRefreshTime,targetRefreshInterval=.05f;public MagicType sampledMagic;
 public void SetArrowVisible(bool visible){}
 public bool TryGetAlignedAim(out Vector3 dir){dir=new(0,0,1);return true;}
 public bool HasLineOfSight(Vector3 origin,Player candidate)=>!candidate.Blocked;
 public bool HasEnemyInRange()=>true;
 public Vector3 TargetingViewport(Vector3 point)=>targetCamera.WorldToViewportPoint(point);
 public bool IsVisible(NetworkObject o){var p=o?.Player;return o!=null&&o.IsValid&&p!=null&&p.Runner==owner.Runner&&p.IsAlive&&p.Visible&&p.InRange&&!p.Blocked&&
  (owner.SelectedMagicStats.TargetsAllies?p!=owner&&p.TeamIndex==owner.TeamIndex:p.IsTargetableBy(owner));}
'@
foreach ($name in 'IsLocallyDesignated','SetDesignatedTarget','FindNextDesignatedTarget','Update','FindClosestVisibleEnemy') {
    $code += Method $targeting $name
}
$code += Method $targeting 'GetInputTarget' 'bool held'
$code += @'
}
public class CombatPresentation {
 public static bool MenuOpen;public Player owner;public Outline enemyOutline=new();
'@
$code += (Method $presentation 'CanShowEnemyOutline') + (Method $presentation 'UpdateEnemyOutline')
$code += @'
}
public static class Tests {
 static int count;static void Check(bool ok,string label){if(!ok)throw new Exception(label);count++;}
 static Player Make(object runner,int id,int team,float x,float z){var p=new Player{Runner=runner,TeamIndex=team,LockAimPoint=new(x,0,z)};p.Object.Id=id;p.Object.Player=p;Player.ActiveCombatants.Add(p);return p;}
 static void Click(enemyLockOn selector){Input.Click=true;selector.Update();Input.Click=false;}
 static bool Green(Outline o)=>o.enabled&&o.OutlineColor.g==1&&o.OutlineColor.r==0;
 public static int Run(){
  var runner=new object();var owner=Make(runner,1,1,0,0);owner.Object.HasInputAuthority=true;Player.LocalPlayer=owner;
  var a=Make(runner,2,2,0,100);var b=Make(runner,3,2,2,100);var ally=Make(runner,4,1,0,50);
  var selector=new enemyLockOn{owner=owner};var av=new CombatPresentation{owner=a};var bv=new CombatPresentation{owner=b};
  Click(selector);Check(selector.designatedTarget==a,"first wheel click chooses nearest screen target");
  av.UpdateEnemyOutline();bv.UpdateEnemyOutline();Check(Green(av.enemyOutline)&&!Green(bv.enemyOutline),"only local selected enemy is green");
  Click(selector);av.UpdateEnemyOutline();bv.UpdateEnemyOutline();
  Check(selector.designatedTarget==b&&!Green(av.enemyOutline)&&Green(bv.enemyOutline),"second click excludes old target and restores its normal color");
  Check(selector.GetInputTarget(true)==b.Object.Id,"designation outranks closer enemy for lock spells");
  Click(selector);Check(selector.designatedTarget==a&&selector.GetInputTarget(true)==a.Object.Id,"wheel click during held charge refreshes lock target");
  Check(selector.GetInputTarget(false)==a.Object.Id&&selector.currentTarget==null,"release returns last selection before clearing lock state");
  owner.SelectedMagicStats=new(){TargetsAllies=true};owner.Magic=MagicType.Healing;
  Check(selector.GetInputTarget(true)==ally.Object.Id,"hostile designation never overrides ally healing");
  owner.SelectedMagicStats=default;owner.Magic=MagicType.Fire;a.InRange=false;
  Check(selector.FindClosestVisibleEnemy()==b.Object,"out-of-range designation falls back to valid target");a.InRange=true;
  a.Visible=false;Check(selector.FindClosestVisibleEnemy()==b.Object,"offscreen designation does not bypass spell visibility");a.Visible=true;
  owner.RainTargets.Add(a.Object.Id);Check(selector.FindClosestVisibleEnemy(true)==b.Object,"blade rain does not reacquire already locked designation");owner.RainTargets.Clear();
  var remote=new enemyLockOn{owner=b};Click(remote);Check(enemyLockOn.IsLocallyDesignated(a),"remote component cannot replace local designation");
  Player.LocalPlayer=b;av.UpdateEnemyOutline();Check(!Green(av.enemyOutline),"another viewer does not inherit local green");Player.LocalPlayer=owner;
  a.Hidden=true;av.UpdateEnemyOutline();Check(!av.enemyOutline.enabled,"smoke visibility remains authoritative for local outline");a.Hidden=false;
  Input.Click=false;a.IsAlive=false;selector.Update();av.UpdateEnemyOutline();Check(selector.designatedTarget==null&&!av.enemyOutline.enabled,"dead target clears designation and outline");a.IsAlive=true;
  a.LockAimPoint=new(0,0,-100);b.LockAimPoint=new(100,0,100);Click(selector);Check(selector.designatedTarget==null,"behind-camera/offscreen targets excluded");
  a.LockAimPoint=new(0,0,100);b.LockAimPoint=new(20,0,100);BattleHud.Aim=new(){direction=new(.2f,0,1)};
  Click(selector);Check(selector.designatedTarget==b,"uses moving aim cursor instead of screen center");
  a.Blocked=true;Click(selector);Check(selector.designatedTarget==null,"no eligible alternative clears designation");a.Blocked=false;
  Cursor.lockState=CursorLockMode.None;Click(selector);Check(selector.designatedTarget==null,"UI cursor middle click does not target");Cursor.lockState=CursorLockMode.Locked;
  b.Runner=new object();BattleHud.Aim=new(){direction=new(0,0,1)};Click(selector);Check(selector.designatedTarget==a,"cross-runner target excluded");
  owner.IsAlive=false;selector.Update();Check(selector.designatedTarget==null&&!enemyLockOn.IsLocallyDesignated(a),"owner death clears local highlight");
  return count;
 }
}}
'@
Add-Type -TypeDefinition $code
"Designated target checks passed: $([DesignatedTargetChecks.Tests]::Run())"
