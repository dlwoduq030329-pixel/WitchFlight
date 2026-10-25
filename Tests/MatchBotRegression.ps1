# Production counter selection and previous-round snapshots; no live Unity/Fusion simulation.
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
function Method([string]$s,[string]$name) {
 $m=[regex]::Match($s,'(?ms)^    public \w+ '+$name+'\([^{}]*?\)\r?\n    \{.*?^    \}')
 if(!$m.Success){throw "Missing method $name"};$m.Value
}
$source=@'
using UnityEngine;
using System.Collections.Generic;
namespace UnityEngine {
 public class ScriptableObject {public static T CreateInstance<T>() where T:new()=>new T();}
 public class CreateAssetMenuAttribute:System.Attribute {public string menuName;}
 public class MinAttribute:System.Attribute {public MinAttribute(float n){}}
 public class TooltipAttribute:System.Attribute {public TooltipAttribute(string s){}}
 public struct Color {}
 public static class Resources {public static T Load<T>(string path) where T:class=>null;}
 public static class Random {public static float value=>.5f;public static int Range(int a,int b)=>a; public static Color ColorHSV(float a,float b,float c,float d,float e,float f)=>default;}
}
public struct PlayerConfig {
 public static PlayerConfig Default=>new(){hatIndex=1,broomIndex=2};
 public int hatIndex,broomIndex;public float bangsLength,bangsDirection,sideHairLength,ahogeLength;
 public Color hairColor,clothColor,eyeColor;
}
public class EyeTexturePresetTable {
 public static EyeTexturePresetTable Default=>null;public int Count=>0;
 public class Preset {public Color savedColorKey;}
 public bool TryGetPreset(int n,out Preset p){p=null;return false;}
}
public struct Participant {public int RawEncoded;}
public class NetworkObject {public bool IsValid=true,HasStateAuthority=true;}
public class PlayerData {
 public static List<PlayerData> PerkPlayers=new();
 public bool IsMatchBot,IsLoadoutInitialized=true;public object Runner;
 public Participant ParticipantRef;public int teamIndex,MagicChangeRound;
 public NetworkObject Object=new();public MagicType magic1,magic2,MagicChangeBase1,MagicChangeBase2;
'@
$data=Get-Content Assets/Script/PlayerData.cs -Raw
$source+=Method $data 'BeginRoundMagicChange'
$source+=Method (Get-Content Assets/Script/PlayerData.MatchBot.cs -Raw) 'ChooseBotCounters'
$source+='}'
foreach($name in @('HatType','BroomType','MagicType')){$source+=[regex]::Match($data,'public enum '+$name+' \{[^}]+\}').Value}
$source+=(Get-Content Assets/Script/MatchBotSettings.cs -Raw)-replace '(?m)^using UnityEngine;\r?\n',''
$source+=@'
public class BattleManager {
 public static BattleManager Instance=new();public int Limit=2;
 public bool TryChangeRoundMagic(PlayerData d,int round,MagicType a,MagicType b,out string error){
  error="";if((a!=d.MagicChangeBase1?1:0)+(b!=d.MagicChangeBase2?1:0)>Limit)return false;
  d.magic1=a;d.magic2=b;return true;
 }
}
public static class BotChecks {
 static int n;static void Check(bool v,string message){if(!v)throw new System.Exception(message);n++;}
 static bool Damage(MagicType t)=>t!=MagicType.None&&t!=MagicType.Flare&&t!=MagicType.Smoke&&t!=MagicType.Healing&&t!=MagicType.Binding;
 public static int Run(){
  var table=MatchBotSettings.Default;
  for(int i=1;i<=12;i++)for(int j=1;j<=12;j++){
   table.CounterLoadout((MagicType)i,(MagicType)j,out var a,out var b);
   Check(a!=b,"counter pair must remain distinct");Check(Damage(a)||Damage(b),"bot always has a damaging spell");
  }
  Check(table.CounterFor(MagicType.Fire)==MagicType.Flare,"projectile counter");
  Check(table.CounterFor(MagicType.Vision)==MagicType.Smoke,"hitscan counter");
  var custom=new MatchBotSettings{counters=new[]{new MatchBotSettings.Counter(MagicType.Fire,MagicType.Ice)}};
  Check(custom.CounterFor(MagicType.Fire)==MagicType.Ice,"editable mapping");
  custom.counters=null;Check(custom.CounterFor(MagicType.Fire)==MagicType.Fire,"empty table fallback");
  var runner=new object();var human=new PlayerData{Runner=runner,teamIndex=1,ParticipantRef=new(){RawEncoded=1},magic1=MagicType.Fire,magic2=MagicType.Vision};
  var bot=new PlayerData{Runner=runner,teamIndex=2,IsMatchBot=true};PlayerData.PerkPlayers.Add(human);PlayerData.PerkPlayers.Add(bot);
  bot.ChooseBotCounters(false);Check(bot.magic1==MagicType.Flare&&bot.magic2==MagicType.Vision,"round one uses current loadout with offensive fallback");
  human.BeginRoundMagicChange(1);bot.BeginRoundMagicChange(1);
  human.magic1=MagicType.Dark;human.magic2=MagicType.Mine;
  bot.ChooseBotCounters(true);Check(bot.magic1==MagicType.Flare&&bot.magic2==MagicType.Vision,"round two uses snapshot, not new selection");
  human.BeginRoundMagicChange(2);bot.BeginRoundMagicChange(2);bot.ChooseBotCounters(true);
  Check(bot.magic1==MagicType.Healing&&bot.magic2==MagicType.Vision,"next round advances counter snapshot");
  human.magic1=MagicType.Healing;human.magic2=MagicType.Razier;human.BeginRoundMagicChange(3);bot.BeginRoundMagicChange(3);BattleManager.Instance.Limit=1;
  bot.ChooseBotCounters(true);Check(bot.magic1==MagicType.Fire&&bot.magic2==MagicType.Vision,"one-slot swap limit respected");
  bot.Object.HasStateAuthority=false;bot.magic1=MagicType.Dark;bot.ChooseBotCounters(false);Check(bot.magic1==MagicType.Dark,"proxy cannot pick bot loadout");
  bot.Object.HasStateAuthority=true;BattleManager.Instance.Limit=2;
  bot.ParticipantRef=new(){RawEncoded=1025};human.magic1=MagicType.Fire;human.magic2=MagicType.Ice;
  var bot2=new PlayerData{Runner=runner,teamIndex=2,IsMatchBot=true,ParticipantRef=new(){RawEncoded=1026}};
  PlayerData.PerkPlayers.Add(bot2);
  bot2.ChooseBotCounters(false);bot.ChooseBotCounters(false);
  Check(bot.magic1==MagicType.Flare&&bot2.magic1==MagicType.Flare,"both bots retain primary counter");
  Check(bot.magic2!=bot2.magic2,"same human loadout gets distinct supporting spells");
  var saved=bot2.magic2;PlayerData.PerkPlayers.Reverse();bot.ChooseBotCounters(false);bot2.ChooseBotCounters(false);
  Check(bot2.magic2==saved,"selection order does not affect diversity");
  var human2=new PlayerData{Runner=runner,teamIndex=1,ParticipantRef=new(){RawEncoded=2},magic1=MagicType.Dark,magic2=MagicType.Mine};
  PlayerData.PerkPlayers.Add(human2);bot2.ChooseBotCounters(false);
  Check(bot2.magic1==MagicType.Healing,"second bot counters second human");
  human.BeginRoundMagicChange(1);human2.BeginRoundMagicChange(1);bot.BeginRoundMagicChange(1);bot2.BeginRoundMagicChange(1);
  human2.magic1=MagicType.Thunder;human2.magic2=MagicType.Razier;bot2.ChooseBotCounters(true);
  Check(bot2.magic1==MagicType.Healing,"assigned opponent uses previous-round snapshot");
  var cosmetics=new List<PlayerConfig>();
  var one=MatchBotSettings.RandomAppearance(cosmetics);cosmetics.Add(one);
  var two=MatchBotSettings.RandomAppearance(cosmetics);cosmetics.Add(two);
  Check(one.hatIndex!=two.hatIndex&&one.broomIndex!=two.broomIndex,"teammate equipment differs even with identical random results");
  var three=MatchBotSettings.RandomAppearance(cosmetics);cosmetics.Add(three);
  Check(three.hatIndex!=one.hatIndex&&three.hatIndex!=two.hatIndex,"all available hats used before repeating");
  var four=MatchBotSettings.RandomAppearance(cosmetics);
  Check(four.hatIndex>=1&&four.hatIndex<=3&&four.broomIndex>=1&&four.broomIndex<=3,"exhausted pool safely reuses valid gear");
  table.availableHats=new[]{HatType.Classic,HatType.Classic};table.availableBrooms=new[]{BroomType.Standard};
  one=MatchBotSettings.RandomAppearance();two=MatchBotSettings.RandomAppearance(new[]{one});
  Check(one.hatIndex==two.hatIndex&&one.broomIndex==two.broomIndex,"single-option pools terminate and preserve authored gear");
  table.availableHats=null;table.availableBrooms=null;
  one=MatchBotSettings.RandomAppearance();Check(one.hatIndex==1&&one.broomIndex==2,"empty gear pools use safe defaults");
  return n;
 }
}
'@
Add-Type -TypeDefinition $source
"Bot counters, damaging loadouts, round snapshots and swap-limit checks passed: $([BotChecks]::Run())"
