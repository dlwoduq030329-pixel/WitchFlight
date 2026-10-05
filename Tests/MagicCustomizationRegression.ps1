# PowerShell 7. Runs the actual controller, DataConfig and MagicStatTable with UI/SDK stubs.
# Offline behavior checks only; does not connect to BACKND/Photon or run Unity Play Mode.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$controller = Get-Content (Join-Path $repo 'Assets/Script/MagicCustomizationUI.cs') -Raw
$dataConfig = Get-Content (Join-Path $repo 'Assets/Script/DataConfig.cs') -Raw
$magicTable = Get-Content (Join-Path $repo 'Assets/Script/MagicStatTable.cs') -Raw
$equipment = Get-Content (Join-Path $repo 'Assets/Script/PlayerEquipment.cs') -Raw
$playerData = Get-Content (Join-Path $repo 'Assets/Script/PlayerData.cs') -Raw
$magicEnum = [regex]::Match($playerData, 'public enum MagicType\s*\{[^}]+\}').Value
$equipmentEnums = [regex]::Matches($playerData, 'public enum (HatType|BroomType)\s*\{[^}]+\}').Value -join "`n"
if (!$magicEnum) { throw 'MagicType enum not found' }
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
 public struct Color {public float r,g,b,a;public static Color white=>new Color{r=1,g=1,b=1,a=1};}
 public static class Mathf {
  public static int Clamp(int v,int min,int max)=>Math.Clamp(v,min,max);
  public static int Max(int a,int b)=>Math.Max(a,b);
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
namespace UnityEngine.UI {
 public class Image:UnityEngine.MonoBehaviour {public UnityEngine.Sprite sprite;public bool enabled=true;}
 public class Button:UnityEngine.MonoBehaviour {
  public Image image=new Image();public bool interactable=true;
  public UnityEngine.Events.UnityEvent onClick=new UnityEngine.Events.UnityEvent();
 }
}
namespace TMPro {public class TMP_Text:UnityEngine.MonoBehaviour {public string text;}}
namespace MagicCustomizationChecks {
 using UnityEngine;
 public class DatabaseManager {
  public static DatabaseManager Instance;public bool IsDataConfigReady,HasLoadedProfile;
 }
 public class NetworkGameManager {public static NetworkGameManager Instance;public bool IsMatching;}
 public struct PlayerConfig {
  public int hairStylePreset,hatIndex,broomIndex,wandIndex;
  public float bangsLength,bangsDirection,sideHairLength,ahogeLength;
  public Color hairColor,clothColor,eyeColor;
  public PlayerConfig Sanitized()=>this;
  public static PlayerConfig Default=>new PlayerConfig();
 }
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
   public Image first=new Image(),second=new Image(),selected=new Image();
   public GameObject details=new GameObject(),h1=new GameObject(),h2=new GameObject();
   public Button equip=new Button(),slot1=new Button(),slot2=new Button(),cancel=new Button();
   public TMP_Text name=new TMP_Text(),description=new TMP_Text(),label=new TMP_Text(),status=new TMP_Text();
   public PlayerEquipment preview=new PlayerEquipment();
   public GameObject[] staffs=new GameObject[11];
   public int StaffWrites {get {int result=0;foreach(var staff in staffs)result+=staff.Writes;return result;}}
   public Sprite empty=new Sprite();
   public int changes,equips,errors,lastSlot;
   public Fixture(bool ready=true){
    DatabaseManager.Instance=new DatabaseManager{IsDataConfigReady=ready,HasLoadedProfile=ready};
    NetworkGameManager.Instance=null;
    DataConfig.ResetToDefaults();
    choices=Get<MagicCustomizationUI.MagicChoice[]>(ui,"magicChoices");
    foreach(var c in choices){c.button=new Button();c.buttonImage=new Image();c.icon=new Sprite();c.displayName=c.magic.ToString();c.description="Description "+c.magic;}
    Set(ui,"slot1Button",slot1);Set(ui,"slot2Button",slot2);
    Set(ui,"slot1Image",first);Set(ui,"slot2Image",second);Set(ui,"emptySlotSprite",empty);
    Set(ui,"slot1Highlight",h1);Set(ui,"slot2Highlight",h2);
    Set(ui,"selectionPanel",details);Set(ui,"selectedMagicImage",selected);
    Set(ui,"selectedMagicName",name);Set(ui,"selectedMagicDescription",description);
    Set(ui,"equipButton",equip);Set(ui,"equipButtonText",label);Set(ui,"statusText",status);
    Set(ui,"cancelSelectionButton",cancel);
    for(int i=0;i<staffs.Length;i++)staffs[i]=new GameObject();
    Set(preview,"magicStaffPrefabs",staffs);
    preview.BindToDataConfig();
    Get<UnityEvent<int>>(ui,"onEquipped").AddListener(s=>{equips++;lastSlot=s;});
    Get<UnityEvent<string>>(ui,"onEquipFailed").AddListener(s=>errors++);
    DataConfig.Changed+=Changed;
    Life(ui,"OnEnable");
   }
   void Changed(){changes++;}
   public void Pick(MagicType magic)=>Array.Find(choices,c=>c.magic==magic).button.onClick.Invoke();
   public Sprite Icon(MagicType magic)=>Array.Find(choices,c=>c.magic==magic).icon;
   public void Dispose(){Life(ui,"OnDisable");preview.UnbindFromDataConfig();DataConfig.Changed-=Changed;}
  }
  public static int Run(){
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
    DataConfig.magic1Index=(int)MagicType.Smoke;
    Check(f.first.sprite==f.Icon(MagicType.Smoke)&&f.staffs[(int)MagicType.Smoke].activeSelf,"external DataConfig edits refresh equipped UI and preview");
    Life(f.ui,"OnDisable");
    DataConfig.magic1Index=(int)MagicType.Fire;
    Check(f.first.sprite==f.Icon(MagicType.Smoke),"disabled controller unsubscribed from data");
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
    using(DataConfig.BeginChangeBatch(false)){DataConfig.magic1Index=(int)MagicType.Decoy;}
    Life(f.ui,"Update");
    Check(f.choices[0].button.interactable&&f.first.sprite==f.Icon(MagicType.Decoy)&&f.changes==0,"silent login load refreshes on readiness transition without save");
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
    f.preview.SetFlightEquipmentVisible(false);
    DataConfig.magic2Index=(int)MagicType.Mine;
    Check(Array.TrueForAll(f.staffs,s=>!s.activeSelf),"data changes outside customization keep flight equipment hidden");
    f.preview.SetFlightEquipmentVisible(true);
    Check(f.staffs[(int)MagicType.Mine].activeSelf,"reopening customization reveals latest equipped staff");
    Life(f.preview,"OnDisable");DataConfig.magic2Index=(int)MagicType.Decoy;
    Check(f.staffs[(int)MagicType.Mine].activeSelf,"disabled model unsubscribes from local data");
    Life(f.preview,"OnEnable");
    Check(f.staffs[(int)MagicType.Decoy].activeSelf,"re-enabled model catches up with local data");
    f.preview.ApplyLoadout(HatType.Classic,BroomType.Standard,MagicType.Ice,MagicType.Fire);
    int writes=f.StaffWrites;
    DataConfig.magic1Index=(int)MagicType.Thunder;
    Check(f.staffs[(int)MagicType.Ice].activeSelf&&f.StaffWrites==writes,"replicated battle loadout unsubscribes and ignores local DataConfig");
   }
   using(var f=new Fixture()){
    var fire=f.choices[0];fire.icon=null;
    var fallbackIcon=new Sprite();
    var table=new MagicStatTable{magics=new[]{new MagicStatEntry{magic=MagicType.Fire,icon=fallbackIcon,displayName="Fallback Fire"}}};
    fire.displayName=null;Set(f.ui,"magicTable",table);f.ui.RefreshFromDataConfig();f.Pick(MagicType.Fire);
    Check(f.first.sprite==fallbackIcon&&f.selected.sprite==fallbackIcon&&f.name.text=="Fallback Fire","table fallback supports icon and name");
    Check(fire.button.image.sprite==null&&fire.buttonImage.sprite==fallbackIcon,"icon does not replace designer button border/background");
    Set(f.ui,"selectionPanel",f.ui.gameObject);f.ui.CancelSelection();
    Check(f.ui.gameObject.activeSelf,"mistaken self panel assignment cannot disable controller");
    Set(f.ui,"magicChoices",new MagicCustomizationUI.MagicChoice[]{null,fire});f.ui.SelectMagic((int)MagicType.Ice);
    Check(f.ui.SelectedMagic==MagicType.None&&f.changes==0,"unregistered magic and null choice are safe");
   }
   {
    // All UI references are optional: this component must remain usable via public methods.
    DatabaseManager.Instance=new DatabaseManager{IsDataConfigReady=true,HasLoadedProfile=true};
    NetworkGameManager.Instance=null;DataConfig.ResetToDefaults();
    var ui=new MagicCustomizationUI();Set(ui,"defaultSlot",2);Life(ui,"OnEnable");
    ui.SelectMagic((int)MagicType.Mine);ui.EquipSelectedMagic();
    Check(ui.SelectedSlot==2&&DataConfig.magic2Index==(int)MagicType.Mine,"manual calls work with no UI references and default slot 2");
    Life(ui,"OnDisable");
   }
   DatabaseManager.Instance=null;NetworkGameManager.Instance=null;
   return checks;
  }
 }
}
'@
$source = $stubs + "`nnamespace MagicCustomizationChecks { $magicEnum $equipmentEnums }`n" +
    "namespace MagicCustomizationChecks {`n$dataConfig`n}`n" +
    "namespace MagicCustomizationChecks {`n$magicTable`n}`n" +
    "namespace MagicCustomizationChecks {`n$equipment`n}`n" +
    "namespace MagicCustomizationChecks {`n$controller`n}`n" + $tests
Add-Type -IgnoreWarnings -WarningAction SilentlyContinue -TypeDefinition $source
"Magic customization checks passed: $([MagicCustomizationChecks.Tests]::Run())"
