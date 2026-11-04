# Production layer routing with small Unity doubles; does not exercise live rendering.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$routing = Get-Content Assets/Script/BattleWorldUI.cs -Raw
$source = @'
using System;
using System.Collections.Generic;
namespace UnityEngine {
 public static class LayerMask {
  public static int WorldLayer=8;
  public static int NameToLayer(string name)=>name=="BattleWorldUI"?WorldLayer:-1;
 }
 public class GameObject {
  public int layer=5;public bool activeSelf=true;public Transform transform;
  public GameObject(){transform=new Transform{gameObject=this};}
 }
 public class Transform {
  public GameObject gameObject;public List<Transform> children=new();
  public int childCount=>children.Count;public Transform GetChild(int index)=>children[index];
 }
}
'@
$source += "`n" + ($routing -replace '(?m)^using .*;\r?\n', '' -creplace '\b(GameObject|Transform|LayerMask)\b', 'UnityEngine.$1')
$source += @'

public static class WorldUiChecks {
 static int n;static void Check(bool value,string message){if(!value)throw new Exception(message);n++;}
 public static int Run(){
  int original=unchecked((int)2147483735u),world=1<<8,ui=1<<5;
  int main=BattleWorldUI.IncludeInMask(original);
  Check((main&world)!=0,"gameplay camera includes world UI even when saved mask omits it");
  Check((main&~world)==(original&~world),"all existing camera layers preserved");
  Check((main&ui)==0,"HUD UI is not enabled on gameplay camera");
  Check(BattleWorldUI.IncludeInMask(main)==main,"idempotent enable");
  Check(BattleWorldUI.IncludeInMask(-1)==-1,"all-layers mask unchanged");
  var root=new UnityEngine.GameObject();var child=new UnityEngine.GameObject();
  var template=new UnityEngine.GameObject{activeSelf=false};var other=new UnityEngine.GameObject();
  root.transform.children.Add(child.transform);child.transform.children.Add(template.transform);
  BattleWorldUI.ApplyLayer(root);
  Check(root.layer==8&&child.layer==8&&template.layer==8,"root plus nested and inactive graphics all route to world layer");
  Check(!template.activeSelf&&other.layer==5,"visibility and unrelated HUD objects untouched");
  var newCell=new UnityEngine.GameObject();child.transform.children.Add(newCell.transform);
  BattleWorldUI.ApplyLayer(root);Check(newCell.layer==8,"late-created HP cells also rerouted");
  BattleWorldUI.ApplyLayer(null);Check(true,"destroyed/missing view is safe");
  UnityEngine.LayerMask.WorldLayer=12;
  Check(BattleWorldUI.IncludeInMask(0)==1<<12,"layer is resolved by name, not a hardcoded index");
  BattleWorldUI.ApplyLayer(root);Check(template.layer==12,"hierarchy follows named layer if project index changes");
  UnityEngine.LayerMask.WorldLayer=-1;
  Check(BattleWorldUI.IncludeInMask(original)==original,"missing layer never shifts a negative index into layer 31");
  BattleWorldUI.ApplyLayer(root);Check(root.layer==0&&template.layer==0,"missing settings falls back to world, never HUD layer");
  return n;
 }
}
'@
Add-Type -TypeDefinition $source
"World UI layer routing checks passed: $([WorldUiChecks]::Run())"

$settings = Get-Content ProjectSettings/TagManager.asset -Raw
$layerSection = [regex]::Match($settings, '(?ms)^  layers:\r?\n(.*?)^  m_SortingLayers:').Groups[1].Value
$layers = @([regex]::Matches($layerSection, '(?m)^  -([^\r\n]*)') | ForEach-Object { $_.Groups[1].Value.Trim() })
if ($layers.Count -ne 32 -or $layers[8] -ne 'BattleWorldUI' -or $layers[5] -ne 'UI' -or $layers[6] -ne 'Charactor' -or $layers[31] -ne 'BattlePortrait') {
    throw 'World layer must occupy unused slot 8 without shifting existing layers'
}
$scene = Get-Content Assets/Scenes/Battle.unity -Raw
function CameraMask([string]$id) {
    $block = [regex]::Match($scene, '(?ms)^--- !u!20 &' + $id + '\r?\n.*?(?=^--- !u!|\z)').Value
    if (!$block) { throw "Missing camera $id" }
    [uint32][regex]::Match($block, 'm_CullingMask:\s+serializedVersion: 2\s+m_Bits: (\d+)').Groups[1].Value
}
$mainMask = CameraMask '1331985548'
$uiMask = CameraMask '1474259857'
if (($uiMask -band 256) -ne 0 -or ($uiMask -band 32) -eq 0 -or ($mainMask -band 32) -ne 0) {
    throw 'Saved UI camera must only draw HUD, and main camera must not duplicate HUD'
}
$canvas = [regex]::Match($scene, '(?ms)^--- !u!223 &2072900285\r?\n.*?(?=^--- !u!|\z)').Value
if (!$canvas.Contains('m_RenderMode: 1') -or !$canvas.Contains('m_Camera: {fileID: 1474259857}')) { throw 'Existing Battle HUD camera binding changed' }
$follow = Get-Content Assets/Script/Camera/CameraFollow.cs -Raw
foreach ($token in 'originalCullingMask = viewCamera.cullingMask;', 'viewCamera.cullingMask = BattleWorldUI.IncludeInMask(viewCamera.cullingMask);', 'viewCamera.cullingMask = originalCullingMask;') {
    if (!$follow.Contains($token)) { throw "Missing camera mask lifecycle: $token" }
}
$hp = Get-Content Assets/Script/hpfollow.cs -Raw
if ($hp -match '\.layer\s*=\s*5\s*;') { throw 'Enemy HP/halo must never use the screen-space UI layer' }
foreach ($method in 'CreateView', 'CreateFallback', 'UpdateEnemyHalo') {
    $body = [regex]::Match($hp, '(?ms)^    private void ' + $method + '\(\).*?^    \}').Value
    if (!$body.Contains('BattleWorldUI.ApplyLayer(')) { throw "Missing routing path: $method" }
}
if ($hp -notmatch 'gridBar.UpdateHealth\([^;]+;\s*(?://[^\r\n]*\r?\n\s*)*BattleWorldUI.ApplyLayer\(viewRoot\);') {
    throw 'Newly generated cells must be routed after health updates'
}
'Named layer registration, authored/fallback/halo routes, HUD isolation and camera mask restoration contracts passed.'
