# Execute the production ReadOnly helper with Unity-style fake-null component doubles.
# A missing Unity component may have a managed reference but compare equal to null.
# Does not substitute for live Editor Canvas rendering.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$ui = Get-Content Assets/Script/BattleEmoteUI.cs -Raw
$method = [regex]::Match($ui, '(?ms)^    private static CanvasGroup ReadOnly\(GameObject root\).*?^    \}')
if (!$method.Success) { throw 'Missing production ReadOnly helper' }
$source = @'
using System;
namespace EmoteInitializationChecks {
 public class UnityObject {
  public bool NativeAlive=true;
  public static bool operator==(UnityObject a,UnityObject b) {
   bool aNull=ReferenceEquals(a,null)||!a.NativeAlive;
   bool bNull=ReferenceEquals(b,null)||!b.NativeAlive;
   return aNull||bNull ? aNull==bNull : ReferenceEquals(a,b);
  }
  public static bool operator!=(UnityObject a,UnityObject b)=>!(a==b);
  public override bool Equals(object obj)=>this==obj as UnityObject;
  public override int GetHashCode()=>base.GetHashCode();
 }
 public class CanvasGroup:UnityObject {
  bool interactive=true,raycasts=true;
  public float alpha=1f;
  public bool interactable {get=>interactive;set{CheckAlive();interactive=value;}}
  public bool blocksRaycasts {get=>raycasts;set{CheckAlive();raycasts=value;}}
  void CheckAlive(){if(!NativeAlive)throw new InvalidOperationException("MissingComponentException: CanvasGroup native object is missing");}
 }
 public class Graphic {public bool raycastTarget=true;}
 public class GameObject {
  public CanvasGroup Group;
  public bool ReturnFakeNull=true,IncludedInactive;
  public int Added;
  public Graphic[] Graphics={new Graphic(),new Graphic(),new Graphic()};
  public T GetComponent<T>() where T:class {
   if(!ReferenceEquals(Group,null))return Group as T;
   return ReturnFakeNull ? new CanvasGroup{NativeAlive=false} as T : null;
  }
  public T AddComponent<T>() where T:class,new() {Added++;Group=new T() as CanvasGroup;return Group as T;}
  public T[] GetComponentsInChildren<T>(bool includeInactive) where T:class {IncludedInactive=includeInactive;return Graphics as T[];}
 }
 public static class Probe {
'@
$source += $method.Value -replace 'private static CanvasGroup','public static CanvasGroup'
$source += @'
 }
 public static class Tests {
  static int count;
  static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;}
  public static int Run(){
   var missing=new CanvasGroup{NativeAlive=false};
   Check(!ReferenceEquals(missing,null)&&missing==null,"test models Unity fake null, not ordinary CLR null");
   foreach(bool fake in new[]{true,false}) {
    var root=new GameObject{ReturnFakeNull=fake};
    var group=Probe.ReadOnly(root);
    Check(group!=null&&group.NativeAlive&&root.Added==1,"missing group is added: fake-null="+fake);
    Check(ReferenceEquals(group,root.Group),"return the newly attached real component");
    Check(!group.interactable&&!group.blocksRaycasts,"wheel cannot capture combat pointer raycasts");
    Check(root.IncludedInactive&&Array.TrueForAll(root.Graphics,g=>!g.raycastTarget),"inactive and active graphics are read-only");
    var again=Probe.ReadOnly(root);
    Check(ReferenceEquals(group,again)&&root.Added==1,"repeat initialization does not add duplicate groups");
   }
   var custom=new GameObject{Group=new CanvasGroup{alpha=.42f}};
   var existing=custom.Group;Probe.ReadOnly(custom);
   Check(custom.Added==0&&ReferenceEquals(existing,custom.Group),"reuse custom UI CanvasGroup");
   Check(Math.Abs(custom.Group.alpha-.42f)<.001f,"preserve custom UI alpha");
   var destroyed=new GameObject{Group=new CanvasGroup{NativeAlive=false}};
   var repaired=Probe.ReadOnly(destroyed);
   Check(repaired!=null&&repaired.NativeAlive&&destroyed.Added==1,"destroyed native component is replaced");
   return count;
  }
 }
}
'@
Add-Type -TypeDefinition $source
Write-Output ("Emote UI initialization checks passed: " + [EmoteInitializationChecks.Tests]::Run())
if (!$ui.Contains('ReadOnly(wheelRoot);') -or !$ui.Contains('view.group = ReadOnly(view.root);')) {
    throw 'Wheel and both display roots must use the validated component setup.'
}
Write-Output 'Generated wheel and display roots share the validated setup.'
if (($ui | Select-String -Pattern 'if \(!uiInitialized\) return;' -AllMatches).Matches.Count -lt 2) {
    throw 'OnEnable and Update must not capture input/use display roots after failed initialization.'
}
if ($ui.Contains('GetComponent<BattleHud>()?.FeedbackFont')) {
    throw 'Optional BattleHud lookup must also use Unity-aware null comparison.'
}
Write-Output 'Failed initialization/input safety contracts passed.'
