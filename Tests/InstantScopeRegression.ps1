$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$player = Get-Content "$repo/Assets/Script/Player.cs" -Raw
$stats = Get-Content "$repo/Assets/Script/MagicStatTable.cs" -Raw
$method = [regex]::Match($player, '(?ms)^    private bool UpdateInstantScopeInput\([^{}]*\)\s*\{.*?^    \}').Value.Replace('private bool','public bool')
if (!$method) { throw 'Missing scope input method' }
$scope = [regex]::Match($stats, '(?ms)    public bool UsesInstantScope =>.*?;').Value
$magic = [regex]::Match((Get-Content "$repo/Assets/Script/PlayerData.cs" -Raw), 'public enum MagicType\s*\{[^}]+\}').Value
$effect = [regex]::Match($stats, 'public enum MagicEffectKind\s*\{[^}]+\}').Value
$code = "using System;`n$magic`n$effect`n" + @'
public class ScopeInputProbe {
 public bool instantScopeArmed, IsParrying;
'@ + $method + @'
}
public class ScopeStatsProbe {
 public MagicType magic;
 public MagicEffectKind effect;
 public bool requiresTarget, TargetsAllies, IsChanneled;
'@ + $scope + @'
}
public static class ScopeTests {
 static int checks;
 static void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;}
 public static int Run(){
 var input=new ScopeInputProbe();
 Check(!input.UpdateInstantScopeInput(true,true,false)&&input.instantScopeArmed,"press arms without firing");
 for(int i=0;i<300;i++) Check(!input.UpdateInstantScopeInput(true,false,false),"holding never fires");
 Check(input.UpdateInstantScopeInput(false,false,true)&&!input.instantScopeArmed,"release fires once");
 Check(!input.UpdateInstantScopeInput(false,false,true),"duplicate release cannot fire");
 input.UpdateInstantScopeInput(true,true,false);input.instantScopeArmed=false;
 Check(!input.UpdateInstantScopeInput(false,false,true),"cancel then release cannot fire");
 input.IsParrying=true;
 Check(!input.UpdateInstantScopeInput(true,true,false)&&!input.instantScopeArmed,"parry cannot arm scope");
 foreach(var magic in new[]{MagicType.Vision,MagicType.Dark,MagicType.Razier}){
 var s=new ScopeStatsProbe{magic=magic,effect=magic==MagicType.Dark?MagicEffectKind.LifeSteal:MagicEffectKind.DirectDamage};
 Check(s.UsesInstantScope,"aimed instant attack has scope");
 s.requiresTarget=true;Check(!s.UsesInstantScope,"lock-on excluded");s.requiresTarget=false;
 s.TargetsAllies=true;Check(!s.UsesInstantScope,"support targeting excluded");s.TargetsAllies=false;
 s.IsChanneled=true;Check(!s.UsesInstantScope,"channel excluded");
 }
 foreach(var magic in new[]{MagicType.None,MagicType.BasicAttack})
 Check(!new ScopeStatsProbe{magic=magic,effect=MagicEffectKind.DirectDamage}.UsesInstantScope,"basic attack excluded");
 foreach(var effect in new[]{MagicEffectKind.BladeVolley,MagicEffectKind.BladeRain,MagicEffectKind.Flare,MagicEffectKind.Smoke,MagicEffectKind.DelayedArea})
 Check(!new ScopeStatsProbe{magic=MagicType.Blade,effect=effect}.UsesInstantScope,"special input preserved");
 return checks;
 }
}
'@
Add-Type -TypeDefinition $code
Write-Output "PASS: $([ScopeTests]::Run()) scope input and classification checks"
