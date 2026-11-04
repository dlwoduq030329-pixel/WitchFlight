$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$text=Get-Content (Join-Path $root 'Assets/Script/BattlePerkUI.LevelUp.cs') -Raw
function Method($name) {
 $m=[regex]::Match($text,'(?ms)^    (?:private|internal) (?:static )?\w+ '+$name+'\([^{}]*\)\r?\n    \{.*?^    \}')
 if(!$m.Success){throw "Missing $name"};$m.Value.Replace('private ','public ').Replace('internal ','public ')
}
$code=@'
using System;
public static class Time {public static float unscaledTime;}
public enum KeyCode {Space}
public static class Input {public static bool down;public static bool GetKeyDown(KeyCode key)=>down;}
public static class BattleEmoteUI {public static bool CapturesMouse;}
public struct Color {public Color(float r,float g,float b,float a){}public static Color white=>new();}
public class Text {public string text;public bool enabled;public Color color;}
public class PlayerData {public int PerkChoicesEarned;public bool CanSelectPerks;}
public class Gate {public bool Showing;}
public struct Vector2 {public float x,y;public Vector2(float x,float y){this.x=x;this.y=y;}}
public struct Rect {public float xMin,yMin,xMax,yMax;}
public static class Mathf {public const float PI=(float)Math.PI;public static float Max(float a,float b)=>Math.Max(a,b);public static float Clamp01(float x)=>Math.Clamp(x,0,1);public static float Clamp(float x,float min,float max)=>Math.Clamp(x,min,max);public static float Sin(float x)=>(float)Math.Sin(x);}
public class Probe {
 public PlayerData local=new(),levelUpObservedPlayer;public bool Allowed=true,requestPending;
 public Gate gate=new();public Text perkPromptText=new();
 public int levelUpObservedEarned=-1;public float levelUpWingStarted=-100,perkUnavailableUntil,perkUnavailableSeconds=1.2f;
 public void EnsureLevelUpBindings(){}public bool Ready(PlayerData d)=>d!=null;
 public bool CanOfferPerk(PlayerData d)=>d!=null&&d.CanSelectPerks&&!requestPending;
 public void ResetLevelUpFeedback(){levelUpWingStarted=-100;perkUnavailableUntil=0;perkPromptText.enabled=false;}
'@+(Method 'UpdateLevelUpFeedback')+(Method 'WingPulse')+(Method 'ClampPerkPrompt')+@'
}
public static class FeedbackChecks {
 static int n;static void Check(bool b,string s){if(!b)throw new Exception(s);n++;}
 public static int Run(){
  var p=new Probe();p.UpdateLevelUpFeedback();Check(!p.perkPromptText.enabled&&p.levelUpWingStarted<0,"No initial level-up flash or ready prompt");
  Input.down=true;p.UpdateLevelUpFeedback();Check(p.perkPromptText.enabled&&p.perkPromptText.text=="레벨이 부족합니다","Space without a choice displays denial");
  Input.down=false;Time.unscaledTime=2;p.UpdateLevelUpFeedback();Check(!p.perkPromptText.enabled,"Denial expires without holding Space");
  p.local.PerkChoicesEarned=1;p.local.CanSelectPerks=true;p.UpdateLevelUpFeedback();
  Check(p.levelUpWingStarted==2&&p.perkPromptText.text=="space바를 눌러서 특전을 선택하세요"&&p.perkPromptText.enabled,"Earned choice flashes wings and prompts selection");
  Time.unscaledTime=3;p.UpdateLevelUpFeedback();Check(p.levelUpWingStarted==2,"No per-frame animation restart");
  p.gate.Showing=true;p.UpdateLevelUpFeedback();Check(!p.perkPromptText.enabled,"Open cards do not repeat press-Space prompt");
  p.gate.Showing=false;p.requestPending=true;p.UpdateLevelUpFeedback();Check(!p.perkPromptText.enabled,"Selection request immediately hides ready prompt");
  p.requestPending=false;p.local.CanSelectPerks=false;Input.down=true;p.UpdateLevelUpFeedback();Check(p.perkPromptText.text=="레벨이 부족합니다","Spent choice shows denial on next Space press");
  p.local.PerkChoicesEarned=2;p.local.CanSelectPerks=true;p.UpdateLevelUpFeedback();Check(p.levelUpWingStarted==3&&p.perkUnavailableUntil==0,"Next level cancels stale denial");
  p.Allowed=false;p.UpdateLevelUpFeedback();Check(!p.perkPromptText.enabled,"Menu or phase change hides prompt");
  Check(Probe.WingPulse(-1,.5f)==0&&Probe.WingPulse(0,.5f)==0&&Probe.WingPulse(.5f,.5f)==0,"Wings return to authored scale outside animation");
  Check(Probe.WingPulse(.25f,.5f)>.99f,"Wing pop reaches peak midway");
  var bounds=new Rect{xMin=-960,xMax=960,yMin=-540,yMax=540};
  var half=new Vector2(255,20);
  var corrected=Probe.ClampPerkPrompt(new Vector2(0,-558),bounds,half);
  Check(corrected.y==-512,"Oversized MP rect must not put prompt below screen");
  var valid=Probe.ClampPerkPrompt(new Vector2(0,-470),bounds,half);
  Check(valid.y==-470,"Visible wing-relative position stays unchanged");
  var edge=Probe.ClampPerkPrompt(new Vector2(1200,800),bounds,half);
  Check(edge.x+half.x<=bounds.xMax-8&&edge.y+half.y<=bounds.yMax-8,"Whole text stays inside right/top safe edges");
  return n;
 }
}
'@
Add-Type -TypeDefinition $code
"Perk level-up and unavailable prompt checks passed: $([FeedbackChecks]::Run())"
