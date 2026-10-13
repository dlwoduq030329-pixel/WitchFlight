# Offline execution of the production rules, table, stat transform and input policy.
# Does not replace Fusion weaving or two-client Play Mode verification.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo
function Method([string]$source,[string]$name) {
    $m=[regex]::Match($source,'(?ms)^    private static void '+$name+'\([^{}]*?\)\r?\n    \{.*?^    \}')
    if(!$m.Success){throw "Missing production validator $name"}; $m.Value
}
$combat=Get-Content Tests/MagicCombatRegression.ps1 -Raw
$stubs=[regex]::Match($combat,'(?ms)\$stubs = @''\r?\n(.*?)\r?\n''@').Groups[1].Value
$stubs=$stubs.Substring(0,$stubs.IndexOf('namespace CombatChecks'))
$stubs=$stubs.Replace('public class Object {}','public enum HideFlags {DontSave} public class Object {public HideFlags hideFlags;}')
$stubs=$stubs.Replace('public class ScriptableObject:Object {}','public class ScriptableObject:Object { public static T CreateInstance<T>() where T:new()=>new T(); }')
$stubs=$stubs.Replace('where T:class=>null;','where T:class=>(T)Activator.CreateInstance(typeof(T));')
$stubs=$stubs.Replace('public static float Max(float a,float b)=>Math.Max(a,b);','public static float Max(float a,float b)=>Math.Max(a,b); public static float Abs(float v)=>Math.Abs(v);')
$enums=[regex]::Match((Get-Content Assets/Script/PlayerData.cs -Raw),'public enum MagicType\s*\{[^}]+\}').Value
$code=$stubs+"`nnamespace Fusion {public enum PlayerRef {None,A,B}}`nnamespace PerkChecks { $enums }`n"
foreach($file in 'MagicStatTable','PerkStatTable','PerkRules','PerkEffects','PerkInputGate') {
    $code+="`nnamespace PerkChecks {`n"+(Get-Content "Assets/Script/$file.cs" -Raw)+"`n}`n"
}
$validation=Get-Content Assets/Editor/WitchFlightPerkValidation.cs -Raw
$ui=Get-Content Assets/Script/BattlePerkUI.cs -Raw
$description=[regex]::Match($ui,'(?ms)^    public static string Description\(.*?;').Value
$code+="`nnamespace PerkChecks { using System; using System.Collections.Generic; using UnityEngine;`n"+@'
public static class AssetDatabase {public static T LoadAssetAtPath<T>(string path) where T:new()=>new T();}
public static class BattlePerkUI {
'@ + $description + @'
}
public static class Tests {
 private static int checks;
 private static void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;}
 private static bool Near(float a,float b)=>Math.Abs(a-b)<.001f;
 public static int Run(){ValidateRules();ValidateEffects();return checks;}
'@+(Method $validation 'ValidateRules')+(Method $validation 'ValidateEffects')+"`n}}"
Add-Type -TypeDefinition $code
"Perk rules, XP ledger, fixed effects and input checks passed: $([PerkChecks.Tests]::Run())"
$asset=Get-Content Assets/Resources/PerkStatTable.asset -Raw
$ids=[regex]::Matches($asset,'(?m)^  - id: (\d+)\r?$')|ForEach-Object{[int]$_.Groups[1].Value}
if($ids.Count-ne 34 -or (@($ids|Sort-Object -Unique)).Count-ne 34){throw 'Real perk asset IDs must be unique 1..34'}
foreach($id in 1..34){if($ids-notcontains $id){throw "Missing perk $id"}}
$data=Get-Content Assets/Script/PlayerData.Perks.cs -Raw
if($data-notmatch 'revision == PerkRevision' -or $data-notmatch 'candidate == expected' -or $data-notmatch 'RpcSources.InputAuthority, RpcTargets.StateAuthority'){throw 'Selection authority/revision protection missing'}
if(($data -replace '(?m)//.*$','')-match 'DataConfig\.|Backend\.|BACKND\.'){throw 'Perks must never persist to account customization'}
$player=Get-Content Assets/Script/Player.cs -Raw
if($player.IndexOf('if (data.suppressCombat || data.suppressActions) CancelCombatForPerks();')-gt $player.IndexOf('else if (Object.HasStateAuthority) UpdatePendingCast();')){throw 'Space must cancel delayed cast before execution'}
if($ui-match 'Cursor\.(visible|lockState)\s*='){throw 'Perk UI must not change cursor lock'}
if($ui-notmatch '!gate.Showing && Input.GetKey\(KeyCode.Tab\)' -or $ui-notmatch 'Show\(selectionRoot,cards\)'){throw 'Held Space/Tab contract missing'}
$scene=Get-Content Assets/Scenes/Battle.unity -Raw
if($scene-notmatch 'component: \{fileID: 1515047385\}' -or $scene-notmatch 'matchDurationSeconds: 160' -or $scene-notmatch 'experienceAnchor: \{fileID: 2027782601\}') {throw 'Battle perk bindings/duration missing'}
'34 saved perk IDs, authority/revision, no account writes, input ordering and scene bindings passed.'
