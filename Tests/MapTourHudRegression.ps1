$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$tour = Get-Content Assets/Script/Camera/BattleMapTour.cs -Raw
function Method($name) {
 $m=[regex]::Match($tour,'(?ms)^    private void '+$name+'\(\)\r?\n    \{.*?^    \}')
 if(!$m.Success){throw "Missing $name"};$m.Value.Replace('private void','public void')
}
$source=@'
using System;
using System.Collections.Generic;
public enum FindObjectsInactive {Include}
public enum FindObjectsSortMode {None}
public class GameObject {public int scene=1;}
public class Transform {
 public Transform parent;
 public bool IsChildOf(Transform other){for(var t=this;t!=null;t=t.parent)if(t==other)return true;return false;}
}
public class Graphic {public bool enabled=true;public Transform transform=new();}
public class Canvas {
 public bool enabled=true;public GameObject gameObject=new();public Transform transform=new();
 public List<Graphic> graphics=new();
 public T[] GetComponentsInChildren<T>(bool include){return graphics.ConvertAll(g=>(T)(object)g).ToArray();}
}
public class Tour {
 public GameObject gameObject=new();public Graphic fadeImage;
 public List<Canvas> canvases=new();
 readonly Dictionary<Canvas,bool> hiddenHudCanvases=new();readonly Dictionary<Graphic,bool> hiddenHudGraphics=new();
 public T[] FindObjectsByType<T>(FindObjectsInactive a,FindObjectsSortMode b){return canvases.ConvertAll(c=>(T)(object)c).ToArray();}
'@ + (Method 'HideHud') + (Method 'RestoreHud') + @'
}
public static class Checks {
 static int count;static void Check(bool ok,string message){if(!ok)throw new Exception(message);count++;}
 public static int Run(){
  var tour=new Tour();var hud=new Canvas();var disabled=new Canvas{enabled=false};var otherScene=new Canvas();otherScene.gameObject.scene=2;
  var fadeCanvas=new Canvas();var fade=new Graphic();fade.transform.parent=fadeCanvas.transform;tour.fadeImage=fade;
  var sharedHud=new Graphic();var sharedDisabled=new Graphic{enabled=false};fadeCanvas.graphics.AddRange(new[]{fade,sharedHud,sharedDisabled});
  tour.canvases.AddRange(new[]{hud,disabled,otherScene,fadeCanvas});
  tour.HideHud();
  Check(!hud.enabled&&!disabled.enabled,"hide all HUD canvases");
  Check(otherScene.enabled,"leave unrelated scenes unchanged");
  Check(fadeCanvas.enabled&&fade.enabled&&!sharedHud.enabled,"shared fade remains while sibling HUD disappears");
  hud.enabled=true;sharedHud.enabled=true;var lateHud=new Canvas();tour.canvases.Add(lateHud);tour.HideHud();
  Check(!hud.enabled&&!sharedHud.enabled&&!lateHud.enabled,"suppress scripts re-enabling HUD and newly created UI");
  tour.RestoreHud();
  Check(hud.enabled&&lateHud.enabled&&sharedHud.enabled,"restore visible HUD at tour exit");
  Check(!disabled.enabled&&!sharedDisabled.enabled,"preserve originally disabled UI");
  hud.enabled=false;tour.RestoreHud();Check(!hud.enabled,"restoration is one-shot");
  hud.enabled=true;tour.HideHud();tour.RestoreHud();Check(hud.enabled,"subsequent tours capture fresh state");
  return count;
 }
}
'@
Add-Type -TypeDefinition $source
"Map tour HUD lifecycle checks passed: $([Checks]::Run())"
