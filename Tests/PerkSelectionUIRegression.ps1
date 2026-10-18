# Executes production selection/notification methods using offline UI/network doubles.
# Actual Unity rendering, Animator playback and Fusion RPC transport still need Play Mode.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$ui = Get-Content (Join-Path $repo 'Assets/Script/BattlePerkUI.cs') -Raw
function Block([string]$signature) {
    $m = [regex]::Match($ui, '(?ms)^    ' + [regex]::Escape($signature) + '.*?^    \}')
    if (!$m.Success) { throw "Missing method: $signature" }; $m.Value
}
function Line([string]$signature) {
    $m = [regex]::Match($ui, '(?m)^    ' + [regex]::Escape($signature) + '[^\r\n]*')
    if (!$m.Success) { throw "Missing expression: $signature" }; $m.Value
}
$source = @'
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
namespace UnityEngine {
 public class TooltipAttribute:Attribute {public TooltipAttribute(string s){}}
 public class Sprite {public int id;public Sprite(int i){id=i;}}
 public class GameObject {public bool activeSelf;public int toggles;public void SetActive(bool b){activeSelf=b;toggles++;}}
 public static class Application {public static bool isFocused=true;}
 public static class Time {public static float unscaledTime;}
}
namespace UnityEngine.UI {public class Image {public Sprite sprite;public bool enabled;}}
namespace TMPro {public class TMP_Text {public string text;}}
public enum MagicType {None,Fire,Ice,Vision,Thunder,Healing,Binding,Dark,Curse,Mine,Razier,Flare,Smoke}
public enum PerkId {None=0}
public enum BattleStartPhase {Playing,Countdown,Changing,Ended}
public class BattleManager {public static BattleManager Instance=new();public BattleStartPhase Phase;}
public static class CombatPresentation {public static bool MenuOpen;}
public class NetworkObject {public bool IsValid=true;}
public class PerkDefinition {public string displayName,description;public Sprite icon;public float amount,secondaryAmount,seconds;}
public class PerkStatTable {
 public static PerkStatTable Default=new();public Dictionary<PerkId,PerkDefinition> entries=new();
 public PerkDefinition Get(PerkId id)=>entries.TryGetValue(id,out var p)?p:null;
}
public class MagicStatTable {public Sprite GetIcon(MagicType magic)=>new Sprite((int)magic);}
public class PlayerData {
 public NetworkObject Object=new();public bool PerksInitialized=true;
 public int PerkChoicesEarned,PerkChoicesUsed,PerkRevision;public MagicType magic1=MagicType.Fire,magic2=MagicType.Binding;
 public PerkId left=(PerkId)1,right=(PerkId)11;
 public PerkStatTable PerkTable=>PerkStatTable.Default;
 public int PendingPerkChoices=>Math.Max(0,PerkChoicesEarned-PerkChoicesUsed);
 public bool CanSelectPerks=>PendingPerkChoices>0;
 public PerkId GetPerkCandidate(bool r)=>PendingPerkChoices>0?(r?right:left):PerkId.None;
}
'@
$source += [regex]::Match($ui, '(?ms)\[Serializable\]\r?\npublic sealed class PerkCardView.*?^\}').Value
$source += [regex]::Replace((Get-Content (Join-Path $repo 'Assets/Script/PerkRules.cs') -Raw), '(?m)^using .*;\r?\n', '')
$source += @'
public class SelectionProbe {
 public PlayerData local=new();public bool requestPending;public string selectionKey;
 public int benefitDismissedAtChoices=-1,benefitObservedChoices=-1,awaitedRevision=-1;public float feedbackAt;
 public Image acquiredIcon=new();public TMP_Text acquiredText=new();
 public GameObject benefitAnimation=new(),selectionRoot=new(),comparisonRoot=new(),acquiredRoot=new();
 public MagicStatTable magicTable=new();public TMP_Text availableChoicesText=new();
 public PerkCardView leftCandidate=Card(),rightCandidate=Card();
 public static PerkCardView Card()=>new(){icon=new Image(),title=new TMP_Text(),description=new TMP_Text(),hint=new TMP_Text()};
'@
$methods = @()
foreach ($s in 'private void UpdateBenefitAvailability(', 'private void OnResult(', 'private void UpdateCandidates(', 'private void Fill(', 'private Sprite Icon(') { $methods += Block $s }
foreach ($s in 'private bool Ready(', 'private static void Text(', 'private static void SetIcon(', 'private static void Show(', 'private void OnApplicationFocus(') { $methods += Line $s }
$methods += [regex]::Match($ui, '(?ms)^    private bool Allowed =>.*?;').Value
$methods += [regex]::Match($ui, '(?ms)^    public static string Description\(.*?;').Value
$source += (($methods -join "`n") -replace '\bprivate\b', 'public') + "`n}`n"
$source += @'
public static class SelectionChecks {
 static int count;
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);count++;}
 public static int Run(){
  var p=new SelectionProbe();p.UpdateBenefitAvailability();Check(!p.benefitAnimation.activeSelf,"initially no choice");
  p.local.PerkChoicesEarned=1;p.UpdateBenefitAvailability();Check(p.benefitAnimation.activeSelf,"level-up lights notification");
  int toggles=p.benefitAnimation.toggles;p.UpdateBenefitAvailability();Check(p.benefitAnimation.toggles==toggles,"do not restart Animator every frame");
  p.requestPending=true;p.UpdateBenefitAvailability();Check(!p.benefitAnimation.activeSelf,"waiting for authority hides availability");
  p.requestPending=false;p.UpdateBenefitAvailability();Check(p.benefitAnimation.activeSelf,"rejection/timeout restores available choice");
  p.local.PerkChoicesUsed=1;p.UpdateBenefitAvailability();Check(!p.benefitAnimation.activeSelf,"last choice consumed hides notification");
  p.local.PerkChoicesEarned=3;p.UpdateBenefitAvailability();Check(p.benefitAnimation.activeSelf,"unspent extra choices remain available");
  p.OnResult(false,PerkId.None,1);p.UpdateBenefitAvailability();Check(p.benefitAnimation.activeSelf,"rejected selection does not dismiss notification");
  p.OnResult(true,(PerkId)1,2);Check(!p.benefitAnimation.activeSelf,"accepted choice immediately hides notification before snapshot update");
  p.requestPending=false;p.UpdateBenefitAvailability();Check(!p.benefitAnimation.activeSelf,"remaining choices do not re-light notification");
  p.local.PerkChoicesUsed=2;p.local.PerkRevision=2;p.UpdateBenefitAvailability();Check(!p.benefitAnimation.activeSelf,"late replicated selection does not re-light notification");
  p.OnApplicationFocus(false);p.UpdateBenefitAvailability();Check(!p.benefitAnimation.activeSelf,"focus return does not re-arm dismissed notification");
  p.local.magic1=MagicType.Dark;p.local.PerkRevision++;p.UpdateBenefitAvailability();Check(!p.benefitAnimation.activeSelf,"loadout change does not re-arm dismissed notification");
  p.local.PerkChoicesEarned=4;p.UpdateBenefitAvailability();Check(p.benefitAnimation.activeSelf,"next earned choice re-arms notification");
  p.OnResult(true,(PerkId)1,3);p.local.PerkChoicesEarned=p.local.PerkChoicesUsed=0;p.UpdateBenefitAvailability();Check(!p.benefitAnimation.activeSelf,"new match starts hidden");
  p.local.PerkChoicesEarned=1;p.UpdateBenefitAvailability();Check(p.benefitAnimation.activeSelf,"first choice in a new match re-arms notification");
  BattleManager.Instance.Phase=BattleStartPhase.Countdown;p.UpdateBenefitAvailability();Check(p.benefitAnimation.activeSelf,"countdown choices supported");
  BattleManager.Instance.Phase=BattleStartPhase.Changing;p.UpdateBenefitAvailability();Check(!p.benefitAnimation.activeSelf,"intermission not selectable");
  BattleManager.Instance.Phase=BattleStartPhase.Ended;p.UpdateBenefitAvailability();Check(!p.benefitAnimation.activeSelf,"match end hides notification");
  BattleManager.Instance.Phase=BattleStartPhase.Playing;CombatPresentation.MenuOpen=true;p.UpdateBenefitAvailability();Check(!p.benefitAnimation.activeSelf,"menu hides notification");
  CombatPresentation.MenuOpen=false;Application.isFocused=false;p.UpdateBenefitAvailability();Check(!p.benefitAnimation.activeSelf,"unfocused hides notification");Application.isFocused=true;
  p.local.PerksInitialized=false;p.UpdateBenefitAvailability();Check(!p.benefitAnimation.activeSelf,"uninitialized player ignored");p.local.PerksInitialized=true;
  p.local.Object.IsValid=false;p.UpdateBenefitAvailability();Check(!p.benefitAnimation.activeSelf,"invalid player ignored");p.local.Object.IsValid=true;
  p.local.left=p.local.right=PerkId.None;p.UpdateBenefitAvailability();Check(!p.benefitAnimation.activeSelf,"no valid candidates cannot advertise availability");
  p.local.right=(PerkId)11;p.UpdateBenefitAvailability();Check(p.benefitAnimation.activeSelf,"only right candidate still selectable");
  p.UpdateBenefitAvailability();p.OnApplicationFocus(false);Check(!p.benefitAnimation.activeSelf,"focus loss immediately hides notification");
  p.benefitAnimation=null;p.UpdateBenefitAvailability();Check(true,"notification assignment is optional");
  p.local=null;p.UpdateBenefitAvailability();Check(true,"player absent safe");p.local=new PlayerData{PerkChoicesEarned=1};
  var table=PerkStatTable.Default;
  table.entries[(PerkId)1]=new(){displayName="고열 잔불",description="{seconds}초 총 {amount} 피해",seconds=2,amount=8,icon=new Sprite(101)};
  table.entries[(PerkId)11]=new(){displayName="신속 결박",description="록온 {percent}% 감소",amount=.2f};
  p.UpdateCandidates();Check(p.leftCandidate.title.text=="고열 잔불"&&p.leftCandidate.description.text=="2초 총 8 피해","left name and resolved description from table");
  Check(p.leftCandidate.icon.sprite.id==101,"explicit perk sprite wins");
  Check(p.rightCandidate.title.text=="신속 결박"&&p.rightCandidate.description.text=="록온 20% 감소","right slot gets its own perk");
  Check(p.rightCandidate.icon.sprite.id==6,"missing perk sprite uses correct magic icon");
  table.entries[(PerkId)25]=new(){displayName="수호의 매듭",description="HP {amount} 증가",amount=20,icon=new Sprite(125)};
  p.local.left=(PerkId)25;p.local.PerkRevision++;p.UpdateCandidates();
  Check(p.leftCandidate.title.text=="수호의 매듭"&&p.leftCandidate.hint.text.Contains("공용 증강")&&p.leftCandidate.icon.sprite.id==125,"common perk updates name, hint and icon");
  p.local.PerkChoicesUsed=1;p.local.PerkRevision++;p.UpdateCandidates();
  Check(!p.leftCandidate.icon.enabled&&p.leftCandidate.title.text=="—"&&p.leftCandidate.description.text.Contains("없습니다"),"spent choices clear stale card");
  p.local.PerkChoicesEarned=2;p.UpdateCandidates();Check(p.leftCandidate.title.text=="수호의 매듭","level-up refreshes even without revision change");
  p.Fill(null,(PerkId)1,"",p.local);p.Fill(new PerkCardView(),(PerkId)1,"",p.local);Check(true,"optional fields are safe");
  return count;
 }
}
'@
Add-Type -TypeDefinition $source
"Perk selection/notification behavior checks passed: $([SelectionChecks]::Run())"

