# Runs the production tooltip/cursor methods against deterministic UI doubles.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$text = Get-Content (Join-Path $repo 'Assets/Script/BattlePerkUI.TabTooltip.cs') -Raw
function Method($name) {
 $m=[regex]::Match($text,'(?ms)^    private void '+$name+'\([^\r\n]*\)\r?\n    \{.*?^    \}')
 if(!$m.Success){throw "Missing $name"};$m.Value.Replace('private void','public void')
}
$source=@'
using System;
public struct Vector2 {
 public float x,y;public Vector2(float x,float y){this.x=x;this.y=y;}
 public static bool operator==(Vector2 a,Vector2 b)=>a.x==b.x&&a.y==b.y;
 public static bool operator!=(Vector2 a,Vector2 b)=>!(a==b);
 public override bool Equals(object o)=>o is Vector2 v&&this==v;public override int GetHashCode()=>HashCode.Combine(x,y);
}
public struct Vector3 {public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}}
public class GameObject {public bool activeSelf=true;public bool activeInHierarchy=>activeSelf;}
public class RectTransform {public GameObject gameObject=new();public Vector2 pivot;public Vector3 position;public float x,y,width=20,height=20;}
public class Image {public RectTransform rectTransform=new();public object sprite;public bool enabled=true;public bool isActiveAndEnabled=>enabled&&rectTransform.gameObject.activeInHierarchy;}
public class TMP_Text {public string text;}
public class Camera {}
public enum RenderMode {ScreenSpaceOverlay,ScreenSpaceCamera}
public class Canvas {public RenderMode renderMode;public Camera worldCamera;}
public static class RectTransformUtility {
 public static int tests;
 public static bool RectangleContainsScreenPoint(RectTransform r,Vector2 p,Camera c){tests++;return p.x>=r.x&&p.x<=r.x+r.width&&p.y>=r.y&&p.y<=r.y+r.height;}
 public static bool ScreenPointToWorldPointInRectangle(RectTransform r,Vector2 p,Camera c,out Vector3 w){w=new(p.x,p.y,0);return true;}
}
public static class Input {public static Vector2 mousePosition;public static bool left,right;public static bool GetMouseButton(int n)=>n==0?left:right;}
public static class Screen {public static int width=1000,height=800;}
public enum CursorLockMode {None,Locked}
public static class Cursor {public static CursorLockMode lockState=CursorLockMode.Locked;public static bool visible;}
public static class Application {public static bool isFocused=true;}
public static class CombatPresentation {public static bool MenuOpen;}
public enum BattleStartPhase {Playing,Countdown,Ended}
public class BattleManager {public static BattleManager Instance=new();public bool IsGameplayActive=true;public BattleStartPhase Phase;}
public enum PerkId {None,First,Second}
public class Definition {public string displayName,description;}
public class Table {public Definition definition=new(){displayName="First perk",description="Damage +30%"};public Definition Get(PerkId id)=>definition;}
public class PlayerData {public bool ready=true;public Table PerkTable=new();}
public class Card {public PerkId displayedPerk;public Image icon=new();}
public class View {public Card[] effects={new(),new(),new(),new()};}
public class Probe {
 public RectTransform tabInfoPanel=new(),tabInfoParent=new();public Image tabInfoImage=new();public TMP_Text tabInfoName=new(),tabInfoDescription=new();public Canvas tabInfoCanvas=new();
 public bool tabOwnsCursor,tabBlocksCombat,tabWaitForMouseRelease,tabTooltipDirty=true;
 public Vector2 tabLastPointer=new(float.PositiveInfinity,float.PositiveInfinity),tabLastScreen;
 public int tabHoveredRow=-1,tabHoveredPerk=-1;
 public View[] authoredTabViews={new(),new()};public RectTransform[] authoredTabRows={new(),new()};public PlayerData[] authoredTabPlayers={new(),new()};
 public static bool Ready(PlayerData p)=>p!=null&&p.ready;
 public static void Show(GameObject g,bool value){g.activeSelf=value;}
 public static void SetIcon(Image i,object s){if(i!=null){i.sprite=s;i.enabled=s!=null;}}
 public static void Text(TMP_Text t,string s){if(t!=null)t.text=s;}
 public static string Description(Definition d)=>d.description;
'@
foreach($name in 'UpdateTabInteraction','HideTabTooltip','UpdateTabTooltip'){$source+=Method $name}
$pivot=[regex]::Match($text,'(?ms)^    private static Vector2 TabTooltipPivot\(.*?;').Value
$source+=$pivot.Replace('private static','public static')+"`n}`n"
$source+=@'
public static class Checks {
 static int count;static void Check(bool b,string m){if(!b)throw new Exception(m);count++;}
 public static int Run(){
  var screen=new Vector2(1000,800);
  Check(Probe.TabTooltipPivot(new(100,700),screen)==new Vector2(0,0),"top-left pivot");
  Check(Probe.TabTooltipPivot(new(900,700),screen)==new Vector2(1,0),"top-right pivot");
  Check(Probe.TabTooltipPivot(new(100,100),screen)==new Vector2(0,1),"bottom-left pivot");
  Check(Probe.TabTooltipPivot(new(900,100),screen)==new Vector2(1,1),"bottom-right pivot");
  var p=new Probe();p.UpdateTabInteraction(true);Check(Cursor.visible&&Cursor.lockState==CursorLockMode.None&&p.tabBlocksCombat,"Tab unlocks cursor and blocks attack");
  Input.mousePosition=new(100,700);p.UpdateTabTooltip();Check(!p.tabInfoPanel.gameObject.activeSelf,"empty perks never show tooltip");
  var card=p.authoredTabViews[1].effects[2];card.displayedPerk=PerkId.First;card.icon.sprite=new object();card.icon.rectTransform.x=90;card.icon.rectTransform.y=690;p.tabTooltipDirty=true;
  p.authoredTabPlayers[1].PerkTable.definition=new(){displayName="Enemy perk",description="Health +25%"};
  p.UpdateTabTooltip();Check(p.tabInfoPanel.gameObject.activeSelf&&p.tabInfoImage.sprite==card.icon.sprite&&p.tabInfoName.text=="Enemy perk"&&p.tabInfoDescription.text=="Health +25%","hover uses target player's perk image/name/description");
  Check(p.tabInfoPanel.pivot==new Vector2(0,0)&&p.tabInfoPanel.position.x==100&&p.tabInfoPanel.position.y==700,"panel pivot and cursor position");
  int queries=RectTransformUtility.tests;p.UpdateTabTooltip();Check(queries==RectTransformUtility.tests,"stationary unchanged pointer avoids repeated hit tests");
  Input.mousePosition=new(105,705);p.UpdateTabTooltip();Check(p.tabInfoPanel.position.x==105&&p.tabInfoPanel.position.y==705,"tooltip follows pointer inside icon");
  Input.mousePosition=new(400,400);p.UpdateTabTooltip();Check(!p.tabInfoPanel.gameObject.activeSelf,"leaving icon hides tooltip");
  Input.mousePosition=new(100,700);p.UpdateTabTooltip();p.authoredTabRows[1].gameObject.activeSelf=false;p.tabTooltipDirty=true;p.UpdateTabTooltip();Check(!p.tabInfoPanel.gameObject.activeSelf,"departed hidden row cannot keep tooltip");
  p.authoredTabRows[1].gameObject.activeSelf=true;p.tabTooltipDirty=true;p.UpdateTabTooltip();
  Input.left=true;p.UpdateTabInteraction(false);Check(!p.tabInfoPanel.gameObject.activeSelf&&!Cursor.visible&&Cursor.lockState==CursorLockMode.Locked,"release Tab hides tooltip and restores flight cursor");
  Check(p.tabBlocksCombat,"held mouse cannot fire on Tab release");
  Input.left=false;p.UpdateTabInteraction(false);Check(p.tabBlocksCombat,"mouse release frame suppressed");p.UpdateTabInteraction(false);Check(!p.tabBlocksCombat,"combat resumes after release");
  p.UpdateTabInteraction(true);CombatPresentation.MenuOpen=true;p.UpdateTabInteraction(false);Check(Cursor.visible&&Cursor.lockState==CursorLockMode.None,"menu retains visible cursor");
  CombatPresentation.MenuOpen=false;p.UpdateTabInteraction(true);BattleManager.Instance.IsGameplayActive=false;BattleManager.Instance.Phase=BattleStartPhase.Ended;p.UpdateTabInteraction(false);Check(Cursor.visible&&Cursor.lockState==CursorLockMode.None,"results retain visible cursor");
  return count;
 }
}
'@
Add-Type -TypeDefinition $source
"Tab tooltip content, quadrants, cursor and cleanup checks passed: $([Checks]::Run())"
