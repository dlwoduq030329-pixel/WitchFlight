# Production area contact checks and MP regeneration; no Unity physics/transport emulation.
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$items=Get-Content (Join-Path $repo 'Assets/Script/Player.Items.cs') -Raw
$player=Get-Content (Join-Path $repo 'Assets/Script/Player.cs') -Raw
function Method($source,$name){
 $m=[regex]::Match($source,'(?ms)^    private void '+$name+'\([^\r\n]*\)\r?\n    \{.*?^    \}')
 if(!$m.Success){throw "Missing $name"};$m.Value.Replace('private void','public void')
}
$code=@'
using System;using System.Collections.Generic;
public class Obj {public bool IsValid=true,HasStateAuthority=true;}
public class Runner {public float DeltaTime=1;}
public class GameObject {public int layer=8;public bool activeInHierarchy=true;}
public class Bounds {public bool touching=true;public bool Intersects(Bounds other)=>touching&&other.touching;}
public class Collider {public bool isTrigger=true,enabled=true;public GameObject gameObject=new();public Bounds bounds=new();}
public static class Mathf {public static float Max(float a,float b)=>Math.Max(a,b);public static float Min(float a,float b)=>Math.Min(a,b);}
public class BattleManager {public static BattleManager Instance=new();public bool IsGameplayActive=true;}
public class FlightCombatTable {public static FlightCombatTable Default=new();public float manaBuffManaRecoveryMultiplier=2,manaBuffDashRechargeMultiplier=4;}
public class Stats {public bool IsChanneled;}
public enum PlayerInputButton {Lock}
public class Buttons {public bool held;public bool IsSet(PlayerInputButton b)=>held;}
public class Probe {
 public Obj Object=new();public Runner Runner=new();public bool networkSpawned=true,ManaBuffActive,IsAlive=true,IsReturningToMap;
 public List<Collider> manaBuffContacts=new();public int manaBuffLayer=8;public Collider characterController=new();
 public bool RainCharging,IsChanneling;public Stats SelectedMagicStats=new();public Buttons previousButtons=new();
 public float MaxAp=100,NowAp=10,ApRecoveryPerSecond=5,FestivalRecoveryBonus=1;
'@
foreach($name in 'HasManaBuff','ManaBuffManaRecoveryMultiplier','ManaBuffDashRechargeMultiplier'){
 $m=[regex]::Match($items,'(?ms)^    (?:private|public) (?:bool|float) '+$name+' =>.*?;')
 if(!$m.Success){throw "Missing $name"};$code+=$m.Value.Replace('private ','public ')
}
foreach($name in 'RegisterManaBuffContact','TickManaBuffArea'){$code+=Method $items $name}
$code+=Method $player 'RegenerateAp'
$code+="`n}`n"
$code+=@'
public static class Checks {
 static int n;static void Check(bool b,string m){if(!b)throw new Exception(m);n++;}
 public static int Run(){
  var p=new Probe();p.TickManaBuffArea();p.RegenerateAp();Check(!p.HasManaBuff&&p.NowAp==15,"normal regeneration outside");
  var a=new Collider();p.RegisterManaBuffContact(a);p.RegisterManaBuffContact(a);p.TickManaBuffArea();
  Check(p.manaBuffContacts.Count==1&&p.HasManaBuff,"contact deduplication and active state");
  p.RegenerateAp();Check(p.NowAp==25&&p.ManaBuffDashRechargeMultiplier==4,"MP doubles and dash rate quadruples");
  var b=new Collider();p.RegisterManaBuffContact(b);p.TickManaBuffArea();p.RegenerateAp();Check(p.NowAp==35,"overlapping zones do not stack");
  p.manaBuffContacts.Remove(a);p.TickManaBuffArea();Check(p.HasManaBuff,"leaving one overlapping zone keeps effect");
  b.enabled=false;p.TickManaBuffArea();Check(!p.HasManaBuff&&p.manaBuffContacts.Count==0,"disabled collider removes stale effect");
  p.RegisterManaBuffContact(a);a.bounds.touching=false;p.TickManaBuffArea();Check(!p.HasManaBuff,"teleport outside removes stale contact");
  a.bounds.touching=true;p.RegisterManaBuffContact(a);p.TickManaBuffArea();p.IsAlive=false;Check(!p.HasManaBuff,"death immediately hides effect");
  p.IsAlive=true;p.IsReturningToMap=true;Check(!p.HasManaBuff,"boundary return excludes area bonus");p.IsReturningToMap=false;
  p.networkSpawned=false;Check(!p.HasManaBuff,"unspawned presentation cannot read area state");p.networkSpawned=true;
  p.FestivalRecoveryBonus=1.5f;p.NowAp=0;p.RegenerateAp();Check(p.NowAp==15,"festival and zone apply once each");
  p.NowAp=99;p.RegenerateAp();Check(p.NowAp==100,"MP never exceeds maximum");
  p.NowAp=0;p.RainCharging=true;p.RegenerateAp();Check(p.NowAp==0,"existing charging regeneration restriction retained");p.RainCharging=false;
  p.IsChanneling=true;p.RegenerateAp();Check(p.NowAp==0,"existing channel regeneration restriction retained");p.IsChanneling=false;
  p.Object.HasStateAuthority=false;p.manaBuffContacts.Clear();p.TickManaBuffArea();Check(p.ManaBuffActive,"clients cannot overwrite replicated host contact state");
  p.Object.HasStateAuthority=true;p.TickManaBuffArea();Check(!p.ManaBuffActive,"last exit clears replicated state");
  var wrong=new Collider();wrong.gameObject.layer=3;p.RegisterManaBuffContact(wrong);p.TickManaBuffArea();Check(!p.HasManaBuff,"other layers ignored");
  p.RegisterManaBuffContact(a);p.TickManaBuffArea();FlightCombatTable.Default.manaBuffManaRecoveryMultiplier=3;FlightCombatTable.Default.manaBuffDashRechargeMultiplier=5;
  Check(p.ManaBuffManaRecoveryMultiplier==3&&p.ManaBuffDashRechargeMultiplier==5,"live table values drive both rates");
  return n;
 }
}
'@
Add-Type -TypeDefinition $code
"ManaBuff contact, recovery and lifecycle checks passed: $([Checks]::Run())"
