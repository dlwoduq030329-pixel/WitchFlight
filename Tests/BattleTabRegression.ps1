# Production stat mutations and comparison rendering with offline Unity/Fusion doubles.
# Not a substitute for Fusion weaving or a two-client Play Mode session.
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$ui=Get-Content (Join-Path $repo 'Assets/Script/BattlePerkUI.cs') -Raw
function Block([string]$signature) {
    $m=[regex]::Match($ui,'(?ms)^    '+[regex]::Escape($signature)+'.*?^    \}')
    if (!$m.Success) {throw "Missing UI method: $signature"}; $m.Value
}
function Line([string]$signature) {
    $m=[regex]::Match($ui,'(?m)^    '+[regex]::Escape($signature)+'[^\r\n]*')
    if (!$m.Success) {throw "Missing UI expression: $signature"}; $m.Value
}
$views=$ui.Substring($ui.IndexOf('[Serializable]'),$ui.IndexOf('// Keep this')-$ui.IndexOf('[Serializable]'))
$source=@'
using System;
using System.Collections.Generic;
using System.Globalization;
using Fusion;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
namespace UnityEngine {
 public class HeaderAttribute:Attribute {public HeaderAttribute(string x){}}
 public class TooltipAttribute:Attribute {public TooltipAttribute(string x){}}
 public class HideInInspector:Attribute {}
 public class Sprite {public int Id;public Sprite(int id){Id=id;}}
 public class GameObject {public bool activeSelf;public void SetActive(bool b){activeSelf=b;}}
 public static class Mathf {public static float Min(float a,float b)=>Math.Min(a,b);}
}
namespace UnityEngine.UI {public class Image {public Sprite sprite;public bool enabled;}}
namespace TMPro {public class TMP_Text {public string text="placeholder";}}
namespace Fusion {
 public class NetworkedAttribute:Attribute {}
 public enum PlayerRef {None,A,B,C,D}
 public struct NetworkId {public int Raw;public NetworkId(int raw){Raw=raw;}public override string ToString()=>Raw.ToString();}
 public class NetworkRunner {}
 public class NetworkObject {public bool IsValid=true,HasStateAuthority=true;public NetworkId Id;public PlayerRef InputAuthority;}
 public class NetworkBehaviour {public NetworkRunner Runner;public NetworkObject Object=new();}
}
public enum MagicType {None,Fire,Ice,Vision,Thunder,Healing,Binding,Dark,Curse,Mine,Razier,Flare,Smoke}
public enum PerkId {None=0}
public class PerkDefinition {
 public string displayName,description;public float amount,seconds,secondaryAmount;public Sprite icon;
}
public class PerkStatTable {
 public static PerkStatTable Default=new();
 public PerkDefinition Get(PerkId id)=>id==PerkId.None?null:new PerkDefinition{displayName="Perk"+(int)id,description="Effect {amount}",amount=(int)id,icon=new Sprite((int)id)};
}
public class MagicStatTable {
 public Sprite GetIcon(MagicType m)=>m==MagicType.None?null:new Sprite((int)m);
 public PerkDefinition GetStats(MagicType m)=>new PerkDefinition{displayName="Magic"+(int)m};
}
public class ProfileImageTable {public Sprite GetSprite(int id)=>new Sprite(id);}
public class BattleManager {public static BattleManager Instance=new();public bool IsGameplayActive=true;public int ExpectedPlayerCount=2;}
public static class BattleMatchRules {public static void CollectRoster(NetworkRunner r,List<PlayerData> list){list.Clear();foreach(var d in PlayerData.PerkPlayers)if(d.Runner==r&&d.Object.IsValid)list.Add(d);list.Sort((a,b)=>a.Object.InputAuthority.CompareTo(b.Object.InputAuthority));}}
public class BattleFlag:NetworkBehaviour {public static BattleFlag Instance;public PlayerRef Carrier;}
public partial class PlayerData:NetworkBehaviour {
 public static List<PlayerData> PerkPlayers=new();
 public static PlayerData FindPerkData(NetworkRunner runner,PlayerRef who)=>PerkPlayers.Find(d=>d.Runner==runner&&d.Object.IsValid&&d.Object.InputAuthority==who);
 public bool PerksInitialized=true;public int teamIndex,playerprofile,PerkRevision,PerkSlot1Level,PerkSlot2Level,CommonPerksMask;
 public string playerName;public MagicType magic1=MagicType.Fire,magic2=MagicType.Ice;
 public PerkStatTable PerkTable=>PerkStatTable.Default;
 public int PerkLevel(int slot)=>slot==1?PerkSlot1Level:PerkSlot2Level;
 public bool HasCommonPerk(PerkId id)=>(CommonPerksMask&PerkRules.Bit(id))!=0;
}
'@
$stats=Get-Content (Join-Path $repo 'Assets/Script/PlayerData.BattleStats.cs') -Raw
$rules=Get-Content (Join-Path $repo 'Assets/Script/PerkRules.cs') -Raw
$source += [regex]::Replace($stats,'(?m)^using .*;\r?\n','')
$source += [regex]::Replace($rules,'(?m)^using .*;\r?\n','')
$source += $views
$source += @'
public class TabProbe {
 public PlayerData local;public string comparisonKey;
 public PerkPlayerView ownView,opponentView;
 public PerkPlayerView[] teamViews=new PerkPlayerView[4];public List<PlayerData> comparisonRoster=new();
 public MagicStatTable magicTable=new();public ProfileImageTable profileTable=new();
'@
$methods=@()
foreach ($s in @('private bool CarriesFlag(', 'private void FillPlayer(', 'private void FillSpellEffects(',
    'private static void ClearEffect(', 'private void Fill(', 'private Sprite Icon(', 'private void UpdateComparison(', 'private PlayerData TeamViewData(')) { $methods+=Block $s }
foreach ($s in @('private bool Ready(', 'private string SnapshotKey(', 'private static string DamageText(',
    'private string MagicName(', 'private static void Text(', 'private static void SetIcon(', 'private static void Show(')) { $methods+=Line $s }
$methods += [regex]::Match($ui,'(?ms)^    public static string Description\(.*?;').Value
$methods += [regex]::Match($ui,'(?m)^    private void FillRow[^\r\n]*\r?\n[^\r\n]*').Value
$source += (($methods -join "`n") -replace '\bprivate\b','public') + "`n}`n"
$source += @'
public static class BattleTabChecks {
 static int count;
 static void Check(bool ok,string label){if(!ok)throw new Exception(label);count++;}
 static PlayerData Make(NetworkRunner r,PlayerRef who,int team){
  var d=new PlayerData{Runner=r,teamIndex=team,playerName=who.ToString(),playerprofile=team};
  d.Object.InputAuthority=who;d.Object.Id=new NetworkId(team);PlayerData.PerkPlayers.Add(d);return d;
 }
 static PerkCardView Card()=>new PerkCardView{title=new TMP_Text(),description=new TMP_Text(),hint=new TMP_Text(),icon=new Image()};
 static PerkPlayerView View()=>new PerkPlayerView{
  playerName=new TMP_Text(),profile=new Image(),slot1Text=new TMP_Text(),slot2Text=new TMP_Text(),slot1Icon=new Image(),slot2Icon=new Image(),
  slot1Effects=new[]{Card(),Card()},slot2Effects=new[]{Card(),Card()},commonEffects=new[]{Card(),Card()},effects=new[]{Card(),Card(),Card(),Card()},
  damageDealtText=new TMP_Text(),damageTakenText=new TMP_Text(),killsText=new TMP_Text(),deathsText=new TMP_Text(),killsDeathsText=new TMP_Text(),flagIndicator=new GameObject(),flagStatusText=new TMP_Text()
 };
 public static int Run(){
  var runner=new NetworkRunner();var a=Make(runner,PlayerRef.A,1);var b=Make(runner,PlayerRef.B,2);
  a.ResetBattleStats();b.ResetBattleStats();b.RecordBattleDamage(PlayerRef.A,25.5f);
  Check(a.BattleDamageDealt==25.5f&&b.BattleDamageTaken==25.5f,"both sides share actual damage event");
  int rev=b.BattleStatsRevision;
  foreach(float invalid in new[]{0f,-1f,float.NaN,float.PositiveInfinity})b.RecordBattleDamage(PlayerRef.A,invalid);
  Check(b.BattleStatsRevision==rev&&a.BattleDamageDealt==25.5f,"invalid damage never counted");
  b.RecordBattleDamage(PlayerRef.None,10);Check(b.BattleDamageTaken==35.5f&&a.BattleDamageDealt==25.5f,"environment damage received only");
  b.RecordBattleDamage(PlayerRef.B,5);Check(b.BattleDamageDealt==0&&b.BattleDamageTaken==40.5f,"self damage not dealt to enemy");
  var friendly=Make(runner,PlayerRef.C,2);b.RecordBattleDamage(PlayerRef.C,3);Check(friendly.BattleDamageDealt==0,"friendly damage not credited as enemy damage");
  a.Object.HasStateAuthority=false;b.RecordBattleDamage(PlayerRef.A,3);Check(a.BattleDamageDealt==25.5f,"non-authority source not mutated");a.Object.HasStateAuthority=true;
  b.Object.HasStateAuthority=false;rev=b.BattleStatsRevision;b.RecordBattleDamage(PlayerRef.A,50);b.RecordBattleDeath(new NetworkId(100),a);b.ResetBattleStats();
  Check(b.BattleStatsRevision==rev&&a.BattleKills==0,"proxy cannot mutate/reset stats");b.Object.HasStateAuthority=true;
  BattleManager.Instance.IsGameplayActive=false;rev=b.BattleStatsRevision;b.RecordBattleDamage(PlayerRef.A,50);b.RecordBattleDeath(new NetworkId(100),a);
  Check(b.BattleStatsRevision==rev,"countdown/intermission events ignored");BattleManager.Instance.IsGameplayActive=true;
  b.RecordBattleDeath(new NetworkId(100),a);b.RecordBattleDeath(new NetworkId(100),a);
  Check(a.BattleKills==1&&b.BattleDeaths==1,"duplicate death counted once");
  b.RecordBattleDeath(new NetworkId(101),null);Check(b.BattleDeaths==2&&a.BattleKills==1,"environment death has no killer");
  b.RecordBattleDeath(new NetworkId(102),b);Check(b.BattleDeaths==3&&b.BattleKills==0,"suicide has no kill credit");
  b.RecordBattleDeath(new NetworkId(103),friendly);Check(friendly.BattleKills==0,"team kill not credited");
  var outsider=Make(new NetworkRunner(),PlayerRef.A,1);b.RecordBattleDeath(new NetworkId(104),outsider);Check(outsider.BattleKills==0,"runner mismatch not credited");
  Check(a.BattleDamageDealt==25.5f&&b.BattleDeaths==5,"totals survive character life IDs");
  b.RecordBattleDeath(default,a);Check(b.BattleDeaths==5,"missing character ID not counted");

  a.PerkSlot1Level=2;a.magic2=MagicType.Binding;a.CommonPerksMask=PerkRules.Bit((PerkId)25)|PerkRules.Bit((PerkId)29);
  var tab=new TabProbe{local=a,ownView=View(),opponentView=View()};tab.UpdateComparison();
  Check(tab.ownView.playerName.text=="A"&&tab.opponentView.playerName.text=="B","own/enemy selected by local identity");
  Check(tab.ownView.profile.sprite.Id==1&&tab.opponentView.profile.sprite.Id==2,"portraits use profile IDs");
  Check(tab.ownView.slot1Text.text=="Magic1"&&tab.ownView.slot2Text.text=="Magic6","magic names have no level suffix");
  Check(tab.ownView.slot1Icon.sprite.Id==1&&tab.ownView.slot2Icon.sprite.Id==6,"magic icons follow loadout");
  Check(tab.ownView.slot1Effects[0].title.text=="Perk1"&&tab.ownView.slot1Effects[1].title.text=="Perk2","slot one fixed stage rows");
  Check(tab.ownView.slot2Effects[0].title.text=="—"&&tab.ownView.slot2Effects[1].title.text=="—","unearned slot two not filled by slot one/common perks");
  Check(tab.ownView.commonEffects[0].title.text=="Perk25"&&tab.ownView.commonEffects[1].title.text=="Perk29","two separate common perk rows");
  Check(tab.ownView.damageDealtText.text=="26"&&tab.opponentView.deathsText.text=="5","numeric totals render");
  Check(tab.ownView.killsText.text=="1"&&tab.opponentView.killsDeathsText.text=="0 / 5","both KD layouts supported");
  Check(tab.ownView.effects[0].title.text=="Perk1","legacy flat list preserved");
  a.magic1=MagicType.Dark;a.PerkRevision++;tab.UpdateComparison();
  Check(tab.ownView.slot1Effects[0].title.text=="Perk13"&&tab.ownView.slot1Effects[1].title.text=="Perk14","magic swap uses new spell effects");
  a.PerkSlot2Level=1;a.PerkRevision++;tab.UpdateComparison();
  Check(tab.ownView.slot2Effects[0].title.text=="Perk11"&&tab.ownView.slot2Effects[1].title.text=="—","stage one never displays unearned stage two");
  var flag=new BattleFlag{Runner=runner,Carrier=PlayerRef.A};BattleFlag.Instance=flag;tab.UpdateComparison();
  Check(tab.ownView.flagIndicator.activeSelf&&!tab.opponentView.flagIndicator.activeSelf,"flag possession updates without perk/stat change");
  flag.Carrier=PlayerRef.B;tab.UpdateComparison();Check(!tab.ownView.flagIndicator.activeSelf&&tab.opponentView.flagIndicator.activeSelf,"flag transfer");
  flag.Carrier=PlayerRef.None;tab.UpdateComparison();Check(!tab.opponentView.flagIndicator.activeSelf,"drop clears flag");
  flag.Carrier=PlayerRef.A;flag.Runner=new NetworkRunner();tab.UpdateComparison();Check(!tab.ownView.flagIndicator.activeSelf,"another runner flag ignored");
  b.RecordBattleDamage(PlayerRef.A,1500);tab.UpdateComparison();Check(tab.ownView.damageDealtText.text=="1,526","damage event invalidates UI cache; grouped digits");
  b.PerksInitialized=false;tab.UpdateComparison();Check(tab.opponentView.playerName.text.Contains("정보")&&tab.opponentView.damageTakenText.text=="—"&&tab.opponentView.profile.sprite==null,"unready opponent clears stale data");
  b.PerksInitialized=true;tab.local=b;tab.UpdateComparison();Check(tab.ownView.playerName.text=="B"&&tab.opponentView.playerName.text=="A","guest sees self on top, not host");
  tab.FillPlayer(new PerkPlayerView{slot1Effects=null,slot2Effects=null,commonEffects=null,effects=null},a);Check(true,"optional UI fields may remain empty");
  var ally=Make(runner,PlayerRef.D,1);BattleManager.Instance.ExpectedPlayerCount=4;tab.local=a;
  for(int i=0;i<4;i++)tab.teamViews[i]=View();tab.UpdateComparison();
  Check(tab.teamViews[0].playerName.text=="A"&&tab.teamViews[1].playerName.text=="D"&&tab.teamViews[2].playerName.text=="B"&&tab.teamViews[3].playerName.text=="C","team Tab: self, ally, both enemies");
  flag.Runner=runner;flag.Carrier=PlayerRef.C;tab.UpdateComparison();
  Check(!tab.teamViews[0].flagIndicator.activeSelf&&!tab.teamViews[1].flagIndicator.activeSelf&&!tab.teamViews[2].flagIndicator.activeSelf&&tab.teamViews[3].flagIndicator.activeSelf,"fourth row independently owns flag");
  tab.local=b;tab.UpdateComparison();Check(tab.teamViews[0].playerName.text=="B"&&tab.teamViews[1].playerName.text=="C"&&tab.teamViews[2].playerName.text=="A"&&tab.teamViews[3].playerName.text=="D","opposite-side client sees own team first");
  friendly.Object.IsValid=false;tab.UpdateComparison();Check(tab.teamViews[1].profile.sprite==null&&tab.teamViews[2].playerName.text=="A","departure leaves teammate slot empty without shifting enemies");
  a.ResetBattleStats();b.ResetBattleStats();Check(a.BattleDamageDealt==0&&a.BattleKills==0&&b.BattleDeaths==0&&b.BattleDamageTaken==0,"new match resets totals");
  b.RecordBattleDeath(new NetworkId(100),a);Check(b.BattleDeaths==1&&a.BattleKills==1,"new match resets death deduplication");
  return count;
 }
}
'@
Add-Type -TypeDefinition $source
"Battle Tab/stat behavior checks passed: $([BattleTabChecks]::Run())"

$player=Get-Content (Join-Path $repo 'Assets/Script/Player.cs') -Raw
$hit=[regex]::Match($player,'(?ms)^    public void TakeDamage\(.*?^    \}').Value
if ($hit.IndexOf('RecordBattleDamage') -lt $hit.IndexOf('NowHp = Mathf.Max') -or
    $hit.IndexOf('RecordBattleDamage') -lt $hit.IndexOf('IsParrying') -or
    $hit -notmatch 'if \(testBot == null\)\s*PlayerData.FindPerkData\(Runner, Object.InputAuthority\)\?\.RecordBattleDamage\(attacker, hpBefore - NowHp\)') {throw 'Damage stats must use post-parry actual HP loss of real players.'}
$manager=Get-Content (Join-Path $repo 'Assets/Script/vibe/BattleManager.cs') -Raw
$roundReset=[regex]::Match($manager,'(?ms)^    public void ResetPlayersForNextRound\(.*?^    \}').Value
if ([regex]::Matches($manager,'\.ResetBattleStats\(').Count -ne 1 -or $roundReset.Contains('ResetBattleStats')) {throw 'Stats reset only once per match, never on round/respawn.'}
$death=[regex]::Match($manager,'(?ms)^    public void PlayerKilled\(.*?^    \}').Value
if ($death.IndexOf('RecordBattleDeath') -lt $death.IndexOf('pending.PlayerRef == deadPlayer')) {throw 'Duplicate death guard must precede statistics.'}
if ($stats -match '\[Rpc\(' -or $ui -match 'Cursor\.(visible|lockState)\s*=') {throw 'Stats must not add UI RPC polling or cursor takeover.'}

$scene=Get-Content (Join-Path $repo 'Assets/Scenes/Battle.unity') -Raw
$prefab=Get-Content (Join-Path $repo 'Assets/UI/Prefab/TabUI.prefab') -Raw
$blocks=@{}; foreach($m in [regex]::Matches($scene,'(?ms)^--- !u!\d+ &(\d+)(?: stripped)?\r?\n.*?(?=^--- !u!|\z)')) {
    $id=$m.Groups[1].Value;if($blocks.ContainsKey($id)){throw "Duplicate scene ID $id"};$blocks[$id]=$m.Value
}
$controller=$blocks['1515047385']
foreach($side in @('ownView','opponentView')) {
    $view=[regex]::Match($controller,'(?ms)^  '+$side+':\r?\n(.*?)(?=^  [a-z]|\z)').Groups[1].Value
    foreach($group in @('slot1Effects','slot2Effects','commonEffects')) {
        $rows=[regex]::Match($view,'(?ms)^    '+$group+':\r?\n(.*?)(?=^    [a-z]|\z)').Groups[1].Value
        $refs=@([regex]::Matches($rows,'title: \{fileID: (\d+)\}')|ForEach-Object{$_.Groups[1].Value})
        if($refs.Count -ne 2 -or $refs -contains '0'){throw "$side/$group needs two connected labels"}
        foreach($id in $refs){if(!$blocks.ContainsKey($id)){throw "Missing effect reference $id"}}
    }
    foreach($field in @('damageDealtText','damageTakenText','killsText','deathsText','flagIndicator')) {
        $id=[regex]::Match($view,$field+': \{fileID: (\d+)\}').Groups[1].Value
        if(!$blocks.ContainsKey($id)){throw "Missing stats/flag reference $side/$field"}
        $sourceId=[regex]::Match($blocks[$id],'m_CorrespondingSourceObject: \{fileID: (\d+)').Groups[1].Value
        if($prefab -notmatch ('&'+$sourceId+'\r?\n')){throw "Missing source object $side/$field"}
    }
}
'Battle Tab scene bindings and combat integration checks passed.'
