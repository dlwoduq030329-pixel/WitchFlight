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
$stubs=$stubs.Replace('where T:class=>null;','where T:class=>(T)Activator.CreateInstance(typeof(T));')
$stubs=$stubs.Replace('public static float Max(float a,float b)=>Math.Max(a,b);','public static float Max(float a,float b)=>Math.Max(a,b); public static float Abs(float v)=>Math.Abs(v);')
$enums=[regex]::Match((Get-Content Assets/Script/PlayerData.cs -Raw),'public enum MagicType\s*\{[^}]+\}').Value
$code=$stubs+"`nnamespace Fusion {public enum PlayerRef {None,A,B}}`nnamespace PerkChecks { $enums }`n"
foreach($file in 'MagicStatTable','PerkStatTable','PerkRules','PerkEffects','PerkInputGate') {
    $code+="`nnamespace PerkChecks {`n"+(Get-Content "Assets/Script/$file.cs" -Raw)+"`n}`n"
}
$validation=Get-Content Assets/Editor/WitchFlightPerkValidation.cs -Raw
$ui=Get-Content Assets/Script/BattlePerkUI.cs -Raw
$description=[regex]::Match($ui,'(?ms)^    public static string Description\(.*?;').Value
$code+="`nnamespace PerkChecks { using System; using System.Collections.Generic; using UnityEngine;`n"+@'
public static class AssetDatabase {public static T LoadAssetAtPath<T>(string path) where T:new()=>new T();}
public static class BattlePerkUI {
'@ + $description + @'
}
public static class Tests {
 private static int checks;
 private static void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;}
 private static bool Near(float a,float b)=>Math.Abs(a-b)<.001f;
 public static int Run(){ValidateRules();ValidateEffects();return checks;}
