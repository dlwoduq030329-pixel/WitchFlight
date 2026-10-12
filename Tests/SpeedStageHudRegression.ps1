# Execute production HUD toggling offline and validate the saved Battle scene bindings.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$hud = Get-Content Assets/Script/BattleHud.cs -Raw
function Method([string]$name) {
    $match = [regex]::Match($hud, '(?ms)^    private (?:static )?[\w<>]+ '+$name+'\([^{}]*?\)\r?\n    \{.*?^    \}')
    if (!$match.Success) { throw "Missing method $name" }
    $match.Value -replace '^    private ', '    public '
}
$fields = [regex]::Matches($hud, '\[SerializeField\] private Image ((?:forward|reverse)Speed[123]Image);')
if ($fields.Count -ne 6) { throw 'Expected six inspector Image slots' }
$visible = [regex]::Match($hud, '(?m)^    private static void Visible\(.*$').Value -replace 'private static', 'public static'
if (!$visible) { throw 'Missing production visibility helper' }
$source = @'
using System;
namespace SpeedHudChecks {
public class GameObject {
 public bool activeSelf=true;public int changes;
 public void SetActive(bool value){activeSelf=value;changes++;}
}
public class Image {public GameObject gameObject=new();}
public class Hud {
'@
foreach ($field in $fields) { $source += "`npublic Image $($field.Groups[1].Value);" }
$source += "`n" + (Method 'BindSpeedStageImages') + "`n" + $visible
$source += @'
}
public static class Tests {
 static int count;
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;}
 public static int Run(){
  var hud=new Hud();var images=new Image[6];
  string[] names={"forwardSpeed1Image","forwardSpeed2Image","forwardSpeed3Image","reverseSpeed1Image","reverseSpeed2Image","reverseSpeed3Image"};
  int[] stages={1,2,3,-1,-2,-3};
  for(int i=0;i<6;i++){images[i]=new Image();typeof(Hud).GetField(names[i]).SetValue(hud,images[i]);}
  hud.BindSpeedStageImages(0);
  for(int i=0;i<6;i++)Check(!images[i].gameObject.activeSelf,"initial zero hides even authored active images");
  for(int from=-3;from<=3;from++)for(int to=-3;to<=3;to++){
   hud.BindSpeedStageImages(from);hud.BindSpeedStageImages(to);
   for(int i=0;i<6;i++)Check(images[i].gameObject.activeSelf==(stages[i]==to),$"exclusive {from} -> {to}: {names[i]}");
   int before=0;foreach(var image in images)before+=image.gameObject.changes;
   for(int frame=0;frame<100;frame++)hud.BindSpeedStageImages(to);
   int after=0;foreach(var image in images)after+=image.gameObject.changes;
   Check(before==after,"unchanged stage does not rebuild UI via SetActive");
  }
  foreach(int invalid in new[]{4,-4,int.MaxValue,int.MinValue}){
   hud.BindSpeedStageImages(3);hud.BindSpeedStageImages(invalid);
   foreach(var image in images)Check(!image.gameObject.activeSelf,"invalid stages safely hide indicators");
  }
  hud.BindSpeedStageImages(3);hud.BindSpeedStageImages(0);
  foreach(var image in images)Check(!image.gameObject.activeSelf,"death/round/disable reset clears previous image");
  hud.reverseSpeed2Image=null;hud.BindSpeedStageImages(-2);
  foreach(var image in images)Check(!image.gameObject.activeSelf,"missing slot never lights another stage");
  new Hud().BindSpeedStageImages(1);new Hud().BindSpeedStageImages(-3);Check(true,"all unassigned references are safe");
  return count;
 }
}}
'@
Add-Type -TypeDefinition $source
"Speed stage HUD behavior checks passed: $([SpeedHudChecks.Tests]::Run())"

if (!(Method 'Update').Contains('BindSpeedStageImages(valid && owner.IsAlive && battle.IsGameplayActive ? owner.CurrentSpeedStage : 0);')) {
    throw 'Read live local stage only while alive and playing, not speed magnitude or absolute stage'
}
foreach ($methodName in 'Awake','OnDisable') {
    if (!(Method $methodName).Contains('HideViews();')) { throw "$methodName must clear stale indicators" }
}
if (!(Method 'HideViews').Contains('BindSpeedStageImages(0);')) { throw 'Hidden HUD must clear stage images' }

$scene = Get-Content Assets/Scenes/Battle.unity -Raw
$prefab = Get-Content Assets/UI/Prefab/BattleUI.prefab -Raw
function Block([string]$yaml, [string]$id) {
    $match = [regex]::Match($yaml, '(?ms)^--- !u!\d+ &'+$id+'(?: stripped)?\r?\n.*?(?=^--- !u!|\z)')
    if (!$match.Success) { throw "Missing YAML object $id" }
    $match.Value
}
$hudBlock = Block $scene '1515047382'
$expected = @(
    @('forwardSpeed1Image','2027782595','2230668527161844054','01'),
    @('forwardSpeed2Image','2027782596','3954472720755499855','02'),
    @('forwardSpeed3Image','2027782597','5963320822151465258','03'),
    @('reverseSpeed1Image','861538116','','01 (1)'),
    @('reverseSpeed2Image','1904546676','','02 (1)'),
    @('reverseSpeed3Image','540987163','','03 (1)')
)
foreach ($entry in $expected) {
    if (!$hudBlock.Contains("$($entry[0]): {fileID: $($entry[1])}")) { throw "Unbound $($entry[0])" }
    $image = Block $scene $entry[1]
    $sourceYaml = $scene
    if ($entry[2]) {
        if (!$image.Contains("m_CorrespondingSourceObject: {fileID: $($entry[2]), guid: f85a690f8032072489dd7b498ab4110d, type: 3}") -or
            !$image.Contains('m_PrefabInstance: {fileID: 2027782578}')) {throw 'Wrong prefab instance reference'}
        $image = Block $prefab $entry[2]
        $sourceYaml = $prefab
    }
    if (!$image.Contains('guid: fe87c0e1cc204ed48ad3b37840f39efc')) {throw 'Speed slot must refer to a UI Image'}
    $objectId = [regex]::Match($image,'m_GameObject: \{fileID: (\d+)\}').Groups[1].Value
    $object = Block $sourceYaml $objectId
    if ($object -notmatch ('(?m)^  m_Name: '+[regex]::Escape($entry[3])+'\r?$')) {throw "Incorrect named image for $($entry[0])"}
}
$ids = [regex]::Matches($scene,'(?m)^--- !u!\d+ &(\d+)') | ForEach-Object {$_.Groups[1].Value}
if (@($ids | Group-Object | Where-Object Count -gt 1).Count) {throw 'Duplicate scene object IDs'}
'Local-player lifecycle and all six saved Battle UI Image references passed.'
