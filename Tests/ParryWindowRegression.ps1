$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$hud = Get-Content (Join-Path $repo 'Assets/Script/BattleHud.cs') -Raw
$player = Get-Content (Join-Path $repo 'Assets/Script/Player.cs') -Raw
$section = [regex]::Match($hud, '(?s)    \[Header\("Active parry window.*?(?=    \[Header\("UI style)').Value
if (!$section) { throw 'Missing active parry HUD' }
$section = [regex]::Replace($section, '(?ms)^    private void PlaceParryRing\(.*?^    \}', '    private void PlaceParryRing(RectTransform ring) {}')
$section = [regex]::Replace($section, '\[(?:Header|Tooltip|SerializeField|Min)[^\r\n]*?\]\s*', '')
$section = $section.Replace('private ', 'public ')
$remaining = [regex]::Match($player, '(?s)    public float ParryRemainingSeconds =>.*?;').Value
if (!$remaining) { throw 'Missing simulation timer query' }
$source = @'
using System;
public class NetworkObject { public bool IsValid = true; }
public class Timer { public float deadline; public float? RemainingTime(object runner) => Math.Max(0, deadline-Time.unscaledTime); }
public static class Time { public static float unscaledTime; }
public class Player {
 public static Player LocalPlayer; public NetworkObject Object=new(); public object Runner=new();
 public bool IsAlive=true,IsReturningToMap; public int ParrySequence;
 public float ParryWindowSeconds=.4f; public Timer ParryTimer=new();
 public bool IsParrying => Time.unscaledTime < ParryTimer.deadline;
'@ + $remaining + @'
}
public class BattleManager { public static BattleManager Instance=new(); public bool IsGameplayActive=true; }
public class GameObject { public bool activeSelf; public void SetActive(bool b) {activeSelf=b;} }
public class RectTransform { public Vector2 sizeDelta; public T GetComponent<T>() where T:new()=>new T(); }
public class CombatFeedbackGraphic {
 public GameObject gameObject=new(); public RectTransform rectTransform=new();
 public float RingFill,RingWidth; public Color color; public void SetVerticesDirty(){}
}
public class Image {
 public enum Type {Filled} public enum FillMethod {Radial360} public enum Origin360 {Top}
 public Type type; public FillMethod fillMethod; public int fillOrigin; public bool fillClockwise,raycastTarget,enabled;
 public float fillAmount; public Color color;
}
public struct Color { public float a; public Color(float r,float g,float b,float a) {this.a=a;} }
public struct Vector2 { public static Vector2 one=>new(); public static Vector2 operator*(Vector2 v,float f)=>v; }
public static class Mathf {
 public static float Max(float a,float b)=>Math.Max(a,b);
 public static float Clamp01(float f)=>Math.Clamp(f,0,1);
 public static float Lerp(float a,float b,float t)=>a+(b-a)*t;
}
public class Hud {
 public bool MenuOpen;
 public static void Visible(GameObject obj,bool b) { if(obj!=null)obj.SetActive(b); }
 public RectTransform CreateFeedbackRing(string title, Color color,float size,object sprite)=>new();
'@ + $section + @'
}
public static class ParryWindowChecks {
 static int count;
 static void Check(bool b,string text) {if(!b)throw new Exception(text); count++;}
 static bool Near(float a,float b)=>Math.Abs(a-b)<.001f;
 public static int Run() {
  var p=new Player(); Player.LocalPlayer=p; var h=new Hud {parryWindowFill=new Image()};
  h.UpdateParryWindow(); Check(!h.parryWindowFill.enabled,"No accepted attempt: no active gauge");
  p.ParryTimer.deadline=.4f; h.UpdateParryWindow();
  Check(h.parryWindowFill.enabled && Near(h.parryWindowFill.fillAmount,1),"Accepted attempt fills immediately");
  Time.unscaledTime=.2f; h.UpdateParryWindow(); Check(Near(h.parryWindowFill.fillAmount,.5f),"Read actual remaining window");
  p.ParrySequence++; h.UpdateParryWindow();
  Check(h.parrySuccessRing.gameObject.activeSelf,"Confirmed success has separate flash");
  Check(Near(h.parryWindowFill.fillAmount,.5f),"Success alone must not extend active window");
  p.ParryTimer.deadline=.6f; h.UpdateParryWindow(); Check(Near(h.parryWindowFill.fillAmount,1),"Accepted repeat restarts without waiting for flash");
  Time.unscaledTime=.6f; p.ParrySequence++; h.UpdateParryWindow();
  Check(!h.parryWindowFill.enabled && h.parrySuccessRing.gameObject.activeSelf,"Late success flash cannot imply active block");
  Time.unscaledTime=.8f; h.UpdateParryWindow(); Check(!h.parrySuccessRing.gameObject.activeSelf,"Flash expires");
  p.ParryTimer.deadline=1.2f; h.MenuOpen=true; h.UpdateParryWindow(); Check(!h.parryWindowFill.enabled,"Menu hides active window");
  h.MenuOpen=false; h.UpdateParryWindow(); Check(Near(h.parryWindowFill.fillAmount,1),"Resume reads timer without resetting it");
  p.IsAlive=false; h.UpdateParryWindow(); Check(!h.parryWindowFill.enabled,"Death hides window");
  var next=new Player {ParrySequence=9}; next.ParryTimer.deadline=1.2f; Player.LocalPlayer=next;
  h.UpdateParryWindow(); Check(!h.parrySuccessRing.gameObject.activeSelf,"Respawn does not replay historical success");
  h.parryWindowFill=null; h.UpdateParryWindow(); Check(h.generatedParryWindow.gameObject.activeSelf,"Unassigned UI generates working fallback");
  Time.unscaledTime=1; h.UpdateParryWindow(); Check(Near(h.generatedParryWindow.RingFill,.5f),"Generated and custom gauge use same deadline");
  BattleManager.Instance.IsGameplayActive=false; h.UpdateParryWindow(); Check(!h.generatedParryWindow.gameObject.activeSelf,"Round end hides fallback");
  next.Object.IsValid=false; Check(next.ParryRemainingSeconds==0,"Despawned object is safe to query");
  return count;
 }
}
'@
Add-Type -TypeDefinition $source
"Parry window behavior checks passed: $([ParryWindowChecks]::Run())"
