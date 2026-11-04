# Read-only checks: real icon resolvers with Unity doubles, plus effective prefab/scene bindings.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$customSource = Get-Content Assets/Script/MagicCustomizationUI.cs -Raw
$hudSource = Get-Content Assets/Script/BattleMagicUI.cs -Raw
$vsSource = Get-Content Assets/Script/linkuserinfo.cs -Raw
$roundSource = Get-Content Assets/Script/BattleRoundUI.cs -Raw
$tableSource = Get-Content Assets/Script/MagicStatTable.cs -Raw
$enums = [regex]::Match((Get-Content Assets/Script/PlayerData.cs -Raw), 'public enum MagicType\s*\{[^}]+\}').Value
$fixture = Get-Content Tests/MagicCustomizationRegression.ps1 -Raw
$stubs = [regex]::Match($fixture, '(?s)\$stubs = @''\r?\n(.*?)\r?\n''@').Groups[1].Value
if (!$stubs -or !$enums) { throw 'Missing Unity doubles or magic enum' }
function Method([string]$source, [string]$name) {
    $match = [regex]::Match($source, '(?ms)^    (?:public|private) (?:static )?[\w<>]+ '+$name+'\([^{}]*?\)\r?\n    \{.*?^    \}')
    if (!$match.Success) { throw "Missing method $name" }
    return $match.Value -replace '^    private ', '    public '
}
$selectable = [regex]::Match($customSource, '(?m)^    private static bool IsSelectableMagic\([^\r\n]+').Value
$wrappers = @'
namespace MagicCustomizationChecks {
 using UnityEngine;
 using UnityEngine.UI;
 public class IconHud {
  public MagicStatTable magicTable;
  public Image parryBackImage=new Image(),parrySliderImage=new Image();
'@ + (Method $hudSource 'ApplyMagicSprites') + (Method $hudSource 'ApplyParrySprites') + (Method $hudSource 'SetIcon') + @'
 }
 public class IconLobby {public MagicStatTable magicTable;
'@ + $selectable + (Method $customSource 'MagicIcon') + @'
 }
 public class IconVS {public MagicStatTable magicTable;
'@ + (Method $vsSource 'GetMagicIcon') + @'
 }
 public class IconRound {public MagicStatTable magicTable;
'@ + (Method $roundSource 'Icon') + @'
 }
 public static class MagicIconChecks {
  static int count;
  static void Check(bool ok,string reason){if(!ok)throw new System.Exception(reason);count++;}
  public static int Run(){
   var table=new MagicStatTable();
   for(int i=0;i<table.magics.Length;i++)table.magics[i].icon=new Sprite();
   // Lookup by enum, never by an array position or the order of UI buttons.
   System.Array.Reverse(table.magics);
   typeof(MagicStatTable).GetField("defaultTable",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).SetValue(null,table);
   var hud=new IconHud();var lobby=new IconLobby();var vs=new IconVS();var round=new IconRound();
   var back=new Image();var fill=new Image();var preview=new Image();
   foreach(MagicType magic in System.Enum.GetValues(typeof(MagicType))){
    var expected=table.GetIcon(magic);
    hud.ApplyMagicSprites(magic,back,fill);round.Icon(preview,magic);
    Check(lobby.MagicIcon(magic)==expected&&vs.GetMagicIcon(magic)==expected,"main and VS match table for "+magic);
    Check(back.sprite==expected&&fill.sprite==expected&&preview.sprite==expected,"HUD/fill and intermission match table for "+magic);
    Check(back.enabled==(expected!=null)&&preview.enabled==(expected!=null),"missing icons never remain visible for "+magic);
   }
   Check(table.GetIcon(MagicType.Binding)!=table.GetIcon(MagicType.Dark),"Binding cannot resolve Dark from an old HUD override");
   table.fallback=new MagicStatEntry{icon=new Sprite()};
   foreach(var invalid in new[]{MagicType.None,(MagicType)(-1),(MagicType)999}){
    hud.ApplyMagicSprites(invalid,back,fill);round.Icon(preview,invalid);
    Check(table.GetIcon(invalid)==null&&lobby.MagicIcon(invalid)==null&&vs.GetMagicIcon(invalid)==null&&!back.enabled&&!fill.enabled&&!preview.enabled,"None/invalid must not show fallback or stale art");
   }
   int index=System.Array.FindIndex(table.magics,e=>e.magic==MagicType.Binding);
   table.magics[index].icon=new Sprite();var replacement=table.magics[index].icon;
   hud.ApplyMagicSprites(MagicType.Binding,back,fill);round.Icon(preview,MagicType.Binding);
   Check(lobby.MagicIcon(MagicType.Binding)==replacement&&vs.GetMagicIcon(MagicType.Binding)==replacement&&back.sprite==replacement&&fill.sprite==replacement&&preview.sprite==replacement,"one table change reaches every resolver");
   table.magics[index].icon=null;
   hud.ApplyMagicSprites(MagicType.Binding,back,fill);round.Icon(preview,MagicType.Binding);
   Check(lobby.MagicIcon(MagicType.Binding)==null&&vs.GetMagicIcon(MagicType.Binding)==null&&!back.enabled&&!fill.enabled&&!preview.enabled,"removed icon clears all views");
   table.parryIcon=new Sprite();hud.ApplyParrySprites();
   Check(hud.parryBackImage.sprite==table.parryIcon&&hud.parrySliderImage.sprite==table.parryIcon,"parry also uses shared table");
   table.parryIcon=null;hud.ApplyParrySprites();Check(!hud.parryBackImage.enabled&&!hud.parrySliderImage.enabled,"missing parry icon clears stale art");
   var alternate=new MagicStatTable();alternate.magics[0].icon=new Sprite();hud.magicTable=alternate;
   hud.ApplyMagicSprites(MagicType.Fire,back,fill);Check(back.sprite==alternate.GetIcon(MagicType.Fire),"explicit MagicStatTable is respected");
   hud.ApplyMagicSprites(MagicType.Fire,null,null);round.Icon(null,MagicType.Fire);Check(true,"optional images are safe");
   typeof(MagicStatTable).GetField("defaultTable",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).SetValue(null,null);
   hud.magicTable=lobby.magicTable=vs.magicTable=round.magicTable=null;
   hud.ApplyMagicSprites(MagicType.Fire,back,fill);round.Icon(preview,MagicType.Fire);
   Check(lobby.MagicIcon(MagicType.Fire)==null&&vs.GetMagicIcon(MagicType.Fire)==null&&!back.enabled&&!fill.enabled&&!preview.enabled,"missing table is safe and has no per-UI fallback");
   return count;
  }
 }
}
'@
Add-Type -IgnoreWarnings -WarningAction SilentlyContinue -TypeDefinition ($stubs + "`nnamespace MagicCustomizationChecks { $enums }`nnamespace MagicCustomizationChecks { $tableSource }`n" + $wrappers)
"Cross-UI magic icon checks passed: $([MagicCustomizationChecks.MagicIconChecks]::Run())"

