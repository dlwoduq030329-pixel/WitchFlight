# PowerShell 7. Runs the actual controller, DataConfig and MagicStatTable with UI/SDK stubs.
# Offline behavior checks only; does not connect to BACKND/Photon or run Unity Play Mode.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$controller = Get-Content (Join-Path $repo 'Assets/Script/MagicCustomizationUI.cs') -Raw
$dataConfig = Get-Content (Join-Path $repo 'Assets/Script/DataConfig.cs') -Raw
$playerConfig = Get-Content (Join-Path $repo 'Assets/Script/PlayerConfig.cs') -Raw
$magicTable = Get-Content (Join-Path $repo 'Assets/Script/MagicStatTable.cs') -Raw
$equipment = Get-Content (Join-Path $repo 'Assets/Script/PlayerEquipment.cs') -Raw
$playerData = Get-Content (Join-Path $repo 'Assets/Script/PlayerData.cs') -Raw
$magicEnum = [regex]::Match($playerData, 'public enum MagicType\s*\{[^}]+\}').Value
$equipmentEnums = [regex]::Matches($playerData, 'public enum (HatType|BroomType)\s*\{[^}]+\}').Value -join "`n"
if (!$magicEnum) { throw 'MagicType enum not found' }
$normalizeHat = [regex]::Match($playerData, '(?s)private static HatType NormalizeHat\(HatType selectedHat\)\s*\{.*?\n    \}').Value
if (!$normalizeHat) { throw 'NormalizeHat method not found' }
$stubs = @'
using System;
using System.Collections.Generic;
namespace UnityEngine {
 public class Object {}
 public class MonoBehaviour : Object { public bool isActiveAndEnabled=true; public GameObject gameObject=new GameObject(); public Transform transform=>gameObject.transform; }
 public class ScriptableObject : Object {}
 public class Sprite : Object {}
 public class GameObject : Object {
  public bool activeSelf=true; public int Writes; public Transform transform;
  public GameObject(){transform=new Transform(this);}
  public void SetActive(bool value){activeSelf=value;Writes++;}
 }
 public class Transform {
  public GameObject gameObject; public Transform parent;
  public Transform(GameObject owner){gameObject=owner;}
  public bool IsChildOf(Transform other)=>ReferenceEquals(this,other)||(parent!=null&&parent.IsChildOf(other));
 }
 public struct Color {
  public float r,g,b,a;public static Color white=>new Color{r=1,g=1,b=1,a=1};
  public Color(float r,float g,float b,float a){this.r=r;this.g=g;this.b=b;this.a=a;}
 }
 public static class Mathf {
  public static int Clamp(int v,int min,int max)=>Math.Clamp(v,min,max);
  public static int Max(int a,int b)=>Math.Max(a,b);
  public static float Max(float a,float b)=>Math.Max(a,b);
  public static float Clamp01(float v)=>Math.Clamp(v,0f,1f);
  public static int RoundToInt(float v)=>(int)Math.Round(v);
 }
 public static class Resources {public static T Load<T>(string path) where T:class=>null;}
 public class SerializeField:Attribute {}
 public class HeaderAttribute:Attribute {public HeaderAttribute(string text){}}
 public class TooltipAttribute:Attribute {public TooltipAttribute(string text){}}
 public class TextAreaAttribute:Attribute {}
 public class RangeAttribute:Attribute {public RangeAttribute(float min,float max){}}
 public class MinAttribute:Attribute {public MinAttribute(float value){}}
 public class DisallowMultipleComponent:Attribute {}
 public class CreateAssetMenuAttribute:Attribute {public string fileName,menuName;}
}
namespace UnityEngine.Events {
 public delegate void UnityAction();
 public class UnityEvent {
  private event UnityAction handlers;
  public void AddListener(UnityAction action){handlers+=action;}
  public void RemoveListener(UnityAction action){handlers-=action;}
  public void Invoke(){handlers?.Invoke();}
 }
 public class UnityEvent<T> {
  private event Action<T> handlers;
  public void AddListener(Action<T> action){handlers+=action;}
  public void Invoke(T value){handlers?.Invoke(value);}
 }
}
namespace UnityEngine.Serialization {
 public class FormerlySerializedAsAttribute:Attribute {public FormerlySerializedAsAttribute(string name){}}
}
namespace UnityEngine.UI {
 public class Image:UnityEngine.MonoBehaviour {public UnityEngine.Sprite sprite;public bool enabled=true;}
 public class Button:UnityEngine.MonoBehaviour {
  public Image image=new Image();public bool interactable=true;
  public UnityEngine.Events.UnityEvent onClick=new UnityEngine.Events.UnityEvent();
 }
}
namespace TMPro {public class TMP_Text:UnityEngine.MonoBehaviour {public string text;}}
namespace Fusion {public interface INetworkStruct {}}
namespace MagicCustomizationChecks {
 using UnityEngine;
 public class DatabaseManager {
  public static DatabaseManager Instance;public bool IsDataConfigReady,HasLoadedProfile;
 }
 public class NetworkGameManager {public static NetworkGameManager Instance;public bool IsMatching;}
}
'@
$tests = @'
namespace MagicCustomizationChecks {
 using System;
 using System.Reflection;
 using TMPro;
 using UnityEngine;
 using UnityEngine.Events;
 using UnityEngine.UI;
 public static class Tests {
  static int checks;
  static void Check(bool condition,string label){if(!condition)throw new Exception(label);checks++;}
  static void Set(object obj,string field,object value)=>obj.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(obj,value);
  static T Get<T>(object obj,string field)=>(T)obj.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(obj);
  static void Life(object obj,string method)=>obj.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(obj,null);
  sealed class Fixture:IDisposable {
   public MagicCustomizationUI ui=new MagicCustomizationUI();
   public MagicCustomizationUI.MagicChoice[] choices;
   public MagicStatTable magicTable=new MagicStatTable();
   public MagicCustomizationUI.HatChoice[] hatChoices;
   public MagicCustomizationUI.BroomChoice[] broomChoices;
   public Image first=new Image(),second=new Image(),selected=new Image();
   public Image hatImage=new Image(),broomImage=new Image();
   public GameObject details=new GameObject(),h1=new GameObject(),h2=new GameObject();
   public GameObject hatHighlight=new GameObject(),broomHighlight=new GameObject();
   public Button equip=new Button(),slot1=new Button(),slot2=new Button(),cancel=new Button();
   public Button hatSlot=new Button(),broomSlot=new Button();
   public TMP_Text name=new TMP_Text(),description=new TMP_Text(),label=new TMP_Text(),status=new TMP_Text();
   public PlayerEquipment preview=new PlayerEquipment();
   public GameObject[] staffs=new GameObject[(int)MagicType.Smoke+1];
   public GameObject[] hats=new GameObject[6],brooms=new GameObject[4];
   public int StaffWrites {get {int result=0;foreach(var staff in staffs)result+=staff.Writes;return result;}}
   public Sprite empty=new Sprite();
   public int changes,equips,errors,lastSlot;
   public int hatEquips,broomEquips,lastHat,lastBroom;
   public Fixture(bool ready=true,bool automaticPanel=true){
    DatabaseManager.Instance=new DatabaseManager{IsDataConfigReady=ready,HasLoadedProfile=ready};
    NetworkGameManager.Instance=null;
    DataConfig.ResetToDefaults();
    choices=Get<MagicCustomizationUI.MagicChoice[]>(ui,"magicChoices");
    foreach(var c in choices){c.button=new Button();c.buttonImage=new Image();c.displayName=c.magic.ToString();c.description="Description "+c.magic;}
    for(int i=0;i<magicTable.magics.Length;i++)magicTable.magics[i].icon=new Sprite();
    Set(ui,"magicTable",magicTable);
    hatChoices=Get<MagicCustomizationUI.HatChoice[]>(ui,"hatChoices");
    foreach(var c in hatChoices){c.button=new Button();c.buttonImage=new Image();c.icon=new Sprite();c.displayName=c.hat.ToString();c.description="Description "+c.hat;}
    broomChoices=Get<MagicCustomizationUI.BroomChoice[]>(ui,"broomChoices");
    foreach(var c in broomChoices){c.button=new Button();c.buttonImage=new Image();c.icon=new Sprite();c.displayName=c.broom.ToString();c.description="Description "+c.broom;}
    Set(ui,"hatSlotButton",hatSlot);Set(ui,"hatSlotImage",hatImage);Set(ui,"hatSlotHighlight",hatHighlight);
    Set(ui,"broomSlotButton",broomSlot);Set(ui,"broomSlotImage",broomImage);Set(ui,"broomSlotHighlight",broomHighlight);
    Set(ui,"slot1Button",slot1);Set(ui,"slot2Button",slot2);
    Set(ui,"slot1Image",first);Set(ui,"slot2Image",second);Set(ui,"emptySlotSprite",empty);
    Set(ui,"slot1Highlight",h1);Set(ui,"slot2Highlight",h2);
    Set(ui,"selectionPanel",details);Set(ui,"selectedItemImage",selected);
    Set(ui,"selectedItemName",name);Set(ui,"selectedItemDescription",description);
    Set(ui,"manageSelectionPanel",automaticPanel);
    Set(ui,"equipButton",equip);Set(ui,"equipButtonText",label);Set(ui,"statusText",status);
    Set(ui,"cancelSelectionButton",cancel);
    for(int i=0;i<staffs.Length;i++)staffs[i]=new GameObject();
    for(int i=0;i<hats.Length;i++)hats[i]=new GameObject();
    for(int i=0;i<brooms.Length;i++)brooms[i]=new GameObject();
    Set(preview,"magicStaffPrefabs",staffs);
    Set(preview,"hatPrefabs",hats);Set(preview,"broomPrefabs",brooms);
    preview.BindToDataConfig();
    Get<UnityEvent<int>>(ui,"onEquipped").AddListener(s=>{equips++;lastSlot=s;});
    Get<UnityEvent<int>>(ui,"onHatEquipped").AddListener(i=>{hatEquips++;lastHat=i;});
    Get<UnityEvent<int>>(ui,"onBroomEquipped").AddListener(i=>{broomEquips++;lastBroom=i;});
    Get<UnityEvent<string>>(ui,"onEquipFailed").AddListener(s=>errors++);
    DataConfig.Changed+=Changed;
    Life(ui,"OnEnable");
   }
   void Changed(){changes++;}
   public void Pick(MagicType magic)=>Array.Find(choices,c=>c.magic==magic).button.onClick.Invoke();
   public Sprite Icon(MagicType magic)=>magicTable.GetIcon(magic);
   public void Pick(HatType hat)=>Array.Find(hatChoices,c=>c.hat==hat).button.onClick.Invoke();
   public Sprite Icon(HatType hat)=>Array.Find(hatChoices,c=>c.hat==hat).icon;
   public void Pick(BroomType broom)=>Array.Find(broomChoices,c=>c.broom==broom).button.onClick.Invoke();
   public Sprite Icon(BroomType broom)=>Array.Find(broomChoices,c=>c.broom==broom).icon;
   public void Dispose(){Life(ui,"OnDisable");preview.UnbindFromDataConfig();DataConfig.Changed-=Changed;}
  }
  public static int Run(){
   foreach(var magic in new[]{MagicType.Flare,MagicType.Smoke}){
    using(var f=new Fixture()){
     f.Pick(magic);f.slot2.onClick.Invoke();f.equip.onClick.Invoke();
     Check(DataConfig.magic1Index==(int)MagicType.Fire&&DataConfig.magic2Index==(int)magic,"new utility equips in the selected slot");
     Check(f.changes==1&&f.equips==1&&f.second.sprite==f.Icon(magic),"utility updates data and icon once");
     Check(f.staffs[(int)magic].activeSelf,"utility changes weapon preview through DataConfig");
    }
   }
   using(var f=new Fixture()){
    Check(f.ui.SelectedSlot==1&&f.ui.SelectedMagic==MagicType.None,"opens in slot 1 without pending magic");
    Check(!f.details.activeSelf&&!f.equip.gameObject.activeSelf,"details and confirm start hidden");
    Check(f.first.sprite==f.Icon(MagicType.Fire)&&f.second.sprite==f.Icon(MagicType.Ice),"initial equipped icons from DataConfig");
    Check(f.changes==0&&f.staffs[1].activeSelf,"opening reads only and character independently reads DataConfig");
    int initialWrites=f.StaffWrites;
    f.Pick(MagicType.Vision);
    Check(f.ui.SelectedMagic==MagicType.Vision&&f.details.activeSelf&&f.equip.gameObject.activeSelf,"image button shows candidate and confirmation");
    Check(f.selected.sprite==f.Icon(MagicType.Vision)&&f.name.text=="Vision"&&f.description.text=="Description Vision","preview icon/name/description follow selection");
    Check(f.changes==0&&DataConfig.magic1Index==1&&DataConfig.magic2Index==2&&f.StaffWrites==initialWrites,"preview does not save or equip character");
    f.slot2.onClick.Invoke();
    Check(f.ui.SelectedSlot==2&&!f.h1.activeSelf&&f.h2.activeSelf&&f.label.text.Contains("2"),"slot 2 selected with highlight and prompt");
    Check(f.changes==0&&f.StaffWrites==initialWrites,"choosing UI slot does not change data or weapon");
    f.equip.onClick.Invoke();
    Check(DataConfig.magic1Index==1&&DataConfig.magic2Index==(int)MagicType.Vision&&f.changes==1,"confirmation changes only selected slot and emits one autosave notification");
    Check(f.equips==1&&f.lastSlot==2&&f.status.text.Contains("장착 완료"),"equip event and local status");
    Check(f.second.sprite==f.Icon(MagicType.Vision)&&!f.equip.interactable&&f.label.text=="장착 중","equipped slot updates and duplicate confirmation disabled");
    Check(f.staffs[(int)MagicType.Vision].activeSelf&&!f.staffs[1].activeSelf,"DataConfig event independently equips the just-changed second slot");
    f.ui.EquipSelectedMagic();
    Check(f.changes==1&&f.equips==1,"repeated confirmation is idempotent");
    f.slot1.onClick.Invoke();f.equip.onClick.Invoke();
    Check(DataConfig.magic1Index==3&&DataConfig.magic2Index==3,"existing duplicate-spell policy preserved");
    f.Pick(MagicType.Thunder);f.cancel.onClick.Invoke();
    Check(f.ui.SelectedMagic==MagicType.None&&!f.details.activeSelf&&f.changes==2,"cancel discards candidate without saving");
    f.ui.SelectSlot(3);f.ui.SelectSlot(0);
    Check(f.ui.SelectedSlot==1&&f.errors==2&&f.changes==2,"fixed parry and invalid slots rejected");
    f.Pick(MagicType.Thunder);f.ui.SelectMagic(999);f.ui.EquipSelectedMagic();f.ui.SelectMagic(0);
    Check(f.ui.SelectedMagic==MagicType.None&&f.changes==2&&f.errors==5,"invalid/None magic cannot equip or reuse old candidate");
    int previewCalls=f.StaffWrites;
    DataConfig.bangsLength=0.7f;
    Check(f.StaffWrites==previewCalls,"unrelated appearance changes do not reset equipment");
    DataConfig.magic1Index=(int)MagicType.Binding;
    Check(f.first.sprite==f.Icon(MagicType.Binding)&&f.staffs[(int)MagicType.Binding].activeSelf,"external DataConfig edits refresh equipped UI and preview");
    Life(f.ui,"OnDisable");
    DataConfig.magic1Index=(int)MagicType.Fire;
    Check(f.first.sprite==f.Icon(MagicType.Binding),"disabled controller unsubscribed from data");
    f.Pick(MagicType.Thunder);
    Check(f.ui.SelectedMagic==MagicType.None,"disabled controller removes button handlers");
    Life(f.ui,"OnEnable");
    Check(f.first.sprite==f.Icon(MagicType.Fire)&&f.ui.SelectedMagic==MagicType.None,"reopen reads latest data and clears pending selection");
    NetworkGameManager.Instance=new NetworkGameManager{IsMatching=true};
    f.Pick(MagicType.Thunder);
    Check(f.errors==6,"reopening does not duplicate button listeners");
   }
   using(var f=new Fixture(false)){
    Check(!f.equip.interactable&&!f.choices[0].button.interactable&&f.first.sprite==f.empty,"before login UI is read-protected");
    f.ui.SelectMagic(1);f.ui.EquipSelectedMagic();
    Check(f.errors==2&&f.changes==0,"before login cannot select or equip");
    DatabaseManager.Instance.IsDataConfigReady=true;DatabaseManager.Instance.HasLoadedProfile=true;
    using(DataConfig.BeginChangeBatch(false)){DataConfig.magic1Index=(int)MagicType.Curse;}
    Life(f.ui,"Update");
    Check(f.choices[0].button.interactable&&f.first.sprite==f.Icon(MagicType.Curse)&&f.changes==0,"silent login load refreshes on readiness transition without save");
    f.Pick(MagicType.Thunder);DatabaseManager.Instance.IsDataConfigReady=false;
    f.equip.onClick.Invoke();
    Check(f.errors==3&&f.changes==0,"profile lost after selection blocks confirmation");
    Life(f.ui,"Update");
    Check(f.ui.SelectedMagic==MagicType.None&&!f.details.activeSelf&&f.first.sprite==f.empty,"logout clears pending selection and displayed profile");
   }
   using(var f=new Fixture()){
    f.Pick(MagicType.Thunder);NetworkGameManager.Instance=new NetworkGameManager{IsMatching=true};
    f.equip.onClick.Invoke();
    Check(f.changes==0&&f.errors==1,"matching begins between selection and confirm: no stale loadout changes");
    Life(f.ui,"Update");
    Check(f.ui.SelectedMagic==MagicType.None&&!f.equip.gameObject.activeSelf&&!f.slot1.interactable,"matching clears candidate and locks buttons");
    NetworkGameManager.Instance.IsMatching=false;Life(f.ui,"Update");
    Check(f.slot1.interactable&&f.ui.SelectedMagic==MagicType.None,"leaving room unlocks without restoring stale candidate");
   }
   using(var f=new Fixture()){
    f.ui.SelectSlot(1);f.Pick(MagicType.Mine);f.slot2.onClick.Invoke();
    Check(f.ui.SelectedSlot==2&&f.ui.SelectedMagic==MagicType.Mine,"user scenario: slot 1 -> Mine -> slot 2 retains chosen magic");
    Check(DataConfig.magic1Index==1&&DataConfig.magic2Index==2&&f.changes==0&&f.first.sprite==f.Icon(MagicType.Fire)&&f.second.sprite==f.Icon(MagicType.Ice),"slot change changes neither saved data nor equipped icons");
    Check(f.equip.interactable&&f.equip.gameObject.activeSelf&&f.label.text=="2번 슬롯에 장착","equip button follows latest target slot");
    f.equip.onClick.Invoke();
    Check(DataConfig.magic1Index==1&&DataConfig.magic2Index==(int)MagicType.Mine&&f.changes==1&&f.lastSlot==2,"confirmation equips Mine in latest slot only");
    Check(f.first.sprite==f.Icon(MagicType.Fire)&&f.second.sprite==f.Icon(MagicType.Mine)&&f.staffs[(int)MagicType.Mine].activeSelf,"equipping updates weapon and only target icon through DataConfig");
    f.slot2.onClick.Invoke();f.ui.EquipSelectedMagic();
    Check(f.ui.SelectedMagic==MagicType.Mine&&f.changes==1&&f.equips==1,"same slot and repeated confirm preserve candidate without repeat save");
    f.slot1.onClick.Invoke();
    Check(f.ui.SelectedMagic==MagicType.Mine&&f.equip.interactable,"same candidate can intentionally be equipped in the other slot");
    f.Pick(MagicType.Thunder);f.slot2.onClick.Invoke();f.slot1.onClick.Invoke();f.equip.onClick.Invoke();
    Check(DataConfig.magic1Index==(int)MagicType.Thunder&&DataConfig.magic2Index==(int)MagicType.Mine&&f.changes==2,"last selected magic and slot win after repeated navigation");
   }
   using(var f=new Fixture(automaticPanel:false)){
    var designerIcon=new Sprite();
    f.name.text="Designer name";f.description.text="Designer description";f.selected.sprite=designerIcon;
    f.details.SetActive(false);
    int designerClicks=0;
    // Existing OnClick callbacks are deliberately not removed by the controller.
    f.choices[(int)MagicType.Mine-1].button.onClick.AddListener(()=>{designerClicks++;f.details.SetActive(true);});
    f.Pick(MagicType.Mine);f.slot2.onClick.Invoke();
    Check(designerClicks==1&&f.details.activeSelf&&f.name.text=="Mine"&&f.description.text=="Description Mine"&&f.selected.sprite==f.Icon(MagicType.Mine),"existing OnClick owns visibility; content still follows the selected magic");
    f.ui.CancelSelection();
    Check(f.details.activeSelf&&f.name.text==string.Empty&&f.description.text==string.Empty&&f.selected.sprite==null,"cancel clears stale content without hiding the manually controlled panel");
    f.Pick(MagicType.Mine);
    // A legacy slot callback may run after our listener. One end-of-frame repair
    // restores controls and candidate content, leaving panel visibility alone.
    f.slot2.onClick.AddListener(()=>{f.first.sprite=null;f.equip.gameObject.SetActive(false);f.description.text="Slot tab text";});
    f.slot2.onClick.Invoke();Life(f.ui,"LateUpdate");
    Check(f.first.sprite==f.Icon(MagicType.Fire)&&f.second.sprite==f.Icon(MagicType.Ice)&&f.equip.gameObject.activeSelf&&f.equip.interactable,"later UI callbacks cannot leave saved icons cleared or valid confirm hidden");
    Check(f.description.text=="Description Mine"&&f.ui.SelectedMagic==MagicType.Mine&&f.changes==0,"end-of-frame refresh restores selected magic description without saving");
    Life(f.ui,"OnDisable");f.Pick(MagicType.Mine);
    Check(designerClicks==3,"disabling removes only controller listeners, not existing OnClick");
   }
   using(var f=new Fixture(automaticPanel:false)){
    f.details.SetActive(false);int panelWrites=f.details.Writes;
    f.Pick(MagicType.Healing);
    Check(!f.details.activeSelf&&f.details.Writes==panelWrites,"automatic panel OFF never opens a manually controlled description panel");
    Check(f.name.text=="Healing"&&f.description.text=="Description Healing"&&f.selected.sprite==f.Icon(MagicType.Healing),"automatic panel OFF still updates all connected content");
    f.Pick(MagicType.Ice);
    Check(f.name.text=="Ice"&&f.description.text=="Description Ice"&&f.changes==0,"switching spell updates content immediately without equipping");
    f.choices[(int)MagicType.Mine-1].button.onClick.AddListener(()=>{f.details.SetActive(true);f.name.text="Fire";f.description.text="Old fire text";});
    f.Pick(MagicType.Mine);Life(f.ui,"LateUpdate");
    Check(f.details.activeSelf&&f.name.text=="Mine"&&f.description.text=="Description Mine","late existing OnClick cannot leave stale fire description after choosing Mine");
    f.slot2.onClick.Invoke();Life(f.ui,"LateUpdate");
    Check(f.name.text=="Mine"&&f.description.text=="Description Mine"&&f.ui.SelectedMagic==MagicType.Mine&&f.changes==0,"slot switch preserves candidate description and saved loadout");
   }
   using(var f=new Fixture()){
    f.Pick(MagicType.Mine);f.slot2.onClick.Invoke();
    Get<UnityEvent<int>>(f.ui,"onEquipped").AddListener(slot=>{f.ui.SelectSlot(1);f.ui.EquipSelectedMagic();});
    f.equip.onClick.Invoke();
    Check(DataConfig.magic1Index==1&&DataConfig.magic2Index==(int)MagicType.Mine&&f.changes==1&&f.equips==1,"reentrant equip callback cannot write a second slot in one confirmation");
    Check(f.equip.interactable,"reentrancy guard releases for next intentional click");
   }
   using(var f=new Fixture()){
    // Common Inspector mistake: preview/list images reference the equipped Image.
    Set(f.ui,"selectedItemImage",f.first);f.choices[0].buttonImage=f.second;
    f.Pick(MagicType.Mine);f.slot2.onClick.Invoke();f.cancel.onClick.Invoke();
    Check(f.first.sprite==f.Icon(MagicType.Fire)&&f.second.sprite==f.Icon(MagicType.Ice),"aliased preview/list image cannot overwrite equipped slot images");
    f.Pick(MagicType.Mine);f.ui.isActiveAndEnabled=false;Life(f.ui,"OnDisable");f.ui.EquipSelectedMagic();
    Check(f.changes==0&&f.ui.SelectedMagic==MagicType.None&&!f.equip.interactable,"closed controller cannot equip from a stale invocation");
   }
   using(var f=new Fixture()){
    f.preview.SetFlightEquipmentVisible(false);
    DataConfig.magic2Index=(int)MagicType.Mine;
    Check(Array.TrueForAll(f.staffs,s=>!s.activeSelf),"data changes outside customization keep flight equipment hidden");
    f.preview.SetFlightEquipmentVisible(true);
    Check(f.staffs[(int)MagicType.Mine].activeSelf,"reopening customization reveals latest equipped staff");
    Life(f.preview,"OnDisable");DataConfig.magic2Index=(int)MagicType.Curse;
    Check(f.staffs[(int)MagicType.Mine].activeSelf,"disabled model unsubscribes from local data");
    Life(f.preview,"OnEnable");
    Check(f.staffs[(int)MagicType.Curse].activeSelf,"re-enabled model catches up with local data");
    f.preview.ApplyLoadout(HatType.Classic,BroomType.Standard,MagicType.Ice,MagicType.Fire);
    int writes=f.StaffWrites;
    DataConfig.magic1Index=(int)MagicType.Thunder;
    Check(f.staffs[(int)MagicType.Ice].activeSelf&&f.StaffWrites==writes,"replicated battle loadout unsubscribes and ignores local DataConfig");
   }
   using(var f=new Fixture()){
    var fire=f.choices[0];
    var fallbackIcon=new Sprite();
    var table=new MagicStatTable{magics=new[]{new MagicStatEntry{magic=MagicType.Fire,icon=fallbackIcon,displayName="Fallback Fire"}}};
    fire.displayName=null;Set(f.ui,"magicTable",table);f.ui.RefreshFromDataConfig();f.Pick(MagicType.Fire);
    Check(f.first.sprite==fallbackIcon&&f.selected.sprite==fallbackIcon&&f.name.text=="Fallback Fire","shared table supplies icon and name");
    Check(fire.button.image.sprite==null&&fire.buttonImage.sprite==fallbackIcon,"icon does not replace designer button border/background");
    var changedIcon=new Sprite();table.magics[0].icon=changedIcon;f.ui.RefreshFromDataConfig();
    Check(f.first.sprite==changedIcon&&f.selected.sprite==changedIcon&&fire.buttonImage.sprite==changedIcon,"editing table updates slot, selected preview and list together");
    table.magics[0].icon=null;f.ui.RefreshFromDataConfig();
    Check(fire.buttonImage.sprite==null&&!fire.buttonImage.enabled&&f.selected.sprite==null,"missing table icon clears stale list and preview art");
    Check(f.first.sprite==f.empty&&f.changes==0,"missing icon does not change equipment and uses only the empty-slot placeholder");
    Set(f.ui,"selectionPanel",f.ui.gameObject);f.ui.CancelSelection();
    Check(f.ui.gameObject.activeSelf,"mistaken self panel assignment cannot disable controller");
    Set(f.ui,"magicChoices",new MagicCustomizationUI.MagicChoice[]{null,fire});f.ui.SelectMagic((int)MagicType.Ice);
    Check(f.ui.SelectedMagic==MagicType.None&&f.changes==0,"unregistered magic and null choice are safe");
   }
   using(var f=new Fixture()){
    Check(f.hatImage.sprite==f.Icon(HatType.Classic)&&f.broomImage.sprite==f.Icon(BroomType.Standard),"hat and broom slots read saved default values");
    f.hatSlot.onClick.Invoke();f.Pick(HatType.Cosmic);
    Check(f.ui.SelectedCategory==MagicCustomizationUI.EquipmentCategory.Hat&&f.ui.SelectedHat==HatType.Cosmic&&f.hatHighlight.activeSelf&&!f.h1.activeSelf&&!f.broomHighlight.activeSelf,"hat target and candidate remain independent of magic slots");
    Check(f.name.text=="Cosmic"&&f.description.text=="Description Cosmic"&&f.selected.sprite==f.Icon(HatType.Cosmic),"hat selection updates shared details");
    Check(f.changes==0&&DataConfig.hatIndex==1&&f.hats[1].activeSelf&&!f.hats[5].activeSelf&&f.hatImage.sprite==f.Icon(HatType.Classic),"hat preview alone does not equip, save, or change slot icon");
    Check(f.equip.interactable&&f.label.text=="모자 슬롯에 장착","hat confirmation targets the hat slot");
    f.equip.onClick.Invoke();
    Check(DataConfig.hatIndex==5&&DataConfig.broomIndex==2&&DataConfig.magic1Index==1&&DataConfig.magic2Index==2&&f.changes==1,"hat confirmation changes only hat and emits one autosave notification");
    Check(f.hats[5].activeSelf&&!f.hats[1].activeSelf&&f.hatImage.sprite==f.Icon(HatType.Cosmic),"DataConfig updates hat model and equipped UI");
    Check(f.hatEquips==1&&f.lastHat==5&&f.broomEquips==0&&f.equips==0,"hat success event uses HatType value without magic event");
    Check(!f.equip.interactable&&f.label.text=="장착 중","already equipped hat disables confirmation");
    f.ui.EquipSelectedItem();Check(f.changes==1&&f.hatEquips==1,"duplicate hat confirmation is idempotent");
    f.broomSlot.onClick.Invoke();f.Pick(BroomType.Speed);
    Check(f.broomHighlight.activeSelf&&!f.hatHighlight.activeSelf&&f.ui.SelectedSlot==1,"broom target does not erase remembered magic slot");
    Check(f.name.text=="Speed"&&f.description.text=="Description Speed"&&f.selected.sprite==f.Icon(BroomType.Speed)&&f.changes==1,"broom choice updates content without saving");
    f.equip.onClick.Invoke();
    Check(DataConfig.broomIndex==3&&DataConfig.hatIndex==5&&f.changes==2&&f.broomEquips==1&&f.lastBroom==3&&f.equips==0,"broom confirmation changes only broom and dispatches its own success event");
    Check(f.brooms[3].activeSelf&&!f.brooms[2].activeSelf&&f.broomImage.sprite==f.Icon(BroomType.Speed),"DataConfig updates broom model and equipped UI");
    f.ui.EquipSelectedItem();Check(f.changes==2&&f.broomEquips==1,"duplicate broom confirmation is idempotent");
    f.preview.SetFlightEquipmentVisible(false);f.Pick(BroomType.Slow);f.equip.onClick.Invoke();
    Check(DataConfig.broomIndex==1&&Array.TrueForAll(f.brooms,b=>!b.activeSelf)&&f.hats[5].activeSelf,"equipping respects hidden flight gear without hiding hat");
    f.preview.SetFlightEquipmentVisible(true);
    Check(f.brooms[1].activeSelf&&!f.brooms[3].activeSelf,"showing flight gear uses latest equipped broom");
    DataConfig.hatIndex=4;DataConfig.broomIndex=2;
    Check(f.hats[4].activeSelf&&f.hatImage.sprite==f.Icon(HatType.Serenity)&&f.brooms[2].activeSelf&&f.broomImage.sprite==f.Icon(BroomType.Standard),"external equipment edits update both model and UI");
   }
   using(var f=new Fixture()){
    f.Pick(HatType.Cosmic);
    Check(!f.equip.interactable&&f.ui.SelectedCategory==MagicCustomizationUI.EquipmentCategory.Magic,"hat candidate does not silently change current target category");
    f.ui.EquipSelectedItem();
    Check(f.errors==1&&f.changes==0&&f.ui.SelectedHat==HatType.Cosmic,"mismatched category rejects equip but retains candidate");
    f.hatSlot.onClick.Invoke();Check(f.equip.interactable,"choosing matching target re-enables retained candidate");
    f.broomSlot.onClick.Invoke();f.ui.EquipSelectedItem();
    Check(f.changes==0&&f.errors==2&&DataConfig.broomIndex==2,"hat cannot be written into broom slot");
    f.hatSlot.onClick.Invoke();f.equip.onClick.Invoke();
    f.Pick(MagicType.Mine);f.ui.EquipSelectedItem();
    Check(f.changes==1&&DataConfig.hatIndex==5&&f.errors==3&&f.ui.SelectedMagic==MagicType.Mine,"latest magic selection replaces active candidate; old hat is not re-equipped");
    f.slot2.onClick.Invoke();f.equip.onClick.Invoke();
    Check(f.changes==2&&DataConfig.magic2Index==(int)MagicType.Mine&&DataConfig.hatIndex==5&&f.equips==1,"switching back to magic target confirms latest candidate there");
    f.Pick(BroomType.Slow);f.cancel.onClick.Invoke();
    Check(f.ui.SelectedHat==HatType.None&&f.ui.SelectedBroom==BroomType.None&&f.ui.SelectedMagic==MagicType.None&&f.changes==2,"cancel clears all candidate categories without saving");
   }
   using(var f=new Fixture(false)){
    Check(!f.hatSlot.interactable&&!f.broomSlot.interactable&&!f.hatChoices[0].button.interactable&&!f.broomChoices[0].button.interactable,"equipment controls locked before profile load");
    f.ui.SelectHatSlot();f.ui.SelectBroomSlot();f.ui.SelectHat(5);f.ui.SelectBroom(3);f.ui.EquipSelectedItem();
    Check(f.changes==0&&f.errors==5,"manual equipment calls also reject unloaded profile");
   }
   using(var f=new Fixture()){
    f.hatSlot.onClick.Invoke();f.Pick(HatType.Cosmic);
    NetworkGameManager.Instance=new NetworkGameManager{IsMatching=true};f.ui.EquipSelectedItem();
    Check(f.changes==0&&f.ui.SelectedHat==HatType.None&&!f.hatSlot.interactable&&!f.broomSlot.interactable,"matching after hat selection blocks confirmation and clears pending candidate");
    NetworkGameManager.Instance.IsMatching=false;Life(f.ui,"Update");
    f.broomSlot.onClick.Invoke();f.Pick(BroomType.Speed);DatabaseManager.Instance.IsDataConfigReady=false;f.ui.EquipSelectedItem();
    Check(f.changes==0&&f.ui.SelectedBroom==BroomType.None&&f.hatImage.sprite==f.empty&&f.broomImage.sprite==f.empty,"logout after broom selection clears candidate and profile icons");
   }
   using(var f=new Fixture()){
    f.hatSlot.onClick.Invoke();f.ui.SelectHat(0);f.ui.SelectHat(999);f.ui.EquipSelectedItem();
    f.broomSlot.onClick.Invoke();f.ui.SelectBroom(0);f.ui.SelectBroom(999);f.ui.EquipSelectedItem();
    Check(f.changes==0&&f.errors==6,"None and invalid equipment enum IDs cannot equip");
    Set(f.ui,"hatChoices",new MagicCustomizationUI.HatChoice[]{null,f.hatChoices[0]});f.ui.SelectHat(5);
    Set(f.ui,"broomChoices",new MagicCustomizationUI.BroomChoice[]{null,f.broomChoices[0]});f.ui.SelectBroom(3);
    Check(f.changes==0&&f.errors==8,"missing and null equipment choices are rejected safely");
   }
   using(var f=new Fixture(automaticPanel:false)){
    int customClicks=0;
    f.hatChoices[4].button.onClick.AddListener(()=>{customClicks++;f.details.SetActive(true);f.name.text="Old text";});
    f.Pick(HatType.Cosmic);f.hatSlot.onClick.Invoke();Life(f.ui,"LateUpdate");
    Check(customClicks==1&&f.name.text=="Cosmic"&&f.details.activeSelf,"designer OnClick preserved while hat content follows selection");
    Set(f.ui,"selectedItemImage",f.hatImage);f.broomChoices[2].buttonImage=f.broomImage;
    f.Pick(BroomType.Speed);f.broomSlot.onClick.Invoke();Life(f.ui,"LateUpdate");
    Check(f.hatImage.sprite==f.Icon(HatType.Classic)&&f.broomImage.sprite==f.Icon(BroomType.Standard),"miswired equipment preview and choice images cannot erase equipped icons");
    Life(f.ui,"OnDisable");f.Pick(HatType.Cosmic);f.Pick(BroomType.Speed);
    Check(customClicks==2&&f.ui.SelectedHat==HatType.None&&f.ui.SelectedBroom==BroomType.None,"disable removes only equipment controller callbacks");
    Life(f.ui,"OnEnable");NetworkGameManager.Instance=new NetworkGameManager{IsMatching=true};
    f.Pick(HatType.Cosmic);f.Pick(BroomType.Speed);
    Check(f.errors==2,"reopening does not duplicate hat or broom listeners");
   }
   foreach(bool fromHat in new[]{true,false}){
    foreach(int slot in new[]{1,2}){
     using(var f=new Fixture(automaticPanel:false)){
      if(fromHat){f.hatSlot.onClick.Invoke();f.Pick(HatType.Cosmic);}
      else{f.broomSlot.onClick.Invoke();f.Pick(BroomType.Speed);}
      f.equip.onClick.Invoke();
      if(slot==1)f.slot1.onClick.Invoke();else f.slot2.onClick.Invoke();
      // Older designer callbacks on the shared list may still touch an icon.
      // The registered selection callback must repair it and update the details.
      f.choices[(int)MagicType.Mine-1].button.onClick.AddListener(()=>{
       f.first.sprite=null;f.second.sprite=null;f.description.text="Old equipment text";f.details.SetActive(true);
      });
      f.Pick(MagicType.Mine);Life(f.ui,"LateUpdate");
      Check(f.name.text=="Mine"&&f.description.text=="Description Mine"&&f.details.activeSelf,"equipment -> magic tab refreshes selected description in slot "+slot);
      Check(f.first.sprite==f.Icon(MagicType.Fire)&&f.second.sprite==f.Icon(MagicType.Ice)&&f.changes==1,"equipment -> magic preview preserves both equipped icons and does not save");
      f.equip.onClick.Invoke();
      Check((slot==1?DataConfig.magic1Index:DataConfig.magic2Index)==(int)MagicType.Mine&&f.changes==2&&f.equips==1,"equipment -> magic confirmation writes only the selected slot");
      Check(fromHat?DataConfig.hatIndex==5:DataConfig.broomIndex==3,"magic confirmation preserves previously equipped hat/broom");
     }
    }
   }
   foreach(HatType hat in new[]{HatType.Classic,HatType.Twisted,HatType.Elemental,HatType.Serenity,HatType.Cosmic}){
    var config=PlayerConfig.Default;config.hatIndex=(int)hat;
    Check(config.Sanitized().hatIndex==(int)hat&&NetworkHatCheck.Normalize(hat)==hat,"save and network sanitizers retain "+hat);
   }
   Check(new PlayerConfig{hatIndex=999,broomIndex=999}.Sanitized().hatIndex==1&&NetworkHatCheck.Normalize((HatType)999)==HatType.Classic,"invalid equipment values still use safe defaults");
   {
    // All UI references are optional: this component must remain usable via public methods.
    DatabaseManager.Instance=new DatabaseManager{IsDataConfigReady=true,HasLoadedProfile=true};
    NetworkGameManager.Instance=null;DataConfig.ResetToDefaults();
    var ui=new MagicCustomizationUI();Set(ui,"defaultSlot",2);Life(ui,"OnEnable");
    ui.SelectMagic((int)MagicType.Mine);ui.EquipSelectedMagic();
    Check(ui.SelectedSlot==2&&DataConfig.magic2Index==(int)MagicType.Mine,"manual calls work with no UI references and default slot 2");
    ui.SelectHatSlot();ui.SelectHat((int)HatType.Cosmic);ui.EquipSelectedItem();
    ui.SelectBroomSlot();ui.SelectBroom((int)BroomType.Speed);ui.EquipSelectedItem();
    Check(DataConfig.hatIndex==5&&DataConfig.broomIndex==3,"manual hat/broom calls work with no UI references");
    Life(ui,"OnDisable");
   }
   DatabaseManager.Instance=null;NetworkGameManager.Instance=null;
   return checks;
  }
 }
}
'@
$source = $stubs + "`nnamespace MagicCustomizationChecks { $magicEnum $equipmentEnums }`n" +
    "namespace MagicCustomizationChecks {`n$playerConfig`n}`n" +
    "namespace MagicCustomizationChecks { public static class NetworkHatCheck { $normalizeHat public static HatType Normalize(HatType hat)=>NormalizeHat(hat); } }`n" +
    "namespace MagicCustomizationChecks {`n$dataConfig`n}`n" +
    "namespace MagicCustomizationChecks {`n$magicTable`n}`n" +
    "namespace MagicCustomizationChecks {`n$equipment`n}`n" +
    "namespace MagicCustomizationChecks {`n$controller`n}`n" + $tests
Add-Type -IgnoreWarnings -WarningAction SilentlyContinue -TypeDefinition $source
"Magic customization checks passed: $([MagicCustomizationChecks.Tests]::Run())"
