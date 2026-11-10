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
  public static float Max(float a,float b)=>Math.Max(a,b);
  public static int Min(int a,int b)=>Math.Min(a,b);
  public static float Min(float a,float b)=>Math.Min(a,b);
  public static float Clamp01(float a)=>Math.Clamp(a,0,1);
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
public class Obj { public bool IsValid=true; }
public enum PerkId {Tailwind}
public enum BattleItemType {HealthPotion,ManaPotion,Booster}
public enum BattleSound {HealthPotion,BoosterPickup}
public class FlightCombatTable { public static FlightCombatTable Default=new(); public float dashRechargeSeconds=4; }
public enum PlayerInputButton {Decelerate,Boost}
public struct NetworkButtons {
 public int Bits;
 public bool IsSet(PlayerInputButton b)=>(Bits&(1<<(int)b))!=0;
 public bool WasPressed(NetworkButtons old,PlayerInputButton b)=>IsSet(b)&&!old.IsSet(b);
}
public partial class Player {
 public Runner Runner=new(); public Obj Object=new();
 public float Tailwind,Speed=26,GlideElapsed,GlideStartSpeed,glideStopSeconds=1.5f,burstBoostSeconds=2;
 public int SpeedStage=2;
 public bool boostNeedsRelease,IsBound,IsHitStunned,IsBoosting;
 public TickTimer BurstBoostTimer;
 public NetworkButtons previousButtons;
 public bool UsesAutomaticCruise=true,HasItemSpeedBoost;
 public float ItemBoostMultiplier=1,ItemSpeedBoostRemaining;
 public TickTimer ItemBoostTimer;
 public int ItemSounds;
 bool CanReceiveBattleItem(BattleItemType type)=>true;
 bool IsPositiveFiniteItemValue(float value)=>value>0 && !float.IsInfinity(value) && !float.IsNaN(value);
 void RestoreHealth(float amount){} void RestoreAp(float amount){} void RPC_ManaPotionPickup(){}
 void RPC_PlayBattleSound(BattleSound sound){ItemSounds++;}
 float CommonAmount(PerkId id)=>Tailwind;
 bool TimerIsActive(TickTimer timer)=>timer.IsRunning && !timer.Expired(Runner);
 public Player Snapshot(){var p=(Player)MemberwiseClone();p.Runner=new Runner{Time=Runner.Time,DeltaTime=Runner.DeltaTime};return p;}
 public void Input(bool held,bool allow=true){var b=new NetworkButtons{Bits=held?2:0};ProcessCruiseInput(b,allow);previousButtons=b;}
 public void Advance(double time){Runner.Time=time;TickDashRecharge();}
 __METHODS__
 static int checks;
 static void Check(bool pass,string name){checks++;if(!pass)throw new Exception(name);}
 public static string Run(){
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
  return checks+" dash checks passed";
 }
}
'@
$methods = @('ProcessCruiseInput','StartCruiseBoost','UpdateHeldBoost') | ForEach-Object { Method $_ }
$items=Get-Content Assets/Script/Player.Items.cs -Raw
$itemMethod=[regex]::Match($items,'(?ms)^    internal bool TryApplyBattleItem\([^{}]*?\)\r?\n    \{.*?^    \}').Value
if(!$itemMethod){throw 'Missing production item method'}
$methods += $itemMethod
$code = $code.Replace('__METHODS__', ($methods -join "`n"))
$code += "`n" + ($dash -replace '(?m)^using [^\r\n]+;\r?\n','')
Add-Type -TypeDefinition $code
[Player]::Run()
if ($player -notmatch 'ResetDashCharges\(\);' -or $player -notmatch 'TickDashRecharge\(\);') { throw 'Charge lifecycle not wired into Player' }
$prefab=Get-Content Assets/UI/Prefab/BattleUI.prefab -Raw
if (($prefab | Select-String -Pattern 'm_Name: Dash[123]\r?\n' -AllMatches).Matches.Count -ne 3) { throw 'Expected three authored slider segments' }
if ($prefab -notmatch 'm_Name: DashGauge' -or $prefab -notmatch 'm_Father: \{fileID: 8323471176851699074\}') { throw 'Dash gauge is not attached to moving aim' }
'Authored gauge and charge lifecycle checks passed'