'@+(Method $validation 'ValidateRules')+(Method $validation 'ValidateEffects')+"`n}}"
function RuntimeMethod([string]$source,[string]$name) {
    $m=[regex]::Match($source,'(?ms)^    (?:private|public) (?:override )?[\w<>]+ '+$name+'\([^{}]*?\)\r?\n    \{.*?^    \}')
    if(!$m.Success){throw "Missing production runtime method $name"}
    $m.Value.Replace('public override void','public void')
}
$dataSource=Get-Content Assets/Script/PlayerData.Perks.cs -Raw
$flagSource=Get-Content Assets/Script/BattleFlag.cs -Raw
$playerPerks=Get-Content Assets/Script/Player.Perks.cs -Raw
$code+=@'
namespace BalanceChecks {
using System;using System.Collections.Generic;using Fusion;using PerkChecks;using UnityEngine;
#pragma warning disable CS0649, CS0414 // Test-double fields for the real periodic-effect method.
public static class Mathf {
 public static int Max(int a,int b)=>Math.Max(a,b);public static float Max(float a,float b)=>Math.Max(a,b);
 public static float Min(float a,float b)=>Math.Min(a,b);public static float Clamp01(float v)=>Math.Clamp(v,0,1);
 public static int Clamp(int v,int a,int b)=>Math.Clamp(v,a,b);public static int RoundToInt(float v)=>(int)Math.Round(v);
}
public class NetworkRunner {public float DeltaTime=1f/64f;}
public struct NetworkId {public uint Raw;}
public class NetworkObject {public bool IsValid=true,HasStateAuthority=true;public PlayerRef InputAuthority=PlayerRef.A;public NetworkId Id;}
public enum BattleStartPhase {Playing,Intermission}
public class BattleManager {public static BattleManager Instance=new();public bool IsGameplayActive=true;public PerkStatTable Perks=new();}
public class BattleFlag {
 public static BattleFlag Instance;public NetworkObject Object=new();public NetworkRunner Runner;
 public PlayerRef Carrier,LastCarrier;public BattleStartPhase Phase=BattleStartPhase.Playing;
'@
$code+=RuntimeMethod $flagSource 'IsCarriedBy'
$code+=@'
}
public class Player {public NetworkObject Object=new(){InputAuthority=PlayerRef.B};public NetworkRunner Runner;public int TeamIndex=2;public bool IsTestBot;}
public class DataProbe {
 public NetworkObject Object=new();public NetworkRunner Runner=new();public int teamIndex=1;
 public bool PerksInitialized=true;public int PerkXp,PerkChoicesEarned,PerkChoicesUsed,PerkSlot1Level,PerkSlot2Level,CommonPerksMask,PerkRevision;
 private PerkXpLedger perkLedger;private float passiveXpTime;
 public PerkStatTable PerkTable=>BattleManager.Instance.Perks;
 private void RefreshCommonCandidates(){}
'@
foreach($name in 'ResetMatchPerks','FixedUpdateNetwork','AddPerkXp','ActionXp','AwardPerkDamage','AwardPerkKill','AwardPerkBinding','AwardPerkFlare','AwardPerkFlag','AwardPerkRound') {
    $code+=RuntimeMethod $dataSource $name
}
$guard=[regex]::Match($dataSource,'(?ms)^    private bool CanAwardPerkXp\(\) =>.*?;').Value
if(!$guard){throw 'Missing authority XP guard'}
$code+=$guard
$code+=@'
}
public class FlareProbe {
 public bool HasBurnStatus;
 public NetworkObject Object=new();public NetworkRunner Runner=new();public bool IsAlive=true;
 private float perkClock,flareCastAt,flarePulseAt=-1;private int flarePulsesRemaining;private bool flareRefunded;
 private MagicStatEntry flarePerkStats;public Vector3 LockAimPoint;public Transform transform=new();
 private sealed class Burn {public PlayerRef attacker;public float remaining,damagePerSecond,nextTick,interval;}
 private readonly List<Burn> perkBurns=new();public int Pulses,Visuals;
 private void TakeDamage(float damage,PlayerRef attacker,MagicType magic,bool parry,float stun,float force){}
 private void TriggerPerkFlare(MagicStatEntry stats){Pulses++;}
 private void RPC_PresentCast(MagicType magic,Vector3 origin,Vector3 direction,float unused){Visuals++;}
 public void Cast(MagicStatEntry s)=>StartPerkFlare(s);public void Tick()=>TickPerkEffects();
'@
$code+=RuntimeMethod $playerPerks 'StartPerkFlare'
$code+=RuntimeMethod $playerPerks 'TickPerkEffects'
$code+=@'
}
public static class Tests {
 static int checks;static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
 static bool Near(float a,float b)=>Math.Abs(a-b)<.001f;
 static void Advance(DataProbe p,int ticks){for(int i=0;i<ticks;i++)p.FixedUpdateNetwork();}
 static void Carry(DataProbe p){BattleFlag.Instance=new(){Runner=p.Runner,Carrier=p.Object.InputAuthority};}
 static Player Victim(DataProbe p,uint life=1)=>new(){Runner=p.Runner,Object=new(){InputAuthority=PlayerRef.B,Id=new(){Raw=life}}};
 public static int Run(){
  var table=new PerkStatTable();BattleManager.Instance=new(){Perks=table};
  Check(table.passiveXp==4&&Near(table.passiveInterval,2),"normal passive rate stays 2 XP per second");
  Check(Near(table.flagCarrierSpeedMultiplier,.8f)&&Near(table.flagCarrierPassiveXpMultiplier,.125f),"carrier penalties are 20 percent speed and 87.5 percent passive XP");
  Check(table.damageXp==12&&table.killXp==30&&table.actionXpLimit==90,"combat rewards and cap buffed independently of carrier");
  float[] balancedAmounts={16,.45f,.15f,.3f,.25f,.75f,.2f,.3f,.5f,.65f,.4f,.45f,.2f,.25f,.3f,.4f,.45f,.5f,.5f,.4f,2,.3f,.2f,2,35,.35f,.45f,.45f,.3f,.25f,.3f,.2f,.2f,.08f};
  for(int i=0;i<34;i++)Check(Near(table.Amount((PerkId)(i+1)),balancedAmounts[i]),"perk magnitude matches new magic balance "+(i+1));
  MagicStatEntry S(MagicType m,int level=2,int common=0)=>PerkEffects.Apply(MagicStatTable.DefaultEntry(m),1,level,common,100,table);
  Check(Near(S(MagicType.Fire).lockChargeSeconds,.44f)&&Near(S(MagicType.Fire).apCost,45),"fire mastery remains fast and affordable without changing 45 base damage");
  Check(Near(S(MagicType.Ice).movementMultiplier,.31f)&&Near(S(MagicType.Ice).movementMultiplier*table.flagCarrierSpeedMultiplier,.248f),"new ice slow plus carrier penalty is not an immobilize");
  Check(Near(S(MagicType.Vision).damage*(1+table.Amount(PerkId.VisionCombo)),43.75f),"vision second consecutive hit rewards aim");
  Check(Near(PerkEffects.AreaHit(S(MagicType.Thunder),0).damage,130)&&Near(S(MagicType.Thunder).radius,14.4f),"thunder mastery does not one-shot 160 HP broom");
  Check(Near(S(MagicType.Healing).healing,60),"healing first perk delivers sixty HP");
  Check(Near(S(MagicType.Binding).lockChargeSeconds,.36f)&&Near(S(MagicType.Binding).apCost,27.5f),"binding retains fast affordable setup");
  Check(Near(S(MagicType.Dark).healthCost,24)&&Near(S(MagicType.Dark).healOnHit,50),"dark mastery limits net healing to 26 per actual hit");
  Check(Near(S(MagicType.Curse).damagePerSecond,14)&&Near(S(MagicType.Curse).range,45.5f)&&Near(S(MagicType.Curse).ChannelManaPerSecond(200),.5f),"curse mastery is damage not fractional maximum-MP drain");
  Check(Near(S(MagicType.Mine).damage,18)&&Near(S(MagicType.Mine).summonAttackInterval,1.6f)&&Near(S(MagicType.Mine).summonAttackRange,82.5f),"summon retains unspecified bolt damage and buffed cadence");
  Check(Near(S(MagicType.Razier).damagePerSecond*(1+table.Amount(PerkId.BeamFocus)),28)&&Near(table.Get(PerkId.BeamFocus).seconds,.75f),"beam rewards continuous aim without burst-sized DPS");
  Check(Near(S(MagicType.Flare).apCost*(1-table.Amount(PerkId.FlareEfficiency)),35)&&Near(S(MagicType.Flare).cooldownSeconds*(1-table.Get(PerkId.FlareEfficiency).secondaryAmount),4.8f),"successful mastered flare still has resource and timing tradeoffs");
  var smoke=S(MagicType.Smoke,2,PerkRules.Bit(PerkId.EchoSpell));
  Check(Near(S(MagicType.Smoke,0).radius,36)&&Near(S(MagicType.Smoke,0).effectDuration,7),"unupgraded smoke uses former perk radius");
  Check(Near(S(MagicType.Smoke,1).radius,43.2f)&&Near(S(MagicType.Smoke,1).effectDuration,7),"first smoke perk enlarges new base by twenty percent");
  Check(Near(smoke.radius,43.2f)&&Near(smoke.effectDuration,9)&&Near(smoke.cooldownSeconds-smoke.effectDuration,3),"second smoke perk adds duration without stacking radius again");
  Check(Near(MagicStatTable.DefaultEntry(MagicType.Smoke).radius,36),"perk application does not mutate smoke base radius");
  Check(Near(S(MagicType.Curse,2,PerkRules.Bit(PerkId.FrugalRune)).ChannelManaPerSecond(200),.4f)&&Near(S(MagicType.Razier,2,PerkRules.Bit(PerkId.FrugalRune)).ChannelManaPerSecond(200),.8f),"common mana savings apply to fixed channel costs once");
  Check(table.RequiredXp(0)==100&&table.RequiredXp(3)==338&&PerkRules.MaxChoices==4&&!PerkRules.CanUpgrade(1,2),"progression thresholds and one mastery unchanged");
  var normal=new DataProbe();Advance(normal,16*64);Check(normal.PerkXp==32,"normal earns 32 XP over sixteen seconds");
  var carrier=new DataProbe();Carry(carrier);Advance(carrier,16*64);Check(carrier.PerkXp==4,"carrier earns 4 XP over sixteen seconds");
  carrier=new();Carry(carrier);BattleFlag.Instance.Carrier=PlayerRef.None;Advance(carrier,64);
  BattleFlag.Instance.Carrier=PlayerRef.A;Advance(carrier,8*64);
  Check(carrier.PerkXp==4,"partial progress uses time spent in each ownership state, not payout-time ownership");
  BattleFlag.Instance.LastCarrier=PlayerRef.A;BattleFlag.Instance.Carrier=PlayerRef.None;Advance(carrier,2*64);
  Check(carrier.PerkXp==8,"drop restores normal passive XP without last-carrier penalty");
  carrier=new();Carry(carrier);BattleFlag.Instance.Carrier=PlayerRef.B;Advance(carrier,2*64);
  Check(carrier.PerkXp==4,"opponent holding flag leaves passive XP normal");
  carrier=new();Carry(carrier);BattleFlag.Instance.Runner=new();Advance(carrier,2*64);
  Check(carrier.PerkXp==4,"unrelated runner's flag cannot reduce passive XP");
  carrier=new();Carry(carrier);table.flagCarrierPassiveXpMultiplier=.01f;Advance(carrier,200*64);
  Check(carrier.PerkXp==4,"small editable passive rate preserves fractional progress");
  carrier=new();Carry(carrier);table.flagCarrierPassiveXpMultiplier=0;Advance(carrier,20*64);
  Check(carrier.PerkXp==0,"zero multiplier disables only carrier passive XP");table.flagCarrierPassiveXpMultiplier=.125f;
  carrier=new();Carry(carrier);carrier.Object.HasStateAuthority=false;Advance(carrier,32*64);carrier.AwardPerkDamage(Victim(carrier),200,200);carrier.AwardPerkKill(Victim(carrier));
  Check(carrier.PerkXp==0,"clients cannot mint passive or combat XP");carrier.Object.HasStateAuthority=true;
  BattleManager.Instance.IsGameplayActive=false;Advance(carrier,32*64);carrier.AwardPerkDamage(Victim(carrier),200,200);
  Check(carrier.PerkXp==0,"no automatic or combat XP outside play");BattleManager.Instance.IsGameplayActive=true;
  normal=new();carrier=new();Carry(carrier);var nVictim=Victim(normal);var cVictim=Victim(carrier);
  normal.AwardPerkDamage(nVictim,200,200);carrier.AwardPerkDamage(cVictim,200,200);
  Check(normal.PerkXp==48&&carrier.PerkXp==48,"four damage quotas award identical XP with or without flag");
  normal.AwardPerkKill(nVictim);carrier.AwardPerkKill(cVictim);
  Check(normal.PerkXp==78&&carrier.PerkXp==78,"kill grants full thirty XP to both carrier and noncarrier");
  normal.AwardPerkKill(nVictim);carrier.AwardPerkKill(cVictim);normal.AwardPerkDamage(nVictim,200,200);
  Check(normal.PerkXp==78&&carrier.PerkXp==78,"duplicate death and healed same-life damage cannot be farmed");
  normal.AwardPerkDamage(Victim(normal,2),200,200);carrier.AwardPerkDamage(Victim(carrier,2),200,200);
  Check(normal.PerkXp==90&&carrier.PerkXp==90,"rolling combat cap applies equally");
  normal.AwardPerkFlare();carrier.AwardPerkBinding(cVictim);Check(normal.PerkXp==90&&carrier.PerkXp==90,"other actions share anti-farming cap");
  table.passiveXp=0;Advance(normal,61*64);Advance(carrier,61*64);
  normal.AwardPerkDamage(Victim(normal,3),50,200);carrier.AwardPerkDamage(Victim(carrier,3),50,200);
  Check(normal.PerkChoicesEarned==1&&carrier.PerkChoicesEarned==1&&normal.PerkXp==2&&carrier.PerkXp==2,"expired cap reopens full combat rewards");table.passiveXp=4;
  var guarded=new DataProbe();var bot=Victim(guarded);bot.IsTestBot=true;guarded.AwardPerkDamage(bot,200,200);guarded.AwardPerkKill(bot);
  var friendly=Victim(guarded);friendly.TeamIndex=1;guarded.AwardPerkDamage(friendly,200,200);guarded.AwardPerkKill(friendly);
  Check(guarded.PerkXp==0,"bots and friendly damage do not give boosted XP");
  var flareXp=new DataProbe();flareXp.AwardPerkFlare();flareXp.AwardPerkFlare();flareXp.AwardPerkFlare();
  Check(flareXp.PerkXp==4,"extra flare pulses cannot duplicate success XP");
  carrier=new();Carry(carrier);Advance(carrier,8*64);carrier.ResetMatchPerks();Advance(carrier,8*64);
  Check(carrier.PerkXp==0,"new match clears partially accrued passive progress");
  var flare=new FlareProbe();var stats=MagicStatTable.DefaultEntry(MagicType.Flare);stats.perkFirst=table.Get(PerkId.FlarePulse);
  flare.Cast(stats);Check(flare.Pulses==1,"flare retains immediate initial break");
  for(int i=0;i<22;i++)flare.Tick();Check(flare.Pulses==1,"extra flare does not trigger before interval");
  for(int i=0;i<80;i++)flare.Tick();Check(flare.Pulses==3&&flare.Visuals==2,"buffed flare emits two delayed extra pulses and visuals");
  for(int i=0;i<100;i++)flare.Tick();Check(flare.Pulses==3,"flare pulses stop after configured count");
  flare=new();flare.Cast(stats);for(int i=0;i<10;i++)flare.Tick();flare.Cast(stats);for(int i=0;i<100;i++)flare.Tick();
  Check(flare.Pulses==4,"recasting replaces pending pulses instead of stacking old schedules");
  flare=new();flare.Object.HasStateAuthority=false;flare.Cast(stats);for(int i=0;i<100;i++)flare.Tick();
  Check(flare.Pulses==1,"non-authority cannot execute scheduled pulse effects");
  flare=new();flare.Cast(stats);flare.IsAlive=false;for(int i=0;i<100;i++)flare.Tick();Check(flare.Pulses==1,"dead caster cannot emit delayed pulses");
  stats.perkFirst=null;flare=new();flare.Cast(stats);for(int i=0;i<100;i++)flare.Tick();Check(flare.Pulses==1,"no perk means no added pulses");
  return checks;
 }
}
}
'@
$code+=@'
namespace HitBalanceChecks {
using System;using System.Collections.Generic;using Fusion;using PerkChecks;using UnityEngine;
using NetworkId=BalanceChecks.NetworkId;using NetworkObject=BalanceChecks.NetworkObject;
#pragma warning disable CS0649
public class Player {
 public NetworkObject Object=new();public int TeamIndex=1;public bool IsAlive=true;
 public bool HasBurnStatus;
 public float MaxAp=100,Restored,perkClock;private float nextManaRemnant;
 public PerkStatTable Perks=new();public int commonMask;
 private float CommonAmount(PerkId id)=>(commonMask&PerkRules.Bit(id))!=0?Perks.Amount(id):0;
 private void RestoreAp(float v){Restored+=v;}
 private readonly float[] visionFirstHit={-100,-100,-100};private readonly NetworkId[] visionVictim=new NetworkId[3];
 private NetworkId beamVictim;private float beamContact,beamLastContact=-100;
 private sealed class Burn {public PlayerRef attacker;public float remaining,damagePerSecond,nextTick,interval;}
 private sealed class IceMark {public int slot,cast;public bool used;public float expires,amount,channelSeconds,channelUntil;}
 private readonly Dictionary<NetworkId,IceMark> iceMarks=new();private readonly List<Burn> perkBurns=new();
 public bool IsEnemyOf(Player other)=>other!=this&&other.TeamIndex!=TeamIndex;
 public MagicStatEntry Hit(MagicStatEntry s,Player source){var result=IncomingPerkDamage(s,source,source.Object.Id);OnAcceptedPerkHit(s,source,source.Object.Id);return result;}
 public void Track(Player target,float dt,MagicStatEntry stats)=>TrackPerkBeam(target,dt,stats);
 public void Miss(MagicStatEntry stats)=>ResetVisionMiss(stats);
 public float BurnDamage=>perkBurns.Count>0?perkBurns[0].remaining*perkBurns[0].damagePerSecond:0;
'@
foreach($name in 'IncomingPerkDamage','OnAcceptedPerkHit','TrackPerkBeam','ResetVisionMiss'){$code+=RuntimeMethod $playerPerks $name}
$code+=@'
}
public static class Tests {
 static int checks;static void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;}
 static bool Near(float a,float b)=>Math.Abs(a-b)<.001f;
 public static int Run(){
  var table=new PerkStatTable();
  MagicStatEntry S(MagicType m,int level=2,int slot=1)=>PerkEffects.Apply(MagicStatTable.DefaultEntry(m),slot,level,0,100,table);
  var source=new Player(){Object=new(){Id=new(){Raw=1}}};var enemy=new Player(){TeamIndex=2,Object=new(){Id=new(){Raw=2}}};
  source.commonMask=PerkRules.Bit(PerkId.ManaRemnant);
  enemy.Hit(S(MagicType.Curse),source);enemy.Hit(S(MagicType.Razier),source);
  Check(Near(source.Restored,0),"actual accepted channel hits do not refund MP");
  enemy.Hit(S(MagicType.Fire),source);
  Check(Near(source.Restored,8),"excluded channel does not consume single-hit refund cooldown");
  enemy.Hit(S(MagicType.Fire),source);Check(Near(source.Restored,8),"same-tick hits do not duplicate remnant");
  source.perkClock=2.99f;enemy.Hit(S(MagicType.Fire),source);Check(Near(source.Restored,8),"refund waits full interval");
  source.perkClock=3;enemy.Hit(S(MagicType.Fire),source);Check(Near(source.Restored,16),"refund reopens after interval");
  source.perkClock=6;source.Hit(S(MagicType.Fire),source);var friendly=new Player();friendly.Hit(S(MagicType.Fire),source);
  Check(Near(source.Restored,16),"self and friendly hits cannot refund");
  Check(Near(enemy.BurnDamage,16),"fire burn refreshes instead of stacking");
  var vision=S(MagicType.Vision);source.perkClock=10;
  Check(Near(enemy.Hit(vision,source).damage,25),"vision first actual hit is twenty-five");
  source.perkClock=12;Check(Near(enemy.Hit(vision,source).damage,43.75f),"vision second actual hit earns seventy-five percent bonus");
  source.perkClock=14;Check(Near(enemy.Hit(vision,source).damage,25),"vision resets after rewarded second hit");
  source.Miss(vision);source.perkClock=16;Check(Near(enemy.Hit(vision,source).damage,25),"miss breaks vision chain");
  source.perkClock=21;Check(Near(enemy.Hit(vision,source).damage,25),"expired vision window starts new chain");
  var ice=S(MagicType.Ice);enemy.perkClock=21;enemy.Hit(ice,source);
  var fire=S(MagicType.Fire,1,2);fire.perkCast=7;
  Check(Near(enemy.Hit(fire,source).damage,58.5f),"ice mastery adds thirty percent to other-slot hit");
  Check(Near(enemy.Hit(fire,source).damage,45),"ice mark consumed once");
  var beam=S(MagicType.Razier);beam.damage=2;
  source.perkClock=30;source.Track(enemy,.5f,beam);Check(Near(enemy.Hit(beam,source).damage,2),"beam focus does not activate prematurely");
  source.perkClock=30.2f;source.Track(enemy,.25f,beam);Check(Near(enemy.Hit(beam,source).damage,2.8f),"beam focus activates after .75 seconds of contact");
  source.perkClock=31;source.Track(enemy,.1f,beam);Check(Near(enemy.Hit(beam,source).damage,2),"loss of beam contact resets bonus");
  var other=new Player(){TeamIndex=2,Object=new(){Id=new(){Raw=3}}};source.Track(other,.1f,beam);
  Check(Near(other.Hit(beam,source).damage,2),"switching targets resets beam focus");
  return checks;
 }
}
}
'@
Add-Type -TypeDefinition $code
"Perk rules, XP ledger, fixed effects and input checks passed: $([PerkChecks.Tests]::Run())"
"Flag XP, combat balance and perk runtime checks passed: $([BalanceChecks.Tests]::Run())"
"Actual perk hit, mana refund and channel-focus checks passed: $([HitBalanceChecks.Tests]::Run())"
$asset=Get-Content Assets/Resources/PerkStatTable.asset -Raw
$ids=[regex]::Matches($asset,'(?m)^  - id: (\d+)\r?$')|ForEach-Object{[int]$_.Groups[1].Value}
if($ids.Count-ne 34 -or (@($ids|Sort-Object -Unique)).Count-ne 34){throw 'Real perk asset IDs must be unique 1..34'}
foreach($id in 1..34){if($ids-notcontains $id){throw "Missing perk $id"}}
$defaults=New-Object PerkChecks.PerkStatTable
foreach($entry in $defaults.perks) {
    $id=[int]$entry.id
    $block=[regex]::Match($asset,"(?ms)^  - id: $id\r?`n.*?(?=^  - id: |^  firstChoiceXp:)").Value
    foreach($field in 'amount','secondaryAmount','seconds','condition') {
        $saved=[regex]::Match($block,"(?m)^    ${field}: ([^\r\n]+)").Groups[1].Value
        if(!$saved -or [Math]::Abs([double]::Parse($saved,[Globalization.CultureInfo]::InvariantCulture)-$entry.$field)-gt .0001) {throw "Saved perk $id $field differs from balanced defaults"}
    }
    if(!$block.Contains('description: "'+$entry.description+'"')){throw "Saved perk $id description differs from runtime defaults"}
}
foreach($field in 'passiveInterval','passiveXp','flagCarrierSpeedMultiplier','flagCarrierPassiveXpMultiplier','damageXp','killXp','actionXpLimit','manaDiscountCap','cooldownDiscountCap') {
    $saved=[regex]::Match($asset,"(?m)^  ${field}: ([^\r\n]+)").Groups[1].Value
    if(!$saved -or [Math]::Abs([double]::Parse($saved,[Globalization.CultureInfo]::InvariantCulture)-$defaults.$field)-gt .0001) {throw "Saved balance field $field differs from defaults"}
}
'All 34 saved perk definitions and flag/XP settings match the new runtime defaults.'
$data=Get-Content Assets/Script/PlayerData.Perks.cs -Raw
if($data-notmatch 'revision == PerkRevision' -or $data-notmatch 'candidate == expected' -or $data-notmatch 'RpcSources.InputAuthority, RpcTargets.StateAuthority'){throw 'Selection authority/revision protection missing'}
if(($data -replace '(?m)//.*$','')-match 'DataConfig\.|Backend\.|BACKND\.'){throw 'Perks must never persist to account customization'}
$player=Get-Content Assets/Script/Player.cs -Raw
if($player.IndexOf('if (data.suppressCombat || data.suppressActions) CancelCombatForPerks();')-gt $player.IndexOf('else if (Object.HasStateAuthority) UpdatePendingCast();')){throw 'Space must cancel delayed cast before execution'}
if($ui-match 'Cursor\.(visible|lockState)\s*='){throw 'Perk UI must not change cursor lock'}
if($ui-notmatch '!gate.Showing && Input.GetKey\(KeyCode.Tab\)' -or $ui-notmatch 'Show\(selectionRoot,cards\)'){throw 'Held Space/Tab contract missing'}
$scene=Get-Content Assets/Scenes/Battle.unity -Raw
if($scene-notmatch 'component: \{fileID: 1515047385\}' -or $scene-notmatch 'matchDurationSeconds: 160' -or $scene-notmatch 'experienceAnchor: \{fileID: 2027782601\}') {throw 'Battle perk bindings/duration missing'}
'34 saved perk IDs, authority/revision, no account writes, input ordering and scene bindings passed.'
