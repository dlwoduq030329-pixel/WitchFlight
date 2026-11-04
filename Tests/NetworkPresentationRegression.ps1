# Offline tests against production methods with Unity/Fusion stubs; not a Play Mode test.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$equipmentSource = Get-Content Assets/Script/PlayerEquipment.cs -Raw
$gridSource = Get-Content Assets/UI/Scripts/GridHPBar.cs -Raw
$playerSource = Get-Content Assets/Script/Player.cs -Raw
function Method([string]$name) {
    $match = [regex]::Match($playerSource, '(?ms)^    private void ' + $name + '\(\)\r?\n    \{.*?^    \}')
    if (!$match.Success) { throw "Method missing: $name" }
    return $match.Value
}
$stubs = @'
namespace UnityEngine {
 public class MonoBehaviour {public bool isActiveAndEnabled=true; protected static UnityEngine.UI.Image Instantiate(UnityEngine.UI.Image t,Transform p,bool world){var c=new UnityEngine.UI.Image();c.color=t.color;p.Add(c.transform);return c;}}
 public class SerializeField:System.Attribute {}
 public class HeaderAttribute:System.Attribute { public HeaderAttribute(string x){} }
 public class RangeAttribute:System.Attribute { public RangeAttribute(float a,float b){} }
 public class MinAttribute:System.Attribute {public MinAttribute(float x){}}
 public class TooltipAttribute:System.Attribute {public TooltipAttribute(string x){}}
 public class GameObject { public bool activeSelf=true; public Transform transform; public void SetActive(bool v){activeSelf=v;} public T AddComponent<T>() where T:new()=>new T(); }
 public struct Color { public float r,g,b,a;public Color(float r,float g,float b,float a){this.r=r;this.g=g;this.b=b;this.a=a;}public static Color white=>new(1,1,1,1);public static Color Lerp(Color a,Color b,float t)=>new(a.r+(b.r-a.r)*t,a.g+(b.g-a.g)*t,a.b+(b.b-a.b)*t,a.a+(b.a-a.a)*t); }
 public class Transform:System.Collections.Generic.IEnumerable<Transform> {
  public UnityEngine.UI.Image[] cells;public UnityEngine.UI.Image image;public string name;public Transform parent;
  public System.Collections.Generic.List<Transform> children=new();public T[] GetComponentsInChildren<T>(bool x){return (T[])(object)cells;}
  public void Add(Transform t){t.parent=this;children.Add(t);}
  public bool IsChildOf(Transform t){for(var p=parent;p!=null;p=p.parent)if(p==t)return true;return false;}
  public bool TryGetComponent<T>(out T result) where T:class{result=image as T;return result!=null;}
  public System.Collections.Generic.IEnumerator<Transform> GetEnumerator()=>children.GetEnumerator();System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
 }
 public class RectTransform:Transform{}
 public static class Time {public static float unscaledDeltaTime=.1f;}
 public static class Mathf { public static float Clamp01(float x)=>System.Math.Clamp(x,0,1); public static float Clamp(float x,float a,float b)=>System.Math.Clamp(x,a,b);public static float Min(float a,float b)=>System.Math.Min(a,b);public static int CeilToInt(float x)=>(int)System.Math.Ceiling(x);public static int RoundToInt(float x)=>(int)System.Math.Round(x); public static float Max(float a,float b)=>System.Math.Max(a,b); }
}
namespace UnityEngine.Events {public class UnityEvent<T>{public event System.Action<T> Handler;public void Invoke(T v)=>Handler?.Invoke(v);}}
namespace UnityEngine.UI {
 public class LayoutElement {public float minWidth,preferredWidth,flexibleWidth;}
 public static class LayoutRebuilder {public static int Rebuilds;public static void MarkLayoutForRebuild(UnityEngine.RectTransform t){Rebuilds++;}}
 public class Image { public enum Type {Filled}public enum FillMethod{Horizontal}public Type type;public FillMethod fillMethod;public int fillOrigin;public bool raycastTarget;public float fillAmount;
 public UnityEngine.GameObject gameObject;public UnityEngine.Transform transform;public string name {get=>transform.name;set=>transform.name=value;}
 public Image(){transform=new UnityEngine.Transform{image=this,name="HP_Cell"};gameObject=new UnityEngine.GameObject{transform=transform};}
 public T GetComponent<T>() where T:class=>null;
 private UnityEngine.Color c; public int Writes; public UnityEngine.Color color {get=>c;set{c=value;Writes++;}}
 }
}
namespace Tested {
 public static class DataConfig { public static event System.Action Changed; public static int magic1Index,magic2Index,hatIndex,broomIndex; }
 public enum MagicType {None,Fire,Ice,Vision} public enum HatType {None,Classic} public enum BroomType {None,Slow,Standard}
 public class Obj { public bool IsValid=true,HasStateAuthority=true; public int InputAuthority; }
 public class RunnerState { public bool IsRunning=true; public float Time; }
 public class PlayerData { public Obj Object=new Obj(); public RunnerState Runner=new RunnerState(); public float BattleMaxHp,BattleCurrentHp,BattleMaxAp,BattleCurrentAp,BattleApRecoveryPerSecond; public int Writes;
 public void SetBattleAp(float max,float current,float recovery){BattleMaxAp=max;BattleCurrentAp=current;BattleApRecoveryPerSecond=recovery;Writes++;} }
 public struct TickTimer { float deadline; public bool ExpiredOrNotRunning(RunnerState r)=>r.Time>=deadline; public static TickTimer CreateFromSeconds(RunnerState r,float s)=>new TickTimer{deadline=r.Time+s}; }
 public class NetworkGameManager { public static NetworkGameManager Instance=new NetworkGameManager(); public PlayerData Data=new PlayerData(); public PlayerData GetPlayerData(int p)=>Data; }
}
'@
$probe = @'
namespace Tested { using UnityEngine;
public class HealthProbe {
 float publishedHp=float.NaN,publishedMaxHp=float.NaN; public float NowHp,MaxHp; public event System.Action<float,float> HealthChanged;
 public void Publish()=>NotifyHealthChanged();
'@ + (Method NotifyHealthChanged) + @'
}
public class ManaProbe {
 public object testBot;
 public float MaxAp=100,NowAp=100,ApRecoveryPerSecond=5; public float infoApSyncInterval=.1f;
 public RunnerState Runner=new RunnerState(); public Obj Object=new Obj(); TickTimer nextInfoApSync;
 public void Publish()=>SyncApToPlayerData();
'@ + (Method SyncApToPlayerData) + "`n}}"
$tests = @'
namespace Tested { using System; using System.Reflection; using UnityEngine; using UnityEngine.UI;
public static class Tests {
 static int n; static void Check(bool c,string name){if(!c)throw new Exception(name);n++;}
 static void Field(object o,string name,object v)=>o.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(o,v);
 public static int Run(){
 var e=new PlayerEquipment(); var staffs=new[]{new GameObject(),new GameObject(),new GameObject(),new GameObject()};
 var brooms=new[]{new GameObject(),new GameObject(),new GameObject()}; var hats=new[]{new GameObject(),new GameObject()};
 Field(e,"magicStaffPrefabs",staffs);Field(e,"broomPrefabs",brooms);Field(e,"hatPrefabs",hats);
 e.SetFlightEquipmentVisible(false); Check(Array.TrueForAll(staffs,x=>!x.activeSelf),"hide authored staffs before load");
 Check(Array.TrueForAll(brooms,x=>!x.activeSelf),"hide authored brooms before load");
 e.ApplyLoadout(HatType.Classic,BroomType.Slow,MagicType.Fire,MagicType.Ice);
 Check(hats[1].activeSelf,"hat remains visible");Check(!staffs[1].activeSelf&&!brooms[1].activeSelf,"hidden loadout");
 e.SetFlightEquipmentVisible(true);Check(staffs[1].activeSelf&&brooms[1].activeSelf,"custom reveals selection");
 e.ChangeMagic(2);e.SetFlightEquipmentVisible(false);e.EquipBroom(2);e.SetFlightEquipmentVisible(true);
 Check(staffs[2].activeSelf&&!staffs[1].activeSelf&&brooms[2].activeSelf&&!brooms[1].activeSelf,"restore current selection");
 e.ChangeMagic(3);e.SetFlightEquipmentVisible(false);e.SetFlightEquipmentVisible(true);Check(Array.TrueForAll(staffs,x=>!x.activeSelf),"parry no staff");
 var g=new GridHPBar(); var cells=new Image[10];for(int i=0;i<10;i++)cells[i]=new Image(); g.gridParent=new Transform{cells=cells};
 g.UpdateHP(1);Check(Array.TrueForAll(cells,x=>x.color.a==1),"full health");g.UpdateHP(.51f);Check(cells[4].color.a==1&&cells[5].color.a==0,"half health");
 int writes=cells[0].Writes;g.UpdateHP(.5f);Check(cells[0].Writes==writes,"same cell count no UI rewrite");g.UpdateHP(-1);Check(Array.TrueForAll(cells,x=>x.color.a==0),"clamp zero");g.UpdateHP(2);Check(Array.TrueForAll(cells,x=>x.color.a==1),"clamp full");
 var dynamicBar=new GridHPBar();var parent=new RectTransform();var template=new Image{color=Color.white};parent.Add(template.transform);dynamicBar.gridParent=parent;Field(dynamicBar,"cellTemplate",template);
 var damage=(UnityEngine.Events.UnityEvent<float>)typeof(GridHPBar).GetField("onDamaged",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(dynamicBar);
 int damageEvents=0;float lost=0;damage.Handler+=v=>{damageEvents++;lost=v;};
 dynamicBar.UpdateHealth(225,225);Check(parent.children.Count==23,"225 max HP creates 23 cells");Check(parent.children[22].image.fillAmount==.5f,"last cell holds five HP");Check(damageEvents==0,"initial bind never damages");
 dynamicBar.UpdateHealth(205,225);Check(damageEvents==1&&lost==20,"actual damage event once");Check(parent.children[20].image.fillAmount==.5f&&parent.children[21].image.color.a==.15f,"partial and empty cells follow HP");
 int rebuilds=LayoutRebuilder.Rebuilds;dynamicBar.UpdateHealth(205,225);Check(damageEvents==1&&LayoutRebuilder.Rebuilds==rebuilds,"no duplicate event or layout rebuild for unchanged HP");
 typeof(GridHPBar).GetMethod("Update",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(dynamicBar,null);Check(parent.children[0].image.color.g<1,"damage flash is visible");
 dynamicBar.UpdateHealth(215,225);Check(damageEvents==1,"healing does not trigger damage");dynamicBar.UpdateHealth(100,100);Check(damageEvents==1,"max HP change is not damage");
 Check(parent.children.FindAll(t=>t.image.gameObject.activeSelf).Count==10,"max HP decrease hides pooled extras");dynamicBar.UpdateHealth(225,225);Check(parent.children.Count==23,"max HP increase reuses cells");
 dynamicBar.UpdateHealth(0,225);Check(damageEvents==2&&lost==225,"death damages once");dynamicBar.ResetBinding();dynamicBar.UpdateHealth(225,225);Check(damageEvents==2,"respawn clears damage history");
 Check(GridHPBar.CellCount(0,10)==0&&GridHPBar.CellCount(float.NaN,10)==0&&GridHPBar.CellCount(100,0)==0,"invalid cell counts safe");
 Check(GridHPBar.CellFill(-5,225,10,0)==0&&GridHPBar.CellFill(500,225,10,22)==.5f,"fill clamps to health bounds");
 var hp=new HealthProbe();int events=0;hp.HealthChanged+=(a,b)=>events++;hp.Publish();hp.Publish();Check(events==1,"spawn once");
 hp.MaxHp=100;hp.NowHp=100;hp.Publish();hp.Publish();Check(events==2,"dual HP property callback dedup");hp.NowHp=70;hp.Publish();Check(events==3,"damage event");hp.NowHp=0;hp.Publish();Check(events==4,"death event");hp.NowHp=100;hp.Publish();Check(events==5,"respawn event");
 var mana=new ManaProbe();var data=NetworkGameManager.Instance.Data;mana.Publish();Check(data.BattleCurrentAp==100&&data.Writes==1,"initial AP snapshot");
 mana.NowAp=99;mana.Runner.Time=.01f;mana.Publish();Check(data.Writes==1,"AP mirror throttled");mana.Runner.Time=.11f;mana.Publish();Check(data.Writes==2&&data.BattleCurrentAp==99,"AP mirror flush");
 mana.NowAp=0;mana.Publish();Check(data.Writes==3&&data.BattleCurrentAp==0,"empty AP immediate");mana.Publish();Check(data.Writes==3,"unchanged AP no copy");mana.NowAp=100;mana.Publish();Check(data.Writes==4,"full AP immediate");
 return n;
}}}
'@
Add-Type -IgnoreWarnings -WarningAction SilentlyContinue -TypeDefinition ($stubs + "`nnamespace Tested {`n" + $equipmentSource + "`n}`nnamespace Tested {`n" + $gridSource + "`n}`n" + $probe + $tests)
"Behavior checks passed: $([Tested.Tests]::Run())"
foreach ($scene in 'Assets/Scenes/Main.unity','Assets/Scenes/Battle.unity') {
    $s = Get-Content $scene -Raw
    $ids = [regex]::Matches($s,'(?m)^--- !u!\d+ &(\d+)') | ForEach-Object {$_.Groups[1].Value}
    if (@($ids | Group-Object | Where-Object Count -gt 1).Count) {throw "Duplicate scene ids: $scene"}
}
$battle = Get-Content Assets/Scenes/Battle.unity -Raw
# The designer can swap/replace markers; validate distinct live RectTransforms, not obsolete fixed IDs.
$desired = [regex]::Match($battle, 'desiredAimMarker: \{fileID: (\d+)\}').Groups[1].Value
$forward = [regex]::Match($battle, 'forwardAimMarker: \{fileID: (\d+)\}').Groups[1].Value
if (!$desired -or !$forward -or $desired -eq '0' -or $forward -eq '0' -or $desired -eq $forward) {throw 'Aim bindings must be distinct'}
foreach ($marker in @($desired,$forward)) {
    if ($battle -notmatch ('(?m)^--- !u!224 &' + $marker + '\b')) {throw "Missing aim RectTransform: $marker"}
}
$main = Get-Content Assets/Scenes/Main.unity -Raw
if ($main -notmatch 'customizationPanel: \{fileID: 1900010007\}') {throw 'Customization binding missing'}
if ($playerSource -notmatch 'Object.ForceRemoteRenderTimeframe = !SimulatesMovement;') {throw 'Owner prediction / remote interpolation mode missing'}
'Scene references and render-mode guard checks passed.'