$scene = Get-Content (Join-Path $repo 'Assets/Scenes/Battle.unity') -Raw
$prefab = Get-Content (Join-Path $repo 'Assets/UI/Prefab/SpecialBenefitsSelectUI.prefab') -Raw
$blocks = @{}
foreach ($m in [regex]::Matches($scene, '(?ms)^--- !u!\d+ &(\d+)[^\r\n]*\r?\n.*?(?=^--- !u!|\z)')) {
    if ($blocks.ContainsKey($m.Groups[1].Value)) { throw 'Duplicate scene fileID' }
    $blocks[$m.Groups[1].Value] = $m.Value
}
$component = $blocks['1515047385']
if ($component -notmatch 'benefitAnimation: \{fileID: 1895097774\}' -or $blocks['1895097774'] -notmatch 'm_Name: BenefitAnimation') { throw 'Availability object not connected' }
if ($component -notmatch 'selectionRoot: \{fileID: 3395347641043298459\}') { throw 'Custom selection root not connected' }
$sources = @('5407950791167252050','2976169627768467571','8687619035543546505','1505709971816660896','3901042315884856896','4031069636765086762','2462267218994597087')
for ($i=0; $i -lt $sources.Count; $i++) {
    $id = (3395347641043298459L + $i).ToString()
    if (!$blocks.ContainsKey($id) -or $component -notmatch ('fileID: '+$id+'\}') -or
        $blocks[$id] -notmatch ('m_CorrespondingSourceObject: \{fileID: '+$sources[$i]+', guid: bbd98078509b20b4da6b1b4a825ec7b4') -or
        $blocks[$id] -notmatch 'm_PrefabInstance: \{fileID: 3395347641043298457\}' -or
        $prefab -notmatch ('&'+$sources[$i]+'\r?\n')) { throw "Invalid selection binding: $id" }
}
if ($ui -notmatch 'UpdateFeedback\(\); UpdateBenefitAvailability\(\);' -or
    $ui -notmatch '(?s)private void OnDisable\(\).*?Show\(benefitAnimation,false\);' -or
    $ui -notmatch 'if \(benefitAnimation != null\) ReadOnly\(benefitAnimation\);') { throw 'Notification lifecycle or non-blocking UI hook missing' }
'Custom root, left/right icon/name/description, notification and lifecycle bindings passed.'
