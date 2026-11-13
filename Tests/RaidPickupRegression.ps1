# Execute production pickup ticks with deterministic Fusion/physics doubles.
# Does not substitute for an online Unity play test.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
function Method($source, $name) {
    $m = [regex]::Match($source, '(?ms)^    (?:private|public) (?:override )?\w+ '+$name+'\([^{}]*?\)\r?\n    \{.*?^    \}')
    if (!$m.Success) { throw "Missing $name" }
    $m.Value -replace '^    private ', '    public ' -replace 'public override ', 'public '
}
$items = Get-Content Assets/Script/BattleItemPickup.cs -Raw
$potions = Get-Content Assets/Script/RaidPotionPickup.cs -Raw
$flag = Get-Content Assets/Script/BattleFlag.cs -Raw
$code = @'
using System;
using System.Collections.Generic;
namespace RaidPickupChecks {
public enum BattleItemType { Booster,HealthPotion,ManaPotion }
public enum MissionObjective {ItemCollected}
public class PlayerData {public static PlayerData FindPerkData(NetworkRunner r,int p)=>null;public void RecordMission(MissionObjective o){}}
public class NetworkObject {public bool HasStateAuthority=true,IsValid=true;public int Id=1;}
public class NetworkGameManager {public static NetworkGameManager Instance;public bool IsDeathMatch;}
public class BattleManager {public static BattleManager Instance;public bool IsGameplayActive=true;public int RoundNumber=1;}
public class RaidBattleManager:BattleManager {}
public struct TickTimer {
 public float? end;public static TickTimer None=>default;
 public static TickTimer CreateFromSeconds(NetworkRunner r,float s)=>new(){end=r.Time+s};
 public bool Expired(NetworkRunner r)=>end.HasValue&&r.Time>=end;
 public bool ExpiredOrNotRunning(NetworkRunner r)=>!end.HasValue||r.Time>=end;
}
public struct Vector3 {
 public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}
 public float sqrMagnitude=>Dot(this,this);public float magnitude=>(float)Math.Sqrt(sqrMagnitude);
 public Vector3 normalized=>this*(1/magnitude);
 public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
 public static Vector3 operator+(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
 public static Vector3 operator-(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
 public static Vector3 operator*(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);
}
public static class Mathf {public static float Max(float a,float b)=>Math.Max(a,b);public static float Abs(float a)=>Math.Abs(a);public static float Clamp01(float a)=>Math.Clamp(a,0,1);}
public class Transform {public Vector3 position;public bool IsChildOf(Transform t)=>this==t;}
public class MagicProjectile {}
public class Collider {public Transform transform=new();public object owner;public T GetComponentInParent<T>() where T:class=>owner as T;}
public struct RaycastHit {public Collider collider;}
public enum QueryTriggerInteraction {Ignore}
public class PhysicsScene {public Collider obstacle;public int queries;public int Raycast(Vector3 o,Vector3 d,RaycastHit[] hits,float distance,int mask,QueryTriggerInteraction q){queries++;if(obstacle==null)return 0;hits[0].collider=obstacle;return 1;}}
public class NetworkRunner {public float Time,DeltaTime=.1f;public PhysicsScene physics=new();public PhysicsScene GetPhysicsScene()=>physics;}
public class BattleFlag {public static BattleFlag Instance;public NetworkObject Object=new();public NetworkRunner Runner;
 public static void GetCapsule(Player p,out Vector3 center,out Vector3 axis,out float radius){center=p.position;axis=default;radius=.5f;}
'@
$segment = [regex]::Match($flag, '(?ms)^    internal static float SegmentDistanceSquared\([^{}]*?\)\r?\n    \{.*?^    \}').Value
if (!$segment) { throw 'Missing sweep geometry' }
$code += $segment + "`n}`n"
$code += @'
public class Player {
 public static List<Player> ActiveCombatants=new();public NetworkObject Object=new();public NetworkRunner Runner;
 public bool IsAlive=true,IsReturningToMap,eligible=true;public int ParticipantRef=1,applications;public float CurrentSpeed=60,received;public Vector3 position;
 public bool CanReceiveBattleItem(BattleItemType t)=>eligible&&Object.HasStateAuthority;
 public bool TryApplyBattleItem(BattleItemType t,float hp,float mult,float sec,float mp){if(!CanReceiveBattleItem(t))return false;applications++;received=t==BattleItemType.HealthPotion?hp:mp;return true;}
}
public class Settings {public float potionRestoreAmount=40,potionRespawnSeconds=8;}
public class Boss {public NetworkObject Object=new();public bool IsTargetable=true;public Settings Settings=new();public bool TryPotionPosition(Vector3 before,out Vector3 p){p=new(50,0,0);return true;}}
public class Item {
 public NetworkObject Object=new();public NetworkRunner Runner;public int ItemRound;public bool Consumed;public TickTimer RespawnAt;
 public Dictionary<int,int> previousPositions=new();public int checks;public void CheckPickup(){checks++;}
'@
$code += (Method $items 'FixedUpdateNetwork') + "`n}`n"
$code += @'
public class RaidPotionPickup {
 public NetworkObject Object=new();public NetworkRunner Runner;public Boss boss=new();public bool Available=true;public Vector3 PickupPosition;public TickTimer NextLocation;
 public Transform transform=new();public BattleItemType itemType=BattleItemType.HealthPotion;public float pickupRadius=1.5f;
 public struct Sample {public int id;public Vector3 position;}public Dictionary<int,Sample> previous=new();public RaycastHit[] sightHits=new RaycastHit[32];
 public void ResolveBoss(){}
'@
$code += (Method $potions 'FixedUpdateNetwork') + (Method $potions 'HasPickupSight') + "`n}`n"
$code += @'
public static class Tests {
 static int count;static void Check(bool pass,string name){if(!pass)throw new Exception(name);count++;}
 public static int Run(){
  var runner=new NetworkRunner();var item=new Item{Runner=runner};
  BattleManager.Instance=new();item.FixedUpdateNetwork();Check(item.checks==0,"normal match still requires flag");
  BattleManager.Instance=new RaidBattleManager();item.FixedUpdateNetwork();Check(item.checks==1,"raid items work without flag");
  BattleManager.Instance.IsGameplayActive=false;item.FixedUpdateNetwork();Check(item.checks==1,"raid countdown cannot collect");
  BattleManager.Instance.IsGameplayActive=true;item.Object.HasStateAuthority=false;item.FixedUpdateNetwork();Check(item.checks==1,"client cannot collect");item.Object.HasStateAuthority=true;
  BattleManager.Instance=new();NetworkGameManager.Instance=new(){IsDeathMatch=true};item.FixedUpdateNetwork();Check(item.checks==2,"deathmatch unchanged");NetworkGameManager.Instance=null;
  BattleFlag.Instance=new(){Runner=runner};item.FixedUpdateNetwork();Check(item.checks==3,"normal match with flag unchanged");
  BattleFlag.Instance.Runner=new();item.FixedUpdateNetwork();Check(item.checks==3,"foreign flag cannot enable normal items");
  var p=new Player{Runner=runner,position=new(-5,0,0),eligible=false};Player.ActiveCombatants.Add(p);
  var hp=new RaidPotionPickup{Runner=runner};hp.FixedUpdateNetwork();Check(hp.previous.Count==1&&hp.Available,"full HP retains sweep sample, does not consume");
  p.position=new(5,0,0);p.eligible=true;hp.FixedUpdateNetwork();Check(!hp.Available&&p.applications==1&&p.received==40,"fast pass collects once after HP changes");
  hp.FixedUpdateNetwork();Check(p.applications==1&&!hp.Available,"consumed potion cannot duplicate recovery");
  runner.Time=7.9f;hp.FixedUpdateNetwork();Check(!hp.Available,"respawn waits configured cooldown");
  runner.Time=8;hp.FixedUpdateNetwork();Check(hp.Available&&hp.PickupPosition.x==50&&hp.previous.Count==0,"respawn moves and resets sweep");
  var mp=new RaidPotionPickup{Runner=runner,itemType=BattleItemType.ManaPotion};p.position=new(1,0,0);runner.physics.obstacle=new();mp.FixedUpdateNetwork();Check(mp.Available,"wall blocks pickup");
  runner.physics.obstacle=new(){owner=p};mp.FixedUpdateNetwork();Check(!mp.Available&&p.received==40&&p.applications==2,"own character does not block MP recovery");
  var teleport=new RaidPotionPickup{Runner=runner};p.position=new(-50,0,0);teleport.FixedUpdateNetwork();p.position=new(50,0,0);teleport.FixedUpdateNetwork();Check(teleport.Available,"teleport cannot vacuum items");
  teleport.boss.IsTargetable=false;teleport.FixedUpdateNetwork();Check(teleport.previous.Count==0,"phase transition clears sweep");
  return count;
 }
}}
'@
Add-Type -TypeDefinition $code
"Raid pickup checks passed: $([RaidPickupChecks.Tests]::Run())"
