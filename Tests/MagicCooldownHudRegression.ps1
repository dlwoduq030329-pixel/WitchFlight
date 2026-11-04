# Exercise the complete production cooldown UI and Player timer accessors with offline Unity doubles.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$ui = Get-Content Assets/Script/BattleMagicUI.cs -Raw
$player = Get-Content Assets/Script/Player.cs -Raw
$enum = [regex]::Match((Get-Content Assets/Script/PlayerData.cs -Raw), 'public enum MagicType\s*\{[^}]+\}').Value
function Method([string]$name) {
    $match = [regex]::Match($player, '(?ms)^    public [\w<>]+ '+$name+'\([^{}]*?\)\r?\n    \{.*?^    \}')
    if (!$match.Success) {throw "Missing method: $name"}
    $match.Value
}
$source = @'
using System;
namespace UnityEngine {
 public class Transform {
  public Transform parent;
  public GameObject gameObject;public System.Collections.Generic.Dictionary<string,Transform> children=new();
  public Transform Find(string name)=>children.TryGetValue(name,out var child)?child:null;
  public bool IsChildOf(Transform other)=>this==other||(parent!=null&&parent.IsChildOf(other));
 }
 public class GameObject {
  public bool activeInHierarchy=>activeSelf;
  public ParticleSystem[] particles=Array.Empty<ParticleSystem>();
  public T[] GetComponentsInChildren<T>(bool includeInactive)=>particles as T[];
  public Transform transform=new();public bool activeSelf=true;public int activeChanges;
  public GameObject(){transform.gameObject=this;}
  public void SetActive(bool value){activeSelf=value;activeChanges++;}
 }
 public class MonoBehaviour {public GameObject gameObject=new();public Transform transform=>gameObject.transform;}
 public enum ParticleSystemStopBehavior {StopEmittingAndClear}
 public class ParticleSystem {public GameObject gameObject=new();public int plays,stops;public void Play(bool children){plays++;}public void Stop(bool children,ParticleSystemStopBehavior mode){stops++;}}
 public class Sprite {}
 public class SerializeField:Attribute {}
 public class DisallowMultipleComponent:Attribute {}
 public class HeaderAttribute:Attribute {public HeaderAttribute(string text){}}
 public class TooltipAttribute:Attribute {public TooltipAttribute(string text){}}
 public static class Mathf {
  public static float Max(float a,float b)=>Math.Max(a,b);
  public static float Clamp01(float x)=>Math.Clamp(x,0,1);
  public static int CeilToInt(float x)=>(int)Math.Ceiling(x);
 }
}
namespace UnityEngine.Serialization {
 public class FormerlySerializedAsAttribute:Attribute {public FormerlySerializedAsAttribute(string text){}}
}
namespace UnityEngine.UI {
 public class Image {public UnityEngine.Sprite sprite;public bool enabled=true;}
 public class Slider {
  public UnityEngine.GameObject gameObject=new();public UnityEngine.Transform transform=>gameObject.transform;
  public float minValue,maxValue,value;public bool interactable,wholeNumbers;
  public void SetValueWithoutNotify(float x){value=x;}
 }
}
namespace TMPro {
 public class TextMeshProUGUI {
  public UnityEngine.GameObject gameObject=new();public UnityEngine.Transform transform=>gameObject.transform;public bool enabled=true;
  private string value="authored placeholder";public int writes;
  public string text {get=>value;set{this.value=value;writes++;}}
  public void SetText(string format,float arg){text=arg.ToString(System.Globalization.CultureInfo.InvariantCulture);}
 }
}
namespace CooldownHudChecks {
using UnityEngine;
public class NetworkObject {public bool IsValid=true,HasInputAuthority=true;}
public class RunnerState {public bool IsRunning=true;public float Time;}
public struct TickTimer {
 public float end;
 public float? RemainingTime(RunnerState runner)=>end>0?Math.Max(0,end-runner.Time):(float?)null;
}
public class TimerArray {
 private TickTimer[] items=new TickTimer[13];public int Length=>items.Length;
 public TickTimer this[int i] {get=>items[i];set=>items[i]=value;}
}
public class MagicStatEntry {public float cooldownSeconds=10;}
public class MagicStatTable {
 public static MagicStatTable Default=new();public Sprite parryIcon=new();public float parryCooldownSeconds=.3f;
 public MagicStatEntry GetStats(MagicType magic)=>new();public Sprite GetIcon(MagicType magic)=>null;
}
public class BattleManager {public static BattleManager Instance=new();public bool IsGameplayActive=true;}
'@
$source += $enum
$source += @'
public class Player {
 public MagicStatEntry GetPerkMagicStats(MagicType magic,int slot)=>magicStatTable!=null?magicStatTable.GetStats(magic):new MagicStatEntry{cooldownSeconds=0};
 public static Player LocalPlayer;
 public NetworkObject Object=new();public RunnerState Runner=new();public bool IsAlive=true;
 public int CurrentMagicSlot=1;
 public MagicType Magic1=MagicType.Fire,Magic2=MagicType.Ice;
 public TimerArray MagicCooldowns=new();public TickTimer ParryCooldown;
 public float parryCooldownSeconds=.3f;public MagicStatTable magicStatTable=new();
'@
$source += (Method 'GetMagicInSlot') + (Method 'GetMagicCooldownRemaining') + (Method 'GetMagicCooldownDuration')
$source += "`n}}`nnamespace CooldownHudChecks {`n" + ($ui -replace '\bprivate\b','public') + "`n}`n"
$source += @'
namespace CooldownHudChecks {
public static class Tests {
 static int count;
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;}
 public static int Run(){
  var p=new Player();Player.LocalPlayer=p;
  var ui=new BattleMagicUI {
   magic1CooldownText=new(),magic2CooldownText=new(),parryCooldownText=new(),
   magic1CooldownSlider=new(),magic2CooldownSlider=new(),parryCooldownSlider=new()
  };
  ui.OnEnable();
  Check(ui.magic1CooldownText.text==""&&ui.magic2CooldownText.text==""&&ui.parryCooldownText.text=="","ready spells clear authored placeholders");
  Check(!ui.magic1CooldownText.gameObject.activeSelf&&!ui.magic2CooldownText.gameObject.activeSelf&&!ui.parryCooldownText.gameObject.activeSelf,"all ready text objects are inactive");
  Check(ui.gameObject.activeSelf,"hiding cooldown texts leaves controller active");
  p.Runner.Time=10;p.MagicCooldowns[(int)MagicType.Fire]=new(){end=13.2f};
  p.MagicCooldowns[(int)MagicType.Ice]=new(){end=10.2f};p.ParryCooldown=new(){end=10.3f};ui.Update();
  Check(ui.magic1CooldownText.text=="4"&&ui.magic2CooldownText.text=="1"&&ui.parryCooldownText.text=="1","each slot shows ceil of replicated remaining seconds");
  Check(ui.magic1CooldownText.gameObject.activeSelf&&ui.magic2CooldownText.gameObject.activeSelf&&ui.parryCooldownText.gameObject.activeSelf&&ui.magic1CooldownText.enabled,"cooldowns activate previously hidden TMP objects");
  Check(ui.magic1CooldownSlider.value==0,"existing slider still starts empty on observed cast");
  p.Runner.Time=10.21f;ui.Update();
  Check(ui.magic1CooldownText.text=="3"&&ui.magic2CooldownText.text==""&&ui.parryCooldownText.text=="1","independent expiry and 3-2-1 countdown");
  Check(ui.magic1CooldownText.gameObject.activeSelf&&!ui.magic2CooldownText.gameObject.activeSelf&&ui.parryCooldownText.gameObject.activeSelf,"only the expired slot is deactivated");
  int writes=ui.magic1CooldownText.writes;
  int activations=ui.magic1CooldownText.gameObject.activeChanges;
  for(int frame=0;frame<120;frame++)ui.Update();
  Check(ui.magic1CooldownText.writes==writes,"unchanged displayed second does not rebuild TMP text");
  Check(ui.magic1CooldownText.gameObject.activeChanges==activations,"unchanged cooldown does not repeatedly SetActive or restart animations");
  p.Runner.Time=10.31f;ui.Update();Check(ui.parryCooldownText.text=="","subsecond parry clears at real expiry");
  p.Runner.Time=11.21f;ui.Update();Check(ui.magic1CooldownText.text=="2","countdown reaches two");
  ui.OnDisable();Check(ui.magic1CooldownText.text=="","disable clears old cooldown");
  Check(!ui.magic1CooldownText.gameObject.activeSelf,"controller disable also deactivates countdown object");
  p.Runner.Time=12.21f;ui.OnEnable();Check(ui.magic1CooldownText.text=="1","reenable resumes actual timer, not a new countdown");
  Check(ui.magic1CooldownText.gameObject.activeSelf,"reenable restores still-running countdown visibility");
  Check(ui.magic1CooldownSlider.value>0.9f,"reenable also resumes slider progress");
  p.IsAlive=false;ui.Update();Check(ui.magic1CooldownText.text=="","death hides cooldown text");
  Check(!ui.magic1CooldownText.gameObject.activeSelf,"death deactivates cooldown object");
  p.IsAlive=true;ui.Update();Check(ui.magic1CooldownText.text=="1","living player resumes remaining cooldown");
  p.Magic2=MagicType.Fire;ui.Update();
  Check(ui.magic1CooldownText.text=="1"&&ui.magic2CooldownText.text=="1","same magic in both slots shares actual spell timer");
  p.Magic1=MagicType.Ice;ui.Update();
  Check(ui.magic1CooldownText.text==""&&ui.magic2CooldownText.text=="1","loadout change resolves by magic ID, not old slot timer");
  p.Runner.Time=13.2f;ui.Update();Check(ui.magic2CooldownText.text=="","exact expiry clears number, never shows zero");
  p.MagicCooldowns[(int)MagicType.Fire]=new(){end=16};ui.Update();Check(ui.magic2CooldownText.text=="3","new timer restores text");
  p.MagicCooldowns[(int)MagicType.Fire]=default;ui.Update();Check(ui.magic2CooldownText.text=="","server timer reset immediately clears number");
  p.MagicCooldowns[(int)MagicType.Fire]=new(){end=16};ui.Update();
  BattleManager.Instance.IsGameplayActive=false;ui.Update();Check(ui.magic2CooldownText.text=="","intermission hides stale cooldown");
  Check(!ui.magic2CooldownText.gameObject.activeSelf,"intermission deactivates cooldown object");
  BattleManager.Instance.IsGameplayActive=true;ui.Update();Check(ui.magic2CooldownText.text=="3","next active phase reads current timer");
  p.Object.HasInputAuthority=false;ui.Update();Check(ui.magic2CooldownText.text=="","enemy cannot drive local cooldown text");p.Object.HasInputAuthority=true;
  p.Runner.IsRunning=false;ui.Update();Check(ui.magic2CooldownText.text=="","disconnect clears cooldown");p.Runner.IsRunning=true;
  p.Object.IsValid=false;ui.Update();Check(ui.magic2CooldownText.text=="","despawn is safe");p.Object.IsValid=true;
  Player.LocalPlayer=new Player();ui.Update();Check(ui.magic2CooldownText.text=="","respawn owner change has no stale cooldown");
  Player.LocalPlayer=null;ui.Update();Check(ui.owner==null,"missing local player clears owner");
  Player.LocalPlayer=p;
  var optional=new BattleMagicUI();optional.OnEnable();optional.Update();optional.OnDisable();
  Check(true,"unassigned TMP texts, images and sliders are safe");
  ui.magic1CooldownSlider=ui.magic2CooldownSlider=ui.parryCooldownSlider=null;ui.OnEnable();
  Check(ui.magic2CooldownText.text=="3","TMP countdown works without sliders");
  p.Runner=null;ui.magiccoolDown();Check(ui.magic2CooldownText.text=="","manual UnityEvent refresh also clears invalid owner");
  Check(!ui.magic2CooldownText.gameObject.activeSelf,"invalid owner deactivates cooldown object");
  var sameObject=new TMPro.TextMeshProUGUI{gameObject=ui.gameObject};ui.SetCooldownText(sameObject,0,1);
  Check(ui.gameObject.activeSelf&&!sameObject.enabled,"accidental controller assignment hides only TMP, not its updater");
  ui.SetCooldownText(sameObject,2,1);Check(sameObject.enabled&&ui.gameObject.activeSelf,"controller-assigned TMP can show again");
  var ancestor=new TMPro.TextMeshProUGUI();ui.transform.parent=ancestor.transform;ui.SetCooldownText(ancestor,0,1);
  Check(ancestor.gameObject.activeSelf&&!ancestor.enabled,"ancestor assignment cannot disable the entire battle UI");
  ui.transform.parent=null;
  p.Runner=new();p.IsAlive=true;Player.LocalPlayer=p;
  var selection=new BattleMagicUI {magic1SelectedFx=new(),magic2SelectedFx=new()};
  var firstFx=new UnityEngine.ParticleSystem();var secondFx=new UnityEngine.ParticleSystem();
  selection.magic1SelectedFx.particles=new[]{firstFx};selection.magic2SelectedFx.particles=new[]{secondFx};
  selection.OnEnable();
  Check(firstFx.plays==1&&secondFx.plays==0,"explicit play for selected feather with PlayOnAwake off");
  Check(selection.magic1SelectedFx.activeSelf&&!selection.magic2SelectedFx.activeSelf,"slot one activates only its FX");
  p.CurrentMagicSlot=2;selection.Update();Check(!selection.magic1SelectedFx.activeSelf&&selection.magic2SelectedFx.activeSelf,"number two swaps FX");
  int fxWrites=selection.magic2SelectedFx.activeChanges;selection.Update();
  Check(firstFx.stops==2&&secondFx.plays==1,"switch clears old particles and plays new particles only once");
  Check(selection.magic2SelectedFx.activeChanges==fxWrites,"unchanged selection never restarts FX");
  p.MagicCooldowns[(int)MagicType.Fire]=new(){end=3};
  selection.Update();Check(selection.magic2SelectedFx.activeSelf,"cooldown does not clear slot selection");
  p.CurrentMagicSlot=3;selection.Update();Check(!selection.magic1SelectedFx.activeSelf&&!selection.magic2SelectedFx.activeSelf,"parry slot clears both FX");
  p.CurrentMagicSlot=1;p.IsAlive=false;selection.Update();Check(!selection.magic1SelectedFx.activeSelf,"death clears selection FX");
  p.IsAlive=true;selection.Update();Check(selection.magic1SelectedFx.activeSelf,"respawn/current slot restores FX");
  selection.OnDisable();Check(!selection.magic1SelectedFx.activeSelf&&!selection.magic2SelectedFx.activeSelf,"HUD disable clears FX");
  selection.SetSelectedFx(selection.gameObject,false);Check(selection.gameObject.activeSelf,"FX assignment cannot disable its controller");
  var slider=new UnityEngine.UI.Slider();var fx=new UnityEngine.GameObject();slider.transform.children["FX_CircularFeather_B"]=fx.transform;fx.transform.parent=slider.transform;
  var auto=new BattleMagicUI{magic1CooldownSlider=slider};auto.OnEnable();Check(auto.magic1SelectedFx==fx,"named slider child binds automatically");
  auto.SetSelectedFx(slider.gameObject,false);Check(slider.gameObject.activeSelf,"wrong slider assignment cannot disable slider");
  return count;
 }
}}
'@
Add-Type -TypeDefinition $source
"Magic cooldown HUD checks passed: $([CooldownHudChecks.Tests]::Run())"
# Inspector bindings are optional additions; do not rename existing serialized slider/image fields.
foreach ($name in 'magic1CooldownText','magic2CooldownText','parryCooldownText') {
    if ($ui -notmatch "\[SerializeField\] private TextMeshProUGUI $name;") {throw "Missing TMP Inspector field: $name"}
}
$tick = [regex]::Match($player, '(?ms)^    public override void FixedUpdateNetwork\(\).*?^    \}').Value
if ($tick.IndexOf('if (!CanAcquireMagicTarget()) ClearLockTargetInternal();') -lt 0 -or
    $tick.IndexOf('if (!CanAcquireMagicTarget()) ClearLockTargetInternal();') -gt $tick.IndexOf('if (!GetInput(')) {
    throw 'No-input ticks must also invalidate unusable locks before mana regeneration'
}
'TMP Inspector bindings and no-input lock invalidation checks passed.'
