# Production charge and input methods; lightweight tick/input doubles, not a Fusion transport test.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$dash = Get-Content Assets/Script/Player.Dash.cs -Raw
$player = Get-Content Assets/Script/Player.cs -Raw
function Method([string]$name) {
    $match = [regex]::Match($player, '(?ms)^    private [^\r\n]+\b' + $name + '\([^\r\n]*\)\r?\n    \{.*?^    \}')
    if (!$match.Success) { throw "Missing production method: $name" }
    $match.Value
}
$code = @'
using System;
using Fusion;
using UnityEngine;
namespace UnityEngine {
 public static class Mathf {
  public static bool Approximately(float a,float b)=>Math.Abs(a-b)<.00001f;
  public static float Max(float a,float b)=>Math.Max(a,b);
  public static int Max(int a,int b)=>Math.Max(a,b);
  public static int RoundToInt(float a)=>(int)Math.Round(a);
  public static int Min(int a,int b)=>Math.Min(a,b);
  public static float Min(float a,float b)=>Math.Min(a,b);
  public static float Clamp01(float a)=>Math.Clamp(a,0,1);
  public static int Clamp(int a,int min,int max)=>Math.Clamp(a,min,max);
 }
}
namespace Fusion {
 public sealed class NetworkedAttribute:Attribute{}
 public struct TickTimer {
  public double End; public bool IsRunning;
  public static TickTimer None=>default;
  public static TickTimer CreateFromSeconds(Runner r,float s)=>new TickTimer{End=r.Time+s,IsRunning=true};
  public bool Expired(Runner r)=>IsRunning && r.Time>=End;
  public float? RemainingTime(Runner r)=>IsRunning?(float)(End-r.Time):(float?)null;
 }
}
public class Runner { public double Time; public float DeltaTime=1f/60; }
public class Obj { public bool IsValid=true,HasStateAuthority=true; }
public class BattleManager {public static BattleManager Instance=new();public bool IsGameplayActive=true;}
public enum PerkId {Tailwind,FirstWind,BrakingRune}
public enum BattleItemType {HealthPotion,ManaPotion,Booster}
public enum BattleSound {HealthPotion,BoosterPickup}
public class FlightCombatTable { public static FlightCombatTable Default=new(); public float dashRechargeSeconds=4,dashWindupSeconds; }
public enum PlayerInputButton {Decelerate,Boost}
public struct NetworkButtons {
 public int Bits;
 public bool IsSet(PlayerInputButton b)=>(Bits&(1<<(int)b))!=0;
 public bool WasPressed(NetworkButtons old,PlayerInputButton b)=>IsSet(b)&&!old.IsSet(b);
}
public partial class Player {
 public float ManaBuffDashRechargeMultiplier=1f;
 public Runner Runner=new(); public Obj Object=new();
 public bool IsAlive=true;
 public float GustBonus,GuardSeconds;
 public bool IsSpawnProtected,HasFestivalGuard;
 public float SpawnShieldVisibility;
 public int AcceptedHits;
 public float Tailwind,Speed=26,GlideElapsed,GlideStartSpeed,glideStopSeconds=1.5f,burstBoostSeconds=2;
 public int SpeedStage=2;
 public bool boostNeedsRelease,IsBound,IsHitStunned,IsBoosting;
 public TickTimer BurstBoostTimer,DashWindupTimer; public float DashWindupDuration; public bool IsReturningToMap,CobraActive;
 public NetworkButtons previousButtons;
 public bool UsesAutomaticCruise=true,HasItemSpeedBoost;
 public float ItemBoostMultiplier=1,ItemSpeedBoostRemaining;
 public TickTimer ItemBoostTimer;
 public int ItemSounds;
 bool CanReceiveBattleItem(BattleItemType type)=>true;
 bool IsPositiveFiniteItemValue(float value)=>value>0 && !float.IsInfinity(value) && !float.IsNaN(value);
 void RestoreHealth(float amount){} void RestoreAp(float amount){} void RPC_ManaPotionPickup(){}
 void RPC_PlayBattleSound(BattleSound sound){ItemSounds++;}
 float CommonAmount(PerkId id)=>id==PerkId.Tailwind?Tailwind:id==PerkId.FirstWind?GustBonus:GuardSeconds;
 bool TimerIsActive(TickTimer timer)=>timer.IsRunning && !timer.Expired(Runner);
 public Player Snapshot(){var p=(Player)MemberwiseClone();p.Runner=new Runner{Time=Runner.Time,DeltaTime=Runner.DeltaTime};return p;}
 public void Input(bool held,bool allow=true){var b=new NetworkButtons{Bits=held?2:0};ProcessCruiseInput(b,allow);previousButtons=b;}
 public void Advance(double time){Runner.Time=time;TickDashRecharge();}
 __METHODS__
 __PROTECTION__
 __DAMAGE_GATES__
 static int checks;
 static void Check(bool pass,string name){checks++;if(!pass)throw new Exception(name);}
 public static string Run(){
  FlightCombatTable.Default.dashWindupSeconds=.08f;
  var windup=new Player{GuardSeconds=1};windup.ResetDashCharges();windup.Input(true);
  Check(windup.DashCharges==2 && !windup.IsBoosting && windup.SpeedStage==2,"Preparation consumes one charge but keeps cruise speed");
  Check(!windup.IsDashGuardActive && !windup.BurstBoostTimer.IsRunning,"Boost duration and guard do not start during compression");
  windup.Advance(.04);windup.Input(false);windup.Input(true);
  Check(windup.DashCharges==2 && !windup.IsBoosting,"Repeated press during preparation cannot spend again");
  var preparing=windup.Snapshot();
  windup.Advance(.081);windup.Input(false);
  Check(windup.IsBoosting && windup.SpeedStage==3 && !windup.DashWindupTimer.IsRunning,"Release starts acceleration even after Shift released");
  Check(Math.Abs(windup.BurstBoostTimer.End-2.081)<.001 && Math.Abs(windup.DashGuardTimer.End-1.081)<.001,"Full boost and guard durations begin at actual release");
  preparing.Advance(.081);preparing.Input(false);
  Check(preparing.BurstBoostTimer.End==windup.BurstBoostTimer.End && preparing.DashCharges==windup.DashCharges,"Restoring a windup snapshot releases deterministically");
  foreach(int reason in new[]{0,1,2,3,4}) {
   var cancel=new Player();cancel.ResetDashCharges();cancel.Input(true);
   if(reason==0)cancel.IsBound=true;if(reason==1)cancel.IsHitStunned=true;
   if(reason==2)cancel.IsAlive=false;if(reason==3)cancel.IsReturningToMap=true;if(reason==4)cancel.CobraActive=true;
   cancel.Advance(.1);cancel.Input(false);
   Check(!cancel.DashWindupTimer.IsRunning && !cancel.IsBoosting,"Interrupted windup never releases: "+reason);
  }
  var brake=new Player();brake.ResetDashCharges();brake.Input(true);
  brake.ProcessCruiseInput(new NetworkButtons{Bits=1},true);brake.Advance(.2);brake.Input(false);
  Check(!brake.IsBoosting && !brake.DashWindupTimer.IsRunning,"Braking cancels pending acceleration");
  var pickup=new Player();pickup.ResetDashCharges();pickup.Input(true);pickup.Advance(.04);
  pickup.TryApplyBattleItem(BattleItemType.Booster,0,1.5f,5);
  Check(pickup.IsBoosting && !pickup.DashWindupTimer.IsRunning,"Pickup remains immediate and cancels pending release");
  double pickupEnd=pickup.BurstBoostTimer.End;pickup.Advance(.2);pickup.Input(false);
  Check(pickup.BurstBoostTimer.End==pickupEnd,"Pending Shift cannot re-trigger a pickup boost");
  var bot=new Player();bot.ResetDashCharges();bot.UpdateHeldBoost(true);bot.Advance(.081);bot.UpdateHeldBoost(false);
  Check(bot.IsBoosting && bot.DashCharges==2,"Bot releases the same scheduled dash");
  FlightCombatTable.Default.dashWindupSeconds=0;
  var p=new Player();p.ResetDashCharges();Check(p.DashCharges==3,"Three starting charges");
  p.Input(true);Check(p.DashCharges==2 && p.IsBoosting,"Shift spends one charge");
  Check(Math.Abs(p.DashRechargeTimer.End-4)<.001,"Recharge starts immediately");
  for(int i=1;i<=180;i++){p.Advance(i/60.0);p.Input(true);}
  Check(p.DashCharges==2,"Holding key does not repeatedly consume charges");
  Check(Math.Abs(p.DashRechargeProgress-.75f)<.001,"Three seconds fills 75 percent");
  p.Input(false);p.Input(true);Check(p.DashCharges==1,"Second press uses next charge");
  Check(Math.Abs(p.DashRechargeTimer.End-4)<.001,"Additional consumption preserves recharge progress");
  p.Advance(4);Check(p.DashCharges==2 && Math.Abs(p.DashRechargeTimer.End-8)<.001,"One charge restored after four seconds");
  p.Advance(8);Check(p.DashCharges==3 && !p.DashRechargeTimer.IsRunning,"Recharge stops at full");
  p=new Player();p.ResetDashCharges();p.TryConsumeDashCharge();p.TryConsumeDashCharge();p.TryConsumeDashCharge();
  Check(p.DashCharges==0 && !p.TryConsumeDashCharge(),"Empty charges cannot be spent");
  p.Input(true);Check(!p.IsBoosting,"No dash at zero charges");
  p.Advance(4);Check(p.DashCharges==1,"Empty pool refills one at a time");
  p.Advance(8);Check(p.DashCharges==2,"Second sequential recharge");
  p.Advance(12);Check(p.DashCharges==3,"Full recharge takes twelve seconds");
  p=new Player();p.ResetDashCharges();p.Input(true);p.Advance(.5);p.Input(false);p.Input(true);
  Check(p.DashCharges==2,"Cannot stack a dash during an active burst");
  int count=p.DashCharges;
  Check(p.TryApplyBattleItem(BattleItemType.Booster,0,1.5f,5) && p.DashCharges==count && p.ItemSounds==1,
   "Real booster pickup path never consumes dash charges");
  Check(p.ItemBoostTimer.End==p.BurstBoostTimer.End,"Pickup shares the Shift burst timer");
  p=new Player{IsBound=true};p.ResetDashCharges();p.Input(true);Check(p.DashCharges==3,"Binding prevents spending");
  p.IsBound=false;p.Input(false);p.Input(true,false);Check(p.DashCharges==3,"Suppressed input prevents spending");
  p=new Player{Tailwind=.3f};p.ResetDashCharges();p.Input(true);Check(Math.Abs(p.DashRechargeDuration-2.8f)<.001,"Tailwind perk reduces recharge time");
  p=new Player();p.ResetDashCharges();p.Input(true);p.Advance(1);var snapshot=p.Snapshot();
  for(int i=61;i<=720;i++){p.Advance(i/60.0);p.Input(i%150==0);}
  for(int replay=0;replay<3;replay++){
   var q=snapshot.Snapshot();for(int i=61;i<=720;i++){q.Advance(i/60.0);q.Input(i%150==0);}
   Check(q.DashCharges==p.DashCharges && Math.Abs(q.DashRechargeTimer.End-p.DashRechargeTimer.End)<.001,"Snapshot replay preserves charges and recharge");
  }
  foreach(int rate in new[]{30,60,120}){
   p=new Player();p.ResetDashCharges();p.Input(true);
   for(int i=1;i<4*rate;i++)p.Advance((double)i/rate);
   Check(p.DashCharges==2,"Never early recharge at tick rate "+rate);
   p.Advance(4);Check(p.DashCharges==3,"Four second recharge at tick rate "+rate);
  }
  p=new Player();p.ResetDashCharges();p.UpdateHeldBoost(true);
  Check(p.DashCharges==2 && p.IsBoosting,"Bot/legacy flight also uses charge pool");
  foreach(int capacity in new[]{2,3,4,5}) {
   p=new Player();p.ResetDashCharges(capacity);
   Check(p.MaximumDashCharges==capacity && p.DashCharges==capacity,"Equipment capacity starts full: "+capacity);
   for(int i=0;i<capacity;i++)Check(p.TryConsumeDashCharge(),"Can consume equipment charge "+i);
   Check(!p.TryConsumeDashCharge(),"Cannot exceed equipment capacity");
   p.Advance(2);var copy=p.Snapshot();
   Check(copy.MaximumDashCharges==capacity && Math.Abs(copy.DashRechargeProgress-.5f)<.001,"Snapshot retains capacity and recharge");
   for(int i=1;i<=capacity;i++){p.Advance(i*4);Check(p.DashCharges==i,"Sequential equipment recharge");}
   p.Advance(100);Check(p.DashCharges==capacity && !p.DashRechargeTimer.IsRunning,"Stops at equipment capacity");
   p.ResetDashCharges(2);Check(p.DashCharges==2 && p.MaximumDashCharges==2,"Re-equipping smaller broom resets pool");
   p.ResetDashCharges(5);Check(p.DashCharges==5 && p.MaximumDashCharges==5,"Re-equipping larger broom resets pool");
  }
  p=new Player();p.ResetDashCharges(5);p.TryConsumeDashCharge();p.Advance(2);
  p.GustBonus=1;p.RefreshDashPerks(false);
  Check(p.MaximumDashCharges==6 && p.DashCharges==5,"Gust adds one ready charge to robot");
  Check(Math.Abs(p.DashRechargeProgress-.5f)<.001,"Gust preserves partial recharge progress");
  p.RefreshDashPerks(false);Check(p.DashCharges==5,"Repeated stat refresh cannot grant charges");
  p.Advance(4);Check(p.DashCharges==6,"Sixth charge fully rechargeable");
  p.TryConsumeDashCharge();p.ResetDashCharges(5);p.RefreshDashPerks(true);
  Check(p.DashCharges==6 && p.MaximumDashCharges==6,"Respawn retains perk capacity and starts full");
  var gustCopy=p.Snapshot();Check(gustCopy.MaximumDashCharges==6,"Snapshot carries replicated perk bonus");
  p.GustBonus=0;p.RefreshDashPerks(false);Check(p.DashCharges==5 && p.MaximumDashCharges==5,"Removing perk clamps pool");
  p=new Player{GuardSeconds=1};p.ResetDashCharges();p.Input(true);
  Check(p.IsDashGuardActive,"Successful dash starts ward");
  p.Advance(.999);Check(p.IsDashGuardActive,"Ward covers first second");
  p.Input(false);p.Input(true);Check(Math.Abs(p.DashGuardTimer.End-1)<.001,"Rejected overlapping dash does not extend ward");
  p.Advance(1);Check(!p.IsDashGuardActive && p.IsBoosting,"Ward expires before two-second dash ends");
  p.Advance(2.1);p.Input(false);p.Input(true);Check(p.IsDashGuardActive,"Next successful dash grants new ward");
  p.IsAlive=false;Check(!p.IsDashGuardActive,"Dead player cannot retain visible ward");
  p.IsAlive=true;p.ResetDashCharges();Check(!p.IsDashGuardActive,"Respawn clears old ward timer");
  p=new Player();p.ResetDashCharges();p.Input(true);Check(!p.IsDashGuardActive,"No ward without perk");
  p=new Player{GuardSeconds=1,IsBound=true};p.ResetDashCharges();p.Input(true);Check(!p.IsDashGuardActive,"Blocked dash grants no ward");
  p=new Player{GuardSeconds=1};p.ResetDashCharges();while(p.TryConsumeDashCharge()){}p.Input(true);
  Check(!p.IsDashGuardActive,"Empty pool grants no ward");
  p=new Player{GuardSeconds=1,HasFestivalGuard=true};p.ResetDashCharges();p.Input(true);
  p.DamageGate(100);p.MagicGate();
  Check(p.AcceptedHits==0,"Ward rejects both direct damage and magic/status hit paths");
  Check(p.HasOrbitingShield && p.OrbitingShieldVisibility==1,"Ward uses full-opacity orbiting shield");
  p.HasFestivalGuard=false;p.Advance(1);p.DamageGate(100);p.MagicGate();
  Check(p.AcceptedHits==2 && !p.HasOrbitingShield,"Damage resumes and ward shield ends at one second");
  p.HasFestivalGuard=true;Check(p.HasOrbitingShield,"Festival shield remains after ward expires");
  p.HasFestivalGuard=false;p.IsSpawnProtected=true;p.DamageGate(100);
  Check(p.AcceptedHits==2 && p.HasOrbitingShield,"Spawn protection still works independently");
  FlightCombatTable.Default.dashRechargeSeconds=8;
  p=new Player();p.ResetDashCharges();p.TryConsumeDashCharge();
  Check(Math.Abs(p.DashRechargeDuration-8)<.001,"New baseline is eight seconds");
  p.Advance(2);float progress=p.DashRechargeProgress;
  p.ManaBuffDashRechargeMultiplier=4;p.Advance(2);
  Check(Math.Abs(p.DashRechargeDuration-2)<.001&&Math.Abs(p.DashRechargeProgress-progress)<.001,"Zone entry quadruples speed without losing progress");
  p.Advance(2.5);Check(Math.Abs(p.DashRechargeProgress-.5f)<.001,"Half a second inside earns quarter charge");
  p.ManaBuffDashRechargeMultiplier=1;p.Advance(2.5);
  Check(Math.Abs(p.DashRechargeDuration-8)<.001&&Math.Abs(p.DashRechargeProgress-.5f)<.001,"Zone exit restores normal speed and preserves earned half charge");
  p.Advance(6.5);Check(p.DashCharges==3,"Remaining half charge takes four seconds outside");
  p=new Player{ManaBuffDashRechargeMultiplier=4};p.ResetDashCharges();
  p.TryConsumeDashCharge();p.TryConsumeDashCharge();p.TryConsumeDashCharge();
  for(int i=1;i<=3;i++){p.Advance(i*2);Check(p.DashCharges==i,"Zone sequential recharge every two seconds");}
  FlightCombatTable.Default.dashRechargeSeconds=4;
  return checks+" dash checks passed";
 }
}
'@
$methods = @('ProcessCruiseInput','BeginDashWindup','TickDashWindup','StartCruiseBoost','UpdateHeldBoost') | ForEach-Object { Method $_ }
$items=Get-Content Assets/Script/Player.Items.cs -Raw
$itemMethod=[regex]::Match($items,'(?ms)^    internal bool TryApplyBattleItem\([^{}]*?\)\r?\n    \{.*?^    \}').Value
if(!$itemMethod){throw 'Missing production item method'}
$methods += $itemMethod
$code = $code.Replace('__METHODS__', ($methods -join "`n"))
$flight = Get-Content Assets/Script/Player.FlightCombat.cs -Raw
$protection = [regex]::Match($flight,'(?m)^    public bool IsDamageProtected[^\r\n]+\r?\n    public bool HasOrbitingShield[^\r\n]+\r?\n    public float OrbitingShieldVisibility[^\r\n]+').Value
if (!$protection) { throw 'Missing production shield/protection properties' }
# Execute the actual entry guards before damage, binding, parry and festival-charge consumption.
$damageGuard = [regex]::Match($player,'(?s)public void TakeDamage\(.*?\)\s*\{(.*?)\s*if \(canBeParried').Groups[1].Value
$magicGuard = [regex]::Match($player,'(?s)public void ReceiveMagicHit\(.*?\)\s*\{(.*?)\s*// Binding also').Groups[1].Value
if (!$damageGuard -or !$magicGuard) { throw 'Missing production damage entry guards' }
$code = $code.Replace('__PROTECTION__',$protection).Replace('__DAMAGE_GATES__', "public void DamageGate(float damage) {$damageGuard AcceptedHits++;}`npublic void MagicGate() {$magicGuard AcceptedHits++;}")
$code += "`n" + ($dash -replace '(?m)^using [^\r\n]+;\r?\n','')
Add-Type -TypeDefinition $code
[Player]::Run()
if ($player -notmatch 'ResetDashCharges\(broomStats.DashChargeCapacity\);' -or $player -notmatch 'TickDashRecharge\(\);') { throw 'Equipment charge lifecycle not wired into Player' }
$prefab=Get-Content Assets/UI/Prefab/BattleUI.prefab -Raw
if (($prefab | Select-String -Pattern 'm_Name: Dash[123456]\r?\n' -AllMatches).Matches.Count -ne 6) { throw 'Expected six authored slider segments including Gust bonus' }
if ($prefab -notmatch 'm_Name: DashGauge' -or $prefab -notmatch 'm_Father: \{fileID: 5902421080451873667\}') { throw 'Dash gauge is not attached to its authored aim parent' }
'Authored gauge and charge lifecycle checks passed'