# Resolve the authored list icon, not the border Image or a selection Check image.
. "$PSScriptRoot/MainSceneBindingAudit.ps1" -DefinitionsOnly
function CheckIconBinding([hashtable]$map, [string]$buttonId, [string]$imageId) {
    if (!$imageId -or $imageId -eq '0') { throw "Missing icon Image for button $buttonId" }
    if ($map[$buttonId] -notmatch 'guid: 4e29b1a8efbd4b44bb3f3716e73f07ff' -or
        $map[$imageId] -notmatch 'guid: fe87c0e1cc204ed48ad3b37840f39efc') { throw 'Wrong UI component type' }
    if ((Ref $map[$buttonId] 'm_TargetGraphic') -eq $imageId) { throw 'Magic icon must not replace the button border' }
    $buttonGo = Ref $map[$buttonId] 'm_GameObject'
    $imageGo = Ref $map[$imageId] 'm_GameObject'
    $buttonTr = [regex]::Match($map[$buttonGo],'component: \{fileID: (\d+)').Groups[1].Value
    $imageTr = [regex]::Match($map[$imageGo],'component: \{fileID: (\d+)').Groups[1].Value
    if ((Ref $map[$imageTr] 'm_Father') -ne $buttonTr -or $map[$imageGo] -notmatch '(?m)^  m_Name: Image\r?$') { throw "Icon $imageId does not belong to button $buttonId" }
}
$files = @('Assets/UI/Prefab/CustumUI.prefab','Assets/UI/Prefab/IntroAndRobby.prefab','Assets/Scenes/Eunseo.unity','Assets/Scenes/Main.unity','Assets/UI/Prefab/BattleUI.prefab','Assets/UI/Prefab/Multi.prefab')
$bindingCount = 0
foreach ($path in $files) {
    $text = Get-Content -LiteralPath $path -Raw
    if ($text -match '(?m)^  (magicIcons|magicSprites|parryBackSprite|parrySliderSprite):|propertyPath: magicChoices.*\.icon\s') { throw "Stale icon override in $path" }
    $ids = [regex]::Matches($text,'(?m)^--- !u!\d+ &(\d+)') | ForEach-Object { $_.Groups[1].Value }
    if (@($ids | Group-Object | Where-Object Count -gt 1).Count) { throw "Duplicate Unity fileID in $path" }
    $map = Blocks $text
    foreach ($section in [regex]::Matches($text,'(?ms)^  magicChoices:.*?(?=^  magicTable:)')) {
        if ($section.Value -match '(?m)^    icon:') { throw "Per-choice sprite retained in $path" }
        foreach ($choice in [regex]::Matches($section.Value,'(?ms)^  - magic: \d+\r?\n(.*?)(?=^  - magic:|\z)')) {
            $buttonId = Ref $choice.Value 'button'
            if ($buttonId -eq '0') { continue }
            CheckIconBinding $map $buttonId (Ref $choice.Value 'buttonImage')
            $bindingCount++
        }
    }
    foreach ($table in [regex]::Matches($text,'(?m)^  magicTable: ([^\r\n]+)')) {
        if (!$table.Value.Contains('guid: d81c29d2f62042b1944c5e42544d9636')) { throw "Different icon table in $path" }
    }
}
$custom = Asset 'c65b85f415ffdf64ab4962c17ee06dc6'
$instance = $scene['2689755113089230609']
$overrides = @{}
foreach ($m in [regex]::Matches($instance,'propertyPath: magicChoices.Array.data\[(\d+)\]\.(button|buttonImage)\r?\n      value: [^\r\n]*\r?\n      objectReference: \{fileID: (\d+)\}')) {
    $overrides[$m.Groups[1].Value+':'+$m.Groups[2].Value] = $m.Groups[3].Value
}
foreach ($key in @($overrides.Keys | Where-Object { $_ -match ':button$' })) {
    $index = $key.Split(':')[0]
    $buttonId = $overrides[$key];$imageId = $overrides[$index+':buttonImage']
    if (!$imageId) { throw "Reordered Main choice $index has no corresponding icon override" }
    foreach ($id in $buttonId,$imageId) {
        if ((Ref $scene[$id] 'm_PrefabInstance') -ne '2689755113089230609') { throw 'Main button and icon must belong to the same active customization instance' }
    }
    $buttonSource = Ref $scene[$buttonId] 'm_CorrespondingSourceObject'
    $imageSource = Ref $scene[$imageId] 'm_CorrespondingSourceObject'
    CheckIconBinding $custom $buttonSource $imageSource
    $bindingCount++
}
if ($hudSource -match '\b(magicSprites|parryBackSprite|parrySliderSprite)\b' -or $vsSource -match '\bmagicIcons\b' -or
    (Get-Content Assets/Editor/WitchFlightIntroBinding.cs -Raw) -match '\bmagicIcons\b') { throw 'Per-UI magic sprite overrides must stay retired' }
"Saved list icon bindings passed: $bindingCount (including Main reordered buttons)."
