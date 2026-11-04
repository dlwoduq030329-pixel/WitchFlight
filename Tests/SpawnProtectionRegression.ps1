$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$flight = Get-Content Assets/Script/Player.FlightCombat.cs -Raw
$player = Get-Content Assets/Script/Player.cs -Raw
$properties = $flight.Substring($flight.IndexOf('    [Networked] private TickTimer SpawnProtectionTimer'), $flight.IndexOf('    [Networked] private TickTimer RamCooldown') - $flight.IndexOf('    [Networked] private TickTimer SpawnProtectionTimer'))
$countdown = [regex]::Match($player, '(?s)            if \(Object.HasStateAuthority && IsCountdownProtected\).*?\n            \}').Value
$start = [regex]::Match($player, '(?s)        if \(Object.HasStateAuthority && SpawnProtectionPending\).*?\n        \}').Value
if (!$countdown -or !$start) { throw 'Protection state transitions not found' }
$source = @'
using System;
public class NetworkedAttribute:Attribute {}
public class Runner { public float now; }
public struct TickTimer {
 public float end; public static TickTimer None=>default;
 public static TickTimer CreateFromSeconds(Runner r,float seconds)=>new(){end=r.now+seconds};
 public float? RemainingTime(Runner r)=>end>0?Math.Max(0,end-r.now):null;
}
public static class Mathf {public static float Max(float a,float b)=>Math.Max(a,b);public static float Clamp01(float v)=>Math.Clamp(v,0,1);}
public enum BattleStartPhase {Countdown,Playing}
public class BattleManager {public static BattleManager Instance=new();public BattleStartPhase Phase;public float PhaseRemainingSeconds=3;}
public class FlightCombatTable {public static FlightCombatTable Default=new();public float spawnProtectionSeconds=2.5f;}
public class NetworkObject {public bool IsValid=true,HasStateAuthority=true;}
public class Player {
 public NetworkObject Object=new();public Runner Runner=new();public bool IsAlive=true;
 bool TimerIsActive(TickTimer timer)=>(timer.RemainingTime(Runner)??0)>0;
'@ + $properties + @'
 public void Spawn(){SpawnProtectionPending=true;SpawnProtectionTimer=TickTimer.None;}
 public void Step(bool gameplay){if(!gameplay){
'@ + $countdown + @'
 return;}
'@ + $start + @'
 }
}
public static class ProtectionChecks {
 static int n;static void Check(bool ok,string message){if(!ok)throw new Exception(message);n++;}
 public static int Run(){
  var p=new Player();p.Spawn();p.Step(false);
  Check(p.IsSpawnProtected&&p.SpawnShieldVisibility==1,"countdown protects and displays full initial shield opacity");
  BattleManager.Instance.PhaseRemainingSeconds=1.5f;p.Step(false);
  Check(p.IsSpawnProtected&&p.SpawnShieldVisibility==.5f,"countdown shield fades while immunity remains");
  BattleManager.Instance.Phase=BattleStartPhase.Playing;p.Step(true);
  Check(!p.IsSpawnProtected&&p.SpawnShieldVisibility==0,"start immediately ends countdown shield and immunity");
  p.Runner.now=10;p.Spawn();p.Step(true);
  Check(p.IsSpawnProtected&&p.SpawnShieldVisibility==1,"mid-match respawn gets its own full protection");
  p.Runner.now=11.25f;Check(p.IsSpawnProtected&&p.SpawnShieldVisibility==.5f,"respawn visual follows replicated remaining timer");
  p.Runner.now=12.5f;Check(!p.IsSpawnProtected&&p.SpawnShieldVisibility==0,"respawn protection ends after 2.5 seconds");
  BattleManager.Instance.Phase=BattleStartPhase.Countdown;p.Step(false);
  Check(p.IsSpawnProtected,"later countdown is protected as well");
  BattleManager.Instance.Phase=BattleStartPhase.Playing;p.Step(true);Check(!p.IsSpawnProtected,"later round does not inherit respawn immunity");
  return n;
 }
}
'@
Add-Type -TypeDefinition $source
"Countdown and respawn protection checks passed: $([ProtectionChecks]::Run())"
