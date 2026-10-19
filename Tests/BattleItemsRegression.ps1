# Real item/player method bodies with lightweight Fusion doubles, not a multiplayer play test.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$items = Get-Content Assets/Script/Player.Items.cs -Raw
$player = Get-Content Assets/Script/Player.cs -Raw
$pickup = Get-Content Assets/Script/BattleItemPickup.cs -Raw
$flag = Get-Content Assets/Script/BattleFlag.cs -Raw
function Method([string]$source, [string]$name) {
    $m = [regex]::Match($source, '(?ms)^    (?:private|public|internal) (?:static |override )?[\w<>]+ '+$name+'\([^{}]*?\)\r?\n    \{.*?^    \}')
    if (!$m.Success) { throw "Missing method $name" }
    $m.Value -replace '^    (?:private|internal) ', '    public ' -replace 'public override ', 'public '
}
$source = @'
using System;
using System.Collections.Generic;
namespace ItemChecks {
public static class Mathf {
 public static float Max(float a,float b)=>Math.Max(a,b);
 public static float Min(float a,float b)=>Math.Min(a,b);
 public static float Clamp01(float a)=>Math.Clamp(a,0,1);
}
public struct Vector3 {
 public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
 public float sqrMagnitude=>x*x+y*y+z*z;
 public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
 public static Vector3 operator+(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
 public static Vector3 operator-(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
 public static Vector3 operator*(Vector3 a,float f)=>new(a.x*f,a.y*f,a.z*f);
}
public class NetworkObject {public bool IsValid=true,HasStateAuthority=true;}
public class NetworkRunner {public float Time;}
public struct TickTimer {
 public bool IsRunning;public float End;
 public static TickTimer None=>default;
 public bool Expired(NetworkRunner r)=>IsRunning&&r.Time>=End;
 public float? RemainingTime(NetworkRunner r)=>IsRunning?Math.Max(0,End-r.Time):null;
 public static TickTimer CreateFromSeconds(NetworkRunner r,float duration)=>new(){IsRunning=true,End=r.Time+duration};
}
public class BattleManager {public static BattleManager Instance=new();public bool IsGameplayActive=true;public int RoundNumber=1;}
public partial class BattleFlag {public static BattleFlag Instance;public NetworkObject Object=new();public NetworkRunner Runner;
'@
$source += (Method $flag 'SegmentDistanceSquared') + "`n}`n"
$source += [regex]::Match($pickup, 'public enum BattleItemType[^\r\n]+').Value
$source += "`n" + (($items.Substring($items.IndexOf('public partial class Player'))) -replace '\[Networked\]\s*','' -replace '\bprivate\b','public')
$source += @'
public partial class Player {
 public NetworkObject Object=new();public NetworkRunner Runner=new();
 public bool IsAlive=true,IsTestBot,IsReturningToMap;
 public float NowHp=50,MaxHp=100,NowAp=20,MaxAp=100;public int HealthNotifications, ManaNotifications;
 private void SyncHealthToPlayerData(){HealthNotifications++;}
 private void SyncApToPlayerData(){ManaNotifications++;}
'@
$source += (Method $player 'RestoreHealth') + (Method $player 'RestoreAp') + (Method $player 'TimerIsActive') + "`n}`n"
$source += @'
public class Pickup {
 public NetworkObject Object=new();public NetworkRunner Runner;
 public bool Consumed;public int ItemRound;public TickTimer RespawnAt;
 public BattleItemType itemType=BattleItemType.HealthPotion;
 public float healAmount=40,manaRestoreAmount=40,speedMultiplier=1.3f,boostDurationSeconds=5,respawnSeconds=20;
 public Dictionary<int,int> previousPositions=new();public int ContactChecks;
 private void CheckPickup(){ContactChecks++;}
'@
$source += (Method $pickup 'FixedUpdateNetwork') + (Method $pickup 'TryCollect') + "`n}`n"
$source += @'
public static class Tests {
 static int count;static void Check(bool v,string name){if(!v)throw new Exception(name);count++;}
 static bool Near(float a,float b)=>Math.Abs(a-b)<.001f;
 public static int Run(){
  var p=new Player();var runner=p.Runner;
  BattleFlag.Instance=new(){Runner=runner};
  Check(!p.HasItemSpeedBoost&&p.GetItemSpeedMultiplier()==1,"no implicit boost before pickup");
  Check(p.TryApplyBattleItem(BattleItemType.HealthPotion,40,1.3f,5)&&p.NowHp==90,"potion heals configured amount");
  Check(p.HealthNotifications==1,"potion uses existing HP notification path");
  Check(p.TryApplyBattleItem(BattleItemType.HealthPotion,40,1.3f,5)&&p.NowHp==100,"heal clamps to maximum");
  Check(!p.TryApplyBattleItem(BattleItemType.HealthPotion,40,1.3f,5)&&p.HealthNotifications==2,"full HP does not waste potion");
  Check(p.TryApplyBattleItem(BattleItemType.ManaPotion,40,1.3f,5,40)&&p.NowAp==60,"MP potion restores configured MP");
  Check(p.ManaNotifications==1&&p.NowHp==100&&!p.HasItemSpeedBoost,"MP pickup updates mana without healing or boosting");
  Check(p.TryApplyBattleItem(BattleItemType.ManaPotion,40,1.3f,5,80)&&p.NowAp==100,"MP restore clamps to maximum");
  Check(!p.TryApplyBattleItem(BattleItemType.ManaPotion,40,1.3f,5,40)&&p.ManaNotifications==2,"full MP does not waste potion");
  p.NowAp=20;
  foreach(float bad in new[]{0f,-1f,float.NaN,float.PositiveInfinity})
   Check(!p.TryApplyBattleItem(BattleItemType.ManaPotion,40,1.3f,5,bad)&&p.NowAp==20,"invalid MP restoration rejected");
  p.Object.HasStateAuthority=false;
  Check(!p.TryApplyBattleItem(BattleItemType.ManaPotion,40,1.3f,5,40)&&p.NowAp==20,"client cannot restore MP");
  p.Object.HasStateAuthority=true;p.IsAlive=false;
  Check(!p.CanReceiveBattleItem(BattleItemType.ManaPotion),"dead player cannot collect MP");p.IsAlive=true;
  BattleManager.Instance.IsGameplayActive=false;
  Check(!p.CanReceiveBattleItem(BattleItemType.ManaPotion),"intermission cannot collect MP");BattleManager.Instance.IsGameplayActive=true;
  p.NowHp=30;
  foreach(float bad in new[]{0f,-1f,float.NaN,float.PositiveInfinity})
   Check(!p.TryApplyBattleItem(BattleItemType.HealthPotion,bad,1.3f,5)&&p.NowHp==30,"invalid healing rejected");
  p.Object.HasStateAuthority=false;
  Check(!p.TryApplyBattleItem(BattleItemType.HealthPotion,40,1.3f,5)&&!p.TryApplyBattleItem(BattleItemType.Booster,40,1.3f,5),"client cannot apply item effects");
  p.Object.HasStateAuthority=true;p.IsAlive=false;
  Check(!p.CanReceiveBattleItem(BattleItemType.HealthPotion),"dead player rejected");p.IsAlive=true;
  p.IsTestBot=true;Check(!p.CanReceiveBattleItem(BattleItemType.Booster),"training dummy does not steal item");p.IsTestBot=false;
  p.IsReturningToMap=true;Check(!p.CanReceiveBattleItem(BattleItemType.Booster),"automatic boundary return cannot collect");p.IsReturningToMap=false;
  p.Object.IsValid=false;Check(!p.CanReceiveBattleItem(BattleItemType.HealthPotion),"unspawned player rejected");p.Object.IsValid=true;
  BattleManager.Instance.IsGameplayActive=false;Check(!p.CanReceiveBattleItem(BattleItemType.Booster),"intermission/countdown cannot collect");BattleManager.Instance.IsGameplayActive=true;
  Check(!p.CanReceiveBattleItem((BattleItemType)99),"unknown item type rejected");
  foreach(float bad in new[]{0f,1f,float.NaN,float.PositiveInfinity})
   Check(!p.TryApplyBattleItem(BattleItemType.Booster,40,bad,5),"invalid speed multiplier rejected");
  Check(!p.TryApplyBattleItem(BattleItemType.Booster,40,1.3f,0),"zero duration rejected");
  Check(!p.TryApplyBattleItem(BattleItemType.Booster,40,1.3f,float.PositiveInfinity),"infinite duration rejected");
  Check(p.TryApplyBattleItem(BattleItemType.Booster,40,1.3f,5)&&Near(p.GetItemSpeedMultiplier(),1.3f),"booster applies 30 percent");
  Check(p.ItemSpeedBoostRemaining==5&&p.NowHp==30,"booster lasts five seconds without healing");
  runner.Time=3;p.TryApplyBattleItem(BattleItemType.Booster,40,1.3f,5);
  Check(Near(p.GetItemSpeedMultiplier(),1.3f)&&p.ItemSpeedBoostRemaining==5,"repeated boost refreshes without multiplication");
  p.TryApplyBattleItem(BattleItemType.Booster,40,1.1f,1);
  Check(Near(p.GetItemSpeedMultiplier(),1.3f)&&p.ItemSpeedBoostRemaining==5,"weaker pickup cannot shorten or reduce bonus");
  runner.Time=8;Check(!p.HasItemSpeedBoost&&p.GetItemSpeedMultiplier()==1,"timer expiry restores normal multiplier");
  p.TryApplyBattleItem(BattleItemType.Booster,40,1.2f,7);p.ResetItemBoost();
  Check(!p.HasItemSpeedBoost&&p.ItemBoostMultiplier==1&&!p.ItemBoostTimer.IsRunning,"death/new-round reset removes boost");
  var item=new Pickup{Runner=runner};item.FixedUpdateNetwork();
  Check(item.ItemRound==1&&item.ContactChecks==1,"new-round item initialized and checked");
  item.previousPositions[1]=1;Check(item.TryCollect(p)&&p.NowHp==70&&item.Consumed,"server consumes and applies atomically");
  Check(item.previousPositions.Count==0&&item.RespawnAt.RemainingTime(runner)==20,"consumption clears sweep history and starts respawn");
  var enemy=new Player{Runner=runner};Check(!item.TryCollect(enemy)&&enemy.NowHp==50,"second claimant cannot collect consumed item");
  item.FixedUpdateNetwork();Check(item.ContactChecks==1,"consumed item skips contacts");
  runner.Time=28;item.FixedUpdateNetwork();Check(!item.Consumed&&item.ContactChecks==2,"respawn makes item collectible again");
  p.NowHp=100;Check(!item.TryCollect(p)&&!item.Consumed,"full HP leaves item available for opponent");
  item.respawnSeconds=0;Check(item.TryCollect(enemy),"zero respawn setting still allows first pickup");
  runner.Time=500;item.FixedUpdateNetwork();Check(item.Consumed&&!item.RespawnAt.IsRunning,"zero respawn waits until next round");
  BattleManager.Instance.RoundNumber=2;item.FixedUpdateNetwork();Check(!item.Consumed&&item.ItemRound==2,"new round restores used item");
  item.Object.HasStateAuthority=false;int prior=item.ContactChecks;item.FixedUpdateNetwork();
  Check(!item.TryCollect(enemy)&&item.ContactChecks==prior,"remote item does not consume or simulate contacts");item.Object.HasStateAuthority=true;
  enemy.Runner=new();Check(!item.TryCollect(enemy),"other runner cannot collect");enemy.Runner=runner;
  BattleManager.Instance.IsGameplayActive=false;item.previousPositions[1]=1;item.FixedUpdateNetwork();
  Check(item.ContactChecks==prior&&item.previousPositions.Count==0,"non-playing phase discards sweep history");
  BattleManager.Instance.IsGameplayActive=true;BattleFlag.Instance.Runner=new();item.FixedUpdateNetwork();
  Check(item.ContactChecks==prior,"unrelated runner flag cannot activate items");
  BattleFlag.Instance.Runner=runner;
  var mana=new Pickup{Runner=runner,itemType=BattleItemType.ManaPotion,manaRestoreAmount=17};
  mana.FixedUpdateNetwork();p.NowAp=100;
  Check(!mana.TryCollect(p)&&!mana.Consumed,"full MP keeps world potion available");
  p.NowAp=10;
  Check(mana.TryCollect(p)&&p.NowAp==27&&mana.Consumed,"world MP pickup uses serialized recovery amount");
  Check(!mana.TryCollect(enemy)&&enemy.NowAp==20,"second player cannot consume same MP pickup");
  runner.Time+=20;mana.FixedUpdateNetwork();
  Check(!mana.Consumed,"MP pickup respawns on authority timer");
  Check(mana.TryCollect(enemy)&&enemy.NowAp==37,"opponent can collect respawned MP pickup");
  BattleManager.Instance.RoundNumber++;mana.FixedUpdateNetwork();
  Check(!mana.Consumed,"new round resets consumed MP pickup");
  float crossed=BattleFlag.SegmentDistanceSquared(new(-2,0,0),new(2,0,0),new(0,-1,0),new(0,1,0),out var contact);
  Check(crossed==0&&Near(contact.x,0),"fast pass-through is caught between tick endpoints");
  Check(BattleFlag.SegmentDistanceSquared(new(-2,0,4),new(2,0,4),new(0,-1,0),new(0,1,0),out _) ==16,"distant path misses");
  Check(BattleFlag.SegmentDistanceSquared(new(0,1,0),new(0,1,0),new(0,-2,0),new(0,2,0),out _) ==0,"standing capsule can collect");
  Check(BattleFlag.SegmentDistanceSquared(new(0,0,3),new(0,0,3),new(0,0,0),new(0,0,0),out _) ==9,"degenerate sweep avoids division by zero");
  return count;
 }
}}
'@
Add-Type -TypeDefinition $source
"Battle item behavior checks passed: $([ItemChecks.Tests]::Run())"
foreach ($name in 'ItemBoostTimer','ItemBoostMultiplier') {
    if ($items -notmatch ('\[Networked\] private \w+ '+$name)) { throw "Item prediction state must be networked: $name" }
}
foreach ($method in 'InitPlayer','Die','FixedUpdateNetwork') {
    if (!(Method $player $method).Contains('ResetItemBoost();')) {throw "Missing item reset in $method"}
}
if (!(Method $pickup 'CheckPickup').Contains('sample.objectId == playerObject.Id') -or
    !(Method $pickup 'CheckPickup').Contains('maxSweep * maxSweep') -or
    !(Method $pickup 'CheckPickup').Contains('HasPickupSight(contact, point)')) {throw 'Missing respawn, teleport, or wall pickup protection'}
if (!(Method $pickup 'HasPickupSight').Contains('QueryTriggerInteraction.Ignore')) {throw 'Pickup sight must ignore trigger volumes'}
foreach ($prefabName in 'Booster','HP_Item','MP_Item') {
    $path="Assets/Item/$prefabName.prefab"
    $yaml=Get-Content $path -Raw
    $ids=@([regex]::Matches($yaml,'(?m)^--- !u!\d+ &(\d+)') | ForEach-Object {$_.Groups[1].Value})
    if (@($ids | Group-Object | Where-Object Count -gt 1).Count) {throw "Duplicate prefab IDs in $path"}
    if (!$yaml.Contains('guid: 03eab103bf384856bc0a59b42beca173') -or !$yaml.Contains('guid: e725a070cec140c4caffb81624c8c787') -or
        $yaml -notmatch 'NetworkedBehaviours:\s+- \{fileID: \d+\}' -or
        (Get-Content "$path.meta" -Raw) -notmatch 'FusionPrefab') {throw "Unregistered network item: $path"}
    $view=[regex]::Match($yaml,'visualRoot: \{fileID: (\d+)\}').Groups[1].Value
    if ($view -notin $ids) {throw "Missing model view: $path"}
    $collider=[regex]::Match($yaml,'(?ms)^--- !u!(?:64|135) &\d+\r?\n.*?(?=^--- !u!|\z)').Value
    if ($collider -notmatch 'm_Enabled: 0') {throw "Item still blocks flight: $path"}
    if ($prefabName -eq 'MP_Item' -and ($yaml -notmatch 'itemType: 2' -or $yaml -notmatch 'manaRestoreAmount: 40')) {
        throw 'MP prefab must select ManaPotion with the configured default recovery'
    }
}
'Network authority, lifecycle, pickup safeguards and saved prefab contracts passed.'
